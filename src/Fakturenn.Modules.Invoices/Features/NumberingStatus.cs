using Fakturenn.Modules.Invoices.Contracts;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class NumberingStatus(InvoicesDbContext db) : INumberingStatus
{
    // public Methods
    public Task<bool> AnyNumberAllocatedAsync(CancellationToken cancellationToken) =>
        db.NumberCounters.AnyAsync(cancellationToken);
}
