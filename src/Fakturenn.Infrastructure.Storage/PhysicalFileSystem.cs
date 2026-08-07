namespace Fakturenn.Infrastructure.Storage;

public sealed class PhysicalFileSystem : IFileSystem
{
    public async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, content, cancellationToken);
    }
}
