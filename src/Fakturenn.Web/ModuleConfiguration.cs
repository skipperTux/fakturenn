using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Features;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Web;

/// <summary>
/// Registers the business modules: each context with the retry policy and the audit
/// interceptor, none enrolled with Wolverine (nothing publishes in Stage 1), and the
/// slices and providers the pages and finalization resolve.
/// </summary>
public static class ModuleConfiguration
{
    // public static Methods
    public static void AddFakturennModules(
        this WebApplicationBuilder builder,
        string? connectionString,
        DatabaseOptions databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(databaseOptions);

        AddModuleContext<OrganizationsDbContext>(builder.Services, connectionString, databaseOptions);
        builder.Services.AddScoped<GetOrganization>();
        builder.Services.AddScoped<SaveOrganization>();
        builder.Services.AddScoped<ISellerProvider, SellerProvider>();
    }

    // private static Methods
    private static void AddModuleContext<TContext>(
        IServiceCollection services,
        string? connectionString,
        DatabaseOptions databaseOptions)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                    databaseOptions.MaxRetries,
                    TimeSpan.FromSeconds(databaseOptions.RetryDelaySeconds),
                    errorCodesToAdd: null))
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));
}
