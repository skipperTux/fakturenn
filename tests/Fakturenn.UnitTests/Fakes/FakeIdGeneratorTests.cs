using AwesomeAssertions;
using Fakturenn.SharedKernel;
using Fakturenn.UnitTests.Fakes;

namespace Fakturenn.UnitTests.FakeTests;

public sealed class FakeIdGeneratorTests
{
    [Fact]
    public void The_fake_generator_hands_out_the_ids_it_was_given_in_order()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        IIdGenerator generator = new FakeIdGenerator(first, second);

        generator.NewId().Should().Be(first);
        generator.NewId().Should().Be(second);
    }

    [Fact]
    public void Asking_for_more_ids_than_were_supplied_throws_rather_than_repeating()
    {
        IIdGenerator generator = new FakeIdGenerator(Guid.Empty);
        generator.NewId();

        var next = () => generator.NewId();

        next.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_real_generator_produces_distinct_sortable_version_seven_ids()
    {
        IIdGenerator generator = new GuidV7IdGenerator();

        Guid[] ids = [generator.NewId(), generator.NewId(), generator.NewId()];

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeInAscendingOrder();
    }
}
