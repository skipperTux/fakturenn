using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Contracts;
using Fakturenn.UnitTests.Fakes;

namespace Fakturenn.UnitTests.Modules;

public sealed class InvoiceIdTests
{
    [Fact]
    public void A_new_invoice_id_takes_its_value_from_the_id_generator()
    {
        var expected = Guid.Parse("0198f3a0-0000-7000-8000-000000000001");

        InvoiceId id = InvoiceId.New(new FakeIdGenerator(expected));

        id.Value.Should().Be(expected);
    }

    [Fact]
    public void An_empty_invoice_id_is_rejected()
    {
        var create = () => new InvoiceId(Guid.Empty);

        create.Should().Throw<ArgumentException>();
    }
}
