using System.Globalization;
using Fakturenn.Web.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddNpgSql(
                connectionStringFactory: _ =>
                    builder.Configuration.GetConnectionString("Fakturenn") ?? string.Empty,
                name: "postgres",
                tags: ["ready"]);

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
