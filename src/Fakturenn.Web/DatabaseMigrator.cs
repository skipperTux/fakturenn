using System.Diagnostics;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.Web;

/// <summary>
/// Drives the "--migrate" entrypoint. A Kubernetes migration Job has no ordering
/// guarantee against the database: it can start before PostgreSQL finishes
/// accepting connections -- and first-boot <c>initdb</c>, WAL replay, or a
/// restored volume can take much longer than a healthy restart. A container
/// that dies instantly on a database that is merely slow to come up is worse
/// than one that waits, so this loop retries a connection failure until a
/// total wall-clock budget (<see cref="DatabaseOptions.StartupTimeoutSeconds"/>)
/// is exhausted, sleeping <see cref="DatabaseOptions.RetryDelaySeconds"/>
/// between attempts, regardless of how the database happens to be unavailable.
/// </summary>
/// <remarks>
/// <para>
/// The budget is a single wall-clock timeout rather than a retry count on
/// purpose: a retry count's real duration depends on the failure mode. A
/// refused connection fails instantly, so N retries cost roughly
/// N * RetryDelaySeconds; a blackholed address (packets silently dropped)
/// instead burns Npgsql's connect timeout (15s by default) on every attempt,
/// so the same N retries cost roughly N * (15s + RetryDelaySeconds) -- nearly
/// five times longer for the exact failure this feature exists to ride out
/// (a booting database that is not refusing connections, just not answering
/// yet). A wall-clock deadline gives the same guarantee regardless of which
/// failure mode is in play.
/// </para>
/// <para>
/// The <paramref name="createContext"/> passed in by <c>Program.cs</c> must NOT
/// have Npgsql's <c>EnableRetryOnFailure</c> enabled. That execution strategy is
/// registered on the runtime <c>InvoicesDbContext</c> (requirement B) to mask
/// transient blips once the application is serving traffic; nesting it inside
/// this loop as well would retry each attempt internally before this loop even
/// sees the failure, turning one wall-clock budget into two independently
/// enforced ones. This loop is deliberately the only retry mechanism active
/// while migrating.
/// </para>
/// </remarks>
public static partial class DatabaseMigrator
{
    /// <summary>
    /// Applied to the migration connection string when it does not already specify
    /// Npgsql's own "Timeout" keyword. Caps a single connect attempt against a
    /// blackholed address so the wall-clock budget is spent retrying rather than
    /// blocked inside one hung connect (Npgsql's own default is 15s).
    /// </summary>
    private const int DefaultConnectTimeoutSeconds = 5;

    // public Methods
    public static async Task<int> RunAsync(
        Func<InvoicesDbContext> createContext,
        DatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        TimeSpan budget = TimeSpan.FromSeconds(options.StartupTimeoutSeconds);
        TimeSpan retryDelay = TimeSpan.FromSeconds(options.RetryDelaySeconds);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                await using InvoicesDbContext context = createContext();
                await context.Database.MigrateAsync(cancellationToken);

                LogMigrationsApplied(logger, attempt, stopwatch.Elapsed.TotalSeconds);

                return 0;
            }
            catch (PostgresException exception)
            {
                // The server answered with an error: the connection succeeded and
                // PostgreSQL rejected the migration itself. That is never transient --
                // retrying just delays a failure a human has to fix.
                LogMigrationFailed(logger, exception, attempt);

                return 1;
            }
            catch (NpgsqlException exception)
            {
                // Any other NpgsqlException means the connection itself could not be
                // established (refused, timed out, host unreachable, DNS failure, ...) --
                // exactly the "database not accepting connections yet" case this loop
                // exists to ride out.
                TimeSpan remaining = budget - stopwatch.Elapsed;

                if (remaining <= TimeSpan.Zero)
                {
                    LogStartupTimeoutExhausted(logger, exception, attempt, options.StartupTimeoutSeconds);

                    return 1;
                }

                LogConnectionAttemptFailed(logger, exception, attempt, remaining.TotalSeconds, options.StartupTimeoutSeconds);

                // Sleep the configured delay, but never past the deadline -- a little
                // budget left still earns one more attempt rather than being skipped,
                // which would make the effective budget silently shorter than configured.
                TimeSpan sleep = retryDelay < remaining ? retryDelay : remaining;

                if (sleep > TimeSpan.Zero)
                {
                    await Task.Delay(sleep, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Caps the connect timeout for the migration connection when the connection string
    /// does not already set one explicitly. Without this, a single attempt against a
    /// blackholed address can consume most or all of a short startup budget on Npgsql's
    /// own 15s default before <see cref="RunAsync"/> ever gets a chance to retry.
    /// </summary>
    public static string ApplyDefaultConnectTimeout(string? connectionString)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString ?? string.Empty);

        // NpgsqlConnectionStringBuilder.ContainsKey is NOT usable here: Npgsql's builder
        // eagerly initializes every known keyword to its default value on construction, so
        // ContainsKey("Timeout") returns true even when "Timeout" never appeared in the
        // input string. Keys, in contrast, only lists keywords that were actually present
        // in the parsed connection string -- verified empirically against both an unset and
        // an explicitly-set "Timeout=" connection string. CA1841's general "prefer
        // ContainsKey" advice does not hold for this specific type.
#pragma warning disable CA1841
        if (!builder.Keys.Contains(nameof(NpgsqlConnectionStringBuilder.Timeout)))
#pragma warning restore CA1841
        {
            builder.Timeout = DefaultConnectTimeoutSeconds;
        }

        return builder.ConnectionString;
    }

    // private Methods
    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations applied successfully on attempt {Attempt} after {ElapsedSeconds:F1}s.")]
    private static partial void LogMigrationsApplied(ILogger logger, int attempt, double elapsedSeconds);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migration attempt {Attempt} failed with a database error (not a connection failure). This will not be retried.")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception, int attempt);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Could not reach the database within the {StartupTimeoutSeconds}s startup budget (attempt {Attempt} was the last). Giving up.")]
    private static partial void LogStartupTimeoutExhausted(ILogger logger, Exception exception, int attempt, int startupTimeoutSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attempt {Attempt} to reach the database failed, {RemainingSeconds:F1}s of {StartupTimeoutSeconds}s startup budget remaining. Retrying.")]
    private static partial void LogConnectionAttemptFailed(ILogger logger, Exception exception, int attempt, double remainingSeconds, int startupTimeoutSeconds);
}
