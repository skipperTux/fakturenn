using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Pins the password policy against the <b>real host composition</b>. A test over a
/// hand-built options object would assert only that the test sets what the test sets;
/// the point is to catch a framework default silently reasserting itself.
/// </summary>
public sealed class IdentityConfigurationTests
{
    [Fact]
    public void The_claims_principal_factory_is_the_permission_factory()
    {
        // The single most consequential registration in this epic, and the one a unit
        // test over PermissionClaimsPrincipalFactory cannot prove: that test passes
        // whether or not AddClaimsPrincipalFactory names the type. Without the
        // registration, Identity uses its stock factory, nothing writes a
        // fakturenn.permission claim, PermissionAuthorizationHandler reads a claim that
        // is never there, and every [Authorize(Policy = ...)] endpoint answers 403 --
        // including the administrator's own page -- with every unit test still green.
        //
        // Resolved from a scope rather than the root provider because Identity
        // registers the factory as scoped.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);
        using IServiceScope scope = app.Services.CreateScope();

        IUserClaimsPrincipalFactory<ApplicationUser> factory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();

        factory.Should().BeOfType<PermissionClaimsPrincipalFactory>();
    }

    [Fact]
    public void The_password_policy_matches_the_documented_defaults()
    {
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);
        IdentityOptions options = app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Password.RequiredLength.Should().Be(12);
        options.Password.RequireUppercase.Should().BeTrue();
        options.Password.RequireLowercase.Should().BeTrue();
        options.Password.RequireDigit.Should().BeTrue();
        options.Password.RequiredUniqueChars.Should().Be(4);

        // The one Identity default deliberately flipped off: requiring punctuation
        // mostly produces an exclamation mark on the end.
        options.Password.RequireNonAlphanumeric.Should().BeFalse();
    }

    [Fact]
    public void The_password_policy_can_be_overridden_by_configuration()
    {
        // The value of binding the section is that a deployment can tighten it. A
        // Configure call that silently fails to bind leaves the defaults in place and
        // looks identical to a working one, so without this test the appsettings block
        // is decoration.
        WebApplication app = FakturennWebApplication.Build(
            ["--urls", "http://127.0.0.1:0", "--Identity:Password:RequiredLength", "20"]);

        app.Services.GetRequiredService<IOptions<IdentityOptions>>()
            .Value.Password.RequiredLength.Should().Be(20);
    }
}
