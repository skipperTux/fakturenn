using AwesomeAssertions;
using Fakturenn.Modules.Customers;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Customers.Features;
using Fakturenn.Modules.Customers.Persistence;
using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.Modules.Projects.Features;
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class CustomersAndProjectsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task A_customer_number_is_unique()
    {
        await using CustomersDbContext db = await CustomersAsync();
        SaveResult first = await new SaveCustomer(db).HandleAsync(
            new CustomerForm(null, "Example Client GmbH", "DE", "", "C-UNIQUE"), TestContext.Current.CancellationToken);

        SaveResult second = await new SaveCustomer(db).HandleAsync(
            new CustomerForm(null, "Another GmbH", "DE", "", "C-UNIQUE"), TestContext.Current.CancellationToken);

        first.Errors.Should().BeEmpty();
        second.Errors.Should().ContainSingle().Which.Should().Be(CustomerError.CustomerNumberTaken);
    }

    [Fact]
    public async Task The_customer_catalog_item_number_is_per_customer()
    {
        // Review C7: SI-9001 is one customer's name for an item, not the item's.
        await using CustomersDbContext db = await CustomersAsync();
        Guid item = Guid.CreateVersion7();
        Guid a = (await new SaveCustomer(db).HandleAsync(new CustomerForm(null, "A", "DE", "", "C-REF-A"), TestContext.Current.CancellationToken)).Id!.Value;
        Guid b = (await new SaveCustomer(db).HandleAsync(new CustomerForm(null, "B", "DE", "", "C-REF-B"), TestContext.Current.CancellationToken)).Id!.Value;

        await new SaveCatalogItemReference(db).HandleAsync(a, item, "SI-9001", TestContext.Current.CancellationToken);

        ICustomerProvider provider = new CustomerProvider(db);
        (await provider.FindCatalogItemReferenceAsync(a, item, TestContext.Current.CancellationToken)).Should().Be("SI-9001");
        (await provider.FindCatalogItemReferenceAsync(b, item, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_project_belongs_to_the_customer_it_was_created_on()
    {
        await using ProjectsDbContext db = postgres.Create<ProjectsDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Guid customer = Guid.CreateVersion7();

        Guid project = await new SaveProject(db).HandleAsync(null, customer, " PRJ-2026-083 ", TestContext.Current.CancellationToken);

        (await new ProjectProvider(db).FindAsync(project, TestContext.Current.CancellationToken))
            .Should().Be(new ProjectSnapshot(project, customer, "PRJ-2026-083"));
    }

    // private Methods
    private async Task<CustomersDbContext> CustomersAsync()
    {
        CustomersDbContext db = postgres.Create<CustomersDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return db;
    }
}
