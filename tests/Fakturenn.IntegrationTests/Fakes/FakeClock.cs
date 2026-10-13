using Fakturenn.SharedKernel;

namespace Fakturenn.IntegrationTests.Fakes;

/// <summary>
/// A controllable clock. Prefer this over mocking <see cref="IClock"/>: the time
/// a test needs is data, not an interaction worth asserting.
/// </summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
