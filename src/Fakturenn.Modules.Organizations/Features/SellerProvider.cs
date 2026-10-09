using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Features;

public sealed class SellerProvider(OrganizationsDbContext db) : ISellerProvider
{
    // public Methods
    public async Task<SellerSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        Organization o = await db.Organizations.AsNoTracking()
            .SingleAsync(row => row.Id == Organization.SingletonId, cancellationToken);

        return new SellerSnapshot(
            o.LegalName, o.CountryCode, o.VatId, o.Iban, o.Currency, o.TimeZoneId, o.PaymentTermsDays,
            o.IssueDateDefault, new NumberScheme(o.NumberPrefix, o.NumberScope, o.NumberPadding, o.NumberNextValue));
    }
}
