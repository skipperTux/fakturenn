using System.Threading.RateLimiting;
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Web;

/// <summary>
/// Registers ASP.NET Core Identity, permission-based authorization and the shared
/// Data Protection key ring in the host. Nothing here reaches into a module: the
/// module owns its entities, its context and its migrations, and this is the one
/// place that composes them with the framework.
/// </summary>
public static class IdentityConfiguration
{
    public static void AddFakturennIdentity(
        this WebApplicationBuilder builder,
        string? connectionString,
        DatabaseOptions databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(databaseOptions);

        builder.Services.AddDbContext<DataProtectionDbContext>(options =>
            options.UseNpgsql(connectionString));

        // A fixed application name is what makes replicas share one ring. Without it
        // each instance derives its own, and a cookie encrypted by one cannot be read
        // by another -- sticky sessions give circuit affinity, not key sharing.
        builder.Services.AddDataProtection()
            .SetApplicationName("Fakturenn")
            .PersistKeysToDbContext<DataProtectionDbContext>();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();

        builder.Services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                    databaseOptions.MaxRetries,
                    TimeSpan.FromSeconds(databaseOptions.RetryDelaySeconds),
                    errorCodesToAdd: null))
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Defaults only. The Configure<IdentityOptions> call after this block
                // binds the "Identity" configuration section over the top, so an
                // operator can tighten or loosen the policy without a rebuild.
                //
                // These rules are known to be insufficient on their own -- Passwort1234
                // satisfies all of them. Three strength scorers were evaluated and none
                // earned a dependency in the sign-in path; see the spec's section 8.
                // The password is one factor of two, and mandatory TOTP, lockout and
                // rate limiting are what carry the weight.
                options.Password.RequiredLength = 12;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequiredUniqueChars = 4;

                // The one Identity default deliberately flipped off: requiring
                // punctuation mostly produces an exclamation mark on the end.
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager()

            // Without this line Identity keeps its stock factory, nothing writes a
            // fakturenn.permission claim, and PermissionAuthorizationHandler reads a
            // claim that is never present -- so every [Authorize(Policy = ...)]
            // endpoint answers 403, including the administrator's own page, while
            // every unit test stays green because they construct principals with the
            // claims already there. IdentityConfigurationTests resolves the factory
            // from the real host to keep that from happening silently again.
            .AddClaimsPrincipalFactory<PermissionClaimsPrincipalFactory>();

        // The policy is configuration, not code: bound over the defaults set above so a
        // deployment can tighten it without a rebuild.
        builder.Services.Configure<IdentityOptions>(builder.Configuration.GetSection("Identity"));

        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        // Identity rotates the security stamp on password and two-factor changes but
        // NOT on lockout, and the default validation interval is thirty minutes. A
        // locked user would keep a working session for half an hour. One minute also
        // bounds how stale a cookie's cached permission claims can be.
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(1));

        // SecurePolicy is SameAsRequest rather than Always because the reference Compose
        // deployment serves plain HTTP on localhost; Always would silently drop the
        // cookie and make sign-in fail with no error. TLS termination is a deployment
        // concern, documented in DEPLOYMENT-BASELINE.md.
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/account/denied";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
        });

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddAuthorization();

        // Lockout alone would make the login endpoint a user-enumeration oracle:
        // a locked account answers differently from an unknown one under load.
        //
        // Partitioned on username PLUS client IP. IP alone is useless behind a shared
        // address and a self-DoS behind a proxy; username alone lets one attacker
        // spray many accounts freely. The client IP is only meaningful because
        // forwarded-header trust is configured -- see AddForwardedHeaderTrust.
        //
        // Accepted trade-off: this limiter is in-memory per replica, so with N
        // replicas the effective limit is N x PermitLimit. Solving that needs shared
        // state this project does not otherwise require. Lockout is a database column
        // and therefore the durable control; the limiter blunts enumeration.
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("account", context =>
            {
                // Upper-cased rather than lower-cased only because CA1308 rejects the
                // other direction. The partition key needs one consistent folding, not
                // a particular one.
                string user = context.Request.HasFormContentType
                    ? context.Request.Form["email"].ToString().Trim().ToUpperInvariant()
                    : string.Empty;
                string address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{user}|{address}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
    }
}
