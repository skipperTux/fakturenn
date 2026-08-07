namespace Fakturenn.SharedKernel;

/// <summary>
/// Produces UUID version 7 values, which sort by creation time. Random v4 keys
/// fragment PostgreSQL B-tree indexes; time-ordered keys do not.
/// </summary>
/// <remarks>
/// Ids are ordered to millisecond resolution only. Order within a single
/// millisecond is unspecified, because .NET does not implement RFC 9562's
/// optional monotonic-counter methods. Never use an id as a creation-order
/// key; use an explicit timestamp or sequence for that.
/// </remarks>
public sealed class GuidV7IdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}
