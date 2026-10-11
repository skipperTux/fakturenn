using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Features;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Fakturenn.IntegrationTests.Invoices;

public sealed class NumberAllocationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task The_first_allocation_in_a_new_scope_is_serialized_too()
    {
        // Review S1: SELECT ... FOR UPDATE locks nothing when the scope's row does not exist
        // yet, so two first-of-day finalizations both took number 1. The interleave is
        // forced: A allocates and holds its transaction open, B is started and must be seen
        // waiting on A's row before A commits.
        await MigrateAsync();
        const string scope = "first-race";

        await using InvoicesDbContext a = postgres.CreateContext();
        await using IDbContextTransaction aTransaction = await a.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        long first = await NumberAllocator.AllocateAsync(a, scope, start: 1, TestContext.Current.CancellationToken);

        await using InvoicesDbContext b = postgres.CreateContext();
        await using IDbContextTransaction bTransaction = await b.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        Task<long> second = NumberAllocator.AllocateAsync(b, scope, start: 1, TestContext.Current.CancellationToken);

        await WaitUntilBlockedAsync();
        second.IsCompleted.Should().BeFalse("B must wait for A's uncommitted row");

        await aTransaction.CommitAsync(TestContext.Current.CancellationToken);
        long secondValue = await second;
        await bTransaction.CommitAsync(TestContext.Current.CancellationToken);

        first.Should().Be(1);
        secondValue.Should().Be(2);
    }

    [Fact]
    public async Task A_rolled_back_allocation_returns_its_number()
    {
        await MigrateAsync();
        const string scope = "rollback";

        await using (InvoicesDbContext a = postgres.CreateContext())
        {
            await using IDbContextTransaction transaction = await a.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            (await NumberAllocator.AllocateAsync(a, scope, 1, TestContext.Current.CancellationToken)).Should().Be(1);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using InvoicesDbContext b = postgres.CreateContext();
        await using IDbContextTransaction again = await b.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        (await NumberAllocator.AllocateAsync(b, scope, 1, TestContext.Current.CancellationToken))
            .Should().Be(1, "a sequence would have burned 1; the counter row rolls back with everything else");
    }

    [Fact]
    public async Task A_fresh_scope_starts_at_the_configured_next_number()
    {
        await MigrateAsync();
        await using InvoicesDbContext db = postgres.CreateContext();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        (await NumberAllocator.AllocateAsync(db, "continuous-start", 451, TestContext.Current.CancellationToken)).Should().Be(451);
        (await NumberAllocator.AllocateAsync(db, "continuous-start", 451, TestContext.Current.CancellationToken)).Should().Be(452);
    }

    // private Methods
    private async Task MigrateAsync()
    {
        await using InvoicesDbContext db = postgres.CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private async Task WaitUntilBlockedAsync()
    {
        await using NpgsqlConnection observer = new(postgres.ConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await using NpgsqlCommand command = observer.CreateCommand();
            command.CommandText = "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%NumberCounters%'";
            long waiting = Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("B never blocked on A's row; the interleave was not forced.");
    }
}
