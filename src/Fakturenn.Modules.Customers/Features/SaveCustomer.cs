using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class SaveCustomer(CustomersDbContext db)
{
    // public Methods
    public async Task<SaveResult> HandleAsync(CustomerForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        CustomerForm normalized = form with
        {
            LegalName = form.LegalName.Trim(),
            CountryCode = form.CountryCode.Trim().ToUpperInvariant(),
            VatId = form.VatId.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            CustomerNumber = form.CustomerNumber.Trim(),
        };

        List<CustomerError> errors = [.. CustomerRules.Validate(normalized)];
        bool taken = await db.Customers.AnyAsync(
            c => c.CustomerNumber == normalized.CustomerNumber && c.Id != normalized.Id, cancellationToken);
        if (taken)
        {
            errors.Add(CustomerError.CustomerNumberTaken);
        }

        if (errors.Count > 0)
        {
            return new SaveResult(null, errors);
        }

        Customer customer = normalized.Id is Guid id
            ? await db.Customers.SingleAsync(c => c.Id == id, cancellationToken)
            : db.Customers.Add(new Customer { Id = Guid.CreateVersion7() }).Entity;

        customer.LegalName = normalized.LegalName;
        customer.CountryCode = normalized.CountryCode;
        customer.VatId = normalized.VatId.Length == 0 ? null : normalized.VatId;
        customer.CustomerNumber = normalized.CustomerNumber;

        await db.SaveChangesAsync(cancellationToken);
        return new SaveResult(customer.Id, []);
    }
}
