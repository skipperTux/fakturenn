namespace Fakturenn.Infrastructure.Storage;

/// <summary>
/// The record of a stored artifact. WALKING-SKELETON.md requires a SHA-256 hash
/// for every artifact, so the hash is part of the write result rather than
/// something a caller has to remember to compute.
/// </summary>
public sealed record StoredBlob(string Path, string Sha256, int SizeInBytes);
