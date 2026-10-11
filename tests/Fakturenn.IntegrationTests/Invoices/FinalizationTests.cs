using AwesomeAssertions;
using Fakturenn.IntegrationTests.Fakes;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Features;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Invoices.Snapshots;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.IntegrationTests.Invoices;

public sealed class FinalizationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // private static readonly Fields
    private static readonly OrganizationForm _organization = OrganizationForm.Empty with
    {
        LegalName = "Example Consulting",
        CountryCode = "DE",
        VatId = "DE123456789",
        Iban = "DE89370400440532013000",
        Currency = "EUR",
        IssueDateDefault = IssueDateDefault.Today,
        NumberPrefix = "R",
        NumberScope = ResetScope.PerDay,
        NumberPadding = 1,
    };

    // public Methods
    [Fact]
    public async Task The_walking_skeleton_finalizes_with_a_hashed_snapshot()
    {
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero), 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 952.00m);

        result.Succeeded.Should().BeTrue();
        result.Number.Should().MatchRegex("^R260831[0-9]+$");
        await using InvoicesDbContext db = postgres.CreateContext();
        Invoice invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, TestContext.Current.CancellationToken);
        invoice.State.Should().Be(InvoiceState.Numbered);
        invoice.GrossAmount.Should().Be(952.00m);
        invoice.DueDate.Should().Be(new DateOnly(2026, 9, 14));
        InvoiceSnapshotRecord snapshot = await db.Snapshots.SingleAsync(s => s.InvoiceId == invoiceId, TestContext.Current.CancellationToken);
        snapshot.Sha256.Should().Equal(InvoiceSnapshotSerializer.Hash(snapshot.Content));
        InvoiceSnapshotDocument document = InvoiceSnapshotSerializer.Deserialize(snapshot.Content);
        document.Lines.Should().ContainSingle().Which.CustomerCatalogItemNumber.Should().Be("SI-9001");
        document.Seller.NumberScheme.Prefix.Should().Be("R");
    }

    [Fact]
    public async Task The_number_carries_the_issue_date_not_the_day_it_was_finalized()
    {
        // Review M9's backdating test: written on 4 September, dated 31 August.
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero), 8m);
        await EditAsync(data, invoiceId, issueDate: new DateOnly(2026, 8, 31), quantity: 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 952.00m);

        result.Number.Should().StartWith("R260831");
    }

    [Fact]
    public async Task Finalizing_one_invoice_twice_concurrently_allocates_once()
    {
        // Review S2 and Review Focus 5.
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero), 8m);

        FinalizationResult[] results = await Task.WhenAll(
            Task.Run(() => FinalizeAsync(data, invoiceId, 952.00m), TestContext.Current.CancellationToken),
            Task.Run(() => FinalizeAsync(data, invoiceId, 952.00m), TestContext.Current.CancellationToken));

        results.Should().OnlyContain(r => r.Succeeded);
        results.Select(r => r.Number).Distinct().Should().ContainSingle();
        await using InvoicesDbContext db = postgres.CreateContext();
        (await db.Snapshots.CountAsync(s => s.InvoiceId == invoiceId, TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.NumberCounters.SingleAsync(c => c.ScopeKey == "260715", TestContext.Current.CancellationToken)).Value
            .Should().Be(1, "the counter advanced once");
    }

    [Fact]
    public async Task An_edit_to_a_numbered_invoice_keeps_its_number_and_replaces_the_snapshot()
    {
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), 8m);
        FinalizationResult first = await FinalizeAsync(data, invoiceId, 952.00m);
        byte[] firstHash = await HashAsync(invoiceId);

        await EditAsync(data, invoiceId, issueDate: null, quantity: 9m);
        await using (InvoicesDbContext db = postgres.CreateContext())
        {
            // The stale figures are cleared until the user re-confirms (spec section 4).
            InvoiceView edited = (await new GetInvoice(db).HandleAsync(invoiceId, TestContext.Current.CancellationToken))!;
            edited.Gross.Should().BeNull();
            edited.Number.Should().Be(first.Number);
        }

        FinalizationResult second = await FinalizeAsync(data, invoiceId, 1071.00m);

        second.Succeeded.Should().BeTrue();
        second.Number.Should().Be(first.Number);
        (await HashAsync(invoiceId)).Should().NotEqual(firstHash);
        await using InvoicesDbContext refreshed = postgres.CreateContext();
        (await new GetInvoice(refreshed).HandleAsync(invoiceId, TestContext.Current.CancellationToken))!.Gross.Should().Be(1071.00m);
    }

    [Fact]
    public async Task A_locked_invoice_is_refused_by_preview_and_finalization_alike()
    {
        // Both paths share one resolution, so the totals a user confirms and the refusals a user
        // sees cannot disagree (review M12).
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 2, 10, 10, 0, 0, TimeSpan.Zero), 8m);
        (await FinalizeAsync(data, invoiceId, 952.00m)).Succeeded.Should().BeTrue();
        await using (InvoicesDbContext db = postgres.CreateContext())
        {
            Invoice invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId, TestContext.Current.CancellationToken);
            invoice.Lock();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var slices = data.Slices();
        await using (slices.Scope)
        {
            (await slices.Preview.HandleAsync(invoiceId, TestContext.Current.CancellationToken)).Refusal.Should().Be(FinalizationRefusal.InvoiceLocked);
        }

        (await FinalizeAsync(data, invoiceId, 952.00m)).Refusal.Should().Be(FinalizationRefusal.InvoiceLocked);
    }

    [Fact]
    public async Task A_draft_save_waits_for_a_finalization_in_flight()
    {
        // Review I1: a save that loaded before a finalization committed applied its edit after it,
        // moving the issue date under a number derived from the old one. A holds the invoice row
        // as finalization does; the save must wait for it, then see the invoice Numbered.
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero), 8m);
        await using InvoicesDbContext a = postgres.CreateContext();
        await using IDbContextTransaction finalizing = await a.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        Invoice invoice = await a.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId, TestContext.Current.CancellationToken);
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(invoice.Lines[0].Quantity, 100m, new Percentage(19m), TaxCategory.S)], "EUR");
        invoice.ApplyFinalization("R2603101", invoice.IssueDate.AddDays(14), totals, TaxCategory.S, 19m);
        await a.SaveChangesAsync(TestContext.Current.CancellationToken);

        Task save = EditAsync(data, invoiceId, issueDate: new DateOnly(2026, 3, 31), quantity: 8m);
        await postgres.WaitUntilBlockedAsync("\"Invoices\"");
        save.IsCompleted.Should().BeFalse("the save must wait for the finalization's row lock");
        await finalizing.CommitAsync(TestContext.Current.CancellationToken);

        Func<Task> saved = () => save;
        await saved.Should().ThrowAsync<InvalidOperationException>("a numbered invoice keeps its issue date");
        await using InvoicesDbContext db = postgres.CreateContext();
        (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, TestContext.Current.CancellationToken))
            .IssueDate.Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public async Task Finalization_refuses_when_the_confirmed_gross_is_stale()
    {
        // Review Focus 4 and M12.
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.Zero), 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 900.00m);

        result.Refusal.Should().Be(FinalizationRefusal.TotalsChanged);
        result.Totals!.Gross.Should().Be(952.00m, "the page shows the real totals for a second confirmation");
        result.Number.Should().BeNull();
    }

    [Fact]
    public async Task An_incomplete_seller_is_refused_by_name()
    {
        InvoiceTestData data = new(postgres, new FakeClock(new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero)));
        await data.SeedAsync(_organization with { Iban = "" });
        Guid invoiceId = await CreateFilledAsync(data, 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 952.00m);

        result.Refusal.Should().Be(FinalizationRefusal.SellerIncomplete);
    }

    // private static Methods
    private static async Task<Guid> CreateFilledAsync(InvoiceTestData data, decimal quantity)
    {
        var slices = data.Slices();
        await using (slices.Scope)
        {
            Guid id = await slices.Create.HandleAsync(data.CustomerId, TestContext.Current.CancellationToken);
            Invoice draft = await slices.Db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id, TestContext.Current.CancellationToken);
            await slices.Save.HandleAsync(
                new DraftForm(id, data.CustomerId, data.ProjectId, data.CatalogItemId, quantity, draft.IssueDate, draft.ServicePeriodStart, draft.ServicePeriodEnd),
                TestContext.Current.CancellationToken);
            return id;
        }
    }

    private static async Task EditAsync(InvoiceTestData data, Guid invoiceId, DateOnly? issueDate, decimal quantity)
    {
        var slices = data.Slices();
        await using (slices.Scope)
        {
            Invoice current = await slices.Db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, TestContext.Current.CancellationToken);
            DateOnly date = issueDate ?? current.IssueDate;
            await slices.Save.HandleAsync(
                new DraftForm(invoiceId, data.CustomerId, data.ProjectId, data.CatalogItemId, quantity, date, current.ServicePeriodStart, current.ServicePeriodEnd),
                TestContext.Current.CancellationToken);
        }
    }

    private static async Task<FinalizationResult> FinalizeAsync(InvoiceTestData data, Guid invoiceId, decimal confirmedGross)
    {
        var slices = data.Slices();
        await using (slices.Scope)
        {
            return await slices.Finalize.HandleAsync(invoiceId, confirmedGross, TestContext.Current.CancellationToken);
        }
    }

    // private Methods
    private async Task<(InvoiceTestData, Guid)> DraftAsync(DateTimeOffset now, decimal quantity)
    {
        InvoiceTestData data = new(postgres, new FakeClock(now));
        await data.SeedAsync(_organization);
        return (data, await CreateFilledAsync(data, quantity));
    }

    private async Task<byte[]> HashAsync(Guid invoiceId)
    {
        await using InvoicesDbContext db = postgres.CreateContext();
        return (await db.Snapshots.AsNoTracking().SingleAsync(s => s.InvoiceId == invoiceId, TestContext.Current.CancellationToken)).Sha256;
    }
}
