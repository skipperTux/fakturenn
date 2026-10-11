using System.Text.Json.Serialization;

namespace Fakturenn.Modules.Invoices.Snapshots;

/// <summary>
/// Fixed options, source-generated. Strict on read: an unknown member or a missing
/// constructor parameter throws (review C3). Enums as names, so a reordered enum cannot
/// silently change an old snapshot's meaning.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InvoiceSnapshotDocument))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext;
