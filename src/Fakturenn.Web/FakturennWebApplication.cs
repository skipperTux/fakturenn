using System.Globalization;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;
using Serilog;

namespace Fakturenn.Web;

public static class FakturennWebApplication
{
    private static readonly string[] SupportedCultures = ["en", "de"];

    /// <summary>
    /// Builds the application without starting it, so tests can host it on a
    /// real socket instead of reimplementing composition.
    /// </summary>
    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration));

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(SupportedCultures[0]);
            options.AddSupportedCultures(SupportedCultures);
            options.AddSupportedUICultures(SupportedCultures);
        });

        // The liveness probe must not depend on PostgreSQL: a database outage
        // should mark the instance unready, not have Kubernetes restart it.
        string? connectionString = builder.Configuration.GetConnectionString("Fakturenn");

        IHealthChecksBuilder healthChecks = builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Readiness must report, never throw. AddNpgSql's own guard throws on a
            // null/empty connection string, which would turn "not configured yet"
            // into an unhandled 500 instead of the 503 a probe expects.
            healthChecks.AddCheck(
                "postgres",
                () => HealthCheckResult.Unhealthy("No connection string is configured for 'Fakturenn'."),
                tags: ["ready"]);
        }
        else
        {
            // A short explicit timeout keeps readiness answering well inside a
            // probe interval even when the database is unreachable rather than
            // hanging; retrying belongs to the Task 8 migration entrypoint, not
            // to a probe that must answer fast every time it is called.
            healthChecks.AddNpgSql(
                connectionString,
                name: "postgres",
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(3));
        }

        builder.Services.Configure<DatabaseOptions>(
            builder.Configuration.GetSection(DatabaseOptions.SectionName));
        DatabaseOptions databaseOptions =
            builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                ?? new DatabaseOptions();

        // EnableRetryOnFailure covers transient failures during normal operation, once the
        // application is already serving traffic (e.g. a brief network blip, a PostgreSQL
        // failover). It is deliberately NOT used by the "--migrate" entrypoint's own
        // DbContext -- see DatabaseMigrator's remarks for why nesting the two would multiply
        // the total wait.
        builder.Services.AddDbContext<InvoicesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                databaseOptions.MaxRetries,
                TimeSpan.FromSeconds(databaseOptions.RetryDelaySeconds),
                errorCodesToAdd: null)));

        WebApplication app = builder.Build();

        app.UseSerilogRequestLogging();
        app.UseRequestLocalization();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        });

        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return app;
    }
}
