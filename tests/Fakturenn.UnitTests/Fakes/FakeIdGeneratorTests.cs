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
    public void The_real_generator_produces_no_duplicates_across_a_large_batch()
    {
        // Batch document creation depends on this. 74 random bits after the
        // timestamp make a same-millisecond collision vanishingly unlikely.
        IIdGenerator generator = new GuidV7IdGenerator();

        Guid[] ids = [.. Enumerable.Range(0, 10_000).Select(_ => generator.NewId())];

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_real_generator_produces_ids_that_sort_by_creation_time()
    {
        // Version 7 timestamps have millisecond resolution, so the calls are
        // spaced. Order within a single millisecond is unspecified by RFC 9562
        // and is not asserted here.
        IIdGenerator generator = new GuidV7IdGenerator();

        Guid first = generator.NewId();
        Thread.Sleep(5);
        Guid second = generator.NewId();
        Thread.Sleep(5);
        Guid third = generator.NewId();

        new[] { first, second, third }.Should().BeInAscendingOrder();
    }
}
