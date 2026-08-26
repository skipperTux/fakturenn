using System.Globalization;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using Serilog.Events;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// What the host does at startup about message storage, for the two un-provisioned
/// cases the design rules on separately.
/// <para>
/// Both tests build the real host through <see cref="FakturennWebApplication.Build"/>.
/// Neither starts a container of its own: the no-connection-string case needs no
/// database at all, and the un-provisioned case borrows the collection fixture's
/// PostgreSQL instance and creates one more database inside it. The integration suite
/// already peaks at eleven concurrent containers on a two-core runner, so a twelfth
/// would be a real cost for no extra coverage.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class MessagingStartupTests(SetupHostFixture host)
{
    /// <summary>
    /// A database the <c>--migrate</c> entrypoint has never touched as far as messaging is
    /// concerned: the EF schemas are migrated into it, Wolverine's is not. That isolates
    /// what the second test asserts — a startup failure here can only be about message
    /// storage.
    /// </summary>
    private const string UnprovisionedDatabase = "messaging_unprovisioned";

    [Fact]
    public async Task A_host_with_no_connection_string_reports_that_messages_are_not_durable()
    {
        // Wolverine silently falls back to in-memory queues when persistence is not
        // configured. A typo in the ConnectionStrings:Fakturenn key therefore produces a
        // host that answers /alive with 200 and loses every queued message on restart,
        // which is the exact failure class the outbox exists to prevent. The fallback is
        // allowed -- the database-free UI fixture depends on it -- but it may not be
        // silent.
        int mark = HostLogCapture.Instance.Mark();

        Exception? startFailure = await StartAndStopAsync(
        [
            "--urls",
            "http://127.0.0.1:0",

            // Index 1, because appsettings.json already occupies index 0 with the console
            // sink -- the same arrangement SetupHostFixture uses, and for the same reason:
            // the assertion is about what the real Serilog pipeline carries.
            "--Serilog:WriteTo:1:Name=Sink",
            $"--Serilog:WriteTo:1:Args:sink={HostLogCapture.ConfigurationName}",
        ]);

        startFailure.Should().BeNull(
            "an application with nothing configured still starts -- that invariant is what "
            + "makes the fallback allowable in the first place");

        IReadOnlyList<LogEvent> written = HostLogCapture.Instance.Since(mark);

        // Serilog renders Microsoft's LogLevel.Critical as Fatal.
        written.Should().Contain(
            logEvent => logEvent.Level == LogEventLevel.Fatal
                && logEvent.RenderMessage(CultureInfo.InvariantCulture)
                    .Contains("will not survive a restart", StringComparison.Ordinal),
            "an instance that is not durable must say so at Critical, naming the consequence");
    }

    [Fact]
    public async Task Startup_does_not_provision_the_messaging_schema()
    {
        // CLAUDE.md's invariant: migrations never run automatically at startup. Wolverine
        // would break it by default -- AutoBuildMessageStorageOnStartup defaults to
        // CreateOrUpdate -- so without this test a library upgrade that re-defaults it, or
        // a deleted line in MessagingConfiguration, brings boot-time DDL back with every
        // suite still green. Both host-starting fixtures provision before they start, so
        // nothing else in this suite ever meets an unprovisioned database.
        string connectionString = await CreateEfMigratedDatabaseAsync();

        Exception? startFailure = await StartAndStopAsync(
        [
            "--urls",
            "http://127.0.0.1:0",
            $"--ConnectionStrings:Fakturenn={connectionString}",
        ]);

        // Counted after the host is gone, so DDL applied late during startup still shows up.
        long schemas = await CountMessagingSchemasAsync(connectionString);

        // Both halves, in one scope so a regression reports both. The absent schema is the
        // load-bearing half: a throw on its own could come from an unrelated fault, and it
        // is boot-time DDL rather than the exception that this test exists to catch.
        using (new AssertionScope())
        {
            // The crash is the ruled behaviour, not an accident: an instance pointed at a
            // database that has never been migrated is a deployment error, and retrying
            // cannot create a missing schema.
            startFailure.Should().NotBeNull(
                "a host whose message storage does not exist must refuse to start rather "
                + "than serve traffic with delivery guarantees that turned out imaginary");

            schemas.Should().Be(
                0,
                "--migrate is the only thing that may create Wolverine's schema");
        }
    }

    /// <summary>
    /// Builds the real host, starts it, stops it again, and reports what
    /// <c>StartAsync</c> threw, if anything.
    /// <para>
    /// The static <see cref="Log.Logger"/> is saved and put back because a second host in
    /// this process otherwise takes the collection fixture's host down with it. Serilog's
    /// <c>UseSerilog</c> reconfigures the bootstrap logger <c>FakturennWebApplication.Build</c>
    /// installs and leaves the running host writing through whatever <c>Log.Logger</c>
    /// currently is; disposing this host calls <c>Log.CloseAndFlush</c>, which replaces it
    /// with a silent logger. Measured, not assumed: without the restore the fixture host
    /// kept serving requests happily and every assertion in
    /// <c>AuthEventLoggingTests</c> failed on an empty log.
    /// </para>
    /// </summary>
    private static async Task<Exception?> StartAndStopAsync(string[] arguments)
    {
        Serilog.ILogger fixtureLogger = Log.Logger;

        WebApplication app = FakturennWebApplication.Build(arguments);

        try
        {
            Exception? failure = await Record.ExceptionAsync(
                () => app.StartAsync(TestContext.Current.CancellationToken));

            if (failure is null)
            {
                await app.StopAsync(TestContext.Current.CancellationToken);
            }

            return failure;
        }
        finally
        {
            await app.DisposeAsync();
            Log.Logger = fixtureLogger;
        }
    }

    private static async Task<long> CountMessagingSchemasAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'messaging'";

        object? found = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return Convert.ToInt64(found, CultureInfo.InvariantCulture);
    }

    private async Task<string> CreateEfMigratedDatabaseAsync()
    {
        NpgsqlConnectionStringBuilder builder = new(host.ConnectionString)
        {
            Database = UnprovisionedDatabase,
        };

        await using (NpgsqlConnection administration = new(host.ConnectionString))
        {
            await administration.OpenAsync(TestContext.Current.CancellationToken);

            // Dropped first so a re-run inside one container starts from the same state.
            await using NpgsqlCommand drop = administration.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {UnprovisionedDatabase} WITH (FORCE)";
            await drop.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            await using NpgsqlCommand create = administration.CreateCommand();
            create.CommandText = $"CREATE DATABASE {UnprovisionedDatabase}";
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        string connectionString = builder.ConnectionString;

        await using (DataProtectionDbContext dataProtection = new(
            new DbContextOptionsBuilder<DataProtectionDbContext>().UseNpgsql(connectionString).Options))
        {
            await dataProtection.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (IdentityDbContext identity = new(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options,
            DataProtectionProvider.Create("Fakturenn.Tests")))
        {
            await identity.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (InvoicesDbContext invoices = new(
            new DbContextOptionsBuilder<InvoicesDbContext>().UseNpgsql(connectionString).Options))
        {
            await invoices.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return connectionString;
    }
}
