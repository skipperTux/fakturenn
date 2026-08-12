using System.Security.Cryptography;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fakturenn.Modules.Identity.UnitTests.Persistence;

/// <summary>
/// <see cref="IdentityDbContext.OnModelCreating"/> captures an <c>IDataProtector</c>
/// inside the value converter on <c>IdentityUserToken.Value</c>, and EF caches the
/// compiled model per context type. Without
/// <c>UserTokenProtectorModelCacheKeyFactory</c> the first context built in a process
/// therefore fixes the key ring for every later one, whatever provider its own
/// constructor was handed.
/// <para>
/// Nothing else in the suite could see that. Every other test reads a token back through
/// the same captured protector that wrote it, so it round-trips regardless — the defect
/// only surfaces when a <b>second</b> reader does not share the capture, which is why it
/// first appeared as a subprocess failing with "the key … was not found in the key ring".
/// These tests build the two providers in one process and compare them directly.
/// </para>
/// <para>
/// No database is involved and none is needed: building the model does not connect, and
/// the converter can be taken off the model and invoked on its own.
/// </para>
/// </summary>
public sealed class UserTokenProtectorModelCacheTests : IDisposable
{
    private const string Secret = "2W2NZBPUT2YX3LP3SUMMXICIO2INDYYU";

    /// <summary>
    /// Never connected to. A relational provider is required before EF will build a
    /// model, and that is the whole of what this connection string is for.
    /// </summary>
    private const string UnusedConnectionString =
        "Host=localhost;Database=fakturenn;Username=fakturenn;Password=never-connected";

    private readonly DirectoryInfo _firstKeyRing = Directory.CreateTempSubdirectory("fakturenn-ring-a");

    private readonly DirectoryInfo _secondKeyRing = Directory.CreateTempSubdirectory("fakturenn-ring-b");

    [Fact]
    public void Two_providers_in_one_process_each_protect_under_their_own_key_ring()
    {
        IDataProtectionProvider first = DataProtectionProvider.Create(_firstKeyRing);
        IDataProtectionProvider second = DataProtectionProvider.Create(_secondKeyRing);

        using IdentityDbContext firstContext = CreateContext(first);
        using IdentityDbContext secondContext = CreateContext(second);

        ValueConverter firstConverter = TokenConverter(firstContext);
        ValueConverter secondConverter = TokenConverter(secondContext);

        var firstCiphertext = (string)firstConverter.ConvertToProvider(Secret)!;
        var secondCiphertext = (string)secondConverter.ConvertToProvider(Secret)!;

        // Each reads back what it wrote.
        firstConverter.ConvertFromProvider(firstCiphertext).Should().Be(Secret);
        secondConverter.ConvertFromProvider(secondCiphertext).Should().Be(Secret);

        // And neither reads the other's, which is the property that says the two models
        // really are distinct rather than one model shared. Without the cache-key factory
        // both converters are the same object, both round-trips pass, and this line
        // passes too -- so it is asserted alongside the exception below, not instead of it.
        firstCiphertext.Should().NotBe(secondCiphertext);

        Action crossRead = () => secondConverter.ConvertFromProvider(firstCiphertext);

        // "The key {...} was not found in the key ring" -- the misleading symptom itself,
        // asserted here so that it is documented as an EF model-caching consequence and
        // not mistaken for a Data Protection fault the next time it appears.
        crossRead.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void One_provider_yields_one_model_however_many_contexts_are_built()
    {
        // The other half of the fix, and the reason the key is the provider rather than
        // the protector: CreateProtector returns a fresh object every call, so a key
        // derived from it would rebuild the model on every instantiation and turn a
        // correctness fix into a performance defect. An IDataProtectionProvider is a
        // singleton per dependency-injection container, so a container gets one model.
        IDataProtectionProvider shared = DataProtectionProvider.Create(_firstKeyRing);

        using IdentityDbContext first = CreateContext(shared);
        using IdentityDbContext second = CreateContext(shared);

        second.Model.Should().BeSameAs(first.Model);
    }

    public void Dispose()
    {
        _firstKeyRing.Delete(recursive: true);
        _secondKeyRing.Delete(recursive: true);
    }

    private static IdentityDbContext CreateContext(IDataProtectionProvider provider) =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(UnusedConnectionString)
                .Options,
            provider);

    private static ValueConverter TokenConverter(IdentityDbContext context)
    {
        IEntityType token = context.Model.FindEntityType(typeof(IdentityUserToken<Guid>))
            ?? throw new InvalidOperationException("The token entity is not in the model.");

        return token.FindProperty(nameof(IdentityUserToken<Guid>.Value))?.GetValueConverter()
            ?? throw new InvalidOperationException("The token value carries no converter.");
    }
}
