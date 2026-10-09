using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// The request-bound implementation of <see cref="ICurrentUserAccessor"/>, which is
/// what the audit interceptor consults to stamp a row.
/// <para>
/// Returning null is the contract, not a fallback: <c>AuditStamp.ResolveUser</c> owns
/// the mapping from "nobody" to the <c>system</c> actor, and duplicating it here would
/// give one decision two homes.
/// </para>
/// </summary>
public sealed class HttpContextCurrentUserAccessorTests
{
    [Fact]
    public void An_authenticated_principal_yields_its_name()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "cr@roeper.biz")],
            authenticationType: "Test");

        ICurrentUserAccessor accessor = CreateAccessor(new ClaimsPrincipal(identity));

        accessor.UserName.Should().Be("cr@roeper.biz");
    }

    [Fact]
    public void Outside_a_request_there_is_no_user()
    {
        // Migrations, seeding and the operator entrypoints all run with no HttpContext.
        ICurrentUserAccessor accessor = new HttpContextCurrentUserAccessor(new HttpContextAccessor(), new CurrentPrincipal());

        accessor.UserName.Should().BeNull();
    }

    [Fact]
    public void An_unauthenticated_principal_yields_no_user()
    {
        // The case that matters. A ClaimsIdentity with no authentication type is
        // anonymous, yet it still carries a readable Name -- reading the name without
        // checking IsAuthenticated would let an anonymous request stamp rows with a
        // real user's identity.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "cr@roeper.biz")]);

        ICurrentUserAccessor accessor = CreateAccessor(new ClaimsPrincipal(identity));

        identity.IsAuthenticated.Should().BeFalse("the fixture must actually be anonymous");
        accessor.UserName.Should().BeNull();
    }

    [Fact]
    public void A_principal_set_for_the_operation_wins_over_the_connection_time_request()
    {
        // Inside a circuit, IHttpContextAccessor holds at best the request that opened the
        // circuit. The operation's own principal is the one that changed the IBAN.
        DefaultHttpContext stale = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "connected@example.test")], "test")),
        };
        CurrentPrincipal current = new()
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "acting@example.test")], "test")),
        };

        ICurrentUserAccessor accessor =
            new HttpContextCurrentUserAccessor(new HttpContextAccessor { HttpContext = stale }, current);

        accessor.UserName.Should().Be("acting@example.test");
    }

    private static ICurrentUserAccessor CreateAccessor(ClaimsPrincipal user) =>
        new HttpContextCurrentUserAccessor(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } }, new CurrentPrincipal());
}
