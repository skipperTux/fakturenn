namespace Fakturenn.SharedKernel;

/// <summary>
/// Supplies the current instant. Everything that needs the time takes this,
/// so tests can drive due dates and reminder levels deterministically.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
