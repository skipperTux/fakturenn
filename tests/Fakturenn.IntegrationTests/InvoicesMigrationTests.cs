using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.IntegrationTests;

public sealed class InvoicesMigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migrations_apply_to_a_clean_database()
    {
        await using InvoicesDbContext context = postgres.CreateContext();

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        IEnumerable<string> applied = await context.Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        applied.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Applying_migrations_creates_the_invoices_schema()
    {
        await using InvoicesDbContext context = postgres.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @name";
        command.Parameters.AddWithValue("name", InvoicesDbContext.SchemaName);

        object? count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Convert.ToInt64(count, CultureInfo.InvariantCulture).Should().Be(1);
    }

    [Fact]
    public async Task Applying_migrations_twice_is_idempotent()
    {
        // Migrations must work from clean and from previous states, per the
        // Definition of Done in PLAN-v0.1.md.
        await using InvoicesDbContext context = postgres.CreateContext();

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Func<Task> second = () => context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await second.Should().NotThrowAsync();
    }
}
