namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// Spec section 4. Draft: no number. Numbered: data editable, number and issue date frozen.
/// Locked: PDF and XML rendered, validated and archived — nothing changes, ever.
/// </summary>
public enum InvoiceState
{
    Draft,
    Numbered,
    Locked,
}
