using Fakturenn.Modules.Invoices.Contracts;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Features;

public sealed class SaveOrganization(OrganizationsDbContext db, INumberingStatus numbering)
{
    // public Methods
    public async Task<IReadOnlyList<OrganizationError>> HandleAsync(
        OrganizationForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        OrganizationForm normalized = form with
        {
            LegalName = form.LegalName.Trim(),
            CountryCode = form.CountryCode.Trim().ToUpperInvariant(),
            VatId = form.VatId.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            Iban = OrganizationRules.NormalizeIban(form.Iban),
            Currency = form.Currency.Trim().ToUpperInvariant(),
            NumberPrefix = form.NumberPrefix.Trim(),
        };

        IReadOnlyList<OrganizationError> errors = OrganizationRules.Validate(normalized);
        if (errors.Count > 0)
        {
            return errors;
        }

        Organization organization = await db.Organizations
            .SingleAsync(o => o.Id == Organization.SingletonId, cancellationToken);

        bool schemeChanged = organization.NumberPrefix != normalized.NumberPrefix
            || organization.NumberScope != normalized.NumberScope
            || organization.NumberPadding != normalized.NumberPadding
            || organization.NumberNextValue != normalized.NumberNextValue;
        // Window: a scheme edit racing the first finalization slips past this check; upgrade path: one pg_advisory_xact_lock key taken by both.
        if (schemeChanged && await numbering.AnyNumberAllocatedAsync(cancellationToken))
        {
            // Spec section 6, review M2: another prefix or scope could format a number
            // already issued, and the counter rows mean nothing under another scope.
            return [OrganizationError.SchemeLocked];
        }

        organization.LegalName = normalized.LegalName;
        organization.CountryCode = normalized.CountryCode;
        organization.VatId = normalized.VatId;
        organization.Iban = normalized.Iban;
        organization.Currency = normalized.Currency;
        organization.TimeZoneId = normalized.TimeZoneId;
        organization.PaymentTermsDays = normalized.PaymentTermsDays;
        organization.IssueDateDefault = normalized.IssueDateDefault;
        organization.NumberPrefix = normalized.NumberPrefix;
        organization.NumberScope = normalized.NumberScope;
        organization.NumberPadding = normalized.NumberPadding;
        organization.NumberNextValue = normalized.NumberNextValue;

        await db.SaveChangesAsync(cancellationToken);
        return [];
    }
}
