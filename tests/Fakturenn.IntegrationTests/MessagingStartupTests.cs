using System.Globalization;
using System.Text;
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
/// What the host does at startup about message storage, for the un-provisioned cases
/// the design rules on separately — and how a database in that state is repaired.
/// <para>
/// Every test here builds the real host through <see cref="FakturennWebApplication.Build"/>.
/// None starts a container of its own: the no-connection-string case needs no
/// database at all, and the others borrow the collection fixture's PostgreSQL
/// instance and create one more database inside it. The integration suite
/// already peaks at eleven concurrent containers on a two-core runner, so a twelfth
/// would be a real cost for no extra coverage.
/// </para>
/// <para>
/// The tests are not independent: they swap the process-wide <see cref="Log.Logger"/>
/// and put it back, so one that failed to restore would break the others — and every other
/// class in the collection. That is safe only because the <see cref="RealHost"/> collection
/// serialises its classes and xUnit runs the tests within one class one after another, so
/// no other test observes the swapped logger. If either ever runs in parallel with
/// anything, the save-and-restore in <see cref="StartAndStopAsync"/> stops being enough.
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

    /// <summary>
    /// A second database in the same state, for the upgrade test. Separate from
    /// <see cref="UnprovisionedDatabase"/> on purpose: that one must stay un-provisioned
    /// for the test above to mean anything, and this one gets <c>--migrate</c> run against
    /// it. Sharing one name would make the pair order-dependent.
    /// </summary>
    private const string UpgradedDatabase = "messaging_upgrade";

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
        string connectionString = await CreateEfMigratedDatabaseAsync(UnprovisionedDatabase);

        // Precondition, not the behaviour under test. Everything below reads a startup
        // failure as evidence about messaging, which only holds while messaging is the one
        // thing missing. If a module is added to Program.cs's createMigrationContexts and
        // CreateEfMigratedDatabaseAsync is not extended with it, startup fails on that
        // schema instead and the assertions below still pass -- the test goes green with
        // AutoBuildMessageStorageOnStartup regressed. Asserting the expected set here says
        // so at the point it breaks, and naming the missing schema.
        IReadOnlyList<string> migrated = await ListApplicationSchemasAsync(connectionString);

        migrated.Should().BeEquivalentTo(
            [
                DataProtectionDbContext.SchemaName,
                IdentityDbContext.SchemaName,
                InvoicesDbContext.SchemaName,
            ],
            "this test's premise is a database that is fully EF-migrated and nothing more");

        Exception? startFailure = await StartAndStopAsync(
        [
            "--urls",
            "http://127.0.0.1:0",
            $"--ConnectionStrings:Fakturenn={connectionString}",
        ]);

        // Counted after the host is gone, so DDL applied late during startup still shows up.
        long schemas = await CountMessagingSchemasAsync(connectionString);

        // All three in one scope so a regression reports all of them. The absent schema is
        // the load-bearing one -- it is boot-time DDL, not the exception, that this test
        // exists to catch. The other two are what stop an unrelated fault from reading as
        // proof: a throw alone could come from anywhere, and "no schema" is equally true of
        // a host that fell over before Wolverine ever ran.
        using (new AssertionScope())
        {
            // The crash is the ruled behaviour, not an accident: an instance pointed at a
            // database that has never been migrated is a deployment error, and retrying
            // cannot create a missing schema.
            startFailure.Should().NotBeNull(
                "a host whose message storage does not exist must refuse to start rather "
                + "than serve traffic with delivery guarantees that turned out imaginary");

            // Which storage it crashed over. 6.30.0 raises this from
            // MessageDatabase.AssertStorageExistsAsync, whose message names no table. The
            // durability agent's messaging.wolverine_nodes query is the attribution the
            // design explicitly *retracts*: it is where ContinueOnFailures and Solo crash,
            // not where this configuration does, and neither noun appears anywhere in the
            // exception chain raised here. So the match is on Wolverine's own noun for the
            // missing thing rather than on a relation name or a whole sentence: short enough
            // to survive the library rewording its diagnostics, specific enough that no
            // unrelated startup fault produces it.
            Describe(startFailure).Should().Contain(
                "message storage",
                "a startup failure only proves the invariant if it is *this* failure -- any "
                + "other fault would satisfy the two assertions around it just as well");

            schemas.Should().Be(
                0,
                "--migrate is the only thing that may create Wolverine's schema");
        }
    }

    [Fact]
    public async Task Migrate_provisions_the_messaging_schema_in_an_already_migrated_database()
    {
        // The upgrade path, and the only state every existing installation is actually in:
        // EF-migrated by a previous version, with no messaging schema because no previous
        // version had one. MigrateEntrypointTests covers --migrate from *empty*, which is
        // the state a new installation starts from and nobody upgrades from. The two are
        // different code paths through DatabaseMigrator: the EF contexts have nothing to
        // apply here, so this asserts that Wolverine's provisioning is not conditional on
        // them having done work.
        string connectionString = await CreateEfMigratedDatabaseAsync(UpgradedDatabase);

        long before = await CountMessagingSchemasAsync(connectionString);

        before.Should().Be(
            0,
            "the premise is a database migrated by a version that had no message storage");

        (int exitCode, string output) = await HostProcess.RunAsync(
            connectionString,
            ["--migrate"],
            standardInput: null,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, output);

        long after = await CountMessagingSchemasAsync(connectionString);

        after.Should().Be(
            1,
            "--migrate must provision message storage into a database that already carries "
            + "the EF schemas, not only into an empty one");

        // The claim that matters to an operator is not "a schema appeared" but "the host
        // that refused to start now starts". Same arguments as the test above, same
        // database state apart from this one step.
        Exception? startFailure = await StartAndStopAsync(
        [
            "--urls",
            "http://127.0.0.1:0",
            $"--ConnectionStrings:Fakturenn={connectionString}",
        ]);

        startFailure.Should().BeNull(
            "running --migrate is the whole of the upgrade procedure: after it, a host "
            + "against the same database must start");
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
    /// <para>
    /// Hence two nested <c>finally</c> blocks rather than one. <c>Build</c> replaces
    /// <c>Log.Logger</c> before it can throw, so it belongs inside the outer <c>try</c>;
    /// and <c>DisposeAsync</c> throwing must not skip the restore, which a single block
    /// would let it do. The order the pair produces is the one the restore needs:
    /// dispose first — it calls <c>Log.CloseAndFlush</c> on whatever logger is current —
    /// and only then put the fixture's logger back, so it is never the one flushed shut.
    /// </para>
    /// </summary>
    private static async Task<Exception?> StartAndStopAsync(string[] arguments)
    {
        Serilog.ILogger fixtureLogger = Log.Logger;

        try
        {
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
            }
        }
        finally
        {
            Log.Logger = fixtureLogger;
        }
    }

    /// <summary>
    /// Every message down the chain, joined. What the host throws today is an
    /// <see cref="AggregateException"/> around Wolverine's own exception, and the number of
    /// layers is the library's business rather than this test's, so matching the outermost
    /// message alone would be brittle for no reason.
    /// </summary>
    private static string Describe(Exception? failure)
    {
        StringBuilder chain = new();

        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            chain.AppendLine(current.Message);
        }

        return chain.ToString();
    }

    /// <summary>
    /// The schemas this application owns: PostgreSQL's own are excluded, and so is
    /// <c>public</c>, which every database has whether or not anything migrated into it.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ListApplicationSchemasAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT schema_name FROM information_schema.schemata "
            + "WHERE schema_name NOT IN ('public', 'information_schema') "
            + "AND schema_name NOT LIKE 'pg\\_%'";

        List<string> schemas = [];

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
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

    private async Task<string> CreateEfMigratedDatabaseAsync(string database)
    {
        NpgsqlConnectionStringBuilder builder = new(host.ConnectionString)
        {
            Database = database,
        };

        await using (NpgsqlConnection administration = new(host.ConnectionString))
        {
            await administration.OpenAsync(TestContext.Current.CancellationToken);

            // Dropped first so a re-run inside one container starts from the same state.
            await using NpgsqlCommand drop = administration.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {database} WITH (FORCE)";
            await drop.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            await using NpgsqlCommand create = administration.CreateCommand();
            create.CommandText = $"CREATE DATABASE {database}";
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
