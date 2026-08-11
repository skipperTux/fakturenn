using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Infrastructure.DataProtection;

/// <summary>
/// Stores the Data Protection key ring.
/// <para>
/// Infrastructure rather than a module: MODULE-OWNERSHIP.md assigns no key material
/// to any module, and a key ring is not domain data. Modules never reference this
/// assembly; the Identity module depends only on the framework's
/// <c>IDataProtectionProvider</c> abstraction and the concrete store is wired in
/// Fakturenn.Web.
/// </para>
/// <para>
/// The ring lives in the same database as the data it protects on purpose. That
/// keeps ciphertext and key atomic under backup and restore: neither can be
/// restored without the other. Moving the ring to a mounted certificate separates
/// the trust boundaries but introduces a restore in which every enrolled
/// authenticator is silently destroyed.
/// </para>
/// </summary>
public sealed class DataProtectionDbContext(DbContextOptions<DataProtectionDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public const string SchemaName = "dataprotection";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // The key ring gets its own schema so a backup, a restore or a grant can
        // name key material separately from the data it protects.
        modelBuilder.HasDefaultSchema(SchemaName);
    }
}
