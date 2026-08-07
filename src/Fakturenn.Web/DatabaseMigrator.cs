using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.Web;

/// <summary>
/// Drives the "--migrate" entrypoint. A Kubernetes migration Job has no ordering
/// guarantee against the database: it can start before PostgreSQL finishes
/// accepting connections. A container that dies instantly on a database that is
/// merely slow to come up is worse than one that waits, so this loop retries a
/// connection failure up to <see cref="DatabaseOptions.MaxRetries"/> times,
/// sleeping <see cref="DatabaseOptions.RetryDelaySeconds"/> between attempts, and
/// only gives up after the budget is exhausted.
/// </summary>
/// <remarks>
/// The <paramref name="createContext"/> passed in by <c>Program.cs</c> must NOT
/// have Npgsql's <c>EnableRetryOnFailure</c> enabled. That execution strategy is
/// registered on the runtime <c>InvoicesDbContext</c> (requirement B) to mask
/// transient blips once the application is serving traffic; nesting it inside
/// this loop as well would retry each attempt internally before this loop even
/// sees the failure, multiplying <c>MaxRetries * MaxRetries</c> worth of waiting
/// into a surprisingly long total. This loop is deliberately the only retry
/// mechanism active while migrating.
/// </remarks>
public static partial class DatabaseMigrator
{
    // public Methods
    public static async Task<int> RunAsync(
        Func<InvoicesDbContext> createContext,
        DatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= options.MaxRetries; attempt++)
        {
            try
            {
                await using InvoicesDbContext context = createContext();
                await context.Database.MigrateAsync(cancellationToken);

                LogMigrationsApplied(logger, attempt, options.MaxRetries);

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
                if (attempt == options.MaxRetries)
                {
                    LogConnectionRetriesExhausted(logger, exception, options.MaxRetries);

                    return 1;
                }

                LogConnectionAttemptFailed(logger, exception, attempt, options.MaxRetries, options.RetryDelaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds), cancellationToken);
            }
        }

        return 1;
    }

    // private Methods
    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations applied successfully on attempt {Attempt} of {MaxRetries}.")]
    private static partial void LogMigrationsApplied(ILogger logger, int attempt, int maxRetries);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migration attempt {Attempt} failed with a database error (not a connection failure). This will not be retried.")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception, int attempt);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Could not reach the database after {MaxRetries} attempts. Giving up.")]
    private static partial void LogConnectionRetriesExhausted(ILogger logger, Exception exception, int maxRetries);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attempt {Attempt} of {MaxRetries} to reach the database failed. Retrying in {DelaySeconds}s.")]
    private static partial void LogConnectionAttemptFailed(ILogger logger, Exception exception, int attempt, int maxRetries, int delaySeconds);
}
