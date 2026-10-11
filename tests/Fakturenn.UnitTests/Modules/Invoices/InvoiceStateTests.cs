using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;

namespace Fakturenn.UnitTests.Modules.Invoices;

/// <summary>Spec section 4's table, as the aggregate enforces it (review C4).</summary>
public sealed class InvoiceStateTests
{
    // private static readonly Fields
    private static readonly DateOnly _issue = new(2026, 8, 31);
    private static readonly InvoiceTotals _totals = InvoiceCalculator.Calculate([new PricedLine(8m, 100m, 19m, TaxCategory.S)], "EUR");

    // public Methods
    [Fact]
    public void A_draft_accepts_every_change()
    {
        Invoice invoice = Draft();

        invoice.Edit(Guid.CreateVersion7(), null, _issue.AddDays(1), _issue, _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), 4m);

        invoice.IssueDate.Should().Be(_issue.AddDays(1));
        invoice.Lines.Should().ContainSingle().Which.Quantity.Should().Be(4m);
    }

    [Fact]
    public void A_numbered_invoice_keeps_its_issue_date()
    {
        Invoice invoice = Numbered();

        Action act = () => invoice.Edit(invoice.CustomerId, null, _issue.AddDays(1), _issue, _issue);

        act.Should().Throw<InvalidOperationException>().WithMessage("*issue date*");
    }

    [Fact]
    public void A_numbered_invoice_accepts_a_data_change_under_the_same_number()
    {
        Invoice invoice = Numbered();
        Guid otherCustomer = Guid.CreateVersion7();

        invoice.Edit(otherCustomer, null, _issue, _issue, _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), 9m);

        invoice.CustomerId.Should().Be(otherCustomer);
        invoice.Number.Should().Be("R2608311");
    }

    [Fact]
    public void A_numbered_invoice_cannot_be_given_another_number()
    {
        Invoice invoice = Numbered();

        Action act = () => invoice.ApplyFinalization("R2608312", _issue, _totals, TaxCategory.S, 19m);

        act.Should().Throw<InvalidOperationException>().WithMessage("*number*");
    }

    [Fact]
    public void A_locked_invoice_accepts_nothing()
    {
        Invoice invoice = Numbered();
        invoice.Lock();

        Action edit = () => invoice.Edit(invoice.CustomerId, null, _issue, _issue, _issue);
        Action line = () => invoice.SetSingleLine(Guid.CreateVersion7(), 1m);
        Action renumber = () => invoice.ApplyFinalization("R2608311", _issue, _totals, TaxCategory.S, 19m);

        edit.Should().Throw<InvalidOperationException>();
        line.Should().Throw<InvalidOperationException>();
        renumber.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Only_a_numbered_invoice_locks()
    {
        Action act = () => Draft().Lock();

        act.Should().Throw<InvalidOperationException>();
    }

    // private static Methods
    private static Invoice Draft()
    {
        Invoice invoice = Invoice.CreateDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), null, _issue, new DateOnly(2026, 8, 1), _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), 8m);
        return invoice;
    }

    private static Invoice Numbered()
    {
        Invoice invoice = Draft();
        invoice.ApplyFinalization("R2608311", _issue.AddDays(14), _totals, TaxCategory.S, 19m);
        return invoice;
    }
}
