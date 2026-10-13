using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Snapshots;

namespace Fakturenn.UnitTests.Modules.Invoices;

/// <summary>
/// The reader is strict so an upgrade between numbering and rendering fails loudly instead
/// of rendering 0.00 with no tax category (spec section 4, review C3).
/// </summary>
public sealed class InvoiceSnapshotSerializerTests
{
    // public Methods
    [Fact]
    public void A_snapshot_round_trips()
    {
        InvoiceSnapshotDocument document = InvoiceSnapshotSamples.WalkingSkeleton();

        byte[] bytes = InvoiceSnapshotSerializer.Serialize(document);

        InvoiceSnapshotSerializer.Deserialize(bytes).Should().BeEquivalentTo(document);
    }

    [Fact]
    public void An_unknown_member_fails_loudly()
    {
        string json = Encoding.UTF8.GetString(InvoiceSnapshotSerializer.Serialize(InvoiceSnapshotSamples.WalkingSkeleton()));
        byte[] extended = Encoding.UTF8.GetBytes(json.Insert(1, "\"renamedLater\":1,"));

        Action act = () => InvoiceSnapshotSerializer.Deserialize(extended);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void A_missing_member_fails_loudly()
    {
        string json = Encoding.UTF8.GetString(InvoiceSnapshotSerializer.Serialize(InvoiceSnapshotSamples.WalkingSkeleton()));
        using JsonDocument parsed = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> members = parsed.RootElement.EnumerateObject()
            .Where(member => member.Name != "gross")
            .ToDictionary(member => member.Name, member => member.Value.Clone());

        Action act = () => InvoiceSnapshotSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(members));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void The_hash_is_over_the_stored_bytes()
    {
        byte[] bytes = InvoiceSnapshotSerializer.Serialize(InvoiceSnapshotSamples.WalkingSkeleton());

        InvoiceSnapshotSerializer.Hash(bytes).Should().Equal(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}
