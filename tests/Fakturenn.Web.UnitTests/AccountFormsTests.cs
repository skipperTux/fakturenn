using System.Reflection;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Keeps <c>AccountForms</c>' endpoint-to-page table in step with the routes actually mapped.
/// <para>
/// A missing entry has no compiler error and no startup failure behind it. It surfaces the
/// first time a user's submission is refused — as an <c>InvalidOperationException</c> from the
/// handler, which is a 500 at the exact moment the application was meant to be answering
/// gracefully. A stale entry is quieter still and never surfaces at all.
/// </para>
/// <para>
/// Read by reflection because <c>AccountForms</c> is <c>internal</c>, following
/// <c>AuthEventNamesTests</c>: a test is not a consumer worth widening the public surface for.
/// </para>
/// </summary>
public sealed class AccountFormsTests
{
    [Fact]
    public void Every_account_post_endpoint_names_the_page_that_renders_its_form()
    {
        string[] posts = AccountPostRoutes();
        IReadOnlyCollection<string> mapped = MappedEndpoints();

        using AssertionScope scope = new();

        posts.Should().NotBeEmpty(
            "a route scan that finds nothing would make both assertions below vacuous");

        posts.Except(mapped).Should().BeEmpty(
            "an /account post with no entry throws from AccountForms.Rejected the first time "
            + "its handler refuses a submission, which is a 500 exactly where a redirect was "
            + "the point");

        mapped.Except(posts).Should().BeEmpty(
            "an entry naming a route that no longer exists sends a user somewhere the "
            + "application does not answer");
    }

    private static string[] AccountPostRoutes()
    {
        // The real host, so the list is what MapAccountEndpoints actually produced rather
        // than a second copy of it kept in this file. No connection string: nothing here
        // opens the database, and FakturennWebApplication registers an Unhealthy readiness
        // check rather than throwing when one is absent.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        return [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()
                ?.HttpMethods.Contains(HttpMethods.Post) == true)

            // A static-SSR page endpoint answers POST as well as GET -- the same fact that
            // forces every form in this application to post to a route other than its own
            // page. /account/denied is a page, not a form endpoint, and it would otherwise
            // show up here demanding a table entry it has no use for.
            .Where(endpoint => !IsRazorComponentPage(endpoint))
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))
            .Where(route => route.StartsWith("/account/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];
    }

    private static bool IsRazorComponentPage(Endpoint endpoint) =>
        endpoint.Metadata.Any(metadata => string.Equals(
            metadata.GetType().FullName,
            "Microsoft.AspNetCore.Components.Endpoints.ComponentTypeMetadata",
            StringComparison.Ordinal));

    private static IReadOnlyCollection<string> MappedEndpoints()
    {
        Type forms = Type.GetType("Fakturenn.Web.Components.Account.AccountForms, Fakturenn.Web")
            ?? throw new InvalidOperationException(
                "Fakturenn.Web.Components.Account.AccountForms was not found. Renaming it breaks "
                + "every call site at compile time, so a failure here means the type moved.");

        PropertyInfo mapped = forms.GetProperty(
            "MappedEndpoints", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("AccountForms.MappedEndpoints was not found.");

        return (IReadOnlyCollection<string>)mapped.GetValue(null)!;
    }
}
