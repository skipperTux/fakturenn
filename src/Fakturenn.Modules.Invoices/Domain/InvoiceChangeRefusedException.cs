namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// The invoice's state forbids the change (spec section 4): a numbered invoice keeps its
/// number and issue date, a locked one keeps everything. A type of its own so a page can tell
/// it from every other <see cref="InvalidOperationException"/> EF, Npgsql or DI might throw.
/// </summary>
public sealed class InvoiceChangeRefusedException(string message) : InvalidOperationException(message);
