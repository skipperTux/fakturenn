using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class CreateDraft(InvoicesDbContext db, ISellerProvider sellers, IClock clock)
{
    // public Methods
    /// <summary>The issue date is taken once, here, in the organization's time zone (review M1).</summary>
    public async Task<Guid> HandleAsync(Guid customerId, CancellationToken cancellationToken)
    {
        SellerSnapshot seller = await sellers.GetAsync(cancellationToken);
        DateOnly issueDate = IssueDateRule.DefaultFor(seller.IssueDateDefault, clock.UtcNow, seller.TimeZoneId);
        (DateOnly start, DateOnly end) = IssueDateRule.ServicePeriodFor(issueDate);

        Invoice invoice = Invoice.CreateDraft(Guid.CreateVersion7(), customerId, projectId: null, issueDate, start, end);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice.Id;
    }
}
