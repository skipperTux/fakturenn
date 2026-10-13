using System.Security.Cryptography;
using System.Text.Json;

namespace Fakturenn.Modules.Invoices.Snapshots;

/// <summary>
/// Serialized once per finalization and stored as raw bytes — not jsonb, which reorders and
/// normalizes keys so the bytes read back would not be the bytes hashed. Nothing
/// re-serializes for verification, so no canonical form is needed (spec section 4).
/// </summary>
public static class InvoiceSnapshotSerializer
{
    // public static Methods
    public static byte[] Serialize(InvoiceSnapshotDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, SnapshotJsonContext.Default.InvoiceSnapshotDocument);

    public static InvoiceSnapshotDocument Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, SnapshotJsonContext.Default.InvoiceSnapshotDocument)
            ?? throw new JsonException("The snapshot is empty.");

    public static byte[] Hash(byte[] bytes) => SHA256.HashData(bytes);
}
