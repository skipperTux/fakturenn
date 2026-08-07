namespace Fakturenn.Infrastructure.Storage;

/// <summary>
/// The narrow slice of the file system this adapter needs. Exists so writing
/// logic can be tested without touching a disk.
/// </summary>
public interface IFileSystem
{
    Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
