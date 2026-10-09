using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Features;

public sealed class GetOrganization(OrganizationsDbContext db)
{
    // public Methods
    public async Task<OrganizationForm> HandleAsync(CancellationToken cancellationToken)
    {
        Organization organization = await db.Organizations.AsNoTracking()
            .SingleAsync(o => o.Id == Organization.SingletonId, cancellationToken);

        return new OrganizationForm(
            organization.LegalName, organization.CountryCode, organization.VatId, organization.Iban,
            organization.Currency, organization.TimeZoneId, organization.PaymentTermsDays,
            organization.IssueDateDefault, organization.NumberPrefix, organization.NumberScope,
            organization.NumberPadding, organization.NumberNextValue);
    }
}
