using AwesomeAssertions;
using Fakturenn.Modules.Identity.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class UserIdTests
{
    [Fact]
    public void A_new_user_id_takes_its_value_from_the_id_generator()
    {
        var expected = Guid.Parse("0198f3a0-0000-7000-8000-00000000000a");
        IIdGenerator generator = new StubIdGenerator(expected);

        UserId id = UserId.New(generator);

        id.Value.Should().Be(expected);
    }

    [Fact]
    public void An_empty_user_id_is_rejected()
    {
        var create = () => new UserId(Guid.Empty);

        create.Should().Throw<ArgumentException>();
    }

    private sealed class StubIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
