using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Persistence;

/// <summary>
/// Inserts the one organization row (spec section 2). Called from the <c>--migrate</c>
/// entrypoint, beside the role seeding, and never from a request path.
/// <para>
/// <b>Insert-if-absent, never an update.</b> <c>--migrate</c> runs on every upgrade, and the
/// operator's edits — the number scheme included — must survive each one. The values are the
/// entity's own defaults and apply only to the first insert. Raw SQL bypasses the audit
/// interceptor, so the provenance is written here: the database's clock, and the system user.
/// </para>
/// </summary>
public static class OrganizationSeeder
{
    public static async Task SeedAsync(OrganizationsDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Organization row = new();
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO organizations."Organizations" (
                "Id", "LegalName", "CountryCode", "VatId", "Iban", "Currency", "TimeZoneId",
                "PaymentTermsDays", "IssueDateDefault", "NumberPrefix", "NumberScope", "NumberPadding",
                "NumberNextValue", "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy")
            VALUES (
                {Organization.SingletonId}, {row.LegalName}, {row.CountryCode}, {row.VatId}, {row.Iban},
                {row.Currency}, {row.TimeZoneId}, {row.PaymentTermsDays}, {row.IssueDateDefault.ToString()},
                {row.NumberPrefix}, {row.NumberScope.ToString()}, {row.NumberPadding}, {row.NumberNextValue},
                now(), {AuditStamp.SystemUser}, now(), {AuditStamp.SystemUser})
            ON CONFLICT ("Id") DO NOTHING
            """,
            cancellationToken);
    }
}
