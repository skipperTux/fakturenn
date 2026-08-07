using System.Security.Cryptography;

namespace Fakturenn.Infrastructure.Storage;

public sealed class FilesystemBlobWriter(IFileSystem fileSystem, string rootPath)
{
    public async Task<StoredBlob> WriteAsync(
        string relativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        string fullPath = ResolveWithinRoot(relativePath);
        string hash = Convert.ToHexStringLower(SHA256.HashData(content.Span));

        await fileSystem.WriteAsync(fullPath, content, cancellationToken);

        return new StoredBlob(fullPath, hash, content.Length);
    }

    private string ResolveWithinRoot(string relativePath)
    {
        string combined = Path.Combine(rootPath, relativePath);
        string normalizedRoot = Path.GetFullPath(rootPath);

        if (!Path.GetFullPath(combined).StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{relativePath}' resolves outside the storage root.", nameof(relativePath));
        }

        return combined;
    }
}
