namespace Fakturenn.Modules.Organizations.Contracts;

/// <summary>
/// The invoice number scheme: prefix, then the date part the scope implies, then the
/// counter zero-padded to <paramref name="Padding"/> digits. <paramref name="NextNumber"/>
/// is what the first finalization in a fresh scope issues.
/// </summary>
public sealed record NumberScheme(string Prefix, ResetScope Scope, int Padding, long NextNumber);
