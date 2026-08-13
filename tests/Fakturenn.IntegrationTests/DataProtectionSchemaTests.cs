using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Infrastructure.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The key ring's schema, asserted directly rather than through the application that
/// depends on it. <c>Program.cs</c> passes a <c>DataProtectionDbContext</c> factory to
/// <c>DatabaseMigrator.RunAsync</c>, so <c>--migrate</c> does apply this migration; what
/// that does not tell anyone is <i>where</i> the table landed, and a ring in the wrong
/// schema still works right up until something else owns the name.
/// <para>
/// The schema is asserted against <c>information_schema</c> rather than through EF
/// alone: an EF round-trip passes just as happily when <c>HasDefaultSchema</c> has
/// been dropped and the table has silently moved to <c>public</c>, which is exactly
/// the placement the key ring must not have.
/// </para>
/// </summary>
public sealed class DataProtectionSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task The_key_ring_table_lives_in_its_own_schema_and_round_trips_a_key()
    {
        await using DataProtectionDbContext context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables "
            + "WHERE table_schema = @schema AND table_name = @table";
        command.Parameters.AddWithValue("schema", DataProtectionDbContext.SchemaName);
        command.Parameters.AddWithValue("table", "DataProtectionKeys");

        object? count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Convert.ToInt64(count, CultureInfo.InvariantCulture).Should().Be(1);

        // The framework reads and writes the ring through this DbSet, so the columns
        // have to accept a real key element, not merely exist.
        var key = new DataProtectionKey
        {
            FriendlyName = $"key-{Guid.NewGuid():N}",
            Xml = "<key id=\"00000000-0000-0000-0000-000000000001\" />",
        };
        context.DataProtectionKeys.Add(key);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        DataProtectionKey stored = await context.DataProtectionKeys.AsNoTracking()
            .SingleAsync(k => k.FriendlyName == key.FriendlyName, TestContext.Current.CancellationToken);

        stored.Xml.Should().Be(key.Xml);
    }

    private DataProtectionDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);
}
