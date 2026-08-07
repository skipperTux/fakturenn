namespace Fakturenn.SharedKernel;

/// <summary>
/// Produces UUID version 7 values, which sort by creation time. Random v4 keys
/// fragment PostgreSQL B-tree indexes; time-ordered keys do not.
/// </summary>
public sealed class GuidV7IdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}
