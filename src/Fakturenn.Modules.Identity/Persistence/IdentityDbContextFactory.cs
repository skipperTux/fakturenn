using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. The connection string is never read at design
/// time because migrations are generated here, not applied.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
                .Options,
            new DesignTimeDataProtectionProvider());

    /// <summary>
    /// Building the model needs a provider, but design time never protects or
    /// unprotects anything: the converter's expressions are only compiled, never
    /// invoked. A real provider would mean the module referencing a Data Protection
    /// implementation package, which the design keeps on the abstraction alone.
    /// </summary>
    private sealed class DesignTimeDataProtectionProvider : IDataProtectionProvider, IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) =>
            throw new NotSupportedException("Design-time contexts never protect data.");

        public byte[] Unprotect(byte[] protectedData) =>
            throw new NotSupportedException("Design-time contexts never unprotect data.");
    }
}
