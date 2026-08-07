using AwesomeAssertions;
using Fakturenn.SharedKernel;
using Fakturenn.UnitTests.Fakes;

namespace Fakturenn.UnitTests.FakeTests;

public sealed class FakeClockTests
{
    [Fact]
    public void The_fake_clock_returns_the_time_it_was_given()
    {
        var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        IClock clock = new FakeClock(now);

        clock.UtcNow.Should().Be(now);
    }

    [Fact]
    public void Advancing_the_fake_clock_moves_time_forward()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));

        clock.Advance(TimeSpan.FromDays(30));

        clock.UtcNow.Should().Be(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
    }
}
