namespace Fakturenn.Modules.Invoices.Persistence;

// Not IAuditable: a sequence row written only by NumberAllocator's raw SQL, which the audit interceptor never sees.
/// <summary>One row per scope key — "260831", "2026", "continuous". Written only by <c>NumberAllocator</c>.</summary>
public sealed class NumberCounter
{
    public string ScopeKey { get; set; } = string.Empty;
    public long Value { get; set; }
}
