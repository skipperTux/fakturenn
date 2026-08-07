using System.Diagnostics;
using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Covers <see cref="DatabaseMigrator"/> against real infrastructure -- the
/// behaviour a reviewer previously verified by hand for Task 8 (timeout
/// convergence, idempotency, fail-fast-on-genuine-error) and then discarded.
/// These tests automate that evidence so the behaviour cannot regress silently.
/// </summary>
public sealed class DatabaseMigratorTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migrate_succeeds_against_a_reachable_database_and_stays_idempotent_on_rerun()
    {
        DatabaseOptions options = new() { StartupTimeoutSeconds = 30, RetryDelaySeconds = 1 };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        int firstResult = await DatabaseMigrator.RunAsync(
            postgres.CreateContext, options, NullLogger.Instance, cancellationToken);

        firstResult.Should().Be(0);
        (await CountSchemas(cancellationToken)).Should().Be(1L);
        (await CountHistoryRows(cancellationToken)).Should().Be(1L);

        // Running "--migrate" again -- e.g. a redeployed migration Job that finds
        // nothing pending -- must succeed and must not duplicate the history row.
        int secondResult = await DatabaseMigrator.RunAsync(
            postgres.CreateContext, options, NullLogger.Instance, cancellationToken);

        secondResult.Should().Be(0);
        (await CountHistoryRows(cancellationToken)).Should().Be(1L);
    }

    [Fact]
    public async Task Migrate_fails_within_the_configured_startup_budget_when_the_database_is_unreachable()
    {
        // A closed local port refuses the connection almost instantly, so the loop
        // spends its budget retrying (sleeping RetryDelaySeconds between attempts)
        // rather than blocking inside one hung connect -- unlike a blackholed
        // address, which would instead burn Npgsql's own connect timeout.
        DatabaseOptions options = new() { StartupTimeoutSeconds = 3, RetryDelaySeconds = 1 };
        string connectionString = DatabaseMigrator.ApplyDefaultConnectTimeout(
            "Host=127.0.0.1;Port=1;Database=fakturenn;Username=fakturenn;Password=fakturenn");

        InvoicesDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<InvoicesDbContext>().UseNpgsql(connectionString).Options);

        Stopwatch stopwatch = Stopwatch.StartNew();

        int result = await DatabaseMigrator.RunAsync(
            CreateContext, options, NullLogger.Instance, TestContext.Current.CancellationToken);

        stopwatch.Stop();

        result.Should().Be(1);

        // Comfortably above zero (it did not exit instantly on the first refusal)
        // and comfortably below a generous ceiling (it did not hang past the
        // budget). Not an exact duration -- that would be flaky.
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1.5));
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Migrate_fails_fast_without_retrying_on_a_genuine_database_error()
    {
        // A genuine migration error is provoked without touching the real migration
        // history: a fresh container's initial superuser owns the database, and
        // PostgreSQL's default database ACL does not grant CREATE (the privilege to
        // create new schemas) to any other role. A role with CONNECT but not CREATE
        // hits the exact server-side rejection DatabaseMigrator's PostgresException
        // branch exists for -- the connection succeeds, the server refuses the
        // statement -- without ever committing a broken migration file.
        await using PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("fakturenn")
            .WithUsername("fakturenn")
            .WithPassword("fakturenn")
            .Build();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        await using (var adminConnection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await adminConnection.OpenAsync(cancellationToken);
            await using NpgsqlCommand createRole = adminConnection.CreateCommand();
            createRole.CommandText =
                "CREATE ROLE restricted LOGIN PASSWORD 'restricted'; " +
                "GRANT CONNECT ON DATABASE fakturenn TO restricted;";
            await createRole.ExecuteNonQueryAsync(cancellationToken);
        }

        NpgsqlConnectionStringBuilder restrictedConnectionString = new(container.GetConnectionString())
        {
            Username = "restricted",
            Password = "restricted",
        };

        InvoicesDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<InvoicesDbContext>()
                .UseNpgsql(restrictedConnectionString.ConnectionString)
                .Options);

        // A generous budget: proving the call returns almost immediately, well
        // under RetryDelaySeconds, is what demonstrates no retry was attempted.
        DatabaseOptions options = new() { StartupTimeoutSeconds = 30, RetryDelaySeconds = 10 };
        Stopwatch stopwatch = Stopwatch.StartNew();

        int result = await DatabaseMigrator.RunAsync(
            CreateContext, options, NullLogger.Instance, cancellationToken);

        stopwatch.Stop();

        result.Should().Be(1);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private async Task<long> CountSchemas(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @name";
        command.Parameters.AddWithValue("name", InvoicesDbContext.SchemaName);

        object? count = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt64(count, CultureInfo.InvariantCulture);
    }

    private async Task<long> CountHistoryRows(CancellationToken cancellationToken)
    {
        // EF Core places "__EFMigrationsHistory" in the connection's default schema
        // ("public") unless MigrationsHistoryTable(...) overrides it -- it is not
        // affected by InvoicesDbContext.SchemaName, which only governs entity mapping.
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM public.\"__EFMigrationsHistory\"";

        object? count = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt64(count, CultureInfo.InvariantCulture);
    }
}
