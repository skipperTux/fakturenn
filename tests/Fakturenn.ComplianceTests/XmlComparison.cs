namespace Fakturenn.ComplianceTests;

public sealed record XmlComparison(bool IsMatch, IReadOnlyList<string> Differences);
