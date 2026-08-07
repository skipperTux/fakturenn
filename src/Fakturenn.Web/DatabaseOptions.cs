namespace Fakturenn.Web;

/// <summary>
/// Binds the "Database" configuration section. Drives both the EF Core execution
/// strategy registered on <c>InvoicesDbContext</c> (transient failures during
/// normal request handling) and the "--migrate" entrypoint's own connection
/// retry loop (a database that is not accepting connections yet). See
/// appsettings.json for the chosen defaults and the task 8 report for the
/// reasoning behind them.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public int MaxRetries { get; set; } = 5;

    public int RetryDelaySeconds { get; set; } = 5;
}
