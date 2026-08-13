using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Makes the Data Protection provider part of <see cref="IdentityDbContext"/>'s model
/// cache key, so two providers in one process get two models instead of silently
/// sharing one.
/// <para>
/// <see cref="IdentityDbContext.OnModelCreating"/> captures an <c>IDataProtector</c>
/// inside the value converter on <c>IdentityUserToken.Value</c>, and EF caches the
/// compiled model per context type. Without this factory the <b>first</b> context built
/// in a process decides which key ring <b>every</b> later context encrypts with,
/// whatever provider its own constructor was handed — and the symptom is a
/// <c>CryptographicException</c> saying "the key … was not found in the key ring", which
/// reads as a Data Protection fault rather than an EF model-caching one. That
/// misdirection is most of the cost.
/// </para>
/// <para>
/// Keyed on the <b>provider</b>, deliberately, not on the protector: every
/// <c>CreateProtector</c> call returns a fresh object, so a key derived from the
/// protector would differ per context instance and rebuild the model on every
/// instantiation — turning a correctness fix into a performance defect. An
/// <c>IDataProtectionProvider</c> is a singleton per dependency-injection container, so
/// the cache holds one model per container.
/// </para>
/// </summary>
internal sealed class UserTokenProtectorModelCacheKeyFactory : IModelCacheKeyFactory
{
    // public Methods
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context is IdentityDbContext identity
            ? new CacheKey(context.GetType(), identity.DataProtectionProvider, designTime)
            : new ModelCacheKey(context, designTime);
    }

    /// <summary>
    /// Compares the provider by <b>reference</b> rather than by <c>Equals</c>: two
    /// providers are interchangeable only when they are the same object, and a provider
    /// that overrode equality would otherwise be able to merge two distinct key rings
    /// back into one model — the exact defect this type exists to prevent.
    /// </summary>
    private sealed class CacheKey(Type contextType, IDataProtectionProvider provider, bool designTime)
    {
        // private readonly Fields
        private readonly Type _contextType = contextType;

        private readonly IDataProtectionProvider _provider = provider;

        private readonly bool _designTime = designTime;

        // public Methods
        public override bool Equals(object? obj) =>
            obj is CacheKey other
            && other._contextType == _contextType
            && ReferenceEquals(other._provider, _provider)
            && other._designTime == _designTime;

        public override int GetHashCode() =>
            HashCode.Combine(_contextType, RuntimeHelpers.GetHashCode(_provider), _designTime);
    }
}
