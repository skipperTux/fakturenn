namespace Fakturenn.ArchitectureTests;

/// <summary>
/// The regex patterns behind <see cref="TechnologyContainmentTests"/>' three rules, named and
/// shared so the rules and <see cref="ArchitecturePatternTests"/> read the same definition
/// instead of keeping two copies that can silently drift apart. Three rules in this branch's
/// history died mid-branch this exact way: a pattern typo made a rule's subject or target set
/// empty while the suite stayed green, because nothing outside the rule itself ever exercised
/// the pattern against a real name.
/// </summary>
public static class ArchitecturePatterns
{
    /// <summary>Subject-side exclusion: exactly the <c>Fakturenn.Web</c> assembly.</summary>
    public const string FakturennWeb = @"^Fakturenn\.Web(,.*)?$";

    /// <summary>Target-side match: MudBlazor and anything under its namespace/version suffix.</summary>
    public const string MudBlazor = @"^MudBlazor.*$";

    /// <summary>Subject-side exclusion: <c>Fakturenn.Infrastructure.Mail</c> and any sibling
    /// starting with that prefix (e.g. a future <c>Fakturenn.Infrastructure.Mail.Smtp</c>).</summary>
    public const string FakturennInfrastructureMail = @"^Fakturenn\.Infrastructure\.Mail.*$";

    /// <summary>Target-side match: MimeKit or MailKit.</summary>
    public const string MimeKitOrMailKit = @"^(MimeKit|MailKit).*$";

    /// <summary>Subject-side exclusion: <c>Fakturenn.Infrastructure.Documents</c> and any sibling
    /// starting with that prefix.</summary>
    public const string FakturennInfrastructureDocuments = @"^Fakturenn\.Infrastructure\.Documents.*$";

    /// <summary>Target-side match: PDFsharp or MigraDoc.</summary>
    public const string PdfSharpOrMigraDoc = @"^(PdfSharp|MigraDoc).*$";
}
