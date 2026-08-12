using Fakturenn.Modules.Identity.Domain;
using Fakturenn.SharedKernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// The Identity module owns this context and its migrations.
/// <para>
/// Derives from <see cref="IdentityUserContext{TUser, TKey}"/> rather than
/// <c>IdentityDbContext</c> on purpose: the former creates users, claims, logins and
/// tokens but no role tables. Roles live in <see cref="Roles"/> and
/// <see cref="UserRoles"/> instead, because epic E02b needs an OrganizationId on the
/// user-role join and AspNetUserRoles has nowhere to put one. Running both role
/// systems side by side later would be worse than not adopting the stock one now.
/// </para>
/// </summary>
public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    IDataProtectionProvider dataProtectionProvider)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public const string SchemaName = "identity";

    /// <summary>
    /// The Data Protection purpose that protects <c>IdentityUserToken.Value</c>. Part of
    /// the key derivation, so changing it makes every stored second factor undecryptable —
    /// it must never be edited.
    /// </summary>
    private const string UserTokenProtectorPurpose = "Fakturenn.Identity.UserToken.v1";

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>
    /// The provider whose key ring protects <c>IdentityUserToken.Value</c> for this
    /// instance. Read by <see cref="UserTokenProtectorModelCacheKeyFactory"/>, which needs
    /// it to keep two providers in one process from sharing one cached model.
    /// </summary>
    internal IDataProtectionProvider DataProtectionProvider => dataProtectionProvider;

    /// <summary>
    /// Installed here rather than at each call site that builds options, because there are
    /// several — the host, the <c>--migrate</c> entrypoint, the design-time factory and the
    /// test fixtures — and a forgotten one would silently reintroduce the shared-model
    /// defect with no compiler error and no failing test.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        base.OnConfiguring(optionsBuilder);

        optionsBuilder.ReplaceService<IModelCacheKeyFactory, UserTokenProtectorModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(SchemaName);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
            user.Property(u => u.CreatedAt).IsRequired();
        });

        builder.Entity<Role>(role =>
        {
            role.HasKey(r => r.Id);
            role.Property(r => r.Name).HasMaxLength(128).IsRequired();
            role.Property(r => r.Description).HasMaxLength(512);
            role.HasIndex(r => r.Name).IsUnique();
        });

        builder.Entity<RolePermission>(rolePermission =>
        {
            // Composite: a role holds many grants, one row per permission. Collapsing
            // this to RoleId alone would cap a role at a single permission.
            rolePermission.HasKey(rp => new { rp.RoleId, rp.Permission });
            rolePermission.Property(rp => rp.Permission).HasMaxLength(128).IsRequired();

            // No navigation properties: the entities stay POCOs, and nothing in the
            // module traverses these relationships. The constraint is what we are
            // after — a deleted role must not leave its grants behind.
            rolePermission.HasOne<Role>()
                .WithMany()
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserRole>(userRole =>
        {
            userRole.HasKey(ur => new { ur.UserId, ur.RoleId });

            userRole.HasOne<Role>()
                .WithMany()
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            userRole.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Both second factors live in IdentityUserToken.Value.
        //
        // The protector is captured here, and EF caches the model per context type: the
        // FIRST IdentityDbContext in a process to build the model decides which key ring
        // every later instance in that process encrypts with, whatever provider its own
        // constructor was handed. That is invisible in production, where one provider
        // exists; it is not invisible to a test process that builds two.
        //
        // Declared as the non-generic base on purpose: IdentityUserToken.Value is
        // `string?`, so the generic HasConversion<TProvider> overload demands a
        // ValueConverter<string?, string> and rejects this one (CS8620). EF never
        // passes null to a converter, so the non-null model type is correct.
        ValueConverter converter = new EncryptedStringConverter(
            dataProtectionProvider.CreateProtector(UserTokenProtectorPurpose));

        builder.Entity<IdentityUserToken<Guid>>()
            .Property(token => token.Value)
            .HasConversion(converter);

        // One place configures the audit columns for every auditable entity, so a
        // later entity cannot arrive with a different column width by accident.
        // Materialised first: builder.Entity(...) mutates the model that
        // GetEntityTypes() enumerates.
        List<IMutableEntityType> auditableEntityTypes = [.. builder.Model.GetEntityTypes()
            .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType))];

        foreach (IMutableEntityType entityType in auditableEntityTypes)
        {
            builder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
