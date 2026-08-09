using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web;
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

    // One factory per module-owned DbContext. Today there is only the Invoices module;
    // a future module with its own DbContext adds one more entry here rather than a
    // discovery mechanism -- see DatabaseMigrator's remarks for why the signature takes
    // a list instead of a single hard-coded context.
    Func<DbContext>[] createMigrationContexts = [CreateMigrationContext];

    int exitCode = await DatabaseMigrator.RunAsync(createMigrationContexts, databaseOptions, migrationLogger);

    Environment.ExitCode = exitCode;
    return;
}

await app.RunAsync();
