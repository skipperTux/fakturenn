using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.Fakes;

/// <summary>
/// Hands out a fixed sequence of ids and throws when exhausted, so a test that
/// silently starts allocating more ids than it declared fails instead of drifting.
/// </summary>
public sealed class FakeIdGenerator(params Guid[] ids) : IIdGenerator
{
    private readonly Queue<Guid> _remaining = new(ids);

    public Guid NewId() =>
        _remaining.Count > 0
            ? _remaining.Dequeue()
            : throw new InvalidOperationException(
                $"FakeIdGenerator was primed with {ids.Length} id(s) and has run out.");
}
