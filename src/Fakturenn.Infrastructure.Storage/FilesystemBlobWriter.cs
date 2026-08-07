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
        string normalizedRoot = Path.GetFullPath(rootPath);
        string fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));

        // The separator matters: a bare prefix comparison would also accept a
        // sibling directory such as "/srv/fakturenn-archive" for the root
        // "/srv/fakturenn".
        bool insideRoot =
            fullPath.Equals(normalizedRoot, StringComparison.Ordinal)
            || fullPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (!insideRoot)
        {
            throw new ArgumentException(
                $"'{relativePath}' resolves outside the storage root.", nameof(relativePath));
        }

        return fullPath;
    }
}
