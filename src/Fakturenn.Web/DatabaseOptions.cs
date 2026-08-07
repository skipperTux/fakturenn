namespace Fakturenn.Web;

/// <summary>
/// Binds the "Database" configuration section. Two independent self-healing
/// mechanisms share this section, but each property below belongs to exactly
/// one of them -- they are deliberately not unified into a single retry count,
/// because a count-based budget makes the real wait depend on how the database
/// is unavailable (an instantly-refused connection and a blackholed one burn
/// retries at very different rates). See appsettings.json for the chosen
/// defaults and the task 8 report for the reasoning behind them.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Total wall-clock budget, in seconds, for the "--migrate" entrypoint to reach the
    /// database and apply migrations. Measured from a monotonic clock (<see
    /// cref="System.Diagnostics.Stopwatch"/>), so a system-clock adjustment cannot shorten
    /// or extend it. This is the ONLY knob that bounds "--migrate"'s wait; it does not use
    /// <see cref="MaxRetries"/>.
    /// </summary>
    public int StartupTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Sleep between "--migrate" connection retry attempts, in seconds, and the cap
    /// (<c>maxRetryDelay</c>) on the runtime execution strategy's own backoff.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Scoped ONLY to the runtime EF Core execution strategy (<c>EnableRetryOnFailure</c>,
    /// requirement B) -- how many times a single database operation is retried once the
    /// application is already serving traffic. Does NOT apply to the "--migrate"
    /// entrypoint, which is bounded by <see cref="StartupTimeoutSeconds"/> instead. Kept as
    /// a separate property, with a separate lifetime, so the two are never accidentally
    /// unified again.
    /// </summary>
    public int MaxRetries { get; set; } = 5;
}
