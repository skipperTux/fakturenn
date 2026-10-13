using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.Modules.Invoices;

/// <summary>Spec section 4's table, as the aggregate enforces it (review C4).</summary>
public sealed class InvoiceStateTests
{
    // private static readonly Fields
    private static readonly DateOnly _issue = new(2026, 8, 31);
    private static readonly InvoiceTotals _totals = InvoiceCalculator.Calculate([new PricedLine(new Quantity(8m), 100m, new Percentage(19m), TaxCategory.S)], "EUR");

    // public Methods
    [Fact]
    public void A_draft_accepts_every_change()
    {
        Invoice invoice = Draft();

        invoice.Edit(Guid.CreateVersion7(), null, _issue.AddDays(1), _issue, _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), new Quantity(4m));

        invoice.IssueDate.Should().Be(_issue.AddDays(1));
        invoice.Lines.Should().ContainSingle().Which.Quantity.Should().Be(new Quantity(4m));
    }

    [Fact]
    public void A_numbered_invoice_keeps_its_issue_date()
    {
        Invoice invoice = Numbered();

        Action act = () => invoice.Edit(invoice.CustomerId, null, _issue.AddDays(1), _issue, _issue);

        act.Should().Throw<InvoiceChangeRefusedException>().WithMessage("*issue date*");
    }

    [Fact]
    public void A_numbered_invoice_accepts_a_data_change_under_the_same_number()
    {
        Invoice invoice = Numbered();
        Guid otherCustomer = Guid.CreateVersion7();

        invoice.Edit(otherCustomer, null, _issue, _issue, _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), new Quantity(9m));

        invoice.CustomerId.Should().Be(otherCustomer);
        invoice.Number.Should().Be("R2608311");
    }

    [Fact]
    public void A_numbered_invoice_cannot_be_given_another_number()
    {
        Invoice invoice = Numbered();

        Action act = () => invoice.ApplyFinalization("R2608312", _issue, _totals, TaxCategory.S, 19m);

        act.Should().Throw<InvoiceChangeRefusedException>().WithMessage("*number*");
    }

    [Fact]
    public void A_locked_invoice_accepts_nothing()
    {
        Invoice invoice = Numbered();
        invoice.Lock();

        Action edit = () => invoice.Edit(invoice.CustomerId, null, _issue, _issue, _issue);
        Action line = () => invoice.SetSingleLine(Guid.CreateVersion7(), new Quantity(1m));
        Action renumber = () => invoice.ApplyFinalization("R2608311", _issue, _totals, TaxCategory.S, 19m);

        edit.Should().Throw<InvoiceChangeRefusedException>();
        line.Should().Throw<InvoiceChangeRefusedException>();
        renumber.Should().Throw<InvoiceChangeRefusedException>();
    }

    [Fact]
    public void A_data_change_on_a_numbered_invoice_clears_the_figures_but_not_the_number()
    {
        Invoice invoice = Numbered();

        invoice.SetSingleLine(invoice.Lines[0].CatalogItemId, new Quantity(9m));

        invoice.NetAmount.Should().BeNull();
        invoice.VatAmount.Should().BeNull();
        invoice.GrossAmount.Should().BeNull();
        invoice.Lines[0].NetAmount.Should().BeNull();
        invoice.Lines[0].TaxCategory.Should().BeNull();
        invoice.Lines[0].VatRate.Should().BeNull();
        invoice.TaxCategory.Should().BeNull();
        invoice.VatRate.Should().BeNull();
        invoice.DueDate.Should().BeNull("finalization derives it, and finalization repopulates it");
        invoice.Number.Should().Be("R2608311");
        invoice.IssueDate.Should().Be(_issue);
    }

    [Fact]
    public void An_edit_on_a_numbered_invoice_clears_the_figures()
    {
        Invoice invoice = Numbered();

        invoice.Edit(invoice.CustomerId, null, _issue, _issue, _issue);

        invoice.GrossAmount.Should().BeNull();
        invoice.TaxCategory.Should().BeNull();
        invoice.VatRate.Should().BeNull();
        invoice.DueDate.Should().BeNull();
    }

    [Fact]
    public void A_save_that_changes_nothing_keeps_the_figures()
    {
        Invoice invoice = Numbered();

        invoice.Edit(invoice.CustomerId, invoice.ProjectId, invoice.IssueDate, invoice.ServicePeriodStart, invoice.ServicePeriodEnd);
        invoice.SetSingleLine(invoice.Lines[0].CatalogItemId, invoice.Lines[0].Quantity);

        invoice.GrossAmount.Should().Be(952.00m);
        invoice.Lines[0].NetAmount.Should().Be(800.00m);
    }

    [Fact]
    public void Refinalizing_under_the_same_number_repopulates_the_figures()
    {
        Invoice invoice = Numbered();
        invoice.SetSingleLine(invoice.Lines[0].CatalogItemId, new Quantity(9m));
        InvoiceTotals nine = InvoiceCalculator.Calculate([new PricedLine(new Quantity(9m), 100m, new Percentage(19m), TaxCategory.S)], "EUR");

        invoice.ApplyFinalization("R2608311", _issue.AddDays(14), nine, TaxCategory.S, 19m);

        invoice.NetAmount.Should().Be(900.00m);
        invoice.GrossAmount.Should().Be(1071.00m);
        invoice.Lines[0].NetAmount.Should().Be(900.00m);
        invoice.Lines[0].VatRate.Should().Be(19m);
        invoice.TaxCategory.Should().Be(TaxCategory.S);
        invoice.VatRate.Should().Be(19m);
        invoice.DueDate.Should().Be(_issue.AddDays(14));
    }

    [Fact]
    public void Totals_that_do_not_match_the_lines_are_refused_before_anything_changes()
    {
        Invoice invoice = Draft();
        InvoiceTotals twoLines = InvoiceCalculator.Calculate(
            [new PricedLine(new Quantity(1m), 1m, new Percentage(19m), TaxCategory.S), new PricedLine(new Quantity(1m), 1m, new Percentage(19m), TaxCategory.S)], "EUR");

        Action act = () => invoice.ApplyFinalization("R2608311", _issue, twoLines, TaxCategory.S, 19m);

        act.Should().Throw<InvalidOperationException>();
        invoice.Number.Should().BeNull();
        invoice.State.Should().Be(InvoiceState.Draft);
    }

    [Fact]
    public void Only_a_numbered_invoice_locks()
    {
        Action act = () => Draft().Lock();

        act.Should().Throw<InvoiceChangeRefusedException>();
    }

    // private static Methods
    private static Invoice Draft()
    {
        Invoice invoice = Invoice.CreateDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), null, _issue, new DateOnly(2026, 8, 1), _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), new Quantity(8m));
        return invoice;
    }

    private static Invoice Numbered()
    {
        Invoice invoice = Draft();
        invoice.ApplyFinalization("R2608311", _issue.AddDays(14), _totals, TaxCategory.S, 19m);
        return invoice;
    }
}
