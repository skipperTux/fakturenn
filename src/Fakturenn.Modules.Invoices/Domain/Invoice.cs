using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// The invoice aggregate. Its methods are the only way to change it, and they enforce spec
/// section 4: the number and the issue date freeze at allocation, everything freezes at lock.
/// Totals and tax are columns so the detail page never reads the snapshot (review C3).
/// </summary>
public sealed class Invoice : IAuditable
{
    // private Fields
    private readonly List<InvoiceLine> _lines = [];

    // public Properties
    public Guid Id { get; private set; }
    public InvoiceState State { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly ServicePeriodStart { get; private set; }
    public DateOnly ServicePeriodEnd { get; private set; }
    public string? Number { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string? Currency { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal? VatAmount { get; private set; }
    public decimal? GrossAmount { get; private set; }
    public TaxCategory? TaxCategory { get; private set; }
    public decimal? VatRate { get; private set; }
    public IReadOnlyList<InvoiceLine> Lines => _lines.AsReadOnly();
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;

    // public static Methods
    public static Invoice CreateDraft(
        Guid id,
        Guid customerId,
        Guid? projectId,
        DateOnly issueDate,
        DateOnly servicePeriodStart,
        DateOnly servicePeriodEnd) =>
        new()
        {
            Id = id,
            State = InvoiceState.Draft,
            CustomerId = customerId,
            ProjectId = projectId,
            IssueDate = issueDate,
            ServicePeriodStart = servicePeriodStart,
            ServicePeriodEnd = servicePeriodEnd,
        };

    // public Methods
    public void Edit(Guid customerId, Guid? projectId, DateOnly issueDate, DateOnly servicePeriodStart, DateOnly servicePeriodEnd)
    {
        RefuseWhenLocked();
        if (State == InvoiceState.Numbered && issueDate != IssueDate)
        {
            throw new InvoiceChangeRefusedException("A numbered invoice keeps the issue date its number was derived from.");
        }

        bool changed = customerId != CustomerId
            || projectId != ProjectId
            || issueDate != IssueDate
            || servicePeriodStart != ServicePeriodStart
            || servicePeriodEnd != ServicePeriodEnd;
        if (changed)
        {
            ClearFigures();
        }

        CustomerId = customerId;
        ProjectId = projectId;
        IssueDate = issueDate;
        ServicePeriodStart = servicePeriodStart;
        ServicePeriodEnd = servicePeriodEnd;
    }

    public void SetSingleLine(Guid catalogItemId, Quantity quantity)
    {
        RefuseWhenLocked();
        InvoiceLine? line = _lines.SingleOrDefault();
        if (line is null || line.CatalogItemId != catalogItemId || line.Quantity != quantity)
        {
            ClearFigures();
        }

        if (line is null)
        {
            line = new InvoiceLine { Id = Guid.CreateVersion7(), InvoiceId = Id };
            _lines.Add(line);
        }

        line.CatalogItemId = catalogItemId;
        line.Quantity = quantity;
    }

    /// <summary>
    /// Records finalization's outcome. On a draft this assigns the number; on a numbered
    /// invoice it must be called with the same number and only refreshes the totals.
    /// </summary>
    public void ApplyFinalization(string number, DateOnly dueDate, InvoiceTotals totals, TaxCategory category, decimal vatRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(totals);
        RefuseWhenLocked();
        if (State == InvoiceState.Numbered && !string.Equals(Number, number, StringComparison.Ordinal))
        {
            throw new InvoiceChangeRefusedException("A numbered invoice keeps its number.");
        }

        if (totals.LineNets.Count != _lines.Count)
        {
            throw new InvalidOperationException("The totals carry a net for every line, and only for those.");
        }

        Number = number;
        DueDate = dueDate;
        Currency = totals.Currency;
        NetAmount = totals.Net;
        VatAmount = totals.Vat;
        GrossAmount = totals.Gross;
        TaxCategory = category;
        VatRate = vatRate;
        for (int index = 0; index < _lines.Count; index++)
        {
            _lines[index].NetAmount = totals.LineNets[index];
            _lines[index].TaxCategory = category;
            _lines[index].VatRate = vatRate;
        }

        State = InvoiceState.Numbered;
    }

    public void Lock()
    {
        if (State != InvoiceState.Numbered)
        {
            throw new InvoiceChangeRefusedException("Only a numbered invoice can be locked.");
        }

        State = InvoiceState.Locked;
    }

    // private Methods
    // A changed numbered invoice no longer matches its figures; the page autosaves, so an
    // unchanged save must keep them. Finalization under the same number repopulates them.
    private void ClearFigures()
    {
        NetAmount = null;
        VatAmount = null;
        GrossAmount = null;
        foreach (InvoiceLine line in _lines)
        {
            line.NetAmount = null;
            line.TaxCategory = null;
            line.VatRate = null;
        }
    }

    private void RefuseWhenLocked()
    {
        if (State == InvoiceState.Locked)
        {
            throw new InvoiceChangeRefusedException("A locked invoice never changes; correct it with an invoice correction.");
        }
    }
}
