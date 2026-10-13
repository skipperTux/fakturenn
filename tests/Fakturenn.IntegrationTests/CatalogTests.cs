using AwesomeAssertions;
using Fakturenn.Modules.Catalog;
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Catalog.Features;
using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class CatalogTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task An_item_round_trips_with_its_precision()
    {
        await using CatalogDbContext db = postgres.Create<CatalogDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        CatalogSaveResult saved = await new SaveCatalogItem(db).HandleAsync(
            new CatalogItemForm(null, "DEV-BACKEND", "Backend development", "hur", 104.6m, "eur", 19m),
            TestContext.Current.CancellationToken);
        CatalogItemSnapshot? item = await new CatalogItemProvider(db).FindAsync(saved.Id!.Value, TestContext.Current.CancellationToken);

        item.Should().Be(new CatalogItemSnapshot(saved.Id.Value, "DEV-BACKEND", "Backend development", "HUR", 104.6m, "EUR", 19m));
    }
}
