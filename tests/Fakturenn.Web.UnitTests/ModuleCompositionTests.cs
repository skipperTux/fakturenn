using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Catalog.Persistence;
using Fakturenn.Modules.Customers.Persistence;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Organizations.Persistence;
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Every business context carries the audit interceptor. Without it a row saves with empty
/// provenance and a 0001-01-01 timestamp, and no constraint objects (review C5).
/// </summary>
public sealed class ModuleCompositionTests
{
    // public Methods
    [Fact]
    public void The_organizations_context_records_provenance()
    {
        AssertAudited<OrganizationsDbContext>();
    }

    [Fact]
    public void The_customers_context_records_provenance()
    {
        AssertAudited<CustomersDbContext>();
    }

    [Fact]
    public void The_projects_context_records_provenance()
    {
        AssertAudited<ProjectsDbContext>();
    }

    [Fact]
    public void The_catalog_context_records_provenance()
    {
        AssertAudited<CatalogDbContext>();
    }

    [Fact]
    public void The_invoices_context_records_provenance()
    {
        AssertAudited<InvoicesDbContext>();
    }

    [Fact]
    public async Task The_invoices_context_stamps_the_principal_an_operation_runs_as()
    {
        // Wolverine registers this context's options as a singleton, so its interceptor is
        // resolved once, from the root provider -- never from the operation's scope, where
        // CircuitOperations sets the principal. Presence alone, as above, passes either way;
        // only the stamp shows whose name a finalization records (review C5).
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentPrincipal>().Principal =
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "acting@example.test")], "test"));
        InvoicesDbContext context = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        DateOnly today = new(2026, 10, 11);
        Invoice invoice = Invoice.CreateDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), projectId: null, today, today, today);
        context.Invoices.Add(invoice);

        // No database: the interceptor stamps before the connection is opened, then the save fails.
        Func<Task> save = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.Should().ThrowAsync<InvalidOperationException>();
        invoice.CreatedBy.Should().Be("acting@example.test");
    }

    // internal static Methods
    internal static void AssertAudited<TContext>()
        where TContext : DbContext
    {
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);
        using IServiceScope scope = app.Services.CreateScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();

        CoreOptionsExtension? core = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

        core!.Interceptors.Should().ContainSingle(interceptor => interceptor is AuditSaveChangesInterceptor,
            $"{typeof(TContext).Name} must stamp CreatedBy and ModifiedBy");
    }
}
