using AwesomeAssertions;
using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Catalog.Persistence;
using Fakturenn.Modules.Customers.Persistence;
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
