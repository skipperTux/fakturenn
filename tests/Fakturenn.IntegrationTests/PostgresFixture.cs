using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// A real PostgreSQL instance per test class. SPEC-v0.1.md section 10 requires
/// real infrastructure through Testcontainers rather than an in-memory provider,
/// because schemas, sequences and concurrency behaviour are the point.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("fakturenn")
        .WithUsername("fakturenn")
        .WithPassword("fakturenn")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public InvoicesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
}
