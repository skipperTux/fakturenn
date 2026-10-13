using AwesomeAssertions;
using Fakturenn.Modules.Catalog;
using Fakturenn.Modules.Catalog.Features;
using Fakturenn.Modules.Catalog.Persistence;
using Fakturenn.Modules.Customers;
using Fakturenn.Modules.Customers.Features;
using Fakturenn.Modules.Customers.Persistence;
using Fakturenn.Modules.Invoices.Features;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Features;
using Fakturenn.Modules.Organizations.Persistence;
using Fakturenn.Modules.Projects.Features;
using Fakturenn.Modules.Projects.Persistence;
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests.Invoices;

/// <summary>
/// The walking skeleton's fixed example, persisted. Each call to <see cref="Slices"/> uses
/// fresh contexts, which is how CircuitOperations runs them in the host.
/// </summary>
internal sealed class InvoiceTestData(PostgresFixture postgres, IClock clock)
{
    // public Properties
    public Guid CustomerId { get; private set; }
    public string CustomerNumber { get; private set; } = string.Empty;
    public Guid ProjectId { get; private set; }
    public Guid CatalogItemId { get; private set; }

    // public Methods
    public async Task SeedAsync(OrganizationForm organization)
    {
        await using OrganizationsDbContext orgs = postgres.Create<OrganizationsDbContext>(o => new(o));
        await using CustomersDbContext customers = postgres.Create<CustomersDbContext>(o => new(o));
        await using ProjectsDbContext projects = postgres.Create<ProjectsDbContext>(o => new(o));
        await using CatalogDbContext catalog = postgres.Create<CatalogDbContext>(o => new(o));
        await using InvoicesDbContext invoices = postgres.CreateContext();
        foreach (DbContext db in new DbContext[] { orgs, customers, projects, catalog, invoices })
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await OrganizationSeeder.SeedAsync(orgs, TestContext.Current.CancellationToken);
        (await new SaveOrganization(orgs, new NumberingStatus(invoices)).HandleAsync(organization, TestContext.Current.CancellationToken))
            .Should().BeEmpty();
        // Not CreateVersion7: its leading hex digits are the timestamp, the same for a minute.
        string suffix = Guid.NewGuid().ToString("N")[..8];
        CustomerNumber = $"C-{suffix}";
        CustomerId = (await new SaveCustomer(customers).HandleAsync(
            new CustomerForm(null, "Example Client GmbH", "DE", "", CustomerNumber), TestContext.Current.CancellationToken)).Id!.Value;
        ProjectId = await new SaveProject(projects).HandleAsync(null, CustomerId, "PRJ-2026-083", TestContext.Current.CancellationToken);
        CatalogItemId = (await new SaveCatalogItem(catalog).HandleAsync(
            new CatalogItemForm(null, $"DEV-{suffix}", "Backend development", "HUR", 100m, "EUR", 19m), TestContext.Current.CancellationToken)).Id!.Value;
        await new SaveCatalogItemReference(customers).HandleAsync(CustomerId, CatalogItemId, "SI-9001", TestContext.Current.CancellationToken);
    }

    public (CreateDraft Create, SaveDraft Save, PreviewInvoice Preview, FinalizeInvoice Finalize, InvoicesDbContext Db, IAsyncDisposable Scope) Slices()
    {
        OrganizationsDbContext orgs = postgres.Create<OrganizationsDbContext>(o => new(o));
        CustomersDbContext customers = postgres.Create<CustomersDbContext>(o => new(o));
        ProjectsDbContext projects = postgres.Create<ProjectsDbContext>(o => new(o));
        CatalogDbContext catalog = postgres.Create<CatalogDbContext>(o => new(o));
        InvoicesDbContext invoices = postgres.CreateContext();
        InvoiceResolution resolution = new(new SellerProvider(orgs), new CustomerProvider(customers), new ProjectProvider(projects), new CatalogItemProvider(catalog));

        return (new CreateDraft(invoices, new SellerProvider(orgs), clock), new SaveDraft(invoices), new PreviewInvoice(invoices, resolution),
            new FinalizeInvoice(invoices, resolution), invoices, new Disposer(orgs, customers, projects, catalog, invoices));
    }

    // private Types
    private sealed class Disposer(params DbContext[] contexts) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (DbContext context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }
}
