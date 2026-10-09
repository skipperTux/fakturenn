namespace Fakturenn.Web;

/// <summary>
/// The circuit's current user lacks the permission an operation requires. Pages catch it
/// and send the user to <c>/account/denied</c> with a full reload, which re-runs the
/// cookie authentication a circuit never repeats.
/// </summary>
public sealed class OperationRefusedException(string permission)
    : Exception($"The current user lacks the '{permission}' permission.")
{
    public string Permission { get; } = permission;
}
