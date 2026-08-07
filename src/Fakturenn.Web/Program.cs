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
    // the retrying context here would nest both, multiplying MaxRetries * MaxRetries
    // into a surprisingly long total wait -- see DatabaseMigrator's remarks.
    InvoicesDbContext CreateMigrationContext() =>
        new(new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    int exitCode = await DatabaseMigrator.RunAsync(CreateMigrationContext, databaseOptions, migrationLogger);

    Environment.ExitCode = exitCode;
    return;
}

await app.RunAsync();
