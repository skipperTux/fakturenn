using System.Text;
using AwesomeAssertions;
using Fakturenn.Infrastructure.Storage;
using NSubstitute;

namespace Fakturenn.UnitTests.Infrastructure;

public sealed class FilesystemBlobWriterTests
{
    private static readonly byte[] _content = Encoding.UTF8.GetBytes("fakturenn");

    // "fakturenn" hashed with SHA-256, verified independently with:
    //   printf 'fakturenn' | sha256sum
    private const string _expectedHash =
        "b605d3e7799125932e02a9ff56104d77e576a79d865f46a8b14bc5262ca9505f";

    [Fact]
    public async Task Writing_a_blob_reports_its_sha256_hash()
    {
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        StoredBlob blob = await writer.WriteAsync("invoices/invoice.pdf", _content, TestContext.Current.CancellationToken);

        blob.Sha256.Should().Be(_expectedHash);
        blob.SizeInBytes.Should().Be(_content.Length);
    }

    [Fact]
    public async Task Writing_a_blob_places_it_under_the_configured_root()
    {
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        StoredBlob blob = await writer.WriteAsync("invoices/invoice.pdf", _content, TestContext.Current.CancellationToken);

        blob.Path.Should().Be(Path.Combine("/srv/fakturenn", "invoices/invoice.pdf"));
    }

    [Fact]
    public async Task The_underlying_file_system_is_written_to_exactly_once()
    {
        // Interaction is the behaviour under test here, which is what NSubstitute
        // is for. A fake could record the call, but not assert "exactly once"
        // as the contract itself.
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        var writer = new FilesystemBlobWriter(fileSystem, "/srv/fakturenn");

        await writer.WriteAsync("invoices/invoice.pdf", _content, TestContext.Current.CancellationToken);

        await fileSystem.Received(1).WriteAsync(
            Path.Combine("/srv/fakturenn", "invoices/invoice.pdf"),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_absolute_relative_path_is_rejected_so_a_blob_cannot_escape_the_root()
    {
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        Func<Task> write = () => writer.WriteAsync("../../etc/passwd", _content, TestContext.Current.CancellationToken);

        await write.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_sibling_directory_sharing_the_roots_name_prefix_is_rejected()
    {
        // A bare prefix comparison would accept "/srv/fakturenn-archive" for the
        // root "/srv/fakturenn", because the string starts the same way.
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        Func<Task> write = () => writer.WriteAsync(
            "../fakturenn-archive/invoice.pdf", _content, TestContext.Current.CancellationToken);

        await write.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task An_absolute_path_sharing_the_roots_name_prefix_is_rejected()
    {
        // Path.Combine discards its first argument when the second is rooted,
        // so an absolute path must be rejected on its resolved value, not on
        // the assumption that Combine kept the root.
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        Func<Task> write = () => writer.WriteAsync(
            "/srv/fakturennsibling/invoice.pdf", _content, TestContext.Current.CancellationToken);

        await write.Should().ThrowAsync<ArgumentException>();
    }
}
