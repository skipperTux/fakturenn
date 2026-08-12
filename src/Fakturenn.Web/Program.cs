using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

WebApplication app = FakturennWebApplication.Build(args);

// Migrations never run as a side effect of serving traffic. DEPLOYMENT-BASELINE.md
// requires an explicit migration Job, and auto-migrating on startup races when
// more than one replica starts at once.
if (args.Contains("--migrate"))
{
    string? connectionString = app.Configuration.GetConnectionString("Fakturenn");
    DatabaseOptions databaseOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    ILogger migrationLogger = app.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Fakturenn.Web.Migrate");

    // A dedicated, non-retrying context: the runtime InvoicesDbContext registered in
    // FakturennWebApplication.Build has EnableRetryOnFailure for requirement B, and
    // DatabaseMigrator.RunAsync retries around whatever context it is given. Reusing
    // the retrying context here would nest both, turning one wall-clock startup budget
    // into two independently enforced ones -- see DatabaseMigrator's remarks.
    //
    // ApplyDefaultConnectTimeout caps a single connect attempt (Npgsql defaults to 15s)
    // unless the operator already set one explicitly, so a blackholed address cannot
    // burn most of a short startup budget inside one hung connect.
    string migrationConnectionString = DatabaseMigrator.ApplyDefaultConnectTimeout(connectionString);

    InvoicesDbContext CreateMigrationContext() =>
        new(new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseNpgsql(migrationConnectionString)
            .Options);

    // IdentityDbContext demands an IDataProtectionProvider for the value converter on
    // AspNetUserTokens.Value. Migrating only builds the model -- the converter's
    // expressions are compiled, never invoked -- so an ephemeral file-backed provider is
    // enough, and it deliberately does NOT reach into app.Services: the registered
    // provider persists its ring through DataProtectionDbContext, whose table may not
    // exist yet at this point.
    IdentityDbContext CreateIdentityMigrationContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(migrationConnectionString)
                .Options,
            DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(Path.GetTempPath(), "fakturenn-migrate"))));

    DataProtectionDbContext CreateDataProtectionMigrationContext() =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql(migrationConnectionString)
            .Options);

    // One factory per context that owns migrations. A future module with its own
    // DbContext adds one more entry here rather than a discovery mechanism -- see
    // DatabaseMigrator's remarks for why the signature takes a list instead of a single
    // hard-coded context.
    //
    // The Data Protection context is listed first for readability, not correctness: the
    // order was measured by putting Identity first against a clean database, and all
    // three migrations still applied. Nothing in a migration protects or unprotects a
    // value, and the provider above is file-backed anyway, so no ordering constraint
    // between these three exists today.
    Func<DbContext>[] createMigrationContexts =
    [
        CreateDataProtectionMigrationContext,
        CreateIdentityMigrationContext,
        CreateMigrationContext,
    ];

    int exitCode = await DatabaseMigrator.RunAsync(createMigrationContexts, databaseOptions, migrationLogger);

    // Seeding runs here, not at application startup. Startup seeding races on the
    // unique role-name index when more than one replica starts together, and
    // --migrate already runs exactly once by design.
    //
    // RoleSeeder.SeedAsync is a re-sync, not create-if-absent: an installation
    // upgraded to a version that defines a new permission constant gains the grant.
    // The catalogue validator catches stored permissions the code does not define;
    // nothing else would catch permissions the code defines and the database lacks.
    //
    // The registered context is used rather than the migration one above, so the audit
    // interceptor stamps the seeded rows. No request is in flight, so
    // ICurrentUserAccessor reports nobody and AuditStamp resolves the actor to
    // "system" -- which is the truth about who created these rows.
    if (exitCode == 0)
    {
        await using AsyncServiceScope seedScope = app.Services.CreateAsyncScope();
        IdentityDbContext seedContext =
            seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await RoleSeeder.SeedAsync(seedContext, CancellationToken.None);
        MigrationSeedLog.SeededSystemRoles(migrationLogger);

        // A stored permission the code does not define grants nothing, and granting
        // nothing looks exactly like a working configuration until someone is denied
        // access they believe they have. Blocking the deployment here rather than at
        // startup keeps the application's deliberate ability to start without a
        // database: a startup-time query would have taken that away.
        List<string> stored = await seedContext.RolePermissions
            .AsNoTracking()
            .Select(rolePermission => rolePermission.Permission)
            .Distinct()
            .ToListAsync(CancellationToken.None);

        IReadOnlyList<string> unknown = PermissionCatalogValidator.FindUnknownPermissions(stored);
        if (unknown.Count > 0)
        {
            // Joined into a local: CA1873 is an error here, and an IsEnabled guard
            // does not satisfy it.
            string offending = string.Join(", ", unknown);
            MigrationSeedLog.UnknownPermissionsStored(migrationLogger, offending);
            exitCode = 1;
        }
    }

    Environment.ExitCode = exitCode;
    return;
}

await app.RunAsync();

/// <summary>
/// Logging for the seeding step of the <c>--migrate</c> entrypoint. A source-generated
/// delegate rather than a direct <c>LogInformation</c> call because CA1848 is an error
/// in this repository.
/// </summary>
internal static partial class MigrationSeedLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded system roles.")]
    public static partial void SeededSystemRoles(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Stored role permissions that this version does not define: {OffendingPermissions}. "
            + "They grant nothing. Refusing to complete the migration.")]
    public static partial void UnknownPermissionsStored(ILogger logger, string offendingPermissions);
}
