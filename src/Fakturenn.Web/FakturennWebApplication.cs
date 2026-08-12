using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;
using Serilog;
using Serilog.Extensions.Logging;

namespace Fakturenn.Web;

public static class FakturennWebApplication
{
    private static readonly string[] _supportedCultures = ["en", "de"];

    /// <summary>
    /// Builds the application without starting it, so tests can host it on a
    /// real socket instead of reimplementing composition.
    /// </summary>
    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration));

        // Forwarded-header trust is parsed eagerly, which means it needs a logger before
        // the host exists -- and nothing on WebApplicationBuilder exposes one. This is
        // Serilog's documented two-stage initialisation: the bootstrap logger reads the
        // same "Serilog" configuration section the host will, and UseSerilog above
        // replaces it at Build(). One pipeline observed earlier, not a second one.
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateBootstrapLogger();

        using (SerilogLoggerFactory bootstrapLoggerFactory = new())
        {
            builder.AddForwardedHeaderTrust(
                bootstrapLoggerFactory.CreateLogger(nameof(ForwardedHeaderTrust)));
        }

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(_supportedCultures[0]);
            options.AddSupportedCultures(_supportedCultures);
            options.AddSupportedUICultures(_supportedCultures);
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

        builder.AddFakturennIdentity(connectionString, databaseOptions);

        WebApplication app = builder.Build();

        // First in the pipeline, and in this order. UseRfc7239Forwarded only synthesises
        // X-Forwarded-* headers; UseForwardedHeaders is what evaluates trust and rewrites
        // Request.Scheme and Connection.RemoteIpAddress, so it must run immediately after.
        // Everything downstream that reads either -- the cookie's Secure decision, the
        // account rate limiter's client-IP partition, request logging -- would otherwise
        // see the proxy's address and the proxy-to-app scheme.
        app.UseRfc7239Forwarded();
        app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            // Production only. A Strict-Transport-Security header served over plain
            // HTTP from a local run poisons the browser for localhost across every
            // other project on the machine, and it cannot be cleared per-site.
            app.UseHsts();
        }

        app.Use(async (context, next) =>
        {
            // Blazor Server needs its own script and the WebSocket back to the origin.
            // 'unsafe-inline' for styles is required by MudBlazor's component styles;
            // scripts do NOT get it, which is the half that matters for injection.
            //
            // This policy is unproven until Task 15's journey exercises it. Do not tune
            // it by clicking around, and do not widen it to 'unsafe-eval' or an inline
            // hash without recording why here.
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; "
                + "script-src 'self'; "
                + "style-src 'self' 'unsafe-inline'; "
                + "img-src 'self' data:; "
                + "font-src 'self'; "
                + "connect-src 'self' ws: wss:; "
                + "frame-ancestors 'none'; "
                + "base-uri 'self'; "
                + "form-action 'self'";

            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";

            await next();
        });

        app.UseSerilogRequestLogging();
        app.UseRequestLocalization();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

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
