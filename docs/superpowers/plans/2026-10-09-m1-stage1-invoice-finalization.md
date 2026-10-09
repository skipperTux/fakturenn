# M1 Stage 1 — Invoice Finalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Organization, customer, project and catalog master data, plus a one-line invoice that is numbered gaplessly per scope and frozen into a hashed snapshot, behind interactive Blazor pages that keep E02a's session and permission guarantees.

**Architecture:** Four new modules (Organizations, Customers, Projects, Catalog), each a thin write model with its own `DbContext`, schema and migration, exposing immutable snapshot records through its `.Contracts` assembly. The existing Invoices module gains a pure domain (state machine, totals, tax decision, numbering format) and a finalization slice that locks the invoice row, allocates with one `INSERT … ON CONFLICT … RETURNING`, and upserts a System.Text.Json snapshot plus SHA-256 — inside the EF execution strategy. `Fakturenn.Web` composes everything: it registers the contexts with the audit interceptor, runs every page operation through one `CircuitOperations` choke point that re-checks the permission against the circuit's *current* authentication state and opens a fresh DI scope, and revalidates the security stamp of open circuits.

**Tech Stack:** .NET 10, ASP.NET Core Identity, Blazor Interactive Server (per page, prerendering off), MudBlazor 9.11, EF Core 10 + Npgsql, PostgreSQL 18 (`postgres:18-trixie`), Wolverine 6.48.1 (untouched here), xUnit v3 4.0.1 on Microsoft.Testing.Platform, AwesomeAssertions, Testcontainers, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-16-m1-stage1-invoice-finalization-design.md` — revised after `docs/superpowers/reviews/2026-10-08-m1-stage1-invoice-finalization-spec-review.md`. Finding identifiers in brackets (`[S1]`) point into that review. Read the spec before your task; this plan argues from it.

## Global Constraints

- Target `net10.0`. Central Package Management: never put `Version=` on a `PackageReference`; add a version only through `dotnet add package <id>`. This plan needs **no new package**.
- New assemblies are named exactly `Fakturenn.Modules.Organizations`, `.Customers`, `.Projects`, `.Catalog`, each with a `.Contracts` sibling. Schemas: `organizations`, `customers`, `projects`, `catalog`. Invoices keeps `invoices`.
- A module never references another module, only its `.Contracts` (architecture rule 5). No module references `Fakturenn.Infrastructure.*` (rule 4). Only `Fakturenn.Web` references MudBlazor (rule 1).
- Every entity implements `IAuditable`. Every new context is registered **with** `AuditSaveChangesInterceptor` and is **not** enrolled with Wolverine.
- Canonical terms only: **CatalogItem**, **CatalogItemNumber**, **CustomerCatalogItemNumber**. "Issue date", never "document date".
- Localization: every key is looked up as a string literal, `Localizer["Some_Key"]`. `SharedResourceTests` scans source for exactly that pattern; a key built at run time fails `Every_resource_key_is_asked_for_by_the_code`. Map error codes to keys with an explicit `switch`, never `Localizer[code]`.
- Encoding: `.razor` and `.resx` files carry a UTF-8 BOM, `.cs` files do not. CI's Format job checks it.
- `.cs` style: file-scoped namespaces, braces always, StyleCop member order, one group comment per member group (`// public Methods`) per `.claude/rules/dotnet.md`.
- Tests: underscore names, AwesomeAssertions, `TestContext.Current.CancellationToken`. Integration and UI suites run with `DOTNET_USE_POLLING_FILE_WATCHER=1`.
- Before calling a build green, build with `--no-incremental`: an incremental build does not re-report IDE0005.
- Never use `git checkout -- <path>` or `git restore` (denied). To undo a mutation, copy the file aside first, restore by writing it back, verify with `sha256sum --check`.
- One organization per instance. Never add an `OrganizationId` column or a query filter.

## Review Focus

Inputs the spec implies but no test would otherwise pin, most likely to bite first. Each has a test in its owning task.

1. **An invoice drafted late in the evening** — 23:30 UTC on 31 August is 1 September in `Europe/Berlin`; the issue-date default must be 1 September, and the number follows from the issue date. Test: Task 6, `The_default_issue_date_is_today_in_the_organization_time_zone`.
2. **A fractional quantity** — 0.333 h at 100.00 must net 33.30, not 33.3 or 33.299. Test: Task 6, `A_fractional_quantity_nets_to_two_decimals`.
3. **A prefix with an umlaut, a space or ten characters** — refused at save, never discovered at finalization. Test: Task 3, `Prefix_rules` theory.
4. **The price changing between preview and confirmation** — finalization refuses and shows the new totals instead of numbering an amount nobody confirmed. Test: Task 7, `Finalization_refuses_when_the_confirmed_gross_is_stale`.
5. **A second browser tab finalizing the same draft** — one number, one snapshot, counter advanced once. Test: Task 7, `Finalizing_one_invoice_twice_concurrently_allocates_once`.

## Deviations from the spec, decided while planning

- **Permission constants stay in `Fakturenn.Modules.Identity`.** Spec §8 moves them to `Identity.Contracts` "so business modules can reference them". In this plan modules never check permissions: every page operation passes through `Fakturenn.Web`'s `CircuitOperations`, which already references Identity. Moving them would serve no reader. Task 9 amends §8.
- **`CatalogItem` stores no type.** `HourlyService` is read by nothing in Stage 1 — the review's M11 argument. It arrives with the stage that renders it. Task 9 amends §3.
- **The scheme lock lands in Task 7, not Task 3.** Organizations learns "a number exists" through `INumberingStatus` in `Invoices.Contracts`, implemented over the counter table that Task 7 creates.

## File structure

```text
src/Fakturenn.Modules.Organizations.Contracts/  SellerSnapshot, NumberScheme, ResetScope, IssueDateDefault, ISellerProvider
src/Fakturenn.Modules.Organizations/            Organization, OrganizationsDbContext (+factory, migration), Features/{GetOrganization,SaveOrganization,SellerProvider}, OrganizationRules
src/Fakturenn.Modules.Customers.Contracts/      CustomerSnapshot, ICustomerProvider
src/Fakturenn.Modules.Customers/                Customer, CustomerCatalogItemReference, CustomersDbContext, Features/{ListCustomers,SaveCustomer,SaveCatalogItemReference,CustomerProvider}
src/Fakturenn.Modules.Projects.Contracts/       ProjectSnapshot, IProjectProvider
src/Fakturenn.Modules.Projects/                 Project, ProjectsDbContext, Features/{ListProjects,SaveProject,ProjectProvider}
src/Fakturenn.Modules.Catalog.Contracts/        CatalogItemSnapshot, ICatalogItemProvider
src/Fakturenn.Modules.Catalog/                  CatalogItem, CatalogDbContext, Features/{ListCatalogItems,SaveCatalogItem,CatalogItemProvider}
src/Fakturenn.Modules.Invoices.Contracts/       + INumberingStatus
src/Fakturenn.Modules.Invoices/Domain/          Invoice, InvoiceLine, InvoiceState, TaxCategory, FinalizationRefusal, InvoiceCalculator, TaxDecision, IssueDateRule, InvoiceNumberFormat, FinalizationChecks
src/Fakturenn.Modules.Invoices/Snapshots/       InvoiceSnapshotDocument, InvoiceSnapshotSerializer, SnapshotJsonContext
src/Fakturenn.Modules.Invoices/Features/        CreateDraft, SaveDraft, PreviewInvoice, FinalizeInvoice, GetInvoice, ListInvoices, NumberAllocator, NumberingStatus
src/Fakturenn.Web/                              ModuleConfiguration, CircuitOperations, CurrentPrincipal, SecurityStampRevalidatingAuthenticationStateProvider, Components/Layout/InteractiveProviders.razor, Components/Business/*.razor
```

---

### Task 1: Authenticated by default, and three new permissions

**Files:**
- Modify: `src/Fakturenn.Modules.Identity/Authorization/Permissions.cs`
- Modify: `src/Fakturenn.Web/IdentityConfiguration.cs` (the `AddAuthorization()` call)
- Modify: `src/Fakturenn.Web/FakturennWebApplication.cs` (the two `MapHealthChecks` calls)
- Modify: `src/Fakturenn.Web/Components/Account/AccountEndpoints.cs` (four anonymous `MapPost` calls)
- Modify: `src/Fakturenn.Web/Components/Pages/Home.razor`, and in `Components/Account/`: `AccessDenied.razor`, `Lockout.razor`, `Login.razor`, `LoginWith2fa.razor`, `LoginWithRecoveryCode.razor`, `Setup.razor`
- Test: `tests/Fakturenn.Web.UnitTests/IdentityConfigurationTests.cs`, `tests/Fakturenn.IntegrationTests/AnonymousAccessTests.cs` (create)

**Interfaces:**
- Produces: `Permissions.OrganizationManage = "organization.manage"`, `Permissions.MasterDataManage = "masterdata.manage"`, `Permissions.InvoicesManage = "invoices.manage"`, all in `Permissions.All`. `AuthorizationOptions.FallbackPolicy` requires an authenticated user.

- [ ] **Step 1: Write the failing host-composition test**

Add to `tests/Fakturenn.Web.UnitTests/IdentityConfigurationTests.cs` (inside the existing class, with the file's existing usings plus `Microsoft.AspNetCore.Authorization` and `Microsoft.Extensions.Options`):

```csharp
    [Fact]
    public void An_endpoint_without_an_authorize_attribute_requires_a_signed_in_user()
    {
        // Spec section 8 [S4]: a page that forgets its attribute must be refused, not
        // anonymous. Without a fallback policy it is reachable without signing in --
        // the organization page with its IBAN included.
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        AuthorizationOptions options =
            app.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        options.FallbackPolicy.Should().NotBeNull();
        options.FallbackPolicy!.Requirements.Should().ContainSingle()
            .Which.Should().BeOfType<Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement>();
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test --project tests/Fakturenn.Web.UnitTests --configuration Release -- --filter-method "*An_endpoint_without_an_authorize_attribute*"`
Expected: FAIL, `Expected options.FallbackPolicy not to be <null>`.

- [ ] **Step 3: Add the permissions and the fallback policy**

`src/Fakturenn.Modules.Identity/Authorization/Permissions.cs` — add three constants after `UsersManage` and to `All`:

```csharp
    /// <summary>
    /// The organization record: legal identity, IBAN, VAT ID, number scheme. Separate from
    /// master data because a changed IBAN redirects every future payment.
    /// </summary>
    public const string OrganizationManage = "organization.manage";

    /// <summary>Customers, projects, catalog items and customer catalog-item references.</summary>
    public const string MasterDataManage = "masterdata.manage";

    /// <summary>Drafting and finalizing invoices.</summary>
    public const string InvoicesManage = "invoices.manage";
```

```csharp
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UsersRead,
        UsersManage,
        OrganizationManage,
        MasterDataManage,
        InvoicesManage,
    };
```

`src/Fakturenn.Web/IdentityConfiguration.cs` — replace `builder.Services.AddAuthorization();` with:

```csharp
        // Authenticated by default (spec section 8, review S4). A page or endpoint that
        // forgets its attribute is refused rather than anonymous. Everything a signed-out
        // visitor must reach carries an explicit AllowAnonymous instead, so the exceptions
        // are greppable. Static files are unaffected: UseStaticFiles runs before
        // UseAuthorization.
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
```

- [ ] **Step 4: Run the composition test**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Write the failing anonymous-reachability test**

Create `tests/Fakturenn.IntegrationTests/AnonymousAccessTests.cs`:

```csharp
using System.Net;
using AwesomeAssertions;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// What a signed-out visitor may reach under the authenticated-by-default fallback policy,
/// and nothing more. Each row is a path that worked before the fallback policy and must
/// keep working; each refusal is a path the policy exists to close.
/// </summary>
[Collection(RealHost.Name)]
public sealed class AnonymousAccessTests(SetupHostFixture host)
{
    // public Methods
    [Theory]
    [InlineData("/")]
    [InlineData("/setup")]
    [InlineData("/account/login")]
    [InlineData("/account/login-2fa")]
    [InlineData("/account/login-recovery")]
    [InlineData("/account/lockout")]
    [InlineData("/account/denied")]
    [InlineData("/alive")]
    [InlineData("/_framework/blazor.web.js")]
    public async Task A_signed_out_visitor_reaches(string path)
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Found,
            $"{path} must not redirect a signed-out visitor to the sign-in page");
        ((int)response.StatusCode).Should().BeLessThan(400, $"{path} must answer a signed-out visitor");
    }

    [Fact]
    public async Task A_signed_out_probe_reaches_the_readiness_check()
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        // 200 or 503 depending on the database; never a redirect to a sign-in page a probe
        // cannot follow.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/account/change-password")]
    public async Task A_signed_out_visitor_is_sent_to_sign_in(string path)
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().StartWith("/account/login");
    }

    [Fact]
    public async Task A_signed_out_visitor_cannot_open_a_circuit()
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(
            "/_blazor/negotiate?negotiateVersion=1", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found,
            "the circuit endpoint carries no AllowAnonymous, so the fallback policy challenges it");
    }
}
```

`host.CreateClient()` already disables auto-redirect; confirm by reading `SetupHostFixture.CreateClient` before relying on it.

- [ ] **Step 6: Run it and watch the signed-out rows fail**

Run: `DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project tests/Fakturenn.IntegrationTests --configuration Release -- --filter-class "*AnonymousAccessTests"`
Expected: `A_signed_out_visitor_reaches` FAILS for every page row with a 302; `/alive` FAILS likewise. `/_framework/blazor.web.js` PASSES (static file). The refusal rows PASS.

- [ ] **Step 7: Mark the anonymous surface**

Add as the first `@attribute` line (after `@page`) of `Home.razor`, `AccessDenied.razor`, `Lockout.razor`, `Login.razor`, `LoginWith2fa.razor`, `LoginWithRecoveryCode.razor`, `Setup.razor`:

```razor
@attribute [Microsoft.AspNetCore.Authorization.AllowAnonymous]
```

`login-2fa` and `login-recovery` are reached while the user holds only Identity's partial two-factor cookie, which does not authenticate the application scheme — so they must be anonymous too.

In `FakturennWebApplication.cs`, append `.AllowAnonymous()` to both `MapHealthChecks(...)` calls:

```csharp
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        }).AllowAnonymous();
```

(and the same for `/health`). In `AccountEndpoints.cs`, append `.AllowAnonymous()` to exactly these four: `group.MapPost("/setup", …)`, `group.MapPost("/login/submit", …)`, `group.MapPost("/login-2fa/submit", …)`, `group.MapPost("/login-recovery/submit", …)`. The others — `enrol-totp/verify`, `change-password/submit`, `logout`, `admin/*` — are for signed-in users and stay under the fallback.

- [ ] **Step 8: Run every suite**

```bash
dotnet build --configuration Release --no-incremental
for p in Fakturenn.Web.UnitTests Fakturenn.IntegrationTests Fakturenn.UiTests; do
  DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project "tests/$p" --configuration Release
done
```

Expected: all PASS. If a pre-existing test now fails on a 302, the path it requests needs `AllowAnonymous` — add it and say so in the commit; do not loosen the fallback. `MigrateEntrypointTests` and `RoleSeedingTests` compare against `Permissions.All` and pick the three new grants up without edits.

- [ ] **Step 9: Mutation — the fallback policy is load-bearing**

Copy `IdentityConfiguration.cs` aside, revert its `AddAuthorization` body to `builder.Services.AddAuthorization();`, rebuild, run the Step 2 test and `A_signed_out_visitor_cannot_open_a_circuit`. Expected: both FAIL. Restore the file and `sha256sum --check` it.

- [ ] **Step 10: Commit**

```bash
git add src/Fakturenn.Modules.Identity/Authorization/Permissions.cs src/Fakturenn.Web tests/Fakturenn.Web.UnitTests/IdentityConfigurationTests.cs tests/Fakturenn.IntegrationTests/AnonymousAccessTests.cs
git commit -m "feat(identity): authenticated by default, with three business permissions"
```

---

### Task 2: Circuit identity — revalidation, one operation choke point, provenance, MudBlazor providers

**Files:**
- Create: `src/Fakturenn.Web/CurrentPrincipal.cs`, `src/Fakturenn.Web/CircuitOperations.cs`, `src/Fakturenn.Web/OperationRefusedException.cs`, `src/Fakturenn.Web/SecurityStampRevalidatingAuthenticationStateProvider.cs`, `src/Fakturenn.Web/Components/Layout/InteractiveProviders.razor`
- Modify: `src/Fakturenn.Web/HttpContextCurrentUserAccessor.cs`, `src/Fakturenn.Web/IdentityConfiguration.cs`, `src/Fakturenn.Web/Components/Layout/MainLayout.razor`
- Test: `tests/Fakturenn.Web.UnitTests/HttpContextCurrentUserAccessorTests.cs`, `tests/Fakturenn.Web.UnitTests/CircuitOperationsTests.cs` (create), `tests/Fakturenn.IntegrationTests/CircuitRevalidationTests.cs` (create), `tests/Fakturenn.IntegrationTests/EnrolmentGateTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class CurrentPrincipal { public ClaimsPrincipal? Principal { get; set; } }` — scoped.
  - `public sealed class CircuitOperations` with
    `Task<TResult> RunAsync<TService, TResult>(string permission, Func<TService, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken) where TService : notnull`
    and `Task RunAsync<TService>(string permission, Func<TService, CancellationToken, Task> operation, CancellationToken cancellationToken) where TService : notnull`.
    Throws `OperationRefusedException` when the circuit's current user lacks `permission`.
  - `public sealed class SecurityStampRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider` with `public static Task<bool> StampMatchesAsync(UserManager<ApplicationUser> users, IdentityOptions options, ClaimsPrincipal principal)`.
  - `Components/Layout/InteractiveProviders.razor` — every interactive business page renders `<InteractiveProviders />`.

- [ ] **Step 1: Write the failing accessor tests**

In `tests/Fakturenn.Web.UnitTests/HttpContextCurrentUserAccessorTests.cs`, change both constructions to pass a `CurrentPrincipal` — `new HttpContextCurrentUserAccessor(new HttpContextAccessor(), new CurrentPrincipal())` — and add:

```csharp
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
```

- [ ] **Step 2: Write the failing operation tests**

Create `tests/Fakturenn.Web.UnitTests/CircuitOperationsTests.cs`:

```csharp
using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

public sealed class CircuitOperationsTests
{
    // public Methods
    [Fact]
    public async Task An_operation_runs_when_the_current_user_holds_the_permission()
    {
        await using ServiceProvider services = Services(User(Permissions.MasterDataManage));
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();

        string ran = await operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        ran.Should().Be("user@example.test", "the operation's scope carries the circuit's current user");
    }

    [Fact]
    public async Task An_operation_is_refused_without_the_permission()
    {
        await using ServiceProvider services = Services(User(Permissions.UsersRead));
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();

        Func<Task> act = () => operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationRefusedException>();
    }

    [Fact]
    public async Task An_operation_is_refused_once_the_state_turns_anonymous()
    {
        // What revalidation does to a locked user's circuit: the provider's state becomes
        // anonymous, and the very next operation must see it.
        await using ServiceProvider services = Services(new ClaimsPrincipal(new ClaimsIdentity()));
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();

        Func<Task> act = () => operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationRefusedException>();
    }

    // private static Methods
    private static ClaimsPrincipal User(string permission) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "user@example.test"), new Claim(PermissionClaims.Type, permission)],
            authenticationType: "test"));

    private static ServiceProvider Services(ClaimsPrincipal user)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationStateProvider(user));
        services.AddScoped<CurrentPrincipal>();
        services.AddScoped<CircuitOperations>();
        services.AddScoped<Probe>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    // private Types
    private sealed class Probe(CurrentPrincipal current)
    {
        public string Seen => current.Principal?.Identity?.Name ?? "nobody";
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }
}
```

- [ ] **Step 3: Run both test classes and watch them fail to compile**

Run: `dotnet build tests/Fakturenn.Web.UnitTests --configuration Release`
Expected: errors for `CurrentPrincipal`, `CircuitOperations`, `OperationRefusedException`, and the two-argument accessor constructor.

- [ ] **Step 4: Implement the principal holder, the accessor change and the choke point**

`src/Fakturenn.Web/CurrentPrincipal.cs`:

```csharp
using System.Security.Claims;

namespace Fakturenn.Web;

/// <summary>
/// The principal an operation acts as, set by <see cref="CircuitOperations"/> on the fresh
/// scope it opens. Null on an ordinary HTTP request, where the request's own user applies.
/// </summary>
public sealed class CurrentPrincipal
{
    public ClaimsPrincipal? Principal { get; set; }
}
```

`src/Fakturenn.Web/OperationRefusedException.cs`:

```csharp
namespace Fakturenn.Web;

/// <summary>
/// The circuit's current user lacks the permission an operation requires. Pages catch it
/// and send the user to <c>/account/denied</c> with a full reload, which re-runs the
/// cookie authentication a circuit never repeats.
/// </summary>
public sealed class OperationRefusedException(string permission)
    : Exception($"The current user lacks the '{permission}' permission.")
{
    public string Permission { get; } = permission;
}
```

`src/Fakturenn.Web/HttpContextCurrentUserAccessor.cs` — replace the class:

```csharp
using System.Security.Claims;
using Fakturenn.SharedKernel;

namespace Fakturenn.Web;

/// <summary>
/// The signed-in user's name. Inside a circuit <c>IHttpContextAccessor</c> holds at best
/// the request that opened the circuit, so the principal an operation was started with
/// wins whenever there is one (spec section 8, review C5).
/// </summary>
public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor, CurrentPrincipal current)
    : ICurrentUserAccessor
{
    public string? UserName => NameOf(current.Principal ?? httpContextAccessor.HttpContext?.User);

    // private static Methods
    private static string? NameOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
}
```

`src/Fakturenn.Web/CircuitOperations.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace Fakturenn.Web;

/// <summary>
/// The one way an interactive page touches a module. Two jobs, both forced by circuits:
/// <list type="bullet">
/// <item>The permission is checked against the circuit's <b>current</b> authentication
/// state, not against what the page was rendered with. A circuit makes no new HTTP
/// request, so the page attribute is evaluated once; revalidation turns the state
/// anonymous when the security stamp changes, and this is where that takes effect
/// (spec section 8, review S3/S4).</item>
/// <item>Every operation gets a fresh DI scope and therefore a fresh <c>DbContext</c>. A
/// context injected into a component would live as long as the tab, track everything it
/// ever loaded and be shared by overlapping event handlers (review C1). A scope rather
/// than <c>IDbContextFactory</c>: Wolverine refuses handlers whose only route to a context
/// is a factory, and these become handlers once Stage 2 publishes.</item>
/// </list>
/// </summary>
public sealed class CircuitOperations(
    AuthenticationStateProvider authenticationState,
    IAuthorizationService authorization,
    IServiceScopeFactory scopes)
{
    // public Methods
    public async Task<TResult> RunAsync<TService, TResult>(
        string permission,
        Func<TService, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);

        AuthenticationState state = await authenticationState.GetAuthenticationStateAsync();
        AuthorizationResult result = await authorization.AuthorizeAsync(state.User, resource: null, permission);
        if (!result.Succeeded)
        {
            throw new OperationRefusedException(permission);
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentPrincipal>().Principal = state.User;
        return await operation(scope.ServiceProvider.GetRequiredService<TService>(), cancellationToken);
    }

    public Task RunAsync<TService>(
        string permission,
        Func<TService, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync<TService, bool>(
            permission,
            async (service, token) =>
            {
                await operation(service, token);
                return true;
            },
            cancellationToken);
    }
}
```

- [ ] **Step 5: Run the unit tests**

Run: `dotnet test --project tests/Fakturenn.Web.UnitTests --configuration Release`
Expected: PASS, including the three new operation tests and the new accessor test.

- [ ] **Step 6: Write the failing revalidation test**

Create `tests/Fakturenn.IntegrationTests/CircuitRevalidationTests.cs`:

```csharp
using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fakturenn.IntegrationTests;

[Collection(RealHost.Name)]
public sealed class CircuitRevalidationTests(SetupHostFixture host)
{
    // public Methods
    [Fact]
    public async Task A_circuit_principal_stays_valid_while_the_stamp_is_unchanged()
    {
        (UserManager<ApplicationUser> users, IdentityOptions options, ClaimsPrincipal principal, AsyncServiceScope scope) =
            await PrincipalAsync("revalidate-valid@example.test");
        await using (scope)
        {
            (await SecurityStampRevalidatingAuthenticationStateProvider.StampMatchesAsync(users, options, principal))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_circuit_principal_is_invalidated_when_the_stamp_rotates()
    {
        // Locking, unlocking, password and MFA changes all rotate the stamp (E02a section
        // 8). An open circuit must notice within the same interval a cookie does.
        (UserManager<ApplicationUser> users, IdentityOptions options, ClaimsPrincipal principal, AsyncServiceScope scope) =
            await PrincipalAsync("revalidate-rotated@example.test");
        await using (scope)
        {
            ApplicationUser user = (await users.FindByEmailAsync("revalidate-rotated@example.test"))!;
            await users.UpdateSecurityStampAsync(user);

            (await SecurityStampRevalidatingAuthenticationStateProvider.StampMatchesAsync(users, options, principal))
                .Should().BeFalse();
        }
    }

    // private Methods
    private async Task<(UserManager<ApplicationUser>, IdentityOptions, ClaimsPrincipal, AsyncServiceScope)> PrincipalAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, TestContext.Current.CancellationToken);
        AsyncServiceScope scope = host.Services.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IUserClaimsPrincipalFactory<ApplicationUser> factory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        ClaimsPrincipal principal = await factory.CreateAsync((await users.FindByIdAsync(user.Id.ToString()))!);
        IdentityOptions options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        return (users, options, principal, scope);
    }
}
```

Check `SetupHostFixture.CreateUserAsync(string, CancellationToken)`'s exact return before relying on it (`Task<ApplicationUser>`, line 232).

- [ ] **Step 7: Implement the provider and register it**

`src/Fakturenn.Web/SecurityStampRevalidatingAuthenticationStateProvider.cs`:

```csharp
using System.Security.Claims;
using Fakturenn.Modules.Identity.Domain;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Fakturenn.Web;

/// <summary>
/// Re-checks an open circuit's security stamp. Components.Server sets a circuit's user only
/// when it starts and when it reconnects (decompiled, 10.0.12); without this a user locked
/// mid-session keeps editing the IBAN and finalizing until the tab closes (review S3).
/// When the stamp no longer matches, the base class turns the state anonymous and
/// <see cref="CircuitOperations"/> refuses the next operation.
/// </summary>
public sealed class SecurityStampRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopes,
    IOptions<IdentityOptions> identityOptions,
    IOptions<SecurityStampValidatorOptions> stampOptions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    // protected Properties
    // The interval cookies already use, so a circuit and a page load agree on how stale a
    // revoked session may be.
    protected override TimeSpan RevalidationInterval => stampOptions.Value.ValidationInterval;

    // public static Methods
    public static async Task<bool> StampMatchesAsync(
        UserManager<ApplicationUser> users,
        IdentityOptions options,
        ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(options);

        ApplicationUser? user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }

        string? principalStamp = principal.FindFirstValue(options.ClaimsIdentity.SecurityStampClaimType);
        string userStamp = await users.GetSecurityStampAsync(user);
        return string.Equals(principalStamp, userStamp, StringComparison.Ordinal);
    }

    // protected Methods
    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await StampMatchesAsync(users, identityOptions.Value, authenticationState.User);
    }
}
```

In `IdentityConfiguration.AddFakturennIdentity`, after `builder.Services.AddHttpContextAccessor();`:

```csharp
        builder.Services.AddScoped<CurrentPrincipal>();
        builder.Services.AddScoped<CircuitOperations>();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, SecurityStampRevalidatingAuthenticationStateProvider>();
```

Add `using Microsoft.AspNetCore.Components.Authorization;`.

- [ ] **Step 8: Move MudBlazor's providers out of the static layout**

Create `src/Fakturenn.Web/Components/Layout/InteractiveProviders.razor` (UTF-8 **with** BOM):

```razor
@* MudBlazor's popover and snackbar providers must live in the same interactive render
   scope as the components that use them. The layout is static under per-page
   interactivity, so a provider there is invisible to a business page's MudSelect and
   MudDatePicker, and a second provider on the page throws "Duplicate
   MudPopoverProvider detected" and ends the circuit (review C9). Every interactive page
   renders this component once; the layout renders none. *@
<MudPopoverProvider />
<MudSnackbarProvider />
```

In `MainLayout.razor`, delete the `<MudPopoverProvider />` and `<MudSnackbarProvider />` lines; keep `<MudThemeProvider />`. No static page uses a popover or snackbar today (`grep -rn 'ISnackbar\|MudSelect\|MudDatePicker\|MudMenu' src/Fakturenn.Web --include=*.razor` is empty).

- [ ] **Step 9: The gate keeps a not-enrolled user off the circuit**

Add to `tests/Fakturenn.IntegrationTests/EnrolmentGateTests.cs`:

```csharp
    [Fact]
    public async Task A_user_who_has_not_enrolled_cannot_open_a_circuit()
    {
        // Spec section 9: /_blazor is not on the gate's allowlist, and must not be. A gated
        // user with a circuit could render interactive business pages around the gate.
        using HttpClient client = await NotEnrolledClientAsync("gate-circuit@example.test");

        using HttpResponseMessage response = await client.PostAsync(
            "/_blazor/negotiate?negotiateVersion=1", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/account/enrol-totp");
    }
```

- [ ] **Step 10: Run every suite**

```bash
dotnet build --configuration Release --no-incremental
for p in Fakturenn.Web.UnitTests Fakturenn.IntegrationTests Fakturenn.UiTests; do
  DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project "tests/$p" --configuration Release
done
```

Expected: all PASS.

- [ ] **Step 11: Mutation — revalidation compares the stamp**

Copy the provider file aside, make `StampMatchesAsync` return `true` after the null check, rebuild, run `CircuitRevalidationTests`. Expected: `A_circuit_principal_is_invalidated_when_the_stamp_rotates` FAILS. Restore and `sha256sum --check`.

- [ ] **Step 12: Commit**

```bash
git add src/Fakturenn.Web tests/Fakturenn.Web.UnitTests tests/Fakturenn.IntegrationTests
git commit -m "feat(web): revalidate circuits and route page operations through one choke point"
```

---

### Task 3: Organizations module

**Files:**
- Create: `src/Fakturenn.Modules.Organizations.Contracts/{Fakturenn.Modules.Organizations.Contracts.csproj,ResetScope.cs,IssueDateDefault.cs,NumberScheme.cs,SellerSnapshot.cs,ISellerProvider.cs}`
- Create: `src/Fakturenn.Modules.Organizations/{Fakturenn.Modules.Organizations.csproj,OrganizationsModule.cs,Organization.cs,OrganizationRules.cs}`, `Persistence/{OrganizationsDbContext.cs,OrganizationsDbContextFactory.cs,Migrations/.editorconfig}`, `Features/{GetOrganization.cs,SaveOrganization.cs,SellerProvider.cs}`
- Create: `src/Fakturenn.Web/ModuleConfiguration.cs`
- Modify: `Fakturenn.slnx`, `src/Fakturenn.Web/Fakturenn.Web.csproj`, `src/Fakturenn.Web/FakturennWebApplication.cs`, `src/Fakturenn.Web/Program.cs`, `tests/Fakturenn.ArchitectureTests/{FakturennArchitecture.cs,ModuleBoundaryTests.cs,Fakturenn.ArchitectureTests.csproj}`, `tests/Fakturenn.IntegrationTests/{Fakturenn.IntegrationTests.csproj,PostgresFixture.cs,SetupHostFixture.cs,MessagingStartupTests.cs}`, `tests/Fakturenn.UiTests/AuthenticatedWebAppFixture.cs`, `tests/Fakturenn.UnitTests/Fakturenn.UnitTests.csproj`, `tests/Fakturenn.Web.UnitTests/MessagingCompositionTests.cs`
- Test: `tests/Fakturenn.UnitTests/Modules/OrganizationRulesTests.cs`, `tests/Fakturenn.IntegrationTests/OrganizationsTests.cs`, `tests/Fakturenn.Web.UnitTests/ModuleCompositionTests.cs`

**Interfaces:**
- Produces (Contracts, namespace `Fakturenn.Modules.Organizations.Contracts`):
  - `public enum ResetScope { PerDay, PerMonth, PerYear, Continuous }`
  - `public enum IssueDateDefault { Today, LastDayOfPreviousMonth }`
  - `public sealed record NumberScheme(string Prefix, ResetScope Scope, int Padding, long NextNumber)`
  - `public sealed record SellerSnapshot(string LegalName, string CountryCode, string VatId, string Iban, string Currency, string TimeZoneId, int PaymentTermsDays, IssueDateDefault IssueDateDefault, NumberScheme NumberScheme)`
  - `public interface ISellerProvider { Task<SellerSnapshot> GetAsync(CancellationToken cancellationToken); }`
- Produces (module): `Organization.SingletonId` (`Guid` `00000000-0000-0000-0000-000000000001`), `OrganizationRules.Validate(...)` returning `IReadOnlyList<OrganizationError>`, `GetOrganization.HandleAsync(CancellationToken) → Task<OrganizationForm>`, `SaveOrganization.HandleAsync(OrganizationForm, CancellationToken) → Task<IReadOnlyList<OrganizationError>>`.
- Produces (Web): `ModuleConfiguration.AddFakturennModules(this WebApplicationBuilder, string? connectionString, DatabaseOptions databaseOptions)` and the private helper `AddModuleContext<TContext>` used by Tasks 4, 5 and 7.

- [ ] **Step 1: Create the two projects**

```bash
dotnet new classlib --name Fakturenn.Modules.Organizations.Contracts --output src/Fakturenn.Modules.Organizations.Contracts --framework net10.0
dotnet new classlib --name Fakturenn.Modules.Organizations --output src/Fakturenn.Modules.Organizations --framework net10.0
rm src/Fakturenn.Modules.Organizations.Contracts/Class1.cs src/Fakturenn.Modules.Organizations/Class1.cs
dotnet sln Fakturenn.slnx add --solution-folder src src/Fakturenn.Modules.Organizations.Contracts src/Fakturenn.Modules.Organizations
dotnet add src/Fakturenn.Modules.Organizations.Contracts reference src/Fakturenn.SharedKernel
dotnet add src/Fakturenn.Modules.Organizations reference src/Fakturenn.Modules.Organizations.Contracts src/Fakturenn.SharedKernel
dotnet add src/Fakturenn.Modules.Organizations package Microsoft.EntityFrameworkCore
dotnet add src/Fakturenn.Modules.Organizations package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Fakturenn.Modules.Organizations package Microsoft.EntityFrameworkCore.Design
```

Then make both `.csproj` files match `src/Fakturenn.Modules.Invoices*.csproj` exactly in shape: `ImplicitUsings` and `Nullable` enabled, the `Design` reference with `IncludeAssets`/`PrivateAssets` as Invoices has it, no `Version=` attribute anywhere. Confirm `Fakturenn.slnx` lists both under `/src/` in alphabetical position. Copy `src/Fakturenn.Modules.Identity/Persistence/Migrations/.editorconfig` to `src/Fakturenn.Modules.Organizations/Persistence/Migrations/.editorconfig`.

- [ ] **Step 2: Write the contracts**

`ResetScope.cs`:

```csharp
namespace Fakturenn.Modules.Organizations.Contracts;

/// <summary>What resets the invoice counter. The date part of the number follows from it.</summary>
public enum ResetScope
{
    PerDay,
    PerMonth,
    PerYear,
    Continuous,
}
```

`IssueDateDefault.cs`:

```csharp
namespace Fakturenn.Modules.Organizations.Contracts;

/// <summary>The issue date a new draft starts with. Editable until a number is allocated.</summary>
public enum IssueDateDefault
{
    Today,
    LastDayOfPreviousMonth,
}
```

`NumberScheme.cs`:

```csharp
namespace Fakturenn.Modules.Organizations.Contracts;

/// <summary>
/// The invoice number scheme: prefix, then the date part the scope implies, then the
/// counter zero-padded to <paramref name="Padding"/> digits. <paramref name="NextNumber"/>
/// is what the first finalization in a fresh scope issues.
/// </summary>
public sealed record NumberScheme(string Prefix, ResetScope Scope, int Padding, long NextNumber);
```

`SellerSnapshot.cs`:

```csharp
namespace Fakturenn.Modules.Organizations.Contracts;

/// <summary>
/// The organization as finalization reads it. Fields may be empty: the seeded row starts
/// empty, and finalization — not this record — decides what "complete" means.
/// </summary>
public sealed record SellerSnapshot(
    string LegalName,
    string CountryCode,
    string VatId,
    string Iban,
    string Currency,
    string TimeZoneId,
    int PaymentTermsDays,
    IssueDateDefault IssueDateDefault,
    NumberScheme NumberScheme);
```

`ISellerProvider.cs`:

```csharp
namespace Fakturenn.Modules.Organizations.Contracts;

public interface ISellerProvider
{
    Task<SellerSnapshot> GetAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write the failing rule tests**

Add `<ProjectReference>`s to both Organizations projects in `tests/Fakturenn.UnitTests/Fakturenn.UnitTests.csproj`. Create `tests/Fakturenn.UnitTests/Modules/OrganizationRulesTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.UnitTests.Modules;

public sealed class OrganizationRulesTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_organization_is_valid()
    {
        OrganizationRules.Validate(Valid()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("R", true)]
    [InlineData("RE2026", true)]
    [InlineData("ABCDEFGHI", true)]
    [InlineData("ABCDEFGHIJ", false)]
    [InlineData("RÄ", false)]
    [InlineData("R E", false)]
    [InlineData("R-", false)]
    public void Prefix_rules(string prefix, bool valid)
    {
        IReadOnlyList<OrganizationError> errors = OrganizationRules.Validate(Valid() with { NumberPrefix = prefix });

        if (valid)
        {
            errors.Should().BeEmpty();
        }
        else
        {
            errors.Should().Contain(OrganizationError.PrefixInvalid);
        }
    }

    [Theory]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("DE89 3704 0044 0532 0130 00", true)]
    [InlineData("DE00000000000000000000", false)]
    [InlineData("DE89370400440532013001", false)]
    [InlineData("XX", false)]
    public void Iban_passes_mod_97(string iban, bool valid)
    {
        IReadOnlyList<OrganizationError> errors = OrganizationRules.Validate(Valid() with { Iban = iban });

        (!errors.Contains(OrganizationError.IbanInvalid)).Should().Be(valid);
    }

    [Theory]
    [InlineData("DE", "DE123456789", true)]
    [InlineData("DE", "AT123456789", false)]
    [InlineData("GR", "EL123456789", true)]
    [InlineData("DE", "DE12", true)]
    [InlineData("DE", "DE1", false)]
    public void Vat_id_is_checked_for_format_only(string country, string vatId, bool valid)
    {
        IReadOnlyList<OrganizationError> errors =
            OrganizationRules.Validate(Valid() with { CountryCode = country, VatId = vatId });

        (!errors.Contains(OrganizationError.VatIdInvalid)).Should().Be(valid);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("DEU")]
    [InlineData("ZZ")]
    public void Country_is_an_iso_3166_alpha_2_code(string country)
    {
        OrganizationRules.Validate(Valid() with { CountryCode = country, VatId = "" })
            .Should().Contain(OrganizationError.CountryInvalid);
    }

    [Fact]
    public void An_unknown_time_zone_is_refused()
    {
        OrganizationRules.Validate(Valid() with { TimeZoneId = "Mars/Olympus_Mons" })
            .Should().Contain(OrganizationError.TimeZoneInvalid);
    }

    [Fact]
    public void Empty_fields_are_allowed_while_the_organization_is_being_completed()
    {
        // The seeded row is empty, and the page saves partial input. Completeness is
        // finalization's question (spec section 5 step 3), not the form's.
        OrganizationRules.Validate(OrganizationForm.Empty).Should().BeEmpty();
    }

    // private static Methods
    private static OrganizationForm Valid() => new(
        LegalName: "Example Consulting",
        CountryCode: "DE",
        VatId: "DE123456789",
        Iban: "DE89370400440532013000",
        Currency: "EUR",
        TimeZoneId: "Europe/Berlin",
        PaymentTermsDays: 14,
        IssueDateDefault: IssueDateDefault.Today,
        NumberPrefix: "R",
        NumberScope: ResetScope.PerDay,
        NumberPadding: 1,
        NumberNextValue: 1);
}
```

- [ ] **Step 4: Run them and watch them fail to compile**

Run: `dotnet build tests/Fakturenn.UnitTests --configuration Release`
Expected: `OrganizationRules`, `OrganizationForm`, `OrganizationError` not found.

- [ ] **Step 5: Implement the entity, the form and the rules**

`OrganizationsModule.cs`:

```csharp
namespace Fakturenn.Modules.Organizations;

/// <summary>Assembly marker for the architecture tests and dependency injection.</summary>
public static class OrganizationsModule;
```

`Organization.cs`:

```csharp
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Organizations;

/// <summary>
/// The seller. Exactly one row per instance, inserted by the module's first migration under
/// <see cref="SingletonId"/> and pinned there by a check constraint (spec section 2, review C2).
/// </summary>
public sealed class Organization : IAuditable
{
    // public static readonly Fields
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    // public Properties
    public Guid Id { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string VatId { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Europe/Berlin";
    public int PaymentTermsDays { get; set; } = 14;
    public IssueDateDefault IssueDateDefault { get; set; }
    public string NumberPrefix { get; set; } = string.Empty;
    public ResetScope NumberScope { get; set; } = ResetScope.PerYear;
    public int NumberPadding { get; set; } = 4;
    public long NumberNextValue { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
```

`OrganizationRules.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.Modules.Organizations;

/// <summary>The organization page's input, field for field.</summary>
public sealed record OrganizationForm(
    string LegalName,
    string CountryCode,
    string VatId,
    string Iban,
    string Currency,
    string TimeZoneId,
    int PaymentTermsDays,
    IssueDateDefault IssueDateDefault,
    string NumberPrefix,
    ResetScope NumberScope,
    int NumberPadding,
    long NumberNextValue)
{
    public static OrganizationForm Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            "Europe/Berlin", 14, IssueDateDefault.Today, string.Empty, ResetScope.PerYear, 4, 1);
}

public enum OrganizationError
{
    CountryInvalid,
    VatIdInvalid,
    IbanInvalid,
    CurrencyInvalid,
    TimeZoneInvalid,
    PaymentTermsInvalid,
    PrefixInvalid,
    PaddingInvalid,
    NextNumberInvalid,
    SchemeLocked,
}

/// <summary>
/// Field rules (spec section 10, review M3). Format only: an empty field is not an error
/// here — finalization refuses an incomplete seller with its own named reason.
/// VAT IDs are checked for format and country prefix only; VIES validation is optional
/// and lives in the backlog.
/// </summary>
public static partial class OrganizationRules
{
    // public static Methods
    public static IReadOnlyList<OrganizationError> Validate(OrganizationForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        List<OrganizationError> errors = [];

        if (form.CountryCode.Length > 0 && !IsCountry(form.CountryCode))
        {
            errors.Add(OrganizationError.CountryInvalid);
        }

        if (form.VatId.Length > 0 && !IsVatIdFor(form.CountryCode, form.VatId))
        {
            errors.Add(OrganizationError.VatIdInvalid);
        }

        if (form.Iban.Length > 0 && !IsIban(form.Iban))
        {
            errors.Add(OrganizationError.IbanInvalid);
        }

        if (form.Currency.Length > 0 && !CurrencyPattern().IsMatch(form.Currency))
        {
            errors.Add(OrganizationError.CurrencyInvalid);
        }

        if (!IsTimeZone(form.TimeZoneId))
        {
            errors.Add(OrganizationError.TimeZoneInvalid);
        }

        if (form.PaymentTermsDays is < 0 or > 365)
        {
            errors.Add(OrganizationError.PaymentTermsInvalid);
        }

        if (!PrefixPattern().IsMatch(form.NumberPrefix))
        {
            errors.Add(OrganizationError.PrefixInvalid);
        }

        if (form.NumberPadding is < 1 or > 9)
        {
            errors.Add(OrganizationError.PaddingInvalid);
        }

        if (form.NumberNextValue < 1)
        {
            errors.Add(OrganizationError.NextNumberInvalid);
        }

        return errors;
    }

    /// <summary>Spaces are tolerated on input and removed before storing.</summary>
    public static string NormalizeIban(string iban) =>
        iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    // private static Methods
    private static bool IsCountry(string code)
    {
        if (!CountryPattern().IsMatch(code))
        {
            return false;
        }

        try
        {
            return string.Equals(new RegionInfo(code).TwoLetterISORegionName, code, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsVatIdFor(string country, string vatId)
    {
        Match match = VatIdPattern().Match(vatId);
        if (!match.Success)
        {
            return false;
        }

        // Greece uses EL in VAT IDs and GR as its ISO 3166 code.
        string prefix = match.Groups["country"].Value;
        return string.Equals(prefix, country, StringComparison.Ordinal)
            || (prefix == "EL" && country == "GR");
    }

    private static bool IsIban(string input)
    {
        string iban = NormalizeIban(input);
        if (!IbanPattern().IsMatch(iban))
        {
            return false;
        }

        // ISO 13616: move the first four characters to the end, map A..Z to 10..35, and the
        // whole number modulo 97 must be 1. Computed digit by digit to stay within a long.
        string rearranged = string.Concat(iban.AsSpan(4), iban.AsSpan(0, 4));
        long remainder = 0;
        foreach (char character in rearranged)
        {
            int value = char.IsDigit(character) ? character - '0' : character - 'A' + 10;
            remainder = ((remainder * (value < 10 ? 10 : 100)) + value) % 97;
        }

        return remainder == 1;
    }

    private static bool IsTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();

    [GeneratedRegex("^(?<country>[A-Z]{2})[A-Z0-9+*]{2,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex VatIdPattern();

    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex IbanPattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex("^[A-Za-z0-9]{0,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
```

- [ ] **Step 6: Run the rule tests**

Run: `dotnet test --project tests/Fakturenn.UnitTests --configuration Release -- --filter-class "*OrganizationRulesTests"`
Expected: PASS. If `Country_is_an_iso_3166_alpha_2_code("ZZ")` passes `RegionInfo` on this host, ICU accepts it as a region; then replace `RegionInfo` with an explicit list of the 249 ISO 3166-1 alpha-2 codes in a `FrozenSet<string>` and say so in the commit.

- [ ] **Step 7: The context, the seeded row, the singleton constraint**

`Persistence/OrganizationsDbContext.cs`:

```csharp
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.Modules.Organizations.Persistence;

public sealed class OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options)
    : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "organizations";

    // public Properties
    public DbSet<Organization> Organizations => Set<Organization>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Organization>(organization =>
        {
            organization.ToTable(table => table.HasCheckConstraint(
                "CK_Organizations_Singleton",
                $"\"Id\" = '{Organization.SingletonId}'"));
            organization.Property(o => o.LegalName).HasMaxLength(200);
            organization.Property(o => o.CountryCode).HasMaxLength(2);
            organization.Property(o => o.VatId).HasMaxLength(14);
            organization.Property(o => o.Iban).HasMaxLength(34);
            organization.Property(o => o.Currency).HasMaxLength(3);
            organization.Property(o => o.TimeZoneId).HasMaxLength(64);
            organization.Property(o => o.NumberPrefix).HasMaxLength(9);
            organization.Property(o => o.IssueDateDefault).HasConversion<string>().HasMaxLength(32);
            organization.Property(o => o.NumberScope).HasConversion<string>().HasMaxLength(16);

            // Seeded by the migration, so --migrate creates it and no request path ever
            // does. Provenance is a constant because migrations bypass the interceptor.
            organization.HasData(new Organization
            {
                Id = Organization.SingletonId,
                CreatedAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
                CreatedBy = AuditStamp.SystemUser,
                ModifiedAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
                ModifiedBy = AuditStamp.SystemUser,
            });
        });

        ConfigureAuditColumns(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // private static Methods
    private static void ConfigureAuditColumns(ModelBuilder modelBuilder)
    {
        List<IMutableEntityType> auditable = [.. modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType))];

        foreach (IMutableEntityType entityType in auditable)
        {
            modelBuilder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
```

`Persistence/OrganizationsDbContextFactory.cs` — the Invoices factory, retyped:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Organizations.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string is never opened.</summary>
public sealed class OrganizationsDbContextFactory : IDesignTimeDbContextFactory<OrganizationsDbContext>
{
    public OrganizationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
```

Generate the migration:

```bash
dotnet ef migrations add InitialOrganizations --project src/Fakturenn.Modules.Organizations --output-dir Persistence/Migrations
```

Expected: the migration creates `organizations."Organizations"` with `CK_Organizations_Singleton` and an `InsertData` for the singleton.

- [ ] **Step 8: The slices**

`Features/GetOrganization.cs`:

```csharp
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Features;

public sealed class GetOrganization(OrganizationsDbContext db)
{
    // public Methods
    public async Task<OrganizationForm> HandleAsync(CancellationToken cancellationToken)
    {
        Organization organization = await db.Organizations.AsNoTracking()
            .SingleAsync(o => o.Id == Organization.SingletonId, cancellationToken);

        return new OrganizationForm(
            organization.LegalName, organization.CountryCode, organization.VatId, organization.Iban,
            organization.Currency, organization.TimeZoneId, organization.PaymentTermsDays,
            organization.IssueDateDefault, organization.NumberPrefix, organization.NumberScope,
            organization.NumberPadding, organization.NumberNextValue);
    }
}
```

`Features/SaveOrganization.cs`:

```csharp
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Features;

public sealed class SaveOrganization(OrganizationsDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<OrganizationError>> HandleAsync(
        OrganizationForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        OrganizationForm normalized = form with
        {
            LegalName = form.LegalName.Trim(),
            CountryCode = form.CountryCode.Trim().ToUpperInvariant(),
            VatId = form.VatId.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            Iban = OrganizationRules.NormalizeIban(form.Iban),
            Currency = form.Currency.Trim().ToUpperInvariant(),
            NumberPrefix = form.NumberPrefix.Trim(),
        };

        IReadOnlyList<OrganizationError> errors = OrganizationRules.Validate(normalized);
        if (errors.Count > 0)
        {
            return errors;
        }

        Organization organization = await db.Organizations
            .SingleAsync(o => o.Id == Organization.SingletonId, cancellationToken);

        organization.LegalName = normalized.LegalName;
        organization.CountryCode = normalized.CountryCode;
        organization.VatId = normalized.VatId;
        organization.Iban = normalized.Iban;
        organization.Currency = normalized.Currency;
        organization.TimeZoneId = normalized.TimeZoneId;
        organization.PaymentTermsDays = normalized.PaymentTermsDays;
        organization.IssueDateDefault = normalized.IssueDateDefault;
        organization.NumberPrefix = normalized.NumberPrefix;
        organization.NumberScope = normalized.NumberScope;
        organization.NumberPadding = normalized.NumberPadding;
        organization.NumberNextValue = normalized.NumberNextValue;

        await db.SaveChangesAsync(cancellationToken);
        return [];
    }
}
```

`Features/SellerProvider.cs`:

```csharp
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Features;

public sealed class SellerProvider(OrganizationsDbContext db) : ISellerProvider
{
    // public Methods
    public async Task<SellerSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        Organization o = await db.Organizations.AsNoTracking()
            .SingleAsync(row => row.Id == Organization.SingletonId, cancellationToken);

        return new SellerSnapshot(
            o.LegalName, o.CountryCode, o.VatId, o.Iban, o.Currency, o.TimeZoneId, o.PaymentTermsDays,
            o.IssueDateDefault, new NumberScheme(o.NumberPrefix, o.NumberScope, o.NumberPadding, o.NumberNextValue));
    }
}
```

- [ ] **Step 9: Compose the module into the host**

Add `<ProjectReference>`s from `src/Fakturenn.Web/Fakturenn.Web.csproj` to both Organizations projects and to `src/Fakturenn.Infrastructure.Persistence` if not already referenced.

Create `src/Fakturenn.Web/ModuleConfiguration.cs`:

```csharp
using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Features;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Web;

/// <summary>
/// Registers the business modules: each context with the retry policy and the audit
/// interceptor, none enrolled with Wolverine (nothing publishes in Stage 1), and the
/// slices and providers the pages and finalization resolve.
/// </summary>
public static class ModuleConfiguration
{
    // public static Methods
    public static void AddFakturennModules(
        this WebApplicationBuilder builder,
        string? connectionString,
        DatabaseOptions databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(databaseOptions);

        AddModuleContext<OrganizationsDbContext>(builder.Services, connectionString, databaseOptions);
        builder.Services.AddScoped<GetOrganization>();
        builder.Services.AddScoped<SaveOrganization>();
        builder.Services.AddScoped<ISellerProvider, SellerProvider>();
    }

    // private static Methods
    private static void AddModuleContext<TContext>(
        IServiceCollection services,
        string? connectionString,
        DatabaseOptions databaseOptions)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                    databaseOptions.MaxRetries,
                    TimeSpan.FromSeconds(databaseOptions.RetryDelaySeconds),
                    errorCodesToAdd: null))
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));
}
```

In `FakturennWebApplication.Build`, after `builder.AddFakturennIdentity(connectionString, databaseOptions);`: `builder.AddFakturennModules(connectionString, databaseOptions);`.

In `Program.cs`, inside the `--migrate` block, add a factory and put it in `createMigrationContexts`:

```csharp
    OrganizationsDbContext CreateOrganizationsMigrationContext() =>
        new(new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseNpgsql(migrationConnectionString)
            .Options);
```

```csharp
    Func<DbContext>[] createMigrationContexts =
    [
        CreateDataProtectionMigrationContext,
        CreateIdentityMigrationContext,
        CreateOrganizationsMigrationContext,
        CreateMigrationContext,
    ];
```

- [ ] **Step 10: The checklist edits that no test forces**

- `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs` — add to `Loaded`: `typeof(Modules.Organizations.Contracts.SellerSnapshot).Assembly,` and `typeof(Modules.Organizations.OrganizationsModule).Assembly,`; add `<ProjectReference>`s in its `.csproj`.
- `tests/Fakturenn.ArchitectureTests/ModuleBoundaryTests.cs` — add `"Fakturenn.Modules.Organizations"` and `"Fakturenn.Modules.Organizations.Contracts"` to the list.
- `tests/Fakturenn.IntegrationTests/Fakturenn.IntegrationTests.csproj` — `<ProjectReference>` to `Fakturenn.Modules.Organizations`.
- `tests/Fakturenn.IntegrationTests/PostgresFixture.cs` — add:

```csharp
    public TContext Create<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext =>
        create(new DbContextOptionsBuilder<TContext>().UseNpgsql(ConnectionString).Options);
```

- `tests/Fakturenn.IntegrationTests/SetupHostFixture.cs` and `tests/Fakturenn.UiTests/AuthenticatedWebAppFixture.cs` — each already migrates the existing contexts by hand before `StartAsync`; migrate `OrganizationsDbContext` there too, the same way.
- `tests/Fakturenn.IntegrationTests/MessagingStartupTests.cs` — in `CreateEfMigratedDatabaseAsync`, migrate `OrganizationsDbContext` after Identity; add `OrganizationsDbContext.SchemaName` to the expected schema set in the precondition assert.
- `tests/Fakturenn.Web.UnitTests/MessagingCompositionTests.cs` — in `The_unenrolled_contexts_carry_no_wolverine_model_annotation`, resolve `OrganizationsDbContext` and assert `.Model.FindAnnotation(WolverineEnabledAnnotation).Should().BeNull("organization data publishes nothing in Stage 1")`.

- [ ] **Step 11: Write the failing host-composition test for the interceptor**

Create `tests/Fakturenn.Web.UnitTests/ModuleCompositionTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Every business context carries the audit interceptor. Without it a row saves with empty
/// provenance and a 0001-01-01 timestamp, and no constraint objects (review C5).
/// </summary>
public sealed class ModuleCompositionTests
{
    // public Methods
    [Fact]
    public void The_organizations_context_records_provenance()
    {
        AssertAudited<OrganizationsDbContext>();
    }

    // internal static Methods
    internal static void AssertAudited<TContext>()
        where TContext : DbContext
    {
        WebApplication app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);
        using IServiceScope scope = app.Services.CreateScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();

        CoreOptionsExtension? core = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

        core!.Interceptors.Should().ContainSingle(interceptor => interceptor is AuditSaveChangesInterceptor,
            $"{typeof(TContext).Name} must stamp CreatedBy and ModifiedBy");
    }
}
```

Run it: `dotnet test --project tests/Fakturenn.Web.UnitTests --configuration Release -- --filter-class "*ModuleCompositionTests"`. Expected: PASS (the registration exists). Mutation: copy `ModuleConfiguration.cs` aside, delete the `.AddInterceptors(...)` call, rebuild, rerun — Expected: FAIL. Restore, `sha256sum --check`.

- [ ] **Step 12: Write the integration tests**

Create `tests/Fakturenn.IntegrationTests/OrganizationsTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Features;
using Fakturenn.Modules.Organizations.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.IntegrationTests;

public sealed class OrganizationsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task Migrating_creates_exactly_one_empty_organization()
    {
        await using OrganizationsDbContext db = await MigratedAsync();

        List<Organization> rows = await db.Organizations.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

        // Only the count and the key: the class fixture shares this row with the save test,
        // so its field values depend on order.
        rows.Should().ContainSingle().Which.Id.Should().Be(Organization.SingletonId);
    }

    [Fact]
    public async Task The_database_refuses_a_second_organization()
    {
        // Spec section 2: "organization isolation is tested" means exactly this.
        await using OrganizationsDbContext db = await MigratedAsync();
        db.Organizations.Add(new Organization
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
            ModifiedAt = DateTimeOffset.UtcNow,
            ModifiedBy = "test",
        });

        Func<Task> second = () => db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await second.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("CK_Organizations_Singleton");
    }

    [Fact]
    public async Task A_saved_organization_reads_back_normalized()
    {
        await using OrganizationsDbContext db = await MigratedAsync();
        OrganizationForm form = OrganizationForm.Empty with
        {
            LegalName = "Example Consulting",
            CountryCode = "de",
            VatId = "DE 123456789",
            Iban = "de89 3704 0044 0532 0130 00",
            Currency = "eur",
            NumberPrefix = "R",
            NumberScope = ResetScope.PerDay,
            NumberPadding = 1,
        };

        IReadOnlyList<OrganizationError> errors =
            await new SaveOrganization(db).HandleAsync(form, TestContext.Current.CancellationToken);
        SellerSnapshot seller = await new SellerProvider(db).GetAsync(TestContext.Current.CancellationToken);

        errors.Should().BeEmpty();
        seller.CountryCode.Should().Be("DE");
        seller.VatId.Should().Be("DE123456789");
        seller.Iban.Should().Be("DE89370400440532013000");
        seller.Currency.Should().Be("EUR");
        seller.NumberScheme.Should().Be(new NumberScheme("R", ResetScope.PerDay, 1, 1));
    }

    // private Methods
    private async Task<OrganizationsDbContext> MigratedAsync()
    {
        OrganizationsDbContext db = postgres.Create<OrganizationsDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return db;
    }
}
```


- [ ] **Step 13: Run every suite**

```bash
dotnet build --configuration Release --no-incremental
for p in Fakturenn.UnitTests Fakturenn.Web.UnitTests Fakturenn.ArchitectureTests Fakturenn.IntegrationTests Fakturenn.UiTests; do
  DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project "tests/$p" --configuration Release
done
```

Expected: all PASS, including `The_loader_omits_no_assembly_declared_under_src_in_the_solution` and the messaging precondition.

- [ ] **Step 14: Commit**

```bash
git add Fakturenn.slnx src tests
git commit -m "feat(organizations): seed the one organization and validate its record"
```

---

### Task 4: Customers and Projects

**Files:**
- Create: `src/Fakturenn.Modules.Customers.Contracts/{csproj,CustomerSnapshot.cs,ICustomerProvider.cs}`, `src/Fakturenn.Modules.Customers/{csproj,CustomersModule.cs,Customer.cs,CustomerCatalogItemReference.cs,CustomerRules.cs}`, `Persistence/{CustomersDbContext.cs,CustomersDbContextFactory.cs,Migrations/.editorconfig}`, `Features/{ListCustomers.cs,SaveCustomer.cs,SaveCatalogItemReference.cs,CustomerProvider.cs}`
- Create: `src/Fakturenn.Modules.Projects.Contracts/{csproj,ProjectSnapshot.cs,IProjectProvider.cs}`, `src/Fakturenn.Modules.Projects/{csproj,ProjectsModule.cs,Project.cs}`, `Persistence/{ProjectsDbContext.cs,ProjectsDbContextFactory.cs,Migrations/.editorconfig}`, `Features/{ListProjects.cs,SaveProject.cs,ProjectProvider.cs}`
- Modify: the same composition and checklist files as Task 3 Steps 9–10, for both modules
- Test: `tests/Fakturenn.UnitTests/Modules/CustomerRulesTests.cs`, `tests/Fakturenn.IntegrationTests/CustomersAndProjectsTests.cs`

**Interfaces:**
- Consumes: `OrganizationRules` is **not** reused (module boundary); customers get their own small rule class.
- Produces (`Fakturenn.Modules.Customers.Contracts`):
  - `public sealed record CustomerSnapshot(Guid Id, string LegalName, string CountryCode, string? VatId, string CustomerNumber)`
  - `public interface ICustomerProvider { Task<CustomerSnapshot?> FindAsync(Guid customerId, CancellationToken cancellationToken); Task<string?> FindCatalogItemReferenceAsync(Guid customerId, Guid catalogItemId, CancellationToken cancellationToken); }`
- Produces (`Fakturenn.Modules.Projects.Contracts`):
  - `public sealed record ProjectSnapshot(Guid Id, Guid CustomerId, string Reference)`
  - `public interface IProjectProvider { Task<ProjectSnapshot?> FindAsync(Guid projectId, CancellationToken cancellationToken); }`
- Produces (module slices): `SaveCustomer.HandleAsync(CustomerForm, CancellationToken) → Task<SaveResult>`, `ListCustomers.HandleAsync(CancellationToken) → Task<IReadOnlyList<CustomerSnapshot>>`, `SaveCatalogItemReference.HandleAsync(Guid customerId, Guid catalogItemId, string reference, CancellationToken) → Task`, `SaveProject.HandleAsync(Guid? projectId, Guid customerId, string reference, CancellationToken) → Task<Guid>`, `ListProjects.HandleAsync(Guid customerId, CancellationToken) → Task<IReadOnlyList<ProjectSnapshot>>`. Where `public sealed record CustomerForm(Guid? Id, string LegalName, string CountryCode, string VatId, string CustomerNumber)`, `public sealed record SaveResult(Guid? Id, IReadOnlyList<CustomerError> Errors)`, `public enum CustomerError { LegalNameMissing, CountryInvalid, VatIdInvalid, CustomerNumberMissing, CustomerNumberTaken }`.

- [ ] **Step 1: Create the four projects** — exactly Task 3 Step 1, for `Fakturenn.Modules.Customers(.Contracts)` and `Fakturenn.Modules.Projects(.Contracts)`. Copy the migrations `.editorconfig` into both modules.

- [ ] **Step 2: Contracts**

`src/Fakturenn.Modules.Customers.Contracts/CustomerSnapshot.cs`:

```csharp
namespace Fakturenn.Modules.Customers.Contracts;

/// <summary>A customer as finalization reads it. VatId is null for a private individual.</summary>
public sealed record CustomerSnapshot(Guid Id, string LegalName, string CountryCode, string? VatId, string CustomerNumber);
```

`src/Fakturenn.Modules.Customers.Contracts/ICustomerProvider.cs`:

```csharp
namespace Fakturenn.Modules.Customers.Contracts;

public interface ICustomerProvider
{
    Task<CustomerSnapshot?> FindAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// The customer's own name for a catalog item — the CustomerCatalogItemNumber — or null.
    /// Per customer, which is why it lives here and not on the catalog item (review C7).
    /// </summary>
    Task<string?> FindCatalogItemReferenceAsync(Guid customerId, Guid catalogItemId, CancellationToken cancellationToken);
}
```

`src/Fakturenn.Modules.Projects.Contracts/ProjectSnapshot.cs` and `IProjectProvider.cs`:

```csharp
namespace Fakturenn.Modules.Projects.Contracts;

/// <summary>A project belongs to exactly one customer; Reference is the customer's project number.</summary>
public sealed record ProjectSnapshot(Guid Id, Guid CustomerId, string Reference);
```

```csharp
namespace Fakturenn.Modules.Projects.Contracts;

public interface IProjectProvider
{
    Task<ProjectSnapshot?> FindAsync(Guid projectId, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write the failing customer rule tests**

Reference both Customers projects from `tests/Fakturenn.UnitTests`. Create `tests/Fakturenn.UnitTests/Modules/CustomerRulesTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Customers;

namespace Fakturenn.UnitTests.Modules;

public sealed class CustomerRulesTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_customer_is_valid()
    {
        CustomerRules.Validate(new CustomerForm(null, "Example Client GmbH", "DE", "", "C-4711")).Should().BeEmpty();
    }

    [Fact]
    public void A_private_individual_has_no_vat_id()
    {
        CustomerRules.Validate(new CustomerForm(null, "Erika Mustermann", "DE", "", "C-0001")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "DE", "", "C-1", CustomerError.LegalNameMissing)]
    [InlineData("A", "", "", "C-1", CustomerError.CountryInvalid)]
    [InlineData("A", "AT", "DE123456789", "C-1", CustomerError.VatIdInvalid)]
    [InlineData("A", "DE", "", " ", CustomerError.CustomerNumberMissing)]
    public void Required_fields_and_formats(string name, string country, string vatId, string number, CustomerError expected)
    {
        CustomerRules.Validate(new CustomerForm(null, name, country, vatId, number)).Should().Contain(expected);
    }
}
```

Run: `dotnet build tests/Fakturenn.UnitTests --configuration Release`. Expected: compile errors for `CustomerRules`, `CustomerForm`, `CustomerError`.

- [ ] **Step 4: Customers entities, rules and context**

`CustomersModule.cs`:

```csharp
namespace Fakturenn.Modules.Customers;

/// <summary>Assembly marker for the architecture tests and dependency injection.</summary>
public static class CustomersModule;
```

`Customer.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Customers;

/// <summary>
/// Only what Stage 1 reads. Delivery email, e-invoice profile, PDF template and signing
/// policy arrive with the stage that reads each (spec section 3, review M11).
/// </summary>
public sealed class Customer : IAuditable
{
    // public Properties
    public Guid Id { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string? VatId { get; set; }
    public string CustomerNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
```

`CustomerCatalogItemReference.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Customers;

/// <summary>
/// The customer's own number for a catalog item — "SI-9001" is Example Client GmbH's name
/// for DEV-BACKEND. Holds the catalog item's id only; Catalog is another module.
/// </summary>
public sealed class CustomerCatalogItemReference : IAuditable
{
    // public Properties
    public Guid CustomerId { get; set; }
    public Guid CatalogItemId { get; set; }
    public string CustomerCatalogItemNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
```

`CustomerRules.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Fakturenn.Modules.Customers;

public sealed record CustomerForm(Guid? Id, string LegalName, string CountryCode, string VatId, string CustomerNumber);

public sealed record SaveResult(Guid? Id, IReadOnlyList<CustomerError> Errors);

public enum CustomerError
{
    LegalNameMissing,
    CountryInvalid,
    VatIdInvalid,
    CustomerNumberMissing,
    CustomerNumberTaken,
}

/// <summary>Required fields and formats. VAT ID format only — VIES is optional (spec section 7).</summary>
public static partial class CustomerRules
{
    // public static Methods
    public static IReadOnlyList<CustomerError> Validate(CustomerForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        List<CustomerError> errors = [];

        if (string.IsNullOrWhiteSpace(form.LegalName))
        {
            errors.Add(CustomerError.LegalNameMissing);
        }

        if (!IsCountry(form.CountryCode))
        {
            errors.Add(CustomerError.CountryInvalid);
        }

        if (form.VatId.Length > 0 && !IsVatIdFor(form.CountryCode, form.VatId))
        {
            errors.Add(CustomerError.VatIdInvalid);
        }

        if (string.IsNullOrWhiteSpace(form.CustomerNumber))
        {
            errors.Add(CustomerError.CustomerNumberMissing);
        }

        return errors;
    }

    // private static Methods
    private static bool IsCountry(string code)
    {
        if (!CountryPattern().IsMatch(code))
        {
            return false;
        }

        try
        {
            return string.Equals(new RegionInfo(code).TwoLetterISORegionName, code, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsVatIdFor(string country, string vatId)
    {
        Match match = VatIdPattern().Match(vatId);
        if (!match.Success)
        {
            return false;
        }

        string prefix = match.Groups["country"].Value;
        return string.Equals(prefix, country, StringComparison.Ordinal) || (prefix == "EL" && country == "GR");
    }

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();

    [GeneratedRegex("^(?<country>[A-Z]{2})[A-Z0-9+*]{2,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex VatIdPattern();
}
```

`Persistence/CustomersDbContext.cs` — schema `customers`, two sets, unique customer number, composite key for the reference, and the same `ConfigureAuditColumns` loop as `OrganizationsDbContext` (copy it verbatim into this class):

```csharp
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.Modules.Customers.Persistence;

public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "customers";

    // public Properties
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerCatalogItemReference> CatalogItemReferences => Set<CustomerCatalogItemReference>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Customer>(customer =>
        {
            customer.Property(c => c.LegalName).HasMaxLength(200);
            customer.Property(c => c.CountryCode).HasMaxLength(2);
            customer.Property(c => c.VatId).HasMaxLength(14);
            customer.Property(c => c.CustomerNumber).HasMaxLength(50);
            customer.HasIndex(c => c.CustomerNumber).IsUnique();
        });

        modelBuilder.Entity<CustomerCatalogItemReference>(reference =>
        {
            reference.HasKey(r => new { r.CustomerId, r.CatalogItemId });
            reference.Property(r => r.CustomerCatalogItemNumber).HasMaxLength(50);
            reference.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureAuditColumns(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // private static Methods
    private static void ConfigureAuditColumns(ModelBuilder modelBuilder)
    {
        List<IMutableEntityType> auditable = [.. modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType))];

        foreach (IMutableEntityType entityType in auditable)
        {
            modelBuilder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
```

`Persistence/CustomersDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Customers.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string is never opened.</summary>
public sealed class CustomersDbContextFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CustomersDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
```

- [ ] **Step 5: Customers slices**

`Features/SaveCustomer.cs`:

```csharp
using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class SaveCustomer(CustomersDbContext db)
{
    // public Methods
    public async Task<SaveResult> HandleAsync(CustomerForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        CustomerForm normalized = form with
        {
            LegalName = form.LegalName.Trim(),
            CountryCode = form.CountryCode.Trim().ToUpperInvariant(),
            VatId = form.VatId.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            CustomerNumber = form.CustomerNumber.Trim(),
        };

        List<CustomerError> errors = [.. CustomerRules.Validate(normalized)];
        bool taken = await db.Customers.AnyAsync(
            c => c.CustomerNumber == normalized.CustomerNumber && c.Id != normalized.Id, cancellationToken);
        if (taken)
        {
            errors.Add(CustomerError.CustomerNumberTaken);
        }

        if (errors.Count > 0)
        {
            return new SaveResult(null, errors);
        }

        Customer customer = normalized.Id is Guid id
            ? await db.Customers.SingleAsync(c => c.Id == id, cancellationToken)
            : db.Customers.Add(new Customer { Id = Guid.CreateVersion7() }).Entity;

        customer.LegalName = normalized.LegalName;
        customer.CountryCode = normalized.CountryCode;
        customer.VatId = normalized.VatId.Length == 0 ? null : normalized.VatId;
        customer.CustomerNumber = normalized.CustomerNumber;

        await db.SaveChangesAsync(cancellationToken);
        return new SaveResult(customer.Id, []);
    }
}
```

The `AnyAsync` check is the friendly message; the unique index is the guarantee. Two saves racing on one number get one `DbUpdateException`; the page shows `CustomerNumberTaken` for it too (Task 8).

`Features/SaveCatalogItemReference.cs`:

```csharp
using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class SaveCatalogItemReference(CustomersDbContext db)
{
    // public Methods
    /// <summary>Sets, replaces or — with an empty reference — removes the customer's number for an item.</summary>
    public async Task HandleAsync(Guid customerId, Guid catalogItemId, string reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        CustomerCatalogItemReference? existing = await db.CatalogItemReferences.SingleOrDefaultAsync(
            r => r.CustomerId == customerId && r.CatalogItemId == catalogItemId, cancellationToken);
        string trimmed = reference.Trim();

        if (trimmed.Length == 0)
        {
            if (existing is not null)
            {
                db.CatalogItemReferences.Remove(existing);
            }
        }
        else if (existing is null)
        {
            db.CatalogItemReferences.Add(new CustomerCatalogItemReference
            {
                CustomerId = customerId,
                CatalogItemId = catalogItemId,
                CustomerCatalogItemNumber = trimmed,
            });
        }
        else
        {
            existing.CustomerCatalogItemNumber = trimmed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
```

`Features/ListCustomers.cs`:

```csharp
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class ListCustomers(CustomersDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<CustomerSnapshot>> HandleAsync(CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking()
            .OrderBy(c => c.CustomerNumber)
            .Select(c => new CustomerSnapshot(c.Id, c.LegalName, c.CountryCode, c.VatId, c.CustomerNumber))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> ReferencesAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.CatalogItemReferences.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .ToDictionaryAsync(r => r.CatalogItemId, r => r.CustomerCatalogItemNumber, cancellationToken);
}
```

`Features/CustomerProvider.cs`:

```csharp
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class CustomerProvider(CustomersDbContext db) : ICustomerProvider
{
    // public Methods
    public async Task<CustomerSnapshot?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerSnapshot(c.Id, c.LegalName, c.CountryCode, c.VatId, c.CustomerNumber))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<string?> FindCatalogItemReferenceAsync(Guid customerId, Guid catalogItemId, CancellationToken cancellationToken) =>
        await db.CatalogItemReferences.AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.CatalogItemId == catalogItemId)
            .Select(r => r.CustomerCatalogItemNumber)
            .SingleOrDefaultAsync(cancellationToken);
}
```

- [ ] **Step 6: Projects module**

`ProjectsModule.cs` (marker, as before), `Project.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Projects;

/// <summary>Belongs to one customer, by id only — Customers is another module (spec section 3, review C8).</summary>
public sealed class Project : IAuditable
{
    // public Properties
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
```

`Persistence/ProjectsDbContext.cs`:

```csharp
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.Modules.Projects.Persistence;

public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "projects";

    // public Properties
    public DbSet<Project> Projects => Set<Project>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Project>(project =>
        {
            project.Property(p => p.Reference).HasMaxLength(50);
            project.HasIndex(p => new { p.CustomerId, p.Reference }).IsUnique();
        });

        ConfigureAuditColumns(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // private static Methods
    private static void ConfigureAuditColumns(ModelBuilder modelBuilder)
    {
        List<IMutableEntityType> auditable = [.. modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType))];

        foreach (IMutableEntityType entityType in auditable)
        {
            modelBuilder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
```

`Persistence/ProjectsDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Projects.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string is never opened.</summary>
public sealed class ProjectsDbContextFactory : IDesignTimeDbContextFactory<ProjectsDbContext>
{
    public ProjectsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
```

`Features/SaveProject.cs`:

```csharp
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Projects.Features;

public sealed class SaveProject(ProjectsDbContext db)
{
    // public Methods
    public async Task<Guid> HandleAsync(Guid? projectId, Guid customerId, string reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        Project project = projectId is Guid id
            ? await db.Projects.SingleAsync(p => p.Id == id && p.CustomerId == customerId, cancellationToken)
            : db.Projects.Add(new Project { Id = Guid.CreateVersion7(), CustomerId = customerId }).Entity;

        project.Reference = reference.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return project.Id;
    }
}
```

`Features/ListProjects.cs`:

```csharp
using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Projects.Features;

public sealed class ListProjects(ProjectsDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<ProjectSnapshot>> HandleAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .OrderBy(p => p.Reference)
            .Select(p => new ProjectSnapshot(p.Id, p.CustomerId, p.Reference))
            .ToListAsync(cancellationToken);
}
```

`Features/ProjectProvider.cs`:

```csharp
using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Projects.Features;

public sealed class ProjectProvider(ProjectsDbContext db) : IProjectProvider
{
    // public Methods
    public async Task<ProjectSnapshot?> FindAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectSnapshot(p.Id, p.CustomerId, p.Reference))
            .SingleOrDefaultAsync(cancellationToken);
}
```

- [ ] **Step 7: Migrations, composition and checklist**

```bash
dotnet ef migrations add InitialCustomers --project src/Fakturenn.Modules.Customers --output-dir Persistence/Migrations
dotnet ef migrations add InitialProjects --project src/Fakturenn.Modules.Projects --output-dir Persistence/Migrations
```

In `ModuleConfiguration.AddFakturennModules`, append:

```csharp
        AddModuleContext<CustomersDbContext>(builder.Services, connectionString, databaseOptions);
        builder.Services.AddScoped<ListCustomers>();
        builder.Services.AddScoped<SaveCustomer>();
        builder.Services.AddScoped<SaveCatalogItemReference>();
        builder.Services.AddScoped<ICustomerProvider, CustomerProvider>();

        AddModuleContext<ProjectsDbContext>(builder.Services, connectionString, databaseOptions);
        builder.Services.AddScoped<ListProjects>();
        builder.Services.AddScoped<SaveProject>();
        builder.Services.AddScoped<IProjectProvider, ProjectProvider>();
```

Repeat Task 3 Step 9's `Program.cs` edit (two more factories in `createMigrationContexts`) and every bullet of Task 3 Step 10 for both modules: `Loaded`, the boundary list, project references, the two fixtures, `CreateEfMigratedDatabaseAsync` plus its expected schema set, and the unenrolled-annotation assertion. Add to `ModuleCompositionTests`:

```csharp
    [Fact]
    public void The_customers_context_records_provenance() => AssertAudited<CustomersDbContext>();

    [Fact]
    public void The_projects_context_records_provenance() => AssertAudited<ProjectsDbContext>();
```

- [ ] **Step 8: Integration tests**

Create `tests/Fakturenn.IntegrationTests/CustomersAndProjectsTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Customers;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Customers.Features;
using Fakturenn.Modules.Customers.Persistence;
using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.Modules.Projects.Features;
using Fakturenn.Modules.Projects.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class CustomersAndProjectsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task A_customer_number_is_unique()
    {
        await using CustomersDbContext db = await CustomersAsync();
        SaveResult first = await new SaveCustomer(db).HandleAsync(
            new CustomerForm(null, "Example Client GmbH", "DE", "", "C-UNIQUE"), TestContext.Current.CancellationToken);

        SaveResult second = await new SaveCustomer(db).HandleAsync(
            new CustomerForm(null, "Another GmbH", "DE", "", "C-UNIQUE"), TestContext.Current.CancellationToken);

        first.Errors.Should().BeEmpty();
        second.Errors.Should().ContainSingle().Which.Should().Be(CustomerError.CustomerNumberTaken);
    }

    [Fact]
    public async Task The_customer_catalog_item_number_is_per_customer()
    {
        // Review C7: SI-9001 is one customer's name for an item, not the item's.
        await using CustomersDbContext db = await CustomersAsync();
        Guid item = Guid.CreateVersion7();
        Guid a = (await new SaveCustomer(db).HandleAsync(new CustomerForm(null, "A", "DE", "", "C-REF-A"), TestContext.Current.CancellationToken)).Id!.Value;
        Guid b = (await new SaveCustomer(db).HandleAsync(new CustomerForm(null, "B", "DE", "", "C-REF-B"), TestContext.Current.CancellationToken)).Id!.Value;

        await new SaveCatalogItemReference(db).HandleAsync(a, item, "SI-9001", TestContext.Current.CancellationToken);

        ICustomerProvider provider = new CustomerProvider(db);
        (await provider.FindCatalogItemReferenceAsync(a, item, TestContext.Current.CancellationToken)).Should().Be("SI-9001");
        (await provider.FindCatalogItemReferenceAsync(b, item, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_project_belongs_to_the_customer_it_was_created_on()
    {
        await using ProjectsDbContext db = postgres.Create<ProjectsDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Guid customer = Guid.CreateVersion7();

        Guid project = await new SaveProject(db).HandleAsync(null, customer, " PRJ-2026-083 ", TestContext.Current.CancellationToken);

        (await new ProjectProvider(db).FindAsync(project, TestContext.Current.CancellationToken))
            .Should().Be(new ProjectSnapshot(project, customer, "PRJ-2026-083"));
    }

    // private Methods
    private async Task<CustomersDbContext> CustomersAsync()
    {
        CustomersDbContext db = postgres.Create<CustomersDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return db;
    }
}
```

- [ ] **Step 9: Run every suite** — Task 3 Step 13's loop, plus `Fakturenn.UnitTests`. Expected: all PASS.

- [ ] **Step 10: Commit**

```bash
git add Fakturenn.slnx src tests
git commit -m "feat(customers,projects): customers with per-customer item numbers, and their projects"
```

---

### Task 5: Catalog

**Files:**
- Create: `src/Fakturenn.Modules.Catalog.Contracts/{csproj,CatalogItemSnapshot.cs,ICatalogItemProvider.cs}`, `src/Fakturenn.Modules.Catalog/{csproj,CatalogModule.cs,CatalogItem.cs,CatalogRules.cs}`, `Persistence/{CatalogDbContext.cs,CatalogDbContextFactory.cs,Migrations/.editorconfig}`, `Features/{ListCatalogItems.cs,SaveCatalogItem.cs,CatalogItemProvider.cs}`
- Modify: the composition and checklist files, as Task 3 Steps 9–10
- Test: `tests/Fakturenn.UnitTests/Modules/CatalogRulesTests.cs`, `tests/Fakturenn.IntegrationTests/CatalogTests.cs`

**Interfaces:**
- Produces: `public sealed record CatalogItemSnapshot(Guid Id, string CatalogItemNumber, string Name, string Unit, decimal UnitPrice, string Currency, decimal VatRate)`; `public interface ICatalogItemProvider { Task<CatalogItemSnapshot?> FindAsync(Guid catalogItemId, CancellationToken cancellationToken); }`; `SaveCatalogItem.HandleAsync(CatalogItemForm, CancellationToken) → Task<CatalogSaveResult>`; `ListCatalogItems.HandleAsync(CancellationToken) → Task<IReadOnlyList<CatalogItemSnapshot>>`. With `public sealed record CatalogItemForm(Guid? Id, string CatalogItemNumber, string Name, string Unit, decimal UnitPrice, string Currency, decimal VatRate)`, `public sealed record CatalogSaveResult(Guid? Id, IReadOnlyList<CatalogError> Errors)`, `public enum CatalogError { NumberMissing, NumberTaken, NameMissing, UnitInvalid, PriceNegative, CurrencyInvalid, VatRateInvalid }`.

- [ ] **Step 1: Projects** — Task 3 Step 1 for `Fakturenn.Modules.Catalog(.Contracts)`.

- [ ] **Step 2: Contracts**

```csharp
namespace Fakturenn.Modules.Catalog.Contracts;

/// <summary>
/// A catalog item as finalization reads it. Unit is a UN/ECE Recommendation 20 code
/// ("HUR" is an hour); VatRate is in whole percent. No item type: nothing in Stage 1 reads it.
/// </summary>
public sealed record CatalogItemSnapshot(
    Guid Id,
    string CatalogItemNumber,
    string Name,
    string Unit,
    decimal UnitPrice,
    string Currency,
    decimal VatRate);
```

```csharp
namespace Fakturenn.Modules.Catalog.Contracts;

public interface ICatalogItemProvider
{
    Task<CatalogItemSnapshot?> FindAsync(Guid catalogItemId, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write the failing rule tests**

`tests/Fakturenn.UnitTests/Modules/CatalogRulesTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Catalog;

namespace Fakturenn.UnitTests.Modules;

public sealed class CatalogRulesTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_item_is_valid()
    {
        CatalogRules.Validate(Item()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.01, CatalogError.PriceNegative)]
    public void Price_is_not_negative(decimal price, CatalogError expected)
    {
        CatalogRules.Validate(Item() with { UnitPrice = price }).Should().Contain(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    public void Vat_rate_is_a_percentage(decimal rate)
    {
        CatalogRules.Validate(Item() with { VatRate = rate }).Should().Contain(CatalogError.VatRateInvalid);
    }

    [Theory]
    [InlineData("hur")]
    [InlineData("")]
    [InlineData("HOURS")]
    public void Unit_is_an_uppercase_code(string unit)
    {
        CatalogRules.Validate(Item() with { Unit = unit }).Should().Contain(CatalogError.UnitInvalid);
    }

    // private static Methods
    private static CatalogItemForm Item() =>
        new(null, "DEV-BACKEND", "Backend development", "HUR", 100.00m, "EUR", 19m);
}
```

Run: `dotnet build tests/Fakturenn.UnitTests --configuration Release`. Expected: compile errors.

- [ ] **Step 4: Entity, rules, context, slices**

`CatalogItem.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Catalog;

public sealed class CatalogItem : IAuditable
{
    // public Properties
    public Guid Id { get; set; }
    public string CatalogItemNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal VatRate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
```

`CatalogRules.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Fakturenn.Modules.Catalog;

public sealed record CatalogItemForm(Guid? Id, string CatalogItemNumber, string Name, string Unit, decimal UnitPrice, string Currency, decimal VatRate);

public sealed record CatalogSaveResult(Guid? Id, IReadOnlyList<CatalogError> Errors);

public enum CatalogError
{
    NumberMissing,
    NumberTaken,
    NameMissing,
    UnitInvalid,
    PriceNegative,
    CurrencyInvalid,
    VatRateInvalid,
}

public static partial class CatalogRules
{
    // public static Methods
    public static IReadOnlyList<CatalogError> Validate(CatalogItemForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        List<CatalogError> errors = [];
        if (string.IsNullOrWhiteSpace(form.CatalogItemNumber))
        {
            errors.Add(CatalogError.NumberMissing);
        }

        if (string.IsNullOrWhiteSpace(form.Name))
        {
            errors.Add(CatalogError.NameMissing);
        }

        if (!UnitPattern().IsMatch(form.Unit))
        {
            errors.Add(CatalogError.UnitInvalid);
        }

        if (form.UnitPrice < 0m)
        {
            errors.Add(CatalogError.PriceNegative);
        }

        if (!CurrencyPattern().IsMatch(form.Currency))
        {
            errors.Add(CatalogError.CurrencyInvalid);
        }

        if (form.VatRate is < 0m or > 100m)
        {
            errors.Add(CatalogError.VatRateInvalid);
        }

        return errors;
    }

    // private static Methods
    [GeneratedRegex("^[A-Z0-9]{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex UnitPattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
```

`CatalogModule.cs`:

```csharp
namespace Fakturenn.Modules.Catalog;

/// <summary>Assembly marker for the architecture tests and dependency injection.</summary>
public static class CatalogModule;
```

`Persistence/CatalogDbContext.cs`:

```csharp
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.Modules.Catalog.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "catalog";

    // public Properties
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<CatalogItem>(item =>
        {
            item.Property(i => i.CatalogItemNumber).HasMaxLength(50);
            item.HasIndex(i => i.CatalogItemNumber).IsUnique();
            item.Property(i => i.Name).HasMaxLength(200);
            item.Property(i => i.Unit).HasMaxLength(3);
            item.Property(i => i.Currency).HasMaxLength(3);
            item.Property(i => i.UnitPrice).HasPrecision(18, 4);
            item.Property(i => i.VatRate).HasPrecision(5, 2);
        });

        ConfigureAuditColumns(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // private static Methods
    private static void ConfigureAuditColumns(ModelBuilder modelBuilder)
    {
        List<IMutableEntityType> auditable = [.. modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType))];

        foreach (IMutableEntityType entityType in auditable)
        {
            modelBuilder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
```

`Persistence/CatalogDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Catalog.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string is never opened.</summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
```

`Features/SaveCatalogItem.cs`:

```csharp
using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Catalog.Features;

public sealed class SaveCatalogItem(CatalogDbContext db)
{
    // public Methods
    public async Task<CatalogSaveResult> HandleAsync(CatalogItemForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        CatalogItemForm normalized = form with
        {
            CatalogItemNumber = form.CatalogItemNumber.Trim(),
            Name = form.Name.Trim(),
            Unit = form.Unit.Trim().ToUpperInvariant(),
            Currency = form.Currency.Trim().ToUpperInvariant(),
        };

        List<CatalogError> errors = [.. CatalogRules.Validate(normalized)];
        bool taken = await db.CatalogItems.AnyAsync(
            i => i.CatalogItemNumber == normalized.CatalogItemNumber && i.Id != normalized.Id, cancellationToken);
        if (taken)
        {
            errors.Add(CatalogError.NumberTaken);
        }

        if (errors.Count > 0)
        {
            return new CatalogSaveResult(null, errors);
        }

        CatalogItem item = normalized.Id is Guid id
            ? await db.CatalogItems.SingleAsync(i => i.Id == id, cancellationToken)
            : db.CatalogItems.Add(new CatalogItem { Id = Guid.CreateVersion7() }).Entity;

        item.CatalogItemNumber = normalized.CatalogItemNumber;
        item.Name = normalized.Name;
        item.Unit = normalized.Unit;
        item.UnitPrice = normalized.UnitPrice;
        item.Currency = normalized.Currency;
        item.VatRate = normalized.VatRate;

        await db.SaveChangesAsync(cancellationToken);
        return new CatalogSaveResult(item.Id, []);
    }
}
```

`Features/ListCatalogItems.cs`:

```csharp
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Catalog.Features;

public sealed class ListCatalogItems(CatalogDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<CatalogItemSnapshot>> HandleAsync(CancellationToken cancellationToken) =>
        await db.CatalogItems.AsNoTracking()
            .OrderBy(i => i.CatalogItemNumber)
            .Select(i => new CatalogItemSnapshot(i.Id, i.CatalogItemNumber, i.Name, i.Unit, i.UnitPrice, i.Currency, i.VatRate))
            .ToListAsync(cancellationToken);
}
```

`Features/CatalogItemProvider.cs`:

```csharp
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Catalog.Features;

public sealed class CatalogItemProvider(CatalogDbContext db) : ICatalogItemProvider
{
    // public Methods
    public async Task<CatalogItemSnapshot?> FindAsync(Guid catalogItemId, CancellationToken cancellationToken) =>
        await db.CatalogItems.AsNoTracking()
            .Where(i => i.Id == catalogItemId)
            .Select(i => new CatalogItemSnapshot(i.Id, i.CatalogItemNumber, i.Name, i.Unit, i.UnitPrice, i.Currency, i.VatRate))
            .SingleOrDefaultAsync(cancellationToken);
}
```

- [ ] **Step 5: Migration, composition, checklist**

```bash
dotnet ef migrations add InitialCatalog --project src/Fakturenn.Modules.Catalog --output-dir Persistence/Migrations
```

Register in `ModuleConfiguration` (`AddModuleContext<CatalogDbContext>`, `ListCatalogItems`, `SaveCatalogItem`, `ICatalogItemProvider → CatalogItemProvider`), and repeat Task 3 Steps 9–10 for this module. Add `The_catalog_context_records_provenance` to `ModuleCompositionTests`.

- [ ] **Step 6: Integration test**

`tests/Fakturenn.IntegrationTests/CatalogTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Catalog;
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Catalog.Features;
using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class CatalogTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task An_item_round_trips_with_its_precision()
    {
        await using CatalogDbContext db = postgres.Create<CatalogDbContext>(options => new(options));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        CatalogSaveResult saved = await new SaveCatalogItem(db).HandleAsync(
            new CatalogItemForm(null, "DEV-BACKEND", "Backend development", "hur", 104.6m, "eur", 19m),
            TestContext.Current.CancellationToken);
        CatalogItemSnapshot? item = await new CatalogItemProvider(db).FindAsync(saved.Id!.Value, TestContext.Current.CancellationToken);

        item.Should().Be(new CatalogItemSnapshot(saved.Id.Value, "DEV-BACKEND", "Backend development", "HUR", 104.6m, "EUR", 19m));
    }
}
```

- [ ] **Step 7: Run every suite**, as Task 4 Step 9. Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add Fakturenn.slnx src tests
git commit -m "feat(catalog): catalog items with unit, price, currency and VAT rate"
```

---

### Task 6: The invoice domain — states, totals, tax, issue date, number format

Pure code in `Fakturenn.Modules.Invoices`, no database. Everything finalization decides is decided here and tested with real objects.

**Files:**
- Modify: `src/Fakturenn.Modules.Invoices/Fakturenn.Modules.Invoices.csproj` — `<ProjectReference>`s to all four new `.Contracts` projects
- Create: `src/Fakturenn.Modules.Invoices/Domain/{InvoiceState.cs,TaxCategory.cs,FinalizationRefusal.cs,Invoice.cs,InvoiceLine.cs,InvoiceCalculator.cs,TaxDecision.cs,IssueDateRule.cs,InvoiceNumberFormat.cs,FinalizationChecks.cs}`
- Test: `tests/Fakturenn.UnitTests/Modules/Invoices/{InvoiceCalculatorTests.cs,TaxDecisionTests.cs,IssueDateRuleTests.cs,InvoiceNumberFormatTests.cs,InvoiceStateTests.cs,FinalizationChecksTests.cs}`

**Interfaces:**
- Consumes: `SellerSnapshot`, `NumberScheme`, `ResetScope`, `IssueDateDefault` (Task 3); `CustomerSnapshot` (Task 4); `ProjectSnapshot` (Task 4); `CatalogItemSnapshot` (Task 5); `Money`, `Percentage`, `IAuditable` (shared kernel).
- Produces (namespace `Fakturenn.Modules.Invoices.Domain`):
  - `public enum InvoiceState { Draft, Numbered, Locked }`, `public enum TaxCategory { S }`
  - `public enum FinalizationRefusal { InvoiceLocked, NoLine, QuantityNotPositive, ServicePeriodMissing, SellerIncomplete, CustomerMissing, CustomerIncomplete, ProjectBelongsToAnotherCustomer, CatalogItemMissing, CurrencyMismatch, CrossBorderVatNotImplemented, StandardRateMustBePositive, TotalsChanged }`
  - `public sealed record PricedLine(decimal Quantity, decimal UnitPrice, decimal VatRate, TaxCategory Category)`
  - `public sealed record TaxSubtotal(TaxCategory Category, decimal Rate, decimal TaxableAmount, decimal TaxAmount)`
  - `public sealed record InvoiceTotals(decimal Net, decimal Vat, decimal Gross, string Currency, IReadOnlyList<decimal> LineNets, IReadOnlyList<TaxSubtotal> Subtotals)`
  - `public static class InvoiceCalculator { public static InvoiceTotals Calculate(IReadOnlyList<PricedLine> lines, string currency); }`
  - `public static class TaxDecision { public static FinalizationRefusal? Decide(string sellerCountry, string customerCountry, decimal vatRate, string sellerCurrency, string itemCurrency); }`
  - `public static class IssueDateRule { public static DateOnly DefaultFor(IssueDateDefault rule, DateTimeOffset utcNow, string timeZoneId); public static (DateOnly Start, DateOnly End) ServicePeriodFor(DateOnly issueDate); }`
  - `public static class InvoiceNumberFormat { public static string ScopeKey(ResetScope scope, DateOnly issueDate); public static string Format(NumberScheme scheme, DateOnly issueDate, long counter); }`
  - `public static class FinalizationChecks { public static FinalizationRefusal? Check(SellerSnapshot seller, CustomerSnapshot? customer, ProjectSnapshot? project, Guid invoiceCustomerId, IReadOnlyList<CatalogItemSnapshot?> items); }`
  - `public sealed class Invoice : IAuditable` with `static Invoice CreateDraft(Guid id, Guid customerId, Guid? projectId, DateOnly issueDate, DateOnly servicePeriodStart, DateOnly servicePeriodEnd)`, `void Edit(Guid customerId, Guid? projectId, DateOnly issueDate, DateOnly servicePeriodStart, DateOnly servicePeriodEnd)`, `void SetSingleLine(Guid catalogItemId, decimal quantity)`, `void ApplyFinalization(string number, DateOnly dueDate, InvoiceTotals totals, TaxCategory category, decimal vatRate)`, `void Lock()`; throws `InvalidOperationException` on a refused change.
  - `public sealed class InvoiceLine` with `Id`, `InvoiceId`, `CatalogItemId`, `Quantity`, `NetAmount`, `TaxCategory?`, `VatRate?`.

- [ ] **Step 1: Write the failing calculator tests**

Reference the four new `.Contracts` projects and `Fakturenn.Modules.Invoices` from `tests/Fakturenn.UnitTests` if not yet referenced. Create `tests/Fakturenn.UnitTests/Modules/Invoices/InvoiceCalculatorTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class InvoiceCalculatorTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_totals()
    {
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(8m, 100.00m, 19m, TaxCategory.S)], "EUR");

        totals.Net.Should().Be(800.00m);
        totals.Vat.Should().Be(152.00m);
        totals.Gross.Should().Be(952.00m);
    }

    [Fact]
    public void A_rounding_midpoint_rounds_away_from_zero()
    {
        // Review M4: 1307.50 x 19% = 248.425. Commercial rounding gives 248.43;
        // Math.Round's default, to-even, would give 248.42.
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(12.5m, 104.60m, 19m, TaxCategory.S)], "EUR");

        totals.Net.Should().Be(1307.50m);
        totals.Vat.Should().Be(248.43m);
    }

    [Fact]
    public void Vat_is_rounded_once_per_category_not_per_line()
    {
        // Review M13: two lines of 2.50 at 19%. Per line, 0.475 rounds to 0.48 twice, 0.96.
        // Per category, 5.00 x 19% = 0.95. EN 16931 computes per category.
        InvoiceTotals totals = InvoiceCalculator.Calculate(
            [new PricedLine(1m, 2.50m, 19m, TaxCategory.S), new PricedLine(1m, 2.50m, 19m, TaxCategory.S)], "EUR");

        totals.Vat.Should().Be(0.95m);
        totals.Subtotals.Should().ContainSingle().Which.Should().Be(new TaxSubtotal(TaxCategory.S, 19m, 5.00m, 0.95m));
    }

    [Fact]
    public void A_fractional_quantity_nets_to_two_decimals()
    {
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(0.333m, 100.00m, 19m, TaxCategory.S)], "EUR");

        totals.LineNets.Should().Equal(33.30m);
        totals.Net.Should().Be(33.30m);
    }

    [Fact]
    public void Two_rates_produce_two_subtotals()
    {
        InvoiceTotals totals = InvoiceCalculator.Calculate(
            [new PricedLine(1m, 100m, 19m, TaxCategory.S), new PricedLine(1m, 100m, 7m, TaxCategory.S)], "EUR");

        totals.Subtotals.Should().HaveCount(2);
        totals.Vat.Should().Be(26.00m);
    }
}
```

Run: `dotnet build tests/Fakturenn.UnitTests --configuration Release`. Expected: compile errors.

- [ ] **Step 2: Implement the enums and the calculator**

`Domain/InvoiceState.cs`:

```csharp
namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// Spec section 4. Draft: no number. Numbered: data editable, number and issue date frozen.
/// Locked: PDF and XML rendered, validated and archived — nothing changes, ever.
/// </summary>
public enum InvoiceState
{
    Draft,
    Numbered,
    Locked,
}
```

`Domain/TaxCategory.cs`:

```csharp
namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>EN 16931 tax category codes. Stage 1 implements S only (spec section 7).</summary>
public enum TaxCategory
{
    S,
}
```

`Domain/FinalizationRefusal.cs`:

```csharp
namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>Every reason finalization says no. Each has a localized message (Task 8).</summary>
public enum FinalizationRefusal
{
    InvoiceLocked,
    NoLine,
    QuantityNotPositive,
    ServicePeriodMissing,
    SellerIncomplete,
    CustomerMissing,
    CustomerIncomplete,
    ProjectBelongsToAnotherCustomer,
    CatalogItemMissing,
    CurrencyMismatch,
    CrossBorderVatNotImplemented,
    StandardRateMustBePositive,
    TotalsChanged,
}
```

`Domain/InvoiceCalculator.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Domain;

public sealed record PricedLine(decimal Quantity, decimal UnitPrice, decimal VatRate, TaxCategory Category);

public sealed record TaxSubtotal(TaxCategory Category, decimal Rate, decimal TaxableAmount, decimal TaxAmount);

public sealed record InvoiceTotals(
    decimal Net,
    decimal Vat,
    decimal Gross,
    string Currency,
    IReadOnlyList<decimal> LineNets,
    IReadOnlyList<TaxSubtotal> Subtotals);

/// <summary>
/// Line nets rounded to cents, then VAT per (category, rate) on the summed nets, rounded
/// once — as EN 16931 computes it (spec section 5 step 5, review M13). Rounding is the
/// shared kernel's commercial rounding, away from zero (review M4).
/// </summary>
public static class InvoiceCalculator
{
    // public static Methods
    public static InvoiceTotals Calculate(IReadOnlyList<PricedLine> lines, string currency)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<decimal> lineNets = [.. lines.Select(line => new Money(line.Quantity * line.UnitPrice, currency).Round().Amount)];

        List<TaxSubtotal> subtotals = [.. lines
            .Select((line, index) => (line, net: lineNets[index]))
            .GroupBy(pair => (pair.line.Category, pair.line.VatRate))
            .OrderBy(group => group.Key.Category).ThenByDescending(group => group.Key.VatRate)
            .Select(group =>
            {
                decimal taxable = group.Sum(pair => pair.net);
                decimal tax = new Percentage(group.Key.VatRate).Of(new Money(taxable, currency)).Amount;
                return new TaxSubtotal(group.Key.Category, group.Key.VatRate, taxable, tax);
            })];

        decimal net = lineNets.Sum();
        decimal vat = subtotals.Sum(subtotal => subtotal.TaxAmount);
        return new InvoiceTotals(net, vat, net + vat, currency, lineNets, subtotals);
    }
}
```

Run: `dotnet test --project tests/Fakturenn.UnitTests --configuration Release -- --filter-class "*InvoiceCalculatorTests"`. Expected: PASS.

- [ ] **Step 3: Tax decision — failing tests, then code**

`tests/Fakturenn.UnitTests/Modules/Invoices/TaxDecisionTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class TaxDecisionTests
{
    // public Methods
    [Fact]
    public void A_domestic_invoice_at_a_positive_rate_is_standard_rated()
    {
        TaxDecision.Decide("DE", "DE", 19m, "EUR", "EUR").Should().BeNull();
    }

    [Theory]
    [InlineData("AT")]
    [InlineData("FR")]
    [InlineData("CH")]
    [InlineData("US")]
    public void Every_cross_border_customer_is_refused(string customerCountry)
    {
        // Spec section 7: silently charging 19% to an EU business with a VAT ID is a legal
        // problem. Reverse charge, OSS and export arrive in M2.
        TaxDecision.Decide("DE", customerCountry, 19m, "EUR", "EUR")
            .Should().Be(FinalizationRefusal.CrossBorderVatNotImplemented);
    }

    [Fact]
    public void A_standard_rate_of_zero_is_refused()
    {
        // EN 16931 BR-S-05; review C6.
        TaxDecision.Decide("DE", "DE", 0m, "EUR", "EUR").Should().Be(FinalizationRefusal.StandardRateMustBePositive);
    }

    [Fact]
    public void An_item_in_another_currency_is_refused()
    {
        TaxDecision.Decide("DE", "DE", 19m, "EUR", "USD").Should().Be(FinalizationRefusal.CurrencyMismatch);
    }
}
```

`Domain/TaxDecision.cs`:

```csharp
namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>Stage 1 implements category S and refuses everything else, by name (spec section 7).</summary>
public static class TaxDecision
{
    // public static Methods
    public static FinalizationRefusal? Decide(
        string sellerCountry,
        string customerCountry,
        decimal vatRate,
        string sellerCurrency,
        string itemCurrency)
    {
        if (!string.Equals(sellerCountry, customerCountry, StringComparison.Ordinal))
        {
            return FinalizationRefusal.CrossBorderVatNotImplemented;
        }

        if (vatRate <= 0m)
        {
            return FinalizationRefusal.StandardRateMustBePositive;
        }

        if (!string.Equals(sellerCurrency, itemCurrency, StringComparison.Ordinal))
        {
            return FinalizationRefusal.CurrencyMismatch;
        }

        return null;
    }
}
```

Run the class. Expected: PASS.

- [ ] **Step 4: Issue date — failing tests, then code**

`tests/Fakturenn.UnitTests/Modules/Invoices/IssueDateRuleTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class IssueDateRuleTests
{
    // public Methods
    [Fact]
    public void The_default_issue_date_is_today_in_the_organization_time_zone()
    {
        // Review Focus 1: 23:30 UTC on 31 August is already 1 September in Berlin.
        DateTimeOffset utcNow = new(2026, 8, 31, 23, 30, 0, TimeSpan.Zero);

        IssueDateRule.DefaultFor(IssueDateDefault.Today, utcNow, "Europe/Berlin").Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Billing_the_previous_month_defaults_to_its_last_day()
    {
        DateTimeOffset utcNow = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

        IssueDateRule.DefaultFor(IssueDateDefault.LastDayOfPreviousMonth, utcNow, "Europe/Berlin")
            .Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void Billing_the_previous_month_in_march_of_a_leap_year()
    {
        DateTimeOffset utcNow = new(2028, 3, 1, 12, 0, 0, TimeSpan.Zero);

        IssueDateRule.DefaultFor(IssueDateDefault.LastDayOfPreviousMonth, utcNow, "Europe/Berlin")
            .Should().Be(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void The_default_service_period_is_the_issue_date_month()
    {
        IssueDateRule.ServicePeriodFor(new DateOnly(2026, 8, 31))
            .Should().Be((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
    }
}
```

`Domain/IssueDateRule.cs`:

```csharp
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// The issue date a draft starts with, taken once when the draft is created and stored on
/// it (spec section 6, review M1). "Today" is the organization's today, not the server's.
/// </summary>
public static class IssueDateRule
{
    // public static Methods
    public static DateOnly DefaultFor(IssueDateDefault rule, DateTimeOffset utcNow, string timeZoneId)
    {
        DateTime local = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime;
        DateOnly today = DateOnly.FromDateTime(local);

        return rule switch
        {
            IssueDateDefault.Today => today,
            IssueDateDefault.LastDayOfPreviousMonth => new DateOnly(today.Year, today.Month, 1).AddDays(-1),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
        };
    }

    public static (DateOnly Start, DateOnly End) ServicePeriodFor(DateOnly issueDate)
    {
        DateOnly start = new(issueDate.Year, issueDate.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
```

Run the class. Expected: PASS.

- [ ] **Step 5: Number format — failing tests, then code**

`tests/Fakturenn.UnitTests/Modules/Invoices/InvoiceNumberFormatTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class InvoiceNumberFormatTests
{
    // private static readonly Fields
    private static readonly DateOnly _lastOfAugust = new(2026, 8, 31);

    // public Methods
    [Theory]
    [InlineData(ResetScope.PerDay, "R", 1, 8, "R2608318")]
    [InlineData(ResetScope.PerMonth, "R", 3, 12, "R2608012")]
    [InlineData(ResetScope.PerYear, "", 4, 7, "20260007")]
    [InlineData(ResetScope.Continuous, "INV", 5, 451, "INV00451")]
    public void A_number_is_prefix_date_part_and_padded_counter(ResetScope scope, string prefix, int padding, long counter, string expected)
    {
        InvoiceNumberFormat.Format(new NumberScheme(prefix, scope, padding, 1), _lastOfAugust, counter).Should().Be(expected);
    }

    [Fact]
    public void A_counter_wider_than_the_padding_is_not_truncated()
    {
        InvoiceNumberFormat.Format(new NumberScheme("R", ResetScope.PerDay, 1, 1), _lastOfAugust, 12).Should().Be("R26083112");
    }

    [Theory]
    [InlineData(ResetScope.PerDay, "260831")]
    [InlineData(ResetScope.PerMonth, "2608")]
    [InlineData(ResetScope.PerYear, "2026")]
    [InlineData(ResetScope.Continuous, "continuous")]
    public void The_scope_key_comes_from_the_issue_date(ResetScope scope, string expected)
    {
        InvoiceNumberFormat.ScopeKey(scope, _lastOfAugust).Should().Be(expected);
    }
}
```

The `PerMonth` row is `R` + `2608` + `012`: three digits of padding. `PerYear` with an empty prefix is `2026` + `0007`.

`Domain/InvoiceNumberFormat.cs`:

```csharp
using System.Globalization;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// Prefix, the date part the scope implies, then the zero-padded counter, with no
/// separators (spec section 6, review M2). The date part comes from the invoice's own
/// issue date, never from the wall clock.
/// </summary>
public static class InvoiceNumberFormat
{
    // public static Methods
    public static string ScopeKey(ResetScope scope, DateOnly issueDate) =>
        scope == ResetScope.Continuous ? "continuous" : DatePart(scope, issueDate);

    public static string Format(NumberScheme scheme, DateOnly issueDate, long counter)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        return scheme.Prefix
            + DatePart(scheme.Scope, issueDate)
            + counter.ToString(CultureInfo.InvariantCulture).PadLeft(scheme.Padding, '0');
    }

    // private static Methods
    private static string DatePart(ResetScope scope, DateOnly issueDate) => scope switch
    {
        ResetScope.PerDay => issueDate.ToString("yyMMdd", CultureInfo.InvariantCulture),
        ResetScope.PerMonth => issueDate.ToString("yyMM", CultureInfo.InvariantCulture),
        ResetScope.PerYear => issueDate.ToString("yyyy", CultureInfo.InvariantCulture),
        ResetScope.Continuous => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
```

Run the class. Expected: PASS.

- [ ] **Step 6: Completeness checks — failing tests, then code**

`tests/Fakturenn.UnitTests/Modules/Invoices/FinalizationChecksTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class FinalizationChecksTests
{
    // private static readonly Fields
    private static readonly Guid _customerId = Guid.CreateVersion7();
    private static readonly SellerSnapshot _seller = new(
        "Example Consulting", "DE", "DE123456789", "DE89370400440532013000", "EUR", "Europe/Berlin", 14,
        IssueDateDefault.Today, new NumberScheme("R", ResetScope.PerDay, 1, 1));
    private static readonly CustomerSnapshot _customer = new(_customerId, "Example Client GmbH", "DE", null, "C-4711");
    private static readonly CatalogItemSnapshot _item = new(Guid.CreateVersion7(), "DEV-BACKEND", "Backend development", "HUR", 100m, "EUR", 19m);

    // public Methods
    [Fact]
    public void The_walking_skeleton_passes()
    {
        FinalizationChecks.Check(_seller, _customer, null, _customerId, [_item]).Should().BeNull();
    }

    [Theory]
    [InlineData("LegalName")]
    [InlineData("CountryCode")]
    [InlineData("VatId")]
    [InlineData("Iban")]
    [InlineData("Currency")]
    public void Each_missing_seller_field_refuses(string field)
    {
        SellerSnapshot seller = field switch
        {
            "LegalName" => _seller with { LegalName = "" },
            "CountryCode" => _seller with { CountryCode = "" },
            "VatId" => _seller with { VatId = "" },
            "Iban" => _seller with { Iban = "" },
            "Currency" => _seller with { Currency = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        FinalizationChecks.Check(seller, _customer, null, _customerId, [_item]).Should().Be(FinalizationRefusal.SellerIncomplete);
    }

    [Fact]
    public void A_missing_customer_refuses()
    {
        FinalizationChecks.Check(_seller, null, null, _customerId, [_item]).Should().Be(FinalizationRefusal.CustomerMissing);
    }

    [Fact]
    public void Another_customers_project_refuses()
    {
        ProjectSnapshot project = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "PRJ-2026-083");

        FinalizationChecks.Check(_seller, _customer, project, _customerId, [_item])
            .Should().Be(FinalizationRefusal.ProjectBelongsToAnotherCustomer);
    }

    [Fact]
    public void A_deleted_catalog_item_refuses()
    {
        FinalizationChecks.Check(_seller, _customer, null, _customerId, [null]).Should().Be(FinalizationRefusal.CatalogItemMissing);
    }
}
```

`Domain/FinalizationChecks.cs`:

```csharp
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// What "complete" means at finalization (spec section 5 step 3, review C6). The seller VAT
/// ID is mandatory while it is the only seller tax identifier modelled (EN 16931 BR-S-02).
/// </summary>
public static class FinalizationChecks
{
    // public static Methods
    public static FinalizationRefusal? Check(
        SellerSnapshot seller,
        CustomerSnapshot? customer,
        ProjectSnapshot? project,
        Guid invoiceCustomerId,
        IReadOnlyList<CatalogItemSnapshot?> items)
    {
        ArgumentNullException.ThrowIfNull(seller);
        ArgumentNullException.ThrowIfNull(items);

        string[] sellerFields = [seller.LegalName, seller.CountryCode, seller.VatId, seller.Iban, seller.Currency, seller.TimeZoneId];
        if (sellerFields.Any(string.IsNullOrWhiteSpace))
        {
            return FinalizationRefusal.SellerIncomplete;
        }

        if (customer is null)
        {
            return FinalizationRefusal.CustomerMissing;
        }

        string[] customerFields = [customer.LegalName, customer.CountryCode, customer.CustomerNumber];
        if (customerFields.Any(string.IsNullOrWhiteSpace))
        {
            return FinalizationRefusal.CustomerIncomplete;
        }

        if (project is not null && project.CustomerId != invoiceCustomerId)
        {
            return FinalizationRefusal.ProjectBelongsToAnotherCustomer;
        }

        return items.Any(item => item is null) ? FinalizationRefusal.CatalogItemMissing : null;
    }
}
```

Run the class. Expected: PASS.

- [ ] **Step 7: The aggregate — failing tests, then code**

`tests/Fakturenn.UnitTests/Modules/Invoices/InvoiceStateTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;

namespace Fakturenn.UnitTests.Modules.Invoices;

/// <summary>Spec section 4's table, as the aggregate enforces it (review C4).</summary>
public sealed class InvoiceStateTests
{
    // private static readonly Fields
    private static readonly DateOnly _issue = new(2026, 8, 31);
    private static readonly InvoiceTotals _totals = InvoiceCalculator.Calculate([new PricedLine(8m, 100m, 19m, TaxCategory.S)], "EUR");

    // public Methods
    [Fact]
    public void A_draft_accepts_every_change()
    {
        Invoice invoice = Draft();

        invoice.Edit(Guid.CreateVersion7(), null, _issue.AddDays(1), _issue, _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), 4m);

        invoice.IssueDate.Should().Be(_issue.AddDays(1));
        invoice.Lines.Should().ContainSingle().Which.Quantity.Should().Be(4m);
    }

    [Fact]
    public void A_numbered_invoice_keeps_its_issue_date()
    {
        Invoice invoice = Numbered();

        Action act = () => invoice.Edit(invoice.CustomerId, null, _issue.AddDays(1), _issue, _issue);

        act.Should().Throw<InvalidOperationException>().WithMessage("*issue date*");
    }

    [Fact]
    public void A_numbered_invoice_accepts_a_data_change_under_the_same_number()
    {
        Invoice invoice = Numbered();
        Guid otherCustomer = Guid.CreateVersion7();

        invoice.Edit(otherCustomer, null, _issue, _issue, _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), 9m);

        invoice.CustomerId.Should().Be(otherCustomer);
        invoice.Number.Should().Be("R2608311");
    }

    [Fact]
    public void A_numbered_invoice_cannot_be_given_another_number()
    {
        Invoice invoice = Numbered();

        Action act = () => invoice.ApplyFinalization("R2608312", _issue, _totals, TaxCategory.S, 19m);

        act.Should().Throw<InvalidOperationException>().WithMessage("*number*");
    }

    [Fact]
    public void A_locked_invoice_accepts_nothing()
    {
        Invoice invoice = Numbered();
        invoice.Lock();

        Action edit = () => invoice.Edit(invoice.CustomerId, null, _issue, _issue, _issue);
        Action line = () => invoice.SetSingleLine(Guid.CreateVersion7(), 1m);
        Action renumber = () => invoice.ApplyFinalization("R2608311", _issue, _totals, TaxCategory.S, 19m);

        edit.Should().Throw<InvalidOperationException>();
        line.Should().Throw<InvalidOperationException>();
        renumber.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Only_a_numbered_invoice_locks()
    {
        Action act = () => Draft().Lock();

        act.Should().Throw<InvalidOperationException>();
    }

    // private static Methods
    private static Invoice Draft()
    {
        Invoice invoice = Invoice.CreateDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), null, _issue, new DateOnly(2026, 8, 1), _issue);
        invoice.SetSingleLine(Guid.CreateVersion7(), 8m);
        return invoice;
    }

    private static Invoice Numbered()
    {
        Invoice invoice = Draft();
        invoice.ApplyFinalization("R2608311", _issue.AddDays(14), _totals, TaxCategory.S, 19m);
        return invoice;
    }
}
```

`Domain/InvoiceLine.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// A line references its catalog item by id; price and rate are resolved at finalization
/// and stored here only as the outcome (spec section 5). Stage 1 uses exactly one line.
/// </summary>
public sealed class InvoiceLine : IAuditable
{
    // public Properties
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid CatalogItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? NetAmount { get; set; }
    public TaxCategory? TaxCategory { get; set; }
    public decimal? VatRate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
```

`Domain/Invoice.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// The invoice aggregate. Its methods are the only way to change it, and they enforce spec
/// section 4: the number and the issue date freeze at allocation, everything freezes at lock.
/// Totals and tax are columns so the detail page never reads the snapshot (review C3).
/// </summary>
public sealed class Invoice : IAuditable
{
    // private Fields
    private readonly List<InvoiceLine> _lines = [];

    // public Properties
    public Guid Id { get; private set; }
    public InvoiceState State { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly ServicePeriodStart { get; private set; }
    public DateOnly ServicePeriodEnd { get; private set; }
    public string? Number { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string? Currency { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal? VatAmount { get; private set; }
    public decimal? GrossAmount { get; private set; }
    public TaxCategory? TaxCategory { get; private set; }
    public decimal? VatRate { get; private set; }
    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;

    // public static Methods
    public static Invoice CreateDraft(
        Guid id,
        Guid customerId,
        Guid? projectId,
        DateOnly issueDate,
        DateOnly servicePeriodStart,
        DateOnly servicePeriodEnd) =>
        new()
        {
            Id = id,
            State = InvoiceState.Draft,
            CustomerId = customerId,
            ProjectId = projectId,
            IssueDate = issueDate,
            ServicePeriodStart = servicePeriodStart,
            ServicePeriodEnd = servicePeriodEnd,
        };

    // public Methods
    public void Edit(Guid customerId, Guid? projectId, DateOnly issueDate, DateOnly servicePeriodStart, DateOnly servicePeriodEnd)
    {
        RefuseWhenLocked();
        if (State == InvoiceState.Numbered && issueDate != IssueDate)
        {
            throw new InvalidOperationException("A numbered invoice keeps the issue date its number was derived from.");
        }

        CustomerId = customerId;
        ProjectId = projectId;
        IssueDate = issueDate;
        ServicePeriodStart = servicePeriodStart;
        ServicePeriodEnd = servicePeriodEnd;
    }

    public void SetSingleLine(Guid catalogItemId, decimal quantity)
    {
        RefuseWhenLocked();
        InvoiceLine? line = _lines.SingleOrDefault();
        if (line is null)
        {
            line = new InvoiceLine { Id = Guid.CreateVersion7(), InvoiceId = Id };
            _lines.Add(line);
        }

        line.CatalogItemId = catalogItemId;
        line.Quantity = quantity;
    }

    /// <summary>
    /// Records finalization's outcome. On a draft this assigns the number; on a numbered
    /// invoice it must be called with the same number and only refreshes the totals.
    /// </summary>
    public void ApplyFinalization(string number, DateOnly dueDate, InvoiceTotals totals, TaxCategory category, decimal vatRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(totals);
        RefuseWhenLocked();
        if (State == InvoiceState.Numbered && !string.Equals(Number, number, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A numbered invoice keeps its number.");
        }

        Number = number;
        DueDate = dueDate;
        Currency = totals.Currency;
        NetAmount = totals.Net;
        VatAmount = totals.Vat;
        GrossAmount = totals.Gross;
        TaxCategory = category;
        VatRate = vatRate;
        for (int index = 0; index < _lines.Count; index++)
        {
            _lines[index].NetAmount = totals.LineNets[index];
            _lines[index].TaxCategory = category;
            _lines[index].VatRate = vatRate;
        }

        State = InvoiceState.Numbered;
    }

    public void Lock()
    {
        if (State != InvoiceState.Numbered)
        {
            throw new InvalidOperationException("Only a numbered invoice can be locked.");
        }

        State = InvoiceState.Locked;
    }

    // private Methods
    private void RefuseWhenLocked()
    {
        if (State == InvoiceState.Locked)
        {
            throw new InvalidOperationException("A locked invoice never changes; correct it with an invoice correction.");
        }
    }
}
```

Run: `dotnet test --project tests/Fakturenn.UnitTests --configuration Release`. Expected: all PASS.

- [ ] **Step 8: Mutations**

Copy `Domain/IssueDateRule.cs` aside; replace the `TimeZoneInfo.ConvertTime(...)` line with `DateTime local = utcNow.UtcDateTime;`; run `IssueDateRuleTests` — Expected: `The_default_issue_date_is_today_in_the_organization_time_zone` FAILS. Restore, `sha256sum --check`. Then copy `Domain/InvoiceCalculator.cs` aside; compute VAT per line (`Percentage.Of` on each line net, summed); run `InvoiceCalculatorTests` — Expected: `Vat_is_rounded_once_per_category_not_per_line` FAILS. Restore, check.

- [ ] **Step 9: Commit**

```bash
git add src/Fakturenn.Modules.Invoices tests/Fakturenn.UnitTests
git commit -m "feat(invoices): the invoice domain — states, totals, tax decision, issue date, number format"
```

---

### Task 7: Persistence, gapless numbering and finalization

**Files:**
- Modify: `src/Fakturenn.Modules.Invoices/Persistence/InvoicesDbContext.cs`
- Create: `src/Fakturenn.Modules.Invoices/Persistence/{NumberCounter.cs,InvoiceSnapshotRecord.cs}`, `src/Fakturenn.Modules.Invoices/Snapshots/{InvoiceSnapshotDocument.cs,SnapshotJsonContext.cs,InvoiceSnapshotSerializer.cs}`, `src/Fakturenn.Modules.Invoices/Features/{NumberAllocator.cs,NumberingStatus.cs,CreateDraft.cs,SaveDraft.cs,PreviewInvoice.cs,FinalizeInvoice.cs,GetInvoice.cs,ListInvoices.cs}`, `src/Fakturenn.Modules.Invoices.Contracts/INumberingStatus.cs`
- Modify: `src/Fakturenn.Modules.Organizations/Features/SaveOrganization.cs`, `src/Fakturenn.Modules.Organizations/Fakturenn.Modules.Organizations.csproj` (reference `Invoices.Contracts`), `src/Fakturenn.Web/ModuleConfiguration.cs`, `src/Fakturenn.Web/FakturennWebApplication.cs` (Invoices context gains the interceptor)
- Test: `tests/Fakturenn.UnitTests/Modules/Invoices/InvoiceSnapshotSerializerTests.cs`, `tests/Fakturenn.IntegrationTests/Invoices/{FinalizationTests.cs,NumberAllocationTests.cs,InvoiceTestData.cs}`

**Interfaces:**
- Consumes: everything Task 6 produces; the four providers (Tasks 3–5); `IClock`.
- Produces:
  - `public interface INumberingStatus { Task<bool> AnyNumberAllocatedAsync(CancellationToken cancellationToken); }` in `Invoices.Contracts`.
  - `public sealed record FinalizationResult(string? Number, FinalizationRefusal? Refusal, InvoiceTotals? Totals)` with `bool Succeeded => Refusal is null`.
  - `CreateDraft.HandleAsync(Guid customerId, CancellationToken) → Task<Guid>`
  - `SaveDraft.HandleAsync(DraftForm, CancellationToken) → Task` with `public sealed record DraftForm(Guid InvoiceId, Guid CustomerId, Guid? ProjectId, Guid CatalogItemId, decimal Quantity, DateOnly IssueDate, DateOnly ServicePeriodStart, DateOnly ServicePeriodEnd)`
  - `PreviewInvoice.HandleAsync(Guid invoiceId, CancellationToken) → Task<FinalizationResult>` (Number null on success)
  - `FinalizeInvoice.HandleAsync(Guid invoiceId, decimal confirmedGross, CancellationToken) → Task<FinalizationResult>`
  - `GetInvoice.HandleAsync(Guid invoiceId, CancellationToken) → Task<InvoiceView?>` and `ListInvoices.HandleAsync(CancellationToken) → Task<IReadOnlyList<InvoiceView>>` with `public sealed record InvoiceView(Guid Id, InvoiceState State, string? Number, Guid CustomerId, Guid? ProjectId, Guid? CatalogItemId, decimal Quantity, DateOnly IssueDate, DateOnly ServicePeriodStart, DateOnly ServicePeriodEnd, DateOnly? DueDate, decimal? Net, decimal? Vat, decimal? Gross, string? Currency, TaxCategory? TaxCategory, decimal? VatRate)`
  - `NumberAllocator.AllocateAsync(DbContext db, string scopeKey, long start, CancellationToken) → Task<long>` and the SQL in `NumberAllocator.Sql`.

- [ ] **Step 1: Map the aggregate, the counter and the snapshot**

`Persistence/NumberCounter.cs`:

```csharp
namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>One row per scope key — "260831", "2026", "continuous". Written only by <c>NumberAllocator</c>.</summary>
public sealed class NumberCounter
{
    // public Properties
    public string ScopeKey { get; set; } = string.Empty;
    public long Value { get; set; }
}
```

`Persistence/InvoiceSnapshotRecord.cs`:

```csharp
namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// The snapshot bytes as written, and their SHA-256. One per invoice, overwritten while the
/// invoice is Numbered, never after it locks. The hash proves corruption, not tampering:
/// whoever can rewrite Content can rewrite Sha256 in the same row (spec section 4).
/// </summary>
public sealed class InvoiceSnapshotRecord
{
    // public Properties
    public Guid InvoiceId { get; set; }
    public byte[] Content { get; set; } = [];
    public byte[] Sha256 { get; set; } = [];
}
```

`Persistence/InvoicesDbContext.cs` — replace the class body:

```csharp
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// The Invoices module owns this context and its migrations. No other module
/// may reference the entities it maps.
/// </summary>
public sealed class InvoicesDbContext(DbContextOptions<InvoicesDbContext> options)
    : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "invoices";

    // public Properties
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<NumberCounter> NumberCounters => Set<NumberCounter>();
    public DbSet<InvoiceSnapshotRecord> Snapshots => Set<InvoiceSnapshotRecord>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Property(i => i.State).HasConversion<string>().HasMaxLength(16);
            invoice.Property(i => i.Number).HasMaxLength(40);
            invoice.HasIndex(i => i.Number).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            invoice.Property(i => i.Currency).HasMaxLength(3);
            invoice.Property(i => i.NetAmount).HasPrecision(18, 2);
            invoice.Property(i => i.VatAmount).HasPrecision(18, 2);
            invoice.Property(i => i.GrossAmount).HasPrecision(18, 2);
            invoice.Property(i => i.TaxCategory).HasConversion<string>().HasMaxLength(4);
            invoice.Property(i => i.VatRate).HasPrecision(5, 2);
            invoice.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            invoice.Navigation(i => i.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<InvoiceLine>(line =>
        {
            line.Property(l => l.Quantity).HasPrecision(18, 4);
            line.Property(l => l.NetAmount).HasPrecision(18, 2);
            line.Property(l => l.TaxCategory).HasConversion<string>().HasMaxLength(4);
            line.Property(l => l.VatRate).HasPrecision(5, 2);
        });

        modelBuilder.Entity<NumberCounter>(counter =>
        {
            counter.HasKey(c => c.ScopeKey);
            counter.Property(c => c.ScopeKey).HasMaxLength(16);
        });

        modelBuilder.Entity<InvoiceSnapshotRecord>(snapshot =>
        {
            snapshot.HasKey(s => s.InvoiceId);
            snapshot.HasOne<Invoice>().WithOne().HasForeignKey<InvoiceSnapshotRecord>(s => s.InvoiceId);
        });

        ConfigureAuditColumns(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // private static Methods
    private static void ConfigureAuditColumns(ModelBuilder modelBuilder)
    {
        List<IMutableEntityType> auditable = [.. modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType))];

        foreach (IMutableEntityType entityType in auditable)
        {
            modelBuilder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
```

Generate: `dotnet ef migrations add InvoiceFinalization --project src/Fakturenn.Modules.Invoices --output-dir Persistence/Migrations`. Inspect the migration: tables `Invoices`, `InvoiceLine`, `NumberCounters`, `Snapshots`, the filtered unique index on `Number`. The Wolverine envelope tables must **not** appear — they are mapped `ExcludeFromMigrations`; if they do, stop and report.

- [ ] **Step 2: The snapshot document and its strict serializer — failing test first**

`tests/Fakturenn.UnitTests/Modules/Invoices/InvoiceSnapshotSerializerTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Snapshots;

namespace Fakturenn.UnitTests.Modules.Invoices;

/// <summary>
/// The reader is strict so an upgrade between numbering and rendering fails loudly instead
/// of rendering 0.00 with no tax category (spec section 4, review C3).
/// </summary>
public sealed class InvoiceSnapshotSerializerTests
{
    // public Methods
    [Fact]
    public void A_snapshot_round_trips()
    {
        InvoiceSnapshotDocument document = InvoiceSnapshotSamples.WalkingSkeleton();

        byte[] bytes = InvoiceSnapshotSerializer.Serialize(document);

        InvoiceSnapshotSerializer.Deserialize(bytes).Should().BeEquivalentTo(document);
    }

    [Fact]
    public void An_unknown_member_fails_loudly()
    {
        string json = Encoding.UTF8.GetString(InvoiceSnapshotSerializer.Serialize(InvoiceSnapshotSamples.WalkingSkeleton()));
        byte[] extended = Encoding.UTF8.GetBytes(json.Insert(1, "\"renamedLater\":1,"));

        Action act = () => InvoiceSnapshotSerializer.Deserialize(extended);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void A_missing_member_fails_loudly()
    {
        string json = Encoding.UTF8.GetString(InvoiceSnapshotSerializer.Serialize(InvoiceSnapshotSamples.WalkingSkeleton()));
        using JsonDocument parsed = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> members = parsed.RootElement.EnumerateObject()
            .Where(member => member.Name != "gross")
            .ToDictionary(member => member.Name, member => member.Value.Clone());

        Action act = () => InvoiceSnapshotSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(members));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void The_hash_is_over_the_stored_bytes()
    {
        byte[] bytes = InvoiceSnapshotSerializer.Serialize(InvoiceSnapshotSamples.WalkingSkeleton());

        InvoiceSnapshotSerializer.Hash(bytes).Should().Equal(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}
```

Create `tests/Fakturenn.UnitTests/Modules/Invoices/InvoiceSnapshotSamples.cs`:

```csharp
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Snapshots;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.UnitTests.Modules.Invoices;

internal static class InvoiceSnapshotSamples
{
    // public static Methods
    public static InvoiceSnapshotDocument WalkingSkeleton()
    {
        Guid customerId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(8m, 100m, 19m, TaxCategory.S)], "EUR");
        return new InvoiceSnapshotDocument(
            Guid.Parse("01900000-0000-7000-8000-000000000002"),
            "R2608311",
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 9, 14),
            new SellerSnapshot("Example Consulting", "DE", "DE123456789", "DE89370400440532013000", "EUR", "Europe/Berlin", 14,
                IssueDateDefault.LastDayOfPreviousMonth, new NumberScheme("R", ResetScope.PerDay, 1, 1)),
            new CustomerSnapshot(customerId, "Example Client GmbH", "DE", null, "C-4711"),
            new ProjectSnapshot(Guid.Parse("01900000-0000-7000-8000-000000000003"), customerId, "PRJ-2026-083"),
            [new SnapshotLine(
                new CatalogItemSnapshot(Guid.Parse("01900000-0000-7000-8000-000000000004"), "DEV-BACKEND", "Backend development", "HUR", 100m, "EUR", 19m),
                "SI-9001", 8m, 800.00m, TaxCategory.S, 19m)],
            totals.Subtotals,
            totals.Net,
            totals.Vat,
            totals.Gross,
            "EUR");
    }
}
```

`Snapshots/InvoiceSnapshotDocument.cs`:

```csharp
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.Modules.Invoices.Snapshots;

public sealed record SnapshotLine(
    CatalogItemSnapshot CatalogItem,
    string? CustomerCatalogItemNumber,
    decimal Quantity,
    decimal NetAmount,
    TaxCategory TaxCategory,
    decimal VatRate);

/// <summary>
/// Everything the artifacts are produced from, resolved at finalization. The number scheme
/// travels inside <see cref="Seller"/>, so the snapshot records which scheme produced its
/// number (spec section 6).
/// </summary>
public sealed record InvoiceSnapshotDocument(
    Guid InvoiceId,
    string Number,
    DateOnly IssueDate,
    DateOnly ServicePeriodStart,
    DateOnly ServicePeriodEnd,
    DateOnly DueDate,
    SellerSnapshot Seller,
    CustomerSnapshot Customer,
    ProjectSnapshot? Project,
    IReadOnlyList<SnapshotLine> Lines,
    IReadOnlyList<TaxSubtotal> TaxSubtotals,
    decimal Net,
    decimal Vat,
    decimal Gross,
    string Currency);
```

`Snapshots/SnapshotJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Fakturenn.Modules.Invoices.Snapshots;

/// <summary>
/// Fixed options, source-generated. Strict on read: an unknown member or a missing
/// constructor parameter throws (review C3). Enums as names, so a reordered enum cannot
/// silently change an old snapshot's meaning.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InvoiceSnapshotDocument))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext;
```

`Snapshots/InvoiceSnapshotSerializer.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;

namespace Fakturenn.Modules.Invoices.Snapshots;

/// <summary>
/// Serialized once per finalization and stored as raw bytes — not jsonb, which reorders and
/// normalizes keys so the bytes read back would not be the bytes hashed. Nothing
/// re-serializes for verification, so no canonical form is needed (spec section 4).
/// </summary>
public static class InvoiceSnapshotSerializer
{
    // public static Methods
    public static byte[] Serialize(InvoiceSnapshotDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, SnapshotJsonContext.Default.InvoiceSnapshotDocument);

    public static InvoiceSnapshotDocument Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, SnapshotJsonContext.Default.InvoiceSnapshotDocument)
            ?? throw new JsonException("The snapshot is empty.");

    public static byte[] Hash(byte[] bytes) => SHA256.HashData(bytes);
}
```

Run the serializer tests. Expected: PASS. If `RespectRequiredConstructorParameters` is not accepted on `JsonSourceGenerationOptions` in this SDK, stop and report rather than dropping the missing-member test: it is the half of C3 that matters.

- [ ] **Step 3: The allocator — failing race test first**

Create `tests/Fakturenn.IntegrationTests/Invoices/NumberAllocationTests.cs`:

```csharp
using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Features;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Fakturenn.IntegrationTests.Invoices;

public sealed class NumberAllocationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // public Methods
    [Fact]
    public async Task The_first_allocation_in_a_new_scope_is_serialized_too()
    {
        // Review S1: SELECT ... FOR UPDATE locks nothing when the scope's row does not exist
        // yet, so two first-of-day finalizations both took number 1. The interleave is
        // forced: A allocates and holds its transaction open, B is started and must be seen
        // waiting on A's row before A commits.
        await MigrateAsync();
        const string scope = "first-race";

        await using InvoicesDbContext a = postgres.CreateContext();
        await using IDbContextTransaction aTransaction = await a.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        long first = await NumberAllocator.AllocateAsync(a, scope, start: 1, TestContext.Current.CancellationToken);

        await using InvoicesDbContext b = postgres.CreateContext();
        await using IDbContextTransaction bTransaction = await b.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        Task<long> second = NumberAllocator.AllocateAsync(b, scope, start: 1, TestContext.Current.CancellationToken);

        await WaitUntilBlockedAsync();
        second.IsCompleted.Should().BeFalse("B must wait for A's uncommitted row");

        await aTransaction.CommitAsync(TestContext.Current.CancellationToken);
        long secondValue = await second;
        await bTransaction.CommitAsync(TestContext.Current.CancellationToken);

        first.Should().Be(1);
        secondValue.Should().Be(2);
    }

    [Fact]
    public async Task A_rolled_back_allocation_returns_its_number()
    {
        await MigrateAsync();
        const string scope = "rollback";

        await using (InvoicesDbContext a = postgres.CreateContext())
        {
            await using IDbContextTransaction transaction = await a.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            (await NumberAllocator.AllocateAsync(a, scope, 1, TestContext.Current.CancellationToken)).Should().Be(1);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using InvoicesDbContext b = postgres.CreateContext();
        await using IDbContextTransaction again = await b.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        (await NumberAllocator.AllocateAsync(b, scope, 1, TestContext.Current.CancellationToken))
            .Should().Be(1, "a sequence would have burned 1; the counter row rolls back with everything else");
    }

    [Fact]
    public async Task A_fresh_scope_starts_at_the_configured_next_number()
    {
        await MigrateAsync();
        await using InvoicesDbContext db = postgres.CreateContext();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        (await NumberAllocator.AllocateAsync(db, "continuous-start", 451, TestContext.Current.CancellationToken)).Should().Be(451);
        (await NumberAllocator.AllocateAsync(db, "continuous-start", 451, TestContext.Current.CancellationToken)).Should().Be(452);
    }

    // private Methods
    private async Task MigrateAsync()
    {
        await using InvoicesDbContext db = postgres.CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private async Task WaitUntilBlockedAsync()
    {
        await using NpgsqlConnection observer = new(postgres.ConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await using NpgsqlCommand command = observer.CreateCommand();
            command.CommandText = "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%NumberCounters%'";
            long waiting = Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("B never blocked on A's row; the interleave was not forced.");
    }
}
```

Run: build fails on `NumberAllocator`. Then create `Features/NumberAllocator.cs`:

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.Modules.Invoices.Features;

/// <summary>
/// One statement that also handles the first occurrence in a scope (spec section 6, review
/// S1). Executed during the review against PostgreSQL: three concurrent transactions on a
/// missing row received 1, 2 and 3, and a rolled-back fourth returned its number. Unlike a
/// sequence, whose nextval survives ROLLBACK, a failed finalization returns its number.
/// Plain ADO on the context's own connection and transaction, because EF would wrap a
/// composed SqlQuery in a subquery an INSERT cannot sit in.
/// </summary>
public static class NumberAllocator
{
    // public const Fields
    public const string Sql =
        "INSERT INTO invoices.\"NumberCounters\" (\"ScopeKey\", \"Value\") VALUES (@scope, @start) "
        + "ON CONFLICT (\"ScopeKey\") DO UPDATE SET \"Value\" = invoices.\"NumberCounters\".\"Value\" + 1 "
        + "RETURNING \"Value\"";

    // public static Methods
    public static async Task<long> AllocateAsync(DbContext db, string scopeKey, long start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Allocation must run inside the finalization transaction.");
        command.CommandText = Sql;
        AddParameter(command, "scope", scopeKey);
        AddParameter(command, "start", start);

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // private static Methods
    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
```

Run `NumberAllocationTests` with `DOTNET_USE_POLLING_FILE_WATCHER=1`. Expected: PASS.

- [ ] **Step 4: Mutation — the upsert is what serializes the first allocation**

Copy `NumberAllocator.cs` aside. Replace the body with the obvious version: `SELECT "Value" … FOR UPDATE`, and when no row is found `INSERT … VALUES (@scope, @start)` without `ON CONFLICT`, returning `start`; with the row found, `UPDATE … SET "Value" = "Value" + 1 RETURNING "Value"`. Run `The_first_allocation_in_a_new_scope_is_serialized_too`. Expected: FAIL — either `WaitUntilBlockedAsync` times out because B never waits, or B fails on the primary key. Restore, `sha256sum --check`.

- [ ] **Step 5: The slices**

`src/Fakturenn.Modules.Invoices.Contracts/INumberingStatus.cs`:

```csharp
namespace Fakturenn.Modules.Invoices.Contracts;

/// <summary>Whether any invoice number has ever been allocated. Locks the organization's number scheme.</summary>
public interface INumberingStatus
{
    Task<bool> AnyNumberAllocatedAsync(CancellationToken cancellationToken);
}
```

`Features/NumberingStatus.cs`:

```csharp
using Fakturenn.Modules.Invoices.Contracts;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class NumberingStatus(InvoicesDbContext db) : INumberingStatus
{
    // public Methods
    public Task<bool> AnyNumberAllocatedAsync(CancellationToken cancellationToken) =>
        db.NumberCounters.AnyAsync(cancellationToken);
}
```

`Features/CreateDraft.cs`:

```csharp
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class CreateDraft(InvoicesDbContext db, ISellerProvider sellers, IClock clock)
{
    // public Methods
    /// <summary>The issue date is taken once, here, in the organization's time zone (review M1).</summary>
    public async Task<Guid> HandleAsync(Guid customerId, CancellationToken cancellationToken)
    {
        SellerSnapshot seller = await sellers.GetAsync(cancellationToken);
        DateOnly issueDate = IssueDateRule.DefaultFor(seller.IssueDateDefault, clock.UtcNow, seller.TimeZoneId);
        (DateOnly start, DateOnly end) = IssueDateRule.ServicePeriodFor(issueDate);

        Invoice invoice = Invoice.CreateDraft(Guid.CreateVersion7(), customerId, projectId: null, issueDate, start, end);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice.Id;
    }
}
```

`Features/SaveDraft.cs`:

```csharp
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record DraftForm(
    Guid InvoiceId,
    Guid CustomerId,
    Guid? ProjectId,
    Guid CatalogItemId,
    decimal Quantity,
    DateOnly IssueDate,
    DateOnly ServicePeriodStart,
    DateOnly ServicePeriodEnd);

public sealed class SaveDraft(InvoicesDbContext db)
{
    // public Methods
    /// <summary>The aggregate refuses what the invoice's state forbids; this only persists.</summary>
    public async Task HandleAsync(DraftForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        Invoice invoice = await db.Invoices.Include(i => i.Lines)
            .SingleAsync(i => i.Id == form.InvoiceId, cancellationToken);

        invoice.Edit(form.CustomerId, form.ProjectId, form.IssueDate, form.ServicePeriodStart, form.ServicePeriodEnd);
        invoice.SetSingleLine(form.CatalogItemId, form.Quantity);
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

`Features/FinalizeInvoice.cs` — the core. `PreviewInvoice` reuses its private resolution through a shared internal class, so both compute totals on one path (spec section 5, review M12):

```csharp
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Invoices.Snapshots;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record FinalizationResult(string? Number, FinalizationRefusal? Refusal, InvoiceTotals? Totals)
{
    public bool Succeeded => Refusal is null;
}

/// <summary>
/// Spec section 5. One unit of work inside the execution strategy: the context uses
/// EnableRetryOnFailure, and EF requires a user transaction to be retried as a whole, so
/// every load happens inside the delegate and the change tracker is cleared at the start of
/// each attempt (review S2).
/// </summary>
public sealed class FinalizeInvoice(InvoicesDbContext db, InvoiceResolution resolution)
{
    // public Methods
    public Task<FinalizationResult> HandleAsync(Guid invoiceId, decimal confirmedGross, CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            (invoiceId, confirmedGross),
            async (_, state, token) => await AttemptAsync(state.invoiceId, state.confirmedGross, token),
            verifySucceeded: null,
            cancellationToken);
    }

    // private Methods
    private async Task<FinalizationResult> AttemptAsync(Guid invoiceId, decimal confirmedGross, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Step 1: lock the invoice and re-check its state under the lock. Two tabs, a
        // double click and a strategy retry after a commit-time failure all land here.
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM invoices.\"Invoices\" WHERE \"Id\" = {invoiceId} FOR UPDATE", cancellationToken);
        Invoice invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice.State == InvoiceState.Locked)
        {
            return new FinalizationResult(invoice.Number, FinalizationRefusal.InvoiceLocked, null);
        }

        ResolvedInvoice resolved = await resolution.ResolveAsync(invoice, cancellationToken);
        if (resolved.Refusal is FinalizationRefusal refusal)
        {
            return new FinalizationResult(invoice.Number, refusal, null);
        }

        if (resolved.Totals!.Gross != confirmedGross)
        {
            return new FinalizationResult(invoice.Number, FinalizationRefusal.TotalsChanged, resolved.Totals);
        }

        // Step 6: allocate only when entering Numbered. A numbered invoice keeps its number.
        string number = invoice.Number ?? InvoiceNumberFormat.Format(
            resolved.Seller!.NumberScheme,
            invoice.IssueDate,
            await NumberAllocator.AllocateAsync(
                db,
                InvoiceNumberFormat.ScopeKey(resolved.Seller.NumberScheme.Scope, invoice.IssueDate),
                resolved.Seller.NumberScheme.NextNumber,
                cancellationToken));

        DateOnly dueDate = invoice.IssueDate.AddDays(resolved.Seller!.PaymentTermsDays);
        invoice.ApplyFinalization(number, dueDate, resolved.Totals, TaxCategory.S, resolved.Items[0]!.VatRate);

        // Step 7: serialize, hash, upsert one-to-one by invoice.
        byte[] content = InvoiceSnapshotSerializer.Serialize(resolution.Document(invoice, number, dueDate, resolved));
        InvoiceSnapshotRecord? snapshot = await db.Snapshots.SingleOrDefaultAsync(s => s.InvoiceId == invoiceId, cancellationToken);
        if (snapshot is null)
        {
            snapshot = new InvoiceSnapshotRecord { InvoiceId = invoiceId };
            db.Snapshots.Add(snapshot);
        }

        snapshot.Content = content;
        snapshot.Sha256 = InvoiceSnapshotSerializer.Hash(content);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new FinalizationResult(number, null, resolved.Totals);
    }
}
```

`Features/InvoiceResolution.cs`:

```csharp
using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Snapshots;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record ResolvedInvoice(
    FinalizationRefusal? Refusal,
    SellerSnapshot? Seller,
    CustomerSnapshot? Customer,
    ProjectSnapshot? Project,
    IReadOnlyList<CatalogItemSnapshot?> Items,
    IReadOnlyList<string?> CustomerItemNumbers,
    InvoiceTotals? Totals);

/// <summary>
/// Steps 2 to 5 of spec section 5, shared by preview and finalization so the totals a user
/// confirms come from the code that numbers them (review M12). Reads go through the other
/// modules' contracts and their own contexts — outside the invoice transaction, accepted in
/// the spec.
/// </summary>
public sealed class InvoiceResolution(
    ISellerProvider sellers,
    ICustomerProvider customers,
    IProjectProvider projects,
    ICatalogItemProvider catalog)
{
    // public Methods
    public async Task<ResolvedInvoice> ResolveAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        FinalizationRefusal? draftRefusal = invoice.Lines.Count == 0 ? FinalizationRefusal.NoLine
            : invoice.Lines.Any(line => line.Quantity <= 0m) ? FinalizationRefusal.QuantityNotPositive
            : invoice.ServicePeriodEnd < invoice.ServicePeriodStart ? FinalizationRefusal.ServicePeriodMissing
            : null;
        if (draftRefusal is not null)
        {
            return Refused(draftRefusal.Value);
        }

        SellerSnapshot seller = await sellers.GetAsync(cancellationToken);
        CustomerSnapshot? customer = await customers.FindAsync(invoice.CustomerId, cancellationToken);
        ProjectSnapshot? project = invoice.ProjectId is Guid projectId ? await projects.FindAsync(projectId, cancellationToken) : null;

        List<CatalogItemSnapshot?> items = [];
        List<string?> references = [];
        foreach (InvoiceLine line in invoice.Lines)
        {
            items.Add(await catalog.FindAsync(line.CatalogItemId, cancellationToken));
            references.Add(await customers.FindCatalogItemReferenceAsync(invoice.CustomerId, line.CatalogItemId, cancellationToken));
        }

        FinalizationRefusal? refusal = FinalizationChecks.Check(seller, customer, project, invoice.CustomerId, items);
        if (refusal is not null)
        {
            return Refused(refusal.Value);
        }

        foreach (CatalogItemSnapshot item in items.OfType<CatalogItemSnapshot>())
        {
            FinalizationRefusal? tax = TaxDecision.Decide(seller.CountryCode, customer!.CountryCode, item.VatRate, seller.Currency, item.Currency);
            if (tax is not null)
            {
                return Refused(tax.Value);
            }
        }

        InvoiceTotals totals = InvoiceCalculator.Calculate(
            [.. invoice.Lines.Select((line, index) => new PricedLine(line.Quantity, items[index]!.UnitPrice, items[index]!.VatRate, TaxCategory.S))],
            seller.Currency);

        return new ResolvedInvoice(null, seller, customer, project, items, references, totals);
    }

    public InvoiceSnapshotDocument Document(Invoice invoice, string number, DateOnly dueDate, ResolvedInvoice resolved)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(resolved);

        List<SnapshotLine> lines = [.. invoice.Lines.Select((line, index) => new SnapshotLine(
            resolved.Items[index]!, resolved.CustomerItemNumbers[index], line.Quantity,
            resolved.Totals!.LineNets[index], TaxCategory.S, resolved.Items[index]!.VatRate))];

        return new InvoiceSnapshotDocument(
            invoice.Id, number, invoice.IssueDate, invoice.ServicePeriodStart, invoice.ServicePeriodEnd, dueDate,
            resolved.Seller!, resolved.Customer!, resolved.Project, lines, resolved.Totals!.Subtotals,
            resolved.Totals.Net, resolved.Totals.Vat, resolved.Totals.Gross, resolved.Totals.Currency);
    }

    // private static Methods
    private static ResolvedInvoice Refused(FinalizationRefusal refusal) => new(refusal, null, null, null, [], [], null);
}
```

`Features/PreviewInvoice.cs`:

```csharp
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class PreviewInvoice(InvoicesDbContext db, InvoiceResolution resolution)
{
    // public Methods
    public async Task<FinalizationResult> HandleAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        Invoice invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .SingleAsync(i => i.Id == invoiceId, cancellationToken);

        ResolvedInvoice resolved = await resolution.ResolveAsync(invoice, cancellationToken);
        return new FinalizationResult(invoice.Number, resolved.Refusal, resolved.Totals);
    }
}
```

`Features/GetInvoice.cs` — `InvoiceView` lives here; neither reader touches `Snapshots`, because the detail page reads columns only (review C3):

```csharp
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record InvoiceView(
    Guid Id,
    InvoiceState State,
    string? Number,
    Guid CustomerId,
    Guid? ProjectId,
    Guid? CatalogItemId,
    decimal Quantity,
    DateOnly IssueDate,
    DateOnly ServicePeriodStart,
    DateOnly ServicePeriodEnd,
    DateOnly? DueDate,
    decimal? Net,
    decimal? Vat,
    decimal? Gross,
    string? Currency,
    TaxCategory? TaxCategory,
    decimal? VatRate)
{
    // public static Methods
    public static InvoiceView From(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        InvoiceLine? line = invoice.Lines.SingleOrDefault();
        return new InvoiceView(
            invoice.Id, invoice.State, invoice.Number, invoice.CustomerId, invoice.ProjectId,
            line?.CatalogItemId, line?.Quantity ?? 0m, invoice.IssueDate, invoice.ServicePeriodStart,
            invoice.ServicePeriodEnd, invoice.DueDate, invoice.NetAmount, invoice.VatAmount,
            invoice.GrossAmount, invoice.Currency, invoice.TaxCategory, invoice.VatRate);
    }
}

public sealed class GetInvoice(InvoicesDbContext db)
{
    // public Methods
    public async Task<InvoiceView?> HandleAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        Invoice? invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        return invoice is null ? null : InvoiceView.From(invoice);
    }
}
```

`Features/ListInvoices.cs`:

```csharp
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class ListInvoices(InvoicesDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<InvoiceView>> HandleAsync(CancellationToken cancellationToken)
    {
        List<Invoice> invoices = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .OrderByDescending(i => i.IssueDate).ThenBy(i => i.Number)
            .ToListAsync(cancellationToken);
        return [.. invoices.Select(InvoiceView.From)];
    }
}
```

- [ ] **Step 6: Register, give Invoices the interceptor, lock the scheme**

In `FakturennWebApplication.Build`, change the Invoices registration to the `(IServiceProvider, DbContextOptionsBuilder)` overload so it gains the interceptor:

```csharp
        builder.Services.AddDbContextWithWolverineIntegration<InvoicesDbContext>(
            (serviceProvider, options) => options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                    databaseOptions.MaxRetries,
                    TimeSpan.FromSeconds(databaseOptions.RetryDelaySeconds),
                    errorCodesToAdd: null))
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()),
            MessagingConfiguration.SchemaName);
```

Add `using Fakturenn.Infrastructure.Persistence;`. In `ModuleConfiguration.AddFakturennModules`, append:

```csharp
        builder.Services.AddScoped<InvoiceResolution>();
        builder.Services.AddScoped<CreateDraft>();
        builder.Services.AddScoped<SaveDraft>();
        builder.Services.AddScoped<PreviewInvoice>();
        builder.Services.AddScoped<FinalizeInvoice>();
        builder.Services.AddScoped<GetInvoice>();
        builder.Services.AddScoped<ListInvoices>();
        builder.Services.AddScoped<INumberingStatus, NumberingStatus>();
```

Add `[Fact] public void The_invoices_context_records_provenance() => AssertAudited<InvoicesDbContext>();` to `ModuleCompositionTests`.

Reference `Fakturenn.Modules.Invoices.Contracts` from `Fakturenn.Modules.Organizations.csproj`, then make `SaveOrganization` refuse a scheme change once a number exists:

```csharp
public sealed class SaveOrganization(OrganizationsDbContext db, INumberingStatus numbering)
```

and, after loading `organization` and before assigning:

```csharp
        bool schemeChanged = organization.NumberPrefix != normalized.NumberPrefix
            || organization.NumberScope != normalized.NumberScope
            || organization.NumberPadding != normalized.NumberPadding
            || organization.NumberNextValue != normalized.NumberNextValue;
        if (schemeChanged && await numbering.AnyNumberAllocatedAsync(cancellationToken))
        {
            // Spec section 6, review M2: another prefix or scope could format a number
            // already issued, and the counter rows mean nothing under another scope.
            return [OrganizationError.SchemeLocked];
        }
```

Task 3's `OrganizationsTests` now construct `SaveOrganization` with a second argument: pass a `NumberingStatus` over a migrated `InvoicesDbContext` from the same fixture (`postgres.CreateContext()` after `MigrateAsync`).

- [ ] **Step 7: Finalization integration tests**

Create `tests/Fakturenn.IntegrationTests/Invoices/InvoiceTestData.cs` — a helper that, against one `PostgresFixture`, migrates all five contexts and returns the slices wired by hand:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Catalog;
using Fakturenn.Modules.Catalog.Features;
using Fakturenn.Modules.Catalog.Persistence;
using Fakturenn.Modules.Customers;
using Fakturenn.Modules.Customers.Features;
using Fakturenn.Modules.Customers.Persistence;
using Fakturenn.Modules.Invoices.Features;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Organizations.Features;
using Fakturenn.Modules.Organizations.Persistence;
using Fakturenn.Modules.Projects.Features;
using Fakturenn.Modules.Projects.Persistence;
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests.Invoices;

/// <summary>
/// The walking skeleton's fixed example, persisted. Each call to <see cref="Slices"/> uses
/// fresh contexts, which is how CircuitOperations runs them in the host.
/// </summary>
internal sealed class InvoiceTestData(PostgresFixture postgres, IClock clock)
{
    // public Properties
    public Guid CustomerId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CatalogItemId { get; private set; }

    // public Methods
    public async Task SeedAsync(OrganizationForm organization)
    {
        await using OrganizationsDbContext orgs = postgres.Create<OrganizationsDbContext>(o => new(o));
        await using CustomersDbContext customers = postgres.Create<CustomersDbContext>(o => new(o));
        await using ProjectsDbContext projects = postgres.Create<ProjectsDbContext>(o => new(o));
        await using CatalogDbContext catalog = postgres.Create<CatalogDbContext>(o => new(o));
        await using InvoicesDbContext invoices = postgres.CreateContext();
        foreach (DbContext db in new DbContext[] { orgs, customers, projects, catalog, invoices })
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        (await new SaveOrganization(orgs, new NumberingStatus(invoices)).HandleAsync(organization, TestContext.Current.CancellationToken))
            .Should().BeEmpty();
        string suffix = Guid.CreateVersion7().ToString("N")[..8];
        CustomerId = (await new SaveCustomer(customers).HandleAsync(
            new CustomerForm(null, "Example Client GmbH", "DE", "", $"C-{suffix}"), TestContext.Current.CancellationToken)).Id!.Value;
        ProjectId = await new SaveProject(projects).HandleAsync(null, CustomerId, "PRJ-2026-083", TestContext.Current.CancellationToken);
        CatalogItemId = (await new SaveCatalogItem(catalog).HandleAsync(
            new CatalogItemForm(null, $"DEV-{suffix}", "Backend development", "HUR", 100m, "EUR", 19m), TestContext.Current.CancellationToken)).Id!.Value;
        await new SaveCatalogItemReference(customers).HandleAsync(CustomerId, CatalogItemId, "SI-9001", TestContext.Current.CancellationToken);
    }

    public (CreateDraft Create, SaveDraft Save, PreviewInvoice Preview, FinalizeInvoice Finalize, InvoicesDbContext Db, IAsyncDisposable Scope) Slices()
    {
        OrganizationsDbContext orgs = postgres.Create<OrganizationsDbContext>(o => new(o));
        CustomersDbContext customers = postgres.Create<CustomersDbContext>(o => new(o));
        ProjectsDbContext projects = postgres.Create<ProjectsDbContext>(o => new(o));
        CatalogDbContext catalog = postgres.Create<CatalogDbContext>(o => new(o));
        InvoicesDbContext invoices = postgres.CreateContext();
        InvoiceResolution resolution = new(new SellerProvider(orgs), new CustomerProvider(customers), new ProjectProvider(projects), new CatalogItemProvider(catalog));

        return (new CreateDraft(invoices, new SellerProvider(orgs), clock), new SaveDraft(invoices), new PreviewInvoice(invoices, resolution),
            new FinalizeInvoice(invoices, resolution), invoices, new Disposer(orgs, customers, projects, catalog, invoices));
    }

    // private Types
    private sealed class Disposer(params DbContext[] contexts) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (DbContext context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }
}
```

`PostgresFixture.CreateContext()` builds `InvoicesDbContext` **without** `EnableRetryOnFailure`, so `CreateExecutionStrategy()` returns the non-retrying strategy and the `ExecuteAsync` path still runs. The retry behaviour itself is EF's; this plan does not re-test it.

Create `tests/Fakturenn.IntegrationTests/Invoices/FinalizationTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.IntegrationTests.Fakes;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Features;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Invoices.Snapshots;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests.Invoices;

public sealed class FinalizationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // private static readonly Fields
    private static readonly OrganizationForm _organization = OrganizationForm.Empty with
    {
        LegalName = "Example Consulting",
        CountryCode = "DE",
        VatId = "DE123456789",
        Iban = "DE89370400440532013000",
        Currency = "EUR",
        IssueDateDefault = IssueDateDefault.Today,
        NumberPrefix = "R",
        NumberScope = ResetScope.PerDay,
        NumberPadding = 1,
    };

    // public Methods
    [Fact]
    public async Task The_walking_skeleton_finalizes_with_a_hashed_snapshot()
    {
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero), 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 952.00m);

        result.Succeeded.Should().BeTrue();
        result.Number.Should().MatchRegex("^R260831[0-9]+$");
        await using InvoicesDbContext db = postgres.CreateContext();
        Invoice invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, TestContext.Current.CancellationToken);
        invoice.State.Should().Be(InvoiceState.Numbered);
        invoice.GrossAmount.Should().Be(952.00m);
        invoice.DueDate.Should().Be(new DateOnly(2026, 9, 14));
        InvoiceSnapshotRecord snapshot = await db.Snapshots.SingleAsync(s => s.InvoiceId == invoiceId, TestContext.Current.CancellationToken);
        snapshot.Sha256.Should().Equal(InvoiceSnapshotSerializer.Hash(snapshot.Content));
        InvoiceSnapshotDocument document = InvoiceSnapshotSerializer.Deserialize(snapshot.Content);
        document.Lines.Should().ContainSingle().Which.CustomerCatalogItemNumber.Should().Be("SI-9001");
        document.Seller.NumberScheme.Prefix.Should().Be("R");
    }

    [Fact]
    public async Task The_number_carries_the_issue_date_not_the_day_it_was_finalized()
    {
        // Review M9's backdating test: written on 4 September, dated 31 August.
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero), 8m);
        await EditAsync(data, invoiceId, issueDate: new DateOnly(2026, 8, 31), quantity: 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 952.00m);

        result.Number.Should().StartWith("R260831");
    }

    [Fact]
    public async Task Finalizing_one_invoice_twice_concurrently_allocates_once()
    {
        // Review S2 and Review Focus 5.
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero), 8m);

        FinalizationResult[] results = await Task.WhenAll(
            Task.Run(() => FinalizeAsync(data, invoiceId, 952.00m), TestContext.Current.CancellationToken),
            Task.Run(() => FinalizeAsync(data, invoiceId, 952.00m), TestContext.Current.CancellationToken));

        results.Should().OnlyContain(r => r.Succeeded);
        results.Select(r => r.Number).Distinct().Should().ContainSingle();
        await using InvoicesDbContext db = postgres.CreateContext();
        (await db.Snapshots.CountAsync(s => s.InvoiceId == invoiceId, TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.NumberCounters.SingleAsync(c => c.ScopeKey == "260715", TestContext.Current.CancellationToken)).Value
            .Should().Be(1, "the counter advanced once");
    }

    [Fact]
    public async Task An_edit_to_a_numbered_invoice_keeps_its_number_and_replaces_the_snapshot()
    {
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), 8m);
        FinalizationResult first = await FinalizeAsync(data, invoiceId, 952.00m);
        byte[] firstHash = await HashAsync(invoiceId);

        await EditAsync(data, invoiceId, issueDate: null, quantity: 9m);
        FinalizationResult second = await FinalizeAsync(data, invoiceId, 1071.00m);

        second.Number.Should().Be(first.Number);
        (await HashAsync(invoiceId)).Should().NotEqual(firstHash);
    }

    [Fact]
    public async Task Finalization_refuses_when_the_confirmed_gross_is_stale()
    {
        // Review Focus 4 and M12.
        (InvoiceTestData data, Guid invoiceId) = await DraftAsync(new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.Zero), 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 900.00m);

        result.Refusal.Should().Be(FinalizationRefusal.TotalsChanged);
        result.Totals!.Gross.Should().Be(952.00m, "the page shows the real totals for a second confirmation");
        result.Number.Should().BeNull();
    }

    [Fact]
    public async Task An_incomplete_seller_is_refused_by_name()
    {
        InvoiceTestData data = new(postgres, new FakeClock(new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero)));
        await data.SeedAsync(_organization with { Iban = "" });
        Guid invoiceId = await CreateFilledAsync(data, 8m);

        FinalizationResult result = await FinalizeAsync(data, invoiceId, 952.00m);

        result.Refusal.Should().Be(FinalizationRefusal.SellerIncomplete);
    }

    // private Methods
    private async Task<(InvoiceTestData, Guid)> DraftAsync(DateTimeOffset now, decimal quantity)
    {
        InvoiceTestData data = new(postgres, new FakeClock(now));
        await data.SeedAsync(_organization);
        return (data, await CreateFilledAsync(data, quantity));
    }

    private static async Task<Guid> CreateFilledAsync(InvoiceTestData data, decimal quantity)
    {
        var slices = data.Slices();
        await using (slices.Scope)
        {
            Guid id = await slices.Create.HandleAsync(data.CustomerId, TestContext.Current.CancellationToken);
            Invoice draft = await slices.Db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id, TestContext.Current.CancellationToken);
            await slices.Save.HandleAsync(
                new DraftForm(id, data.CustomerId, data.ProjectId, data.CatalogItemId, quantity, draft.IssueDate, draft.ServicePeriodStart, draft.ServicePeriodEnd),
                TestContext.Current.CancellationToken);
            return id;
        }
    }

    private static async Task EditAsync(InvoiceTestData data, Guid invoiceId, DateOnly? issueDate, decimal quantity)
    {
        var slices = data.Slices();
        await using (slices.Scope)
        {
            Invoice current = await slices.Db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, TestContext.Current.CancellationToken);
            DateOnly date = issueDate ?? current.IssueDate;
            await slices.Save.HandleAsync(
                new DraftForm(invoiceId, data.CustomerId, data.ProjectId, data.CatalogItemId, quantity, date, current.ServicePeriodStart, current.ServicePeriodEnd),
                TestContext.Current.CancellationToken);
        }
    }

    private static async Task<FinalizationResult> FinalizeAsync(InvoiceTestData data, Guid invoiceId, decimal confirmedGross)
    {
        var slices = data.Slices();
        await using (slices.Scope)
        {
            return await slices.Finalize.HandleAsync(invoiceId, confirmedGross, TestContext.Current.CancellationToken);
        }
    }

    private async Task<byte[]> HashAsync(Guid invoiceId)
    {
        await using InvoicesDbContext db = postgres.CreateContext();
        return (await db.Snapshots.AsNoTracking().SingleAsync(s => s.InvoiceId == invoiceId, TestContext.Current.CancellationToken)).Sha256;
    }
}
```

The tests share one database and one organization row; each uses its own issue date, so their counter scopes never meet. `SeedAsync` saves the organization again each time — after the first finalization the scheme is locked, so pass the **same** scheme every time (`_organization`), or `SaveOrganization` answers `SchemeLocked`. `An_incomplete_seller_is_refused_by_name` changes only the IBAN, which is not part of the scheme. Copy `tests/Fakturenn.UnitTests/Fakes/FakeClock.cs` to `tests/Fakturenn.IntegrationTests/Fakes/FakeClock.cs` (namespace `Fakturenn.IntegrationTests.Fakes`) if the integration suite has none, and check its constructor matches `new FakeClock(DateTimeOffset)` before using it.

- [ ] **Step 8: The scheme lock**

Add to `OrganizationsTests`:

```csharp
    [Fact]
    public async Task The_number_scheme_locks_after_the_first_allocation()
    {
        await using OrganizationsDbContext db = await MigratedAsync();
        await using InvoicesDbContext invoices = postgres.CreateContext();
        await invoices.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await using (Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await invoices.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await NumberAllocator.AllocateAsync(invoices, "lock-test", 1, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        OrganizationForm current = await new GetOrganization(db).HandleAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<OrganizationError> errors = await new SaveOrganization(db, new NumberingStatus(invoices))
            .HandleAsync(current with { NumberPrefix = current.NumberPrefix + "X" }, TestContext.Current.CancellationToken);

        errors.Should().ContainSingle().Which.Should().Be(OrganizationError.SchemeLocked);
    }
```

- [ ] **Step 9: Run every suite**, as Task 3 Step 13. Expected: all PASS.

- [ ] **Step 10: Mutations**

Each: copy the file aside, apply, run the named test, expect FAIL, restore, `sha256sum --check`.

- `FinalizeInvoice.cs`: delete the `ExecuteSqlAsync(... FOR UPDATE ...)` line. Run `Finalizing_one_invoice_twice_concurrently_allocates_once` five times; Expected: at least one FAIL (two numbers or a counter of 2). If five runs stay green, add a `Task.Delay(200)` between the state load and the allocation in the mutant to widen the window — the mutant, not the real code — and rerun.
- `FinalizeInvoice.cs`: replace `invoice.Number ?? InvoiceNumberFormat.Format(...)` with the unconditional `InvoiceNumberFormat.Format(...)`. Run `An_edit_to_a_numbered_invoice_keeps_its_number_and_replaces_the_snapshot`. Expected: FAIL.
- `CreateDraft.cs`: replace `clock.UtcNow` with `DateTimeOffset.UtcNow`. Run `The_walking_skeleton_finalizes_with_a_hashed_snapshot`. Expected: FAIL on the `R260831` pattern.

- [ ] **Step 11: Commit**

```bash
git add src tests
git commit -m "feat(invoices): gapless numbering per scope, and finalization into a hashed snapshot"
```

---

### Task 8: The pages

**Files:**
- Create: `src/Fakturenn.Web/Components/Business/{OrganizationPage.razor,CustomersPage.razor,CatalogPage.razor,InvoicesPage.razor,InvoicePage.razor,RefusalMessages.cs}` (`.razor` with BOM)
- Modify: `src/Fakturenn.Web/Components/_Imports.razor`, `src/Fakturenn.Web/Components/Layout/MainLayout.razor`, `src/Fakturenn.Web/Resources/SharedResource.resx`, `src/Fakturenn.Web/Resources/SharedResource.de.resx`
- Modify: `tests/Fakturenn.UiTests/AuthenticatedWebAppFixture.cs`
- Test: `tests/Fakturenn.UiTests/InvoiceJourneyTests.cs`, `tests/Fakturenn.UiTests/CircuitSecurityTests.cs`

**Interfaces:**
- Consumes: `CircuitOperations` (Task 2), every slice and form record of Tasks 3–7, `Permissions.*` (Task 1), `InteractiveProviders` (Task 2).
- Produces: routes `/organization`, `/customers`, `/catalog`, `/invoices`, `/invoices/{Id:guid}`; `data-testid` values used by the UI tests below.

Every page follows one shape. Write each one out in full; do not share a base class (no second caller yet).

```razor
@page "/route"
@rendermode @(new InteractiveServerRenderMode(prerender: false))
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = Permissions.X)]
@inject CircuitOperations Operations
@inject NavigationManager Navigation
@inject IStringLocalizer<SharedResource> Localizer

<InteractiveProviders />
```

and every call to a module is

```csharp
    try
    {
        result = await Operations.RunAsync<SomeSlice, SomeResult>(Permissions.X, (slice, token) => slice.HandleAsync(..., token), CancellationToken.None);
    }
    catch (OperationRefusedException)
    {
        // A full reload re-runs cookie authentication, which a circuit never repeats.
        Navigation.NavigateTo("/account/denied", forceLoad: true);
        return;
    }
```

`prerender: false` is spec section 9 [M8]: prerendering runs `OnInitializedAsync` twice and shows controls before the circuit is live.

- [ ] **Step 1: Imports and navigation**

Append to `_Imports.razor`:

```razor
@using Fakturenn.Modules.Identity.Authorization
@using Fakturenn.Web.Components.Business
```

In `MainLayout.razor`, inside `@if (IsSignedIn)` and before the sign-out form, add plain links — the layout stays static:

```razor
            <MudButton Href="/invoices" Variant="Variant.Text" Color="Color.Inherit" data-testid="nav-invoices">@Localizer["Nav_Invoices"]</MudButton>
            <MudButton Href="/customers" Variant="Variant.Text" Color="Color.Inherit" data-testid="nav-customers">@Localizer["Nav_Customers"]</MudButton>
            <MudButton Href="/catalog" Variant="Variant.Text" Color="Color.Inherit" data-testid="nav-catalog">@Localizer["Nav_Catalog"]</MudButton>
            <MudButton Href="/organization" Variant="Variant.Text" Color="Color.Inherit" data-testid="nav-organization">@Localizer["Nav_Organization"]</MudButton>
```

- [ ] **Step 2: Refusal messages as literal lookups**

`Components/Business/RefusalMessages.cs`:

```csharp
using Fakturenn.Modules.Catalog;
using Fakturenn.Modules.Customers;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations;
using Microsoft.Extensions.Localization;

namespace Fakturenn.Web.Components.Business;

/// <summary>
/// Every code to its message, each key a string literal so SharedResourceTests can see it.
/// A switch rather than Localizer[code]: a key built at run time is invisible to the scan
/// that proves both resource files are complete.
/// </summary>
internal static class RefusalMessages
{
    // public static Methods
    public static string For(IStringLocalizer<SharedResource> localizer, FinalizationRefusal refusal) => refusal switch
    {
        FinalizationRefusal.InvoiceLocked => localizer["Refusal_InvoiceLocked"],
        FinalizationRefusal.NoLine => localizer["Refusal_NoLine"],
        FinalizationRefusal.QuantityNotPositive => localizer["Refusal_QuantityNotPositive"],
        FinalizationRefusal.ServicePeriodMissing => localizer["Refusal_ServicePeriodMissing"],
        FinalizationRefusal.SellerIncomplete => localizer["Refusal_SellerIncomplete"],
        FinalizationRefusal.CustomerMissing => localizer["Refusal_CustomerMissing"],
        FinalizationRefusal.CustomerIncomplete => localizer["Refusal_CustomerIncomplete"],
        FinalizationRefusal.ProjectBelongsToAnotherCustomer => localizer["Refusal_ProjectBelongsToAnotherCustomer"],
        FinalizationRefusal.CatalogItemMissing => localizer["Refusal_CatalogItemMissing"],
        FinalizationRefusal.CurrencyMismatch => localizer["Refusal_CurrencyMismatch"],
        FinalizationRefusal.CrossBorderVatNotImplemented => localizer["Refusal_CrossBorderVatNotImplemented"],
        FinalizationRefusal.StandardRateMustBePositive => localizer["Refusal_StandardRateMustBePositive"],
        FinalizationRefusal.TotalsChanged => localizer["Refusal_TotalsChanged"],
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null),
    };

    public static string For(IStringLocalizer<SharedResource> localizer, OrganizationError error) => error switch
    {
        OrganizationError.CountryInvalid => localizer["Organization_Error_CountryInvalid"],
        OrganizationError.VatIdInvalid => localizer["Organization_Error_VatIdInvalid"],
        OrganizationError.IbanInvalid => localizer["Organization_Error_IbanInvalid"],
        OrganizationError.CurrencyInvalid => localizer["Organization_Error_CurrencyInvalid"],
        OrganizationError.TimeZoneInvalid => localizer["Organization_Error_TimeZoneInvalid"],
        OrganizationError.PaymentTermsInvalid => localizer["Organization_Error_PaymentTermsInvalid"],
        OrganizationError.PrefixInvalid => localizer["Organization_Error_PrefixInvalid"],
        OrganizationError.PaddingInvalid => localizer["Organization_Error_PaddingInvalid"],
        OrganizationError.NextNumberInvalid => localizer["Organization_Error_NextNumberInvalid"],
        OrganizationError.SchemeLocked => localizer["Organization_Error_SchemeLocked"],
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    public static string For(IStringLocalizer<SharedResource> localizer, CustomerError error) => error switch
    {
        CustomerError.LegalNameMissing => localizer["Customer_Error_LegalNameMissing"],
        CustomerError.CountryInvalid => localizer["Customer_Error_CountryInvalid"],
        CustomerError.VatIdInvalid => localizer["Customer_Error_VatIdInvalid"],
        CustomerError.CustomerNumberMissing => localizer["Customer_Error_CustomerNumberMissing"],
        CustomerError.CustomerNumberTaken => localizer["Customer_Error_CustomerNumberTaken"],
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    public static string For(IStringLocalizer<SharedResource> localizer, CatalogError error) => error switch
    {
        CatalogError.NumberMissing => localizer["Catalog_Error_NumberMissing"],
        CatalogError.NumberTaken => localizer["Catalog_Error_NumberTaken"],
        CatalogError.NameMissing => localizer["Catalog_Error_NameMissing"],
        CatalogError.UnitInvalid => localizer["Catalog_Error_UnitInvalid"],
        CatalogError.PriceNegative => localizer["Catalog_Error_PriceNegative"],
        CatalogError.CurrencyInvalid => localizer["Catalog_Error_CurrencyInvalid"],
        CatalogError.VatRateInvalid => localizer["Catalog_Error_VatRateInvalid"],
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}
```

`SharedResourceTests`' regex is `[Ll]ocalizer\[\s*"` — the lowercase parameter name `localizer` matches it. Keep it.

- [ ] **Step 3: The organization page**

`Components/Business/OrganizationPage.razor`:

```razor
@page "/organization"
@rendermode @(new InteractiveServerRenderMode(prerender: false))
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = Permissions.OrganizationManage)]
@using Fakturenn.Modules.Invoices.Contracts
@using Fakturenn.Modules.Organizations
@using Fakturenn.Modules.Organizations.Contracts
@using Fakturenn.Modules.Organizations.Features
@inject CircuitOperations Operations
@inject NavigationManager Navigation
@inject IStringLocalizer<SharedResource> Localizer

<InteractiveProviders />
<PageTitle>@Localizer["Organization_PageTitle"]</PageTitle>
<MudText Typo="Typo.h4" GutterBottom="true" data-testid="organization-title">@Localizer["Organization_Title"]</MudText>

@if (_form is not null)
{
    <MudStack Spacing="2">
        <MudTextField @bind-Value="_legalName" Label="@Localizer["Organization_LegalName"]" data-testid="organization-legal-name" />
        <MudTextField @bind-Value="_country" Label="@Localizer["Organization_Country"]" MaxLength="2" data-testid="organization-country" />
        <MudTextField @bind-Value="_vatId" Label="@Localizer["Organization_VatId"]" data-testid="organization-vat-id" />
        <MudTextField @bind-Value="_iban" Label="@Localizer["Organization_Iban"]" data-testid="organization-iban" />
        <MudTextField @bind-Value="_currency" Label="@Localizer["Organization_Currency"]" MaxLength="3" data-testid="organization-currency" />
        <MudTextField @bind-Value="_timeZone" Label="@Localizer["Organization_TimeZone"]" data-testid="organization-time-zone" />
        <MudNumericField @bind-Value="_paymentTerms" Label="@Localizer["Organization_PaymentTerms"]" Min="0" Max="365" data-testid="organization-payment-terms" />
        <MudSelect T="IssueDateDefault" @bind-Value="_issueDateDefault" Label="@Localizer["Organization_IssueDateDefault"]" data-testid="organization-issue-date-default">
            <MudSelectItem Value="IssueDateDefault.Today">@Localizer["Organization_IssueDateDefault_Today"]</MudSelectItem>
            <MudSelectItem Value="IssueDateDefault.LastDayOfPreviousMonth">@Localizer["Organization_IssueDateDefault_LastDayOfPreviousMonth"]</MudSelectItem>
        </MudSelect>

        <MudText Typo="Typo.h6">@Localizer["Organization_NumberScheme"]</MudText>
        @if (_schemeLocked)
        {
            <MudAlert Severity="Severity.Info" data-testid="organization-scheme-locked">@Localizer["Organization_SchemeLockedNotice"]</MudAlert>
        }
        <MudTextField @bind-Value="_prefix" Label="@Localizer["Organization_Prefix"]" MaxLength="9" Disabled="_schemeLocked" data-testid="organization-prefix" />
        <MudSelect T="ResetScope" @bind-Value="_scope" Label="@Localizer["Organization_ResetScope"]" Disabled="_schemeLocked" data-testid="organization-reset-scope">
            <MudSelectItem Value="ResetScope.PerDay">@Localizer["Organization_ResetScope_PerDay"]</MudSelectItem>
            <MudSelectItem Value="ResetScope.PerMonth">@Localizer["Organization_ResetScope_PerMonth"]</MudSelectItem>
            <MudSelectItem Value="ResetScope.PerYear">@Localizer["Organization_ResetScope_PerYear"]</MudSelectItem>
            <MudSelectItem Value="ResetScope.Continuous">@Localizer["Organization_ResetScope_Continuous"]</MudSelectItem>
        </MudSelect>
        <MudNumericField @bind-Value="_padding" Label="@Localizer["Organization_Padding"]" Min="1" Max="9" Disabled="_schemeLocked" data-testid="organization-padding" />
        <MudNumericField @bind-Value="_nextNumber" Label="@Localizer["Organization_NextNumber"]" Min="1" Disabled="_schemeLocked" data-testid="organization-next-number" />

        @foreach (OrganizationError error in _errors)
        {
            <MudAlert Severity="Severity.Error" data-testid="organization-error">@RefusalMessages.For(Localizer, error)</MudAlert>
        }
        @if (_saved)
        {
            <MudAlert Severity="Severity.Success" data-testid="organization-saved">@Localizer["Organization_Saved"]</MudAlert>
        }
        <MudButton OnClick="SaveAsync" Variant="Variant.Filled" Color="Color.Primary" data-testid="organization-save">@Localizer["Common_Save"]</MudButton>
    </MudStack>
}

@code {
    private OrganizationForm? _form;
    private string _legalName = string.Empty;
    private string _country = string.Empty;
    private string _vatId = string.Empty;
    private string _iban = string.Empty;
    private string _currency = string.Empty;
    private string _timeZone = string.Empty;
    private int _paymentTerms;
    private IssueDateDefault _issueDateDefault;
    private string _prefix = string.Empty;
    private ResetScope _scope;
    private int _padding;
    private long _nextNumber;
    private bool _schemeLocked;
    private bool _saved;
    private IReadOnlyList<OrganizationError> _errors = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _form = await Operations.RunAsync<GetOrganization, OrganizationForm>(
                Permissions.OrganizationManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);
            _schemeLocked = await Operations.RunAsync<INumberingStatus, bool>(
                Permissions.OrganizationManage, (status, token) => status.AnyNumberAllocatedAsync(token), CancellationToken.None);
        }
        catch (OperationRefusedException)
        {
            Navigation.NavigateTo("/account/denied", forceLoad: true);
            return;
        }

        (_legalName, _country, _vatId, _iban, _currency, _timeZone) =
            (_form.LegalName, _form.CountryCode, _form.VatId, _form.Iban, _form.Currency, _form.TimeZoneId);
        (_paymentTerms, _issueDateDefault, _prefix, _scope, _padding, _nextNumber) =
            (_form.PaymentTermsDays, _form.IssueDateDefault, _form.NumberPrefix, _form.NumberScope, _form.NumberPadding, _form.NumberNextValue);
    }

    private async Task SaveAsync()
    {
        _saved = false;
        OrganizationForm form = new(_legalName, _country, _vatId, _iban, _currency, _timeZone, _paymentTerms,
            _issueDateDefault, _prefix, _scope, _padding, _nextNumber);
        try
        {
            _errors = await Operations.RunAsync<SaveOrganization, IReadOnlyList<OrganizationError>>(
                Permissions.OrganizationManage, (slice, token) => slice.HandleAsync(form, token), CancellationToken.None);
        }
        catch (OperationRefusedException)
        {
            Navigation.NavigateTo("/account/denied", forceLoad: true);
            return;
        }

        _saved = _errors.Count == 0;
    }
}
```

- [ ] **Step 4: The customers page, with projects and item numbers**

A table cell carries `data-testid` rather than the row: `MudTable` gives no way to put an attribute on its `<tr>`, and clicking the cell clicks the row.

`Components/Business/CustomersPage.razor` (UTF-8 with BOM):

```razor
@page "/customers"
@rendermode @(new InteractiveServerRenderMode(prerender: false))
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = Permissions.MasterDataManage)]
@using Fakturenn.Modules.Catalog.Contracts
@using Fakturenn.Modules.Catalog.Features
@using Fakturenn.Modules.Customers
@using Fakturenn.Modules.Customers.Contracts
@using Fakturenn.Modules.Customers.Features
@using Fakturenn.Modules.Projects.Contracts
@using Fakturenn.Modules.Projects.Features
@using Microsoft.EntityFrameworkCore
@inject CircuitOperations Operations
@inject NavigationManager Navigation
@inject IStringLocalizer<SharedResource> Localizer

<InteractiveProviders />
<PageTitle>@Localizer["Customers_PageTitle"]</PageTitle>
<MudText Typo="Typo.h4" GutterBottom="true">@Localizer["Customers_Title"]</MudText>

<MudTable Items="_customers" Hover="true" OnRowClick="@((TableRowClickEventArgs<CustomerSnapshot> args) => SelectAsync(args.Item!))" Class="mb-6">
    <HeaderContent>
        <MudTh>@Localizer["Customers_Number"]</MudTh>
        <MudTh>@Localizer["Customers_LegalName"]</MudTh>
        <MudTh>@Localizer["Customers_Country"]</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd data-testid="customer-row">@context.CustomerNumber</MudTd>
        <MudTd>@context.LegalName</MudTd>
        <MudTd>@context.CountryCode</MudTd>
    </RowTemplate>
</MudTable>

<MudStack Spacing="2">
    <MudTextField @bind-Value="_legalName" Label="@Localizer["Customers_LegalName"]" data-testid="customer-legal-name" />
    <MudTextField @bind-Value="_country" Label="@Localizer["Customers_Country"]" MaxLength="2" data-testid="customer-country" />
    <MudTextField @bind-Value="_vatId" Label="@Localizer["Customers_VatId"]" data-testid="customer-vat-id" />
    <MudTextField @bind-Value="_number" Label="@Localizer["Customers_Number"]" data-testid="customer-number" />
    @foreach (string error in _errors)
    {
        <MudAlert Severity="Severity.Error" data-testid="customer-error">@error</MudAlert>
    }
    <MudStack Row="true">
        <MudButton OnClick="SaveAsync" Variant="Variant.Filled" Color="Color.Primary" data-testid="customer-save">@Localizer["Common_Save"]</MudButton>
        <MudButton OnClick="New" Variant="Variant.Text" data-testid="customer-new">@Localizer["Common_New"]</MudButton>
    </MudStack>
</MudStack>

@if (_selectedId is Guid selected)
{
    <MudText Typo="Typo.h6" Class="mt-8">@Localizer["Customers_Projects"]</MudText>
    <MudTable Items="_projects" Dense="true">
        <RowTemplate>
            <MudTd data-testid="project-row">@context.Reference</MudTd>
        </RowTemplate>
    </MudTable>
    <MudStack Row="true" AlignItems="AlignItems.Center">
        <MudTextField @bind-Value="_projectReference" Label="@Localizer["Customers_ProjectReference"]" data-testid="project-reference" />
        <MudButton OnClick="AddProjectAsync" Variant="Variant.Outlined" data-testid="project-add">@Localizer["Common_Add"]</MudButton>
    </MudStack>

    <MudText Typo="Typo.h6" Class="mt-8">@Localizer["Customers_ItemNumbers"]</MudText>
    <MudTable Items="_references" Dense="true">
        <RowTemplate>
            <MudTd data-testid="reference-row">@ItemNumber(context.Key)</MudTd>
            <MudTd>@context.Value</MudTd>
        </RowTemplate>
    </MudTable>
    <MudStack Row="true" AlignItems="AlignItems.Center">
        <MudSelect T="Guid" @bind-Value="_referenceItemId" Label="@Localizer["Customers_CatalogItem"]" data-testid="reference-catalog-item">
            @foreach (CatalogItemSnapshot item in _items)
            {
                <MudSelectItem Value="item.Id">@item.CatalogItemNumber</MudSelectItem>
            }
        </MudSelect>
        <MudTextField @bind-Value="_referenceNumber" Label="@Localizer["Customers_CustomerCatalogItemNumber"]" data-testid="reference-number" />
        <MudButton OnClick="SaveReferenceAsync" Variant="Variant.Outlined" data-testid="reference-save">@Localizer["Common_Save"]</MudButton>
    </MudStack>
}

@code {
    private IReadOnlyList<CustomerSnapshot> _customers = [];
    private IReadOnlyList<ProjectSnapshot> _projects = [];
    private IReadOnlyList<CatalogItemSnapshot> _items = [];
    private IReadOnlyDictionary<Guid, string> _references = new Dictionary<Guid, string>();
    private Guid? _selectedId;
    private string _legalName = string.Empty;
    private string _country = string.Empty;
    private string _vatId = string.Empty;
    private string _number = string.Empty;
    private string _projectReference = string.Empty;
    private Guid _referenceItemId;
    private string _referenceNumber = string.Empty;
    private IReadOnlyList<string> _errors = [];

    protected override Task OnInitializedAsync() => RunAsync(LoadListsAsync);

    private async Task LoadListsAsync()
    {
        _customers = await Operations.RunAsync<ListCustomers, IReadOnlyList<CustomerSnapshot>>(
            Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);
        _items = await Operations.RunAsync<ListCatalogItems, IReadOnlyList<CatalogItemSnapshot>>(
            Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);
    }

    private Task SelectAsync(CustomerSnapshot customer) => RunAsync(async () =>
    {
        _selectedId = customer.Id;
        (_legalName, _country, _vatId, _number) = (customer.LegalName, customer.CountryCode, customer.VatId ?? string.Empty, customer.CustomerNumber);
        _errors = [];
        await LoadSelectionAsync(customer.Id);
    });

    private async Task LoadSelectionAsync(Guid customerId)
    {
        _projects = await Operations.RunAsync<ListProjects, IReadOnlyList<ProjectSnapshot>>(
            Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(customerId, token), CancellationToken.None);
        _references = await Operations.RunAsync<ListCustomers, IReadOnlyDictionary<Guid, string>>(
            Permissions.MasterDataManage, (slice, token) => slice.ReferencesAsync(customerId, token), CancellationToken.None);
    }

    private void New()
    {
        _selectedId = null;
        (_legalName, _country, _vatId, _number) = (string.Empty, string.Empty, string.Empty, string.Empty);
        _errors = [];
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        CustomerForm form = new(_selectedId, _legalName, _country, _vatId, _number);
        SaveResult result;
        try
        {
            result = await Operations.RunAsync<SaveCustomer, SaveResult>(
                Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(form, token), CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            // Two saves raced on one number; the unique index decided.
            result = new SaveResult(null, [CustomerError.CustomerNumberTaken]);
        }

        _errors = [.. result.Errors.Select(error => RefusalMessages.For(Localizer, error))];
        if (result.Id is Guid id)
        {
            _selectedId = id;
            await LoadListsAsync();
            await LoadSelectionAsync(id);
        }
    });

    private Task AddProjectAsync() => RunAsync(async () =>
    {
        if (_selectedId is not Guid customerId || string.IsNullOrWhiteSpace(_projectReference))
        {
            return;
        }

        string reference = _projectReference;
        await Operations.RunAsync<SaveProject, Guid>(
            Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(null, customerId, reference, token), CancellationToken.None);
        _projectReference = string.Empty;
        await LoadSelectionAsync(customerId);
    });

    private Task SaveReferenceAsync() => RunAsync(async () =>
    {
        if (_selectedId is not Guid customerId || _referenceItemId == Guid.Empty)
        {
            return;
        }

        (Guid itemId, string number) = (_referenceItemId, _referenceNumber);
        await Operations.RunAsync<SaveCatalogItemReference>(
            Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(customerId, itemId, number, token), CancellationToken.None);
        _referenceNumber = string.Empty;
        await LoadSelectionAsync(customerId);
    });

    private string ItemNumber(Guid id) =>
        _items.FirstOrDefault(item => item.Id == id)?.CatalogItemNumber ?? id.ToString();

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationRefusedException)
        {
            // A full reload re-runs cookie authentication, which a circuit never repeats.
            Navigation.NavigateTo("/account/denied", forceLoad: true);
        }
    }
}
```

- [ ] **Step 5: The catalog page**

`Components/Business/CatalogPage.razor` (UTF-8 with BOM):

```razor
@page "/catalog"
@rendermode @(new InteractiveServerRenderMode(prerender: false))
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = Permissions.MasterDataManage)]
@using Fakturenn.Modules.Catalog
@using Fakturenn.Modules.Catalog.Contracts
@using Fakturenn.Modules.Catalog.Features
@using Microsoft.EntityFrameworkCore
@inject CircuitOperations Operations
@inject NavigationManager Navigation
@inject IStringLocalizer<SharedResource> Localizer

<InteractiveProviders />
<PageTitle>@Localizer["Catalog_PageTitle"]</PageTitle>
<MudText Typo="Typo.h4" GutterBottom="true">@Localizer["Catalog_Title"]</MudText>

<MudTable Items="_items" Hover="true" OnRowClick="@((TableRowClickEventArgs<CatalogItemSnapshot> args) => Select(args.Item!))" Class="mb-6">
    <HeaderContent>
        <MudTh>@Localizer["Catalog_Number"]</MudTh>
        <MudTh>@Localizer["Catalog_Name"]</MudTh>
        <MudTh>@Localizer["Catalog_UnitPrice"]</MudTh>
        <MudTh>@Localizer["Catalog_VatRate"]</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd data-testid="catalog-row">@context.CatalogItemNumber</MudTd>
        <MudTd>@context.Name</MudTd>
        <MudTd>@context.UnitPrice.ToString("N2") @context.Currency / @context.Unit</MudTd>
        <MudTd>@context.VatRate.ToString("0.##")&nbsp;%</MudTd>
    </RowTemplate>
</MudTable>

<MudStack Spacing="2">
    <MudTextField @bind-Value="_number" Label="@Localizer["Catalog_Number"]" data-testid="catalog-number" />
    <MudTextField @bind-Value="_name" Label="@Localizer["Catalog_Name"]" data-testid="catalog-name" />
    <MudTextField @bind-Value="_unit" Label="@Localizer["Catalog_Unit"]" MaxLength="3" data-testid="catalog-unit" />
    <MudNumericField T="decimal" @bind-Value="_price" Label="@Localizer["Catalog_UnitPrice"]" Min="0" data-testid="catalog-price" />
    <MudTextField @bind-Value="_currency" Label="@Localizer["Catalog_Currency"]" MaxLength="3" data-testid="catalog-currency" />
    <MudNumericField T="decimal" @bind-Value="_vatRate" Label="@Localizer["Catalog_VatRate"]" Min="0" Max="100" data-testid="catalog-vat-rate" />
    @foreach (string error in _errors)
    {
        <MudAlert Severity="Severity.Error" data-testid="catalog-error">@error</MudAlert>
    }
    <MudStack Row="true">
        <MudButton OnClick="SaveAsync" Variant="Variant.Filled" Color="Color.Primary" data-testid="catalog-save">@Localizer["Common_Save"]</MudButton>
        <MudButton OnClick="New" Variant="Variant.Text" data-testid="catalog-new">@Localizer["Common_New"]</MudButton>
    </MudStack>
</MudStack>

@code {
    private IReadOnlyList<CatalogItemSnapshot> _items = [];
    private Guid? _selectedId;
    private string _number = string.Empty;
    private string _name = string.Empty;
    private string _unit = "HUR";
    private decimal _price;
    private string _currency = "EUR";
    private decimal _vatRate = 19m;
    private IReadOnlyList<string> _errors = [];

    protected override Task OnInitializedAsync() => RunAsync(LoadAsync);

    private async Task LoadAsync() =>
        _items = await Operations.RunAsync<ListCatalogItems, IReadOnlyList<CatalogItemSnapshot>>(
            Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);

    private void Select(CatalogItemSnapshot item)
    {
        _selectedId = item.Id;
        (_number, _name, _unit, _price, _currency, _vatRate) = (item.CatalogItemNumber, item.Name, item.Unit, item.UnitPrice, item.Currency, item.VatRate);
        _errors = [];
    }

    private void New()
    {
        _selectedId = null;
        (_number, _name, _unit, _price, _currency, _vatRate) = (string.Empty, string.Empty, "HUR", 0m, "EUR", 19m);
        _errors = [];
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        CatalogItemForm form = new(_selectedId, _number, _name, _unit, _price, _currency, _vatRate);
        CatalogSaveResult result;
        try
        {
            result = await Operations.RunAsync<SaveCatalogItem, CatalogSaveResult>(
                Permissions.MasterDataManage, (slice, token) => slice.HandleAsync(form, token), CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            result = new CatalogSaveResult(null, [CatalogError.NumberTaken]);
        }

        _errors = [.. result.Errors.Select(error => RefusalMessages.For(Localizer, error))];
        if (result.Id is Guid id)
        {
            _selectedId = id;
            await LoadAsync();
        }
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationRefusedException)
        {
            Navigation.NavigateTo("/account/denied", forceLoad: true);
        }
    }
}
```

- [ ] **Step 6: The invoice list and the invoice page**

`Components/Business/InvoicesPage.razor` (UTF-8 with BOM). An invoice author must see the customers to pick one, so this page runs the `ListCustomers` read under `invoices.manage`:

```razor
@page "/invoices"
@rendermode @(new InteractiveServerRenderMode(prerender: false))
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = Permissions.InvoicesManage)]
@using Fakturenn.Modules.Customers.Contracts
@using Fakturenn.Modules.Customers.Features
@using Fakturenn.Modules.Invoices.Features
@inject CircuitOperations Operations
@inject NavigationManager Navigation
@inject IStringLocalizer<SharedResource> Localizer

<InteractiveProviders />
<PageTitle>@Localizer["Invoices_PageTitle"]</PageTitle>
<MudText Typo="Typo.h4" GutterBottom="true">@Localizer["Invoices_Title"]</MudText>

<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-6">
    <MudSelect T="Guid" @bind-Value="_newCustomerId" Label="@Localizer["Invoices_Customer"]" data-testid="invoice-new-customer">
        @foreach (CustomerSnapshot customer in _customers)
        {
            <MudSelectItem Value="customer.Id">@customer.LegalName</MudSelectItem>
        }
    </MudSelect>
    <MudButton OnClick="CreateAsync" Disabled="_newCustomerId == Guid.Empty" Variant="Variant.Filled" Color="Color.Primary" data-testid="invoice-new">@Localizer["Invoices_New"]</MudButton>
</MudStack>

<MudTable Items="_invoices" Hover="true" OnRowClick="@((TableRowClickEventArgs<InvoiceView> args) => Navigation.NavigateTo($"/invoices/{args.Item!.Id}"))">
    <HeaderContent>
        <MudTh>@Localizer["Invoices_Number"]</MudTh>
        <MudTh>@Localizer["Invoices_State"]</MudTh>
        <MudTh>@Localizer["Invoices_IssueDate"]</MudTh>
        <MudTh>@Localizer["Invoices_Gross"]</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd data-testid="invoice-row">@(context.Number ?? "—")</MudTd>
        <MudTd>@context.State</MudTd>
        <MudTd>@context.IssueDate.ToString("d")</MudTd>
        <MudTd>@(context.Gross?.ToString("N2") ?? "—") @context.Currency</MudTd>
    </RowTemplate>
</MudTable>

@code {
    private IReadOnlyList<CustomerSnapshot> _customers = [];
    private IReadOnlyList<InvoiceView> _invoices = [];
    private Guid _newCustomerId;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _customers = await Operations.RunAsync<ListCustomers, IReadOnlyList<CustomerSnapshot>>(
                Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);
            _invoices = await Operations.RunAsync<ListInvoices, IReadOnlyList<InvoiceView>>(
                Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);
        }
        catch (OperationRefusedException)
        {
            Navigation.NavigateTo("/account/denied", forceLoad: true);
        }
    }

    private async Task CreateAsync()
    {
        Guid customerId = _newCustomerId;
        try
        {
            Guid id = await Operations.RunAsync<CreateDraft, Guid>(
                Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(customerId, token), CancellationToken.None);
            Navigation.NavigateTo($"/invoices/{id}");
        }
        catch (OperationRefusedException)
        {
            Navigation.NavigateTo("/account/denied", forceLoad: true);
        }
    }
}
```

`Components/Business/InvoicePage.razor` (UTF-8 with BOM). Every change saves the draft and re-runs the preview, so totals arrive over the circuit with no page load. Finalization is two clicks: the second confirms the gross from the last preview, and a changed price makes it refuse and show the new totals for another confirmation (review M12). The detail fields come from `InvoiceView` columns, never from the snapshot.

```razor
@page "/invoices/{Id:guid}"
@rendermode @(new InteractiveServerRenderMode(prerender: false))
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = Permissions.InvoicesManage)]
@using Fakturenn.Modules.Catalog.Contracts
@using Fakturenn.Modules.Catalog.Features
@using Fakturenn.Modules.Customers.Contracts
@using Fakturenn.Modules.Customers.Features
@using Fakturenn.Modules.Invoices.Domain
@using Fakturenn.Modules.Invoices.Features
@using Fakturenn.Modules.Projects.Contracts
@using Fakturenn.Modules.Projects.Features
@inject CircuitOperations Operations
@inject NavigationManager Navigation
@inject IStringLocalizer<SharedResource> Localizer

<InteractiveProviders />
<PageTitle>@Localizer["Invoice_PageTitle"]</PageTitle>

@if (_invoice is not null)
{
    <MudText Typo="Typo.h4" GutterBottom="true">
        @Localizer["Invoice_Title"] <span data-testid="invoice-number">@_invoice.Number</span>
    </MudText>
    <MudText Typo="Typo.subtitle1" data-testid="invoice-state">@_invoice.State</MudText>

    <MudStack Spacing="2">
        <MudSelect T="Guid" @bind-Value="_customerId" @bind-Value:after="CustomerChangedAsync" Disabled="Locked" Label="@Localizer["Invoice_Customer"]" data-testid="invoice-customer">
            @foreach (CustomerSnapshot customer in _customers)
            {
                <MudSelectItem Value="customer.Id">@customer.LegalName</MudSelectItem>
            }
        </MudSelect>
        <MudSelect T="Guid" @bind-Value="_projectId" @bind-Value:after="ChangedAsync" Disabled="Locked" Clearable="true" Label="@Localizer["Invoice_Project"]" data-testid="invoice-project">
            @foreach (ProjectSnapshot project in _projects)
            {
                <MudSelectItem Value="project.Id">@project.Reference</MudSelectItem>
            }
        </MudSelect>
        <MudSelect T="Guid" @bind-Value="_catalogItemId" @bind-Value:after="ChangedAsync" Disabled="Locked" Label="@Localizer["Invoice_CatalogItem"]" data-testid="invoice-catalog-item">
            @foreach (CatalogItemSnapshot item in _items)
            {
                <MudSelectItem Value="item.Id">@item.CatalogItemNumber</MudSelectItem>
            }
        </MudSelect>
        <MudNumericField T="decimal" @bind-Value="_quantity" @bind-Value:after="ChangedAsync" Disabled="Locked" Label="@Localizer["Invoice_Quantity"]" data-testid="invoice-quantity" />
        <MudDatePicker @bind-Date="_issueDate" @bind-Date:after="ChangedAsync" Disabled="@(_invoice.State != InvoiceState.Draft)" Label="@Localizer["Invoice_IssueDate"]" data-testid="invoice-issue-date" />
        <MudDatePicker @bind-Date="_periodStart" @bind-Date:after="ChangedAsync" Disabled="Locked" Label="@Localizer["Invoice_ServicePeriodStart"]" data-testid="invoice-period-start" />
        <MudDatePicker @bind-Date="_periodEnd" @bind-Date:after="ChangedAsync" Disabled="Locked" Label="@Localizer["Invoice_ServicePeriodEnd"]" data-testid="invoice-period-end" />
    </MudStack>

    <MudPaper Class="pa-4 mt-6" Outlined="true">
        @if (_preview?.Totals is InvoiceTotals totals)
        {
            <MudText>@Localizer["Invoice_Net"]: <span data-testid="invoice-net">@totals.Net.ToString("N2")</span> @totals.Currency</MudText>
            <MudText>@Localizer["Invoice_Vat"]: <span data-testid="invoice-vat">@totals.Vat.ToString("N2")</span> @totals.Currency</MudText>
            <MudText Typo="Typo.h6">@Localizer["Invoice_Gross"]: <span data-testid="invoice-gross">@totals.Gross.ToString("N2")</span> @totals.Currency</MudText>
        }
        @if (_preview?.Refusal is FinalizationRefusal refusal)
        {
            <MudAlert Severity="Severity.Warning" data-testid="invoice-refusal">@RefusalMessages.For(Localizer, refusal)</MudAlert>
        }
        @if (_invoice.DueDate is DateOnly due)
        {
            <MudText>@Localizer["Invoice_DueDate"]: @due.ToString("d")</MudText>
        }
    </MudPaper>

    @if (!Locked && _preview is { Succeeded: true, Totals: not null })
    {
        @if (!_confirming)
        {
            <MudButton OnClick="() => _confirming = true" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4" data-testid="invoice-review">@Localizer["Invoice_Review"]</MudButton>
        }
        else
        {
            <MudPaper Class="pa-4 mt-4" Elevation="2" data-testid="invoice-confirm-panel">
                <MudText>@Localizer["Invoice_ConfirmText"] @_preview.Totals.Gross.ToString("N2") @_preview.Totals.Currency</MudText>
                <MudButton OnClick="FinalizeAsync" Variant="Variant.Filled" Color="Color.Primary" data-testid="invoice-confirm">@Localizer["Invoice_Confirm"]</MudButton>
                <MudButton OnClick="() => _confirming = false" Variant="Variant.Text">@Localizer["Common_Cancel"]</MudButton>
            </MudPaper>
        }
    }
}

@code {
    private InvoiceView? _invoice;
    private FinalizationResult? _preview;
    private IReadOnlyList<CustomerSnapshot> _customers = [];
    private IReadOnlyList<ProjectSnapshot> _projects = [];
    private IReadOnlyList<CatalogItemSnapshot> _items = [];
    private Guid _customerId;
    private Guid _projectId;
    private Guid _catalogItemId;
    private decimal _quantity;
    private DateTime? _issueDate;
    private DateTime? _periodStart;
    private DateTime? _periodEnd;
    private bool _confirming;

    [Parameter]
    public Guid Id { get; set; }

    private bool Locked => _invoice?.State == InvoiceState.Locked;

    protected override Task OnInitializedAsync() => RunAsync(async () =>
    {
        _customers = await Operations.RunAsync<ListCustomers, IReadOnlyList<CustomerSnapshot>>(
            Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);
        _items = await Operations.RunAsync<ListCatalogItems, IReadOnlyList<CatalogItemSnapshot>>(
            Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(token), CancellationToken.None);
        await ReloadAsync();
    });

    private async Task ReloadAsync()
    {
        _invoice = await Operations.RunAsync<GetInvoice, InvoiceView?>(
            Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(Id, token), CancellationToken.None);
        if (_invoice is null)
        {
            Navigation.NavigateTo("/invoices");
            return;
        }

        (_customerId, _projectId, _catalogItemId, _quantity) =
            (_invoice.CustomerId, _invoice.ProjectId ?? Guid.Empty, _invoice.CatalogItemId ?? Guid.Empty, _invoice.Quantity);
        (_issueDate, _periodStart, _periodEnd) = (
            _invoice.IssueDate.ToDateTime(TimeOnly.MinValue),
            _invoice.ServicePeriodStart.ToDateTime(TimeOnly.MinValue),
            _invoice.ServicePeriodEnd.ToDateTime(TimeOnly.MinValue));
        await LoadProjectsAsync();
        await PreviewAsync();
    }

    private async Task LoadProjectsAsync()
    {
        Guid customerId = _customerId;
        _projects = await Operations.RunAsync<ListProjects, IReadOnlyList<ProjectSnapshot>>(
            Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(customerId, token), CancellationToken.None);
    }

    private Task CustomerChangedAsync() => RunAsync(async () =>
    {
        _projectId = Guid.Empty;
        await LoadProjectsAsync();
        await SaveAndPreviewAsync();
    });

    private Task ChangedAsync() => RunAsync(SaveAndPreviewAsync);

    private async Task SaveAndPreviewAsync()
    {
        _confirming = false;
        if (_catalogItemId == Guid.Empty || _issueDate is null || _periodStart is null || _periodEnd is null)
        {
            return;
        }

        DraftForm form = new(
            Id, _customerId, _projectId == Guid.Empty ? null : _projectId, _catalogItemId, _quantity,
            DateOnly.FromDateTime(_issueDate.Value), DateOnly.FromDateTime(_periodStart.Value), DateOnly.FromDateTime(_periodEnd.Value));
        await Operations.RunAsync<SaveDraft>(
            Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(form, token), CancellationToken.None);
        await PreviewAsync();
    }

    private async Task PreviewAsync() =>
        _preview = await Operations.RunAsync<PreviewInvoice, FinalizationResult>(
            Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(Id, token), CancellationToken.None);

    private Task FinalizeAsync() => RunAsync(async () =>
    {
        decimal confirmed = _preview!.Totals!.Gross;
        FinalizationResult result = await Operations.RunAsync<FinalizeInvoice, FinalizationResult>(
            Permissions.InvoicesManage, (slice, token) => slice.HandleAsync(Id, confirmed, token), CancellationToken.None);

        if (result.Refusal is FinalizationRefusal.TotalsChanged)
        {
            // Keep the panel open over the new totals for a second confirmation.
            _preview = result;
            return;
        }

        _confirming = false;
        await ReloadAsync();
        if (!result.Succeeded)
        {
            _preview = result;
        }
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationRefusedException)
        {
            Navigation.NavigateTo("/account/denied", forceLoad: true);
        }
        catch (InvalidOperationException)
        {
            // The aggregate refused a change its state forbids -- another tab numbered or
            // locked the invoice meanwhile. Show the invoice as it now is.
            await ReloadAsync();
        }
    }
}
```

- [ ] **Step 6a: Refused at the page, not only at the slice**

Spec section 11: a signed-in user without the permission is refused at the page. The slice half is `CircuitOperationsTests`. Add to `tests/Fakturenn.IntegrationTests/AdminUserManagementTests.cs`, which already has `SignedInClientAsync` (a fully enrolled user holding no role) and `LocationPath`:

```csharp
    [Theory]
    [InlineData("/organization")]
    [InlineData("/customers")]
    [InlineData("/catalog")]
    [InlineData("/invoices")]
    public async Task A_business_page_refuses_a_user_without_its_permission(string path)
    {
        using HttpClient client = await SignedInClientAsync($"page-forbidden-{Slug(path)}@example.test");

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        LocationPath(response).Should().Be("/account/denied", $"{path} must require its permission");
    }
```

Run it after Step 11's build. Expected: PASS. Mutation: copy `CatalogPage.razor` aside, delete its `@attribute [Authorize…]` line, rebuild, run the `/catalog` row — Expected: FAIL with a 200 (the fallback policy admits any signed-in user). Restore, `sha256sum --check`.

- [ ] **Step 7: Resource keys, English and German**

Add every key the pages, the layout and `RefusalMessages` look up to `SharedResource.resx` and `SharedResource.de.resx`. Generate the list from the source instead of from memory:

```bash
grep -rhoE '[Ll]ocalizer\[\s*"[^"]+"' src/Fakturenn.Web --include=*.razor --include=*.cs | sed -E 's/.*"([^"]+)"/\1/' | sort -u > /tmp/claude-keys.txt
python3 - <<'PY'
import re
have = set(re.findall(r'<data name="([^"]+)"', open("src/Fakturenn.Web/Resources/SharedResource.resx", encoding="utf-8-sig").read()))
need = set(open("/tmp/claude-keys.txt").read().split())
print("\n".join(sorted(need - have)))
PY
```

Expected: the printed list is exactly the new keys. Give each an English and a German value. German refusals name the cause plainly, for example `Refusal_CrossBorderVatNotImplemented` = "Rechnungen an Kunden in einem anderen Land werden noch nicht unterstützt: Reverse Charge und Ausfuhr folgen mit M2." Keep both files' BOMs.

- [ ] **Step 8: UI fixture additions**

In `tests/Fakturenn.UiTests/AuthenticatedWebAppFixture.cs`, add a helper that grants the Administrator role to an enrolled UI account, mirroring `SetupHostFixture.AssignAdministratorRoleAsync` but resolving the user by e-mail through `UserManager<ApplicationUser>` from `Services`:

```csharp
    public async Task AssignAdministratorRoleAsync(string email)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user with the e-mail address {email}.");
        Guid roleId = await context.Roles
            .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
            .Select(role => role.Id)
            .SingleAsync();
        context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        await context.SaveChangesAsync();
        await users.UpdateSecurityStampAsync(user);
    }
```

The fixture's `MigrateAsync` must also run `RoleSeeder.SeedAsync` if it does not already, so the Administrator role holds the three new permissions. Read it before adding the call.

- [ ] **Step 9: The journey test**

Create `tests/Fakturenn.UiTests/InvoiceJourneyTests.cs`, in the same collection and with the same browser setup as `IdentityJourneyTests` (read its class header and fields first and mirror them):

```csharp
    [Fact]
    public async Task An_administrator_numbers_the_walking_skeleton_invoice()
    {
        IPage page = await app.SignInAsAdministratorAsync(browser);
        await EnsureMasterDataAsync(page, app);

        await page.GotoAsync(app.Url("/invoices"));
        await page.GetByTestId("invoice-new-customer").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Example Client GmbH" }).ClickAsync();
        await page.GetByTestId("invoice-new").ClickAsync();
        await page.GetByTestId("invoice-catalog-item").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "DEV-BACKEND" }).ClickAsync();
        await page.GetByTestId("invoice-quantity").Locator("input").FillAsync("8");
        await page.GetByTestId("invoice-quantity").Locator("input").PressAsync("Tab");

        // Totals arrive over the circuit, with no page load in between.
        await Expect(page.GetByTestId("invoice-gross")).ToContainTextAsync("952");
        await page.GetByTestId("invoice-review").ClickAsync();
        await page.GetByTestId("invoice-confirm").ClickAsync();

        await Expect(page.GetByTestId("invoice-number")).ToHaveTextAsync(new Regex("^R[0-9]{6}[0-9]+$"));
    }

    [Fact]
    public async Task A_german_user_enters_a_decimal_quantity_with_a_comma()
    {
        // Spec section 9, review M14: the circuit parses with the culture it started with.
        IPage english = await app.SignInAsAdministratorAsync(browser);
        await EnsureMasterDataAsync(english, app);

        IPage page = await app.SignInAsAdministratorAsync(browser, AuthenticatedWebAppFixture.GermanLocale);
        await page.GotoAsync(app.Url("/invoices"));
        await page.GetByTestId("invoice-new-customer").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Example Client GmbH" }).ClickAsync();
        await page.GetByTestId("invoice-new").ClickAsync();
        await page.GetByTestId("invoice-catalog-item").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "DEV-BACKEND" }).ClickAsync();
        await page.GetByTestId("invoice-quantity").Locator("input").FillAsync("8,5");
        await page.GetByTestId("invoice-quantity").Locator("input").PressAsync("Tab");

        await Expect(page.GetByTestId("invoice-net")).ToContainTextAsync("850,00");
    }

    // private static Methods
    /// <summary>
    /// Completes the organization and creates the walking skeleton's item, customer and
    /// project, skipping whatever an earlier test in this serial assembly already created.
    /// Opening the reset-scope select goes through MudPopoverProvider, so this fails if the
    /// providers are back in the static layout (review C9).
    /// </summary>
    private static async Task EnsureMasterDataAsync(IPage page, AuthenticatedWebAppFixture app)
    {
        await page.GotoAsync(app.Url("/organization"));
        await page.GetByTestId("organization-legal-name").Locator("input").FillAsync("Example Consulting");
        await page.GetByTestId("organization-country").Locator("input").FillAsync("DE");
        await page.GetByTestId("organization-vat-id").Locator("input").FillAsync("DE123456789");
        await page.GetByTestId("organization-iban").Locator("input").FillAsync("DE89370400440532013000");
        await page.GetByTestId("organization-currency").Locator("input").FillAsync("EUR");
        if (await page.GetByTestId("organization-scheme-locked").CountAsync() == 0)
        {
            await page.GetByTestId("organization-prefix").Locator("input").FillAsync("R");
            await page.GetByTestId("organization-reset-scope").ClickAsync();
            await page.GetByRole(AriaRole.Option, new() { Name = "Per day" }).ClickAsync();
        }

        await page.GetByTestId("organization-save").ClickAsync();
        await Expect(page.GetByTestId("organization-saved")).ToBeVisibleAsync();

        await page.GotoAsync(app.Url("/catalog"));
        if (await page.GetByTestId("catalog-row").Filter(new() { HasText = "DEV-BACKEND" }).CountAsync() == 0)
        {
            await page.GetByTestId("catalog-number").Locator("input").FillAsync("DEV-BACKEND");
            await page.GetByTestId("catalog-name").Locator("input").FillAsync("Backend development");
            await page.GetByTestId("catalog-unit").Locator("input").FillAsync("HUR");
            await page.GetByTestId("catalog-price").Locator("input").FillAsync("100");
            await page.GetByTestId("catalog-currency").Locator("input").FillAsync("EUR");
            await page.GetByTestId("catalog-vat-rate").Locator("input").FillAsync("19");
            await page.GetByTestId("catalog-save").ClickAsync();
            await Expect(page.GetByTestId("catalog-row").Filter(new() { HasText = "DEV-BACKEND" })).ToBeVisibleAsync();
        }

        await page.GotoAsync(app.Url("/customers"));
        if (await page.GetByTestId("customer-row").Filter(new() { HasText = "C-4711" }).CountAsync() == 0)
        {
            await page.GetByTestId("customer-legal-name").Locator("input").FillAsync("Example Client GmbH");
            await page.GetByTestId("customer-country").Locator("input").FillAsync("DE");
            await page.GetByTestId("customer-number").Locator("input").FillAsync("C-4711");
            await page.GetByTestId("customer-save").ClickAsync();
            await page.GetByTestId("project-reference").Locator("input").FillAsync("PRJ-2026-083");
            await page.GetByTestId("project-add").ClickAsync();
            await Expect(page.GetByTestId("project-row")).ToHaveTextAsync("PRJ-2026-083");
        }
    }
```

`Per day` is the English value of `Organization_ResetScope_PerDay` — if Step 7 words it differently, use that wording. If MudBlazor 9.11 renders select options without `role="option"`, select them with `page.GetByText(name, new() { Exact = true })` instead and say so in the commit; check once with the browser's accessibility tree before choosing.

- [ ] **Step 10: The circuit security tests**

Create `tests/Fakturenn.UiTests/CircuitSecurityTests.cs`, in the identity collection beside `AuthorizationJourneyTests`:

```csharp
    [Fact]
    public async Task A_user_locked_while_a_business_page_is_open_is_refused_on_the_next_change()
    {
        // Review S3. The page is open and its circuit is live; the lock rotates the
        // security stamp; the revalidating provider notices within one interval; the next
        // operation is refused rather than saved.
        UiAccount account = await app.CreateEnrolledUserAsync("circuit-lock@example.test", "Korrekt-Pferd-42");
        await app.AssignAdministratorRoleAsync(account.Email);
        IPage page = await app.SignInAsync(browser, account);
        await page.GotoAsync(app.Url("/catalog"));
        await Expect(page.GetByTestId("catalog-save")).ToBeVisibleAsync();

        await app.LockUserAsync(account.Email);
        await Task.Delay(TimeSpan.FromSeconds(65), TestContext.Current.CancellationToken);

        await page.GetByTestId("catalog-number").Locator("input").FillAsync("LOCKED-OUT");
        await page.GetByTestId("catalog-name").Locator("input").FillAsync("Never saved");
        await page.GetByTestId("catalog-unit").Locator("input").FillAsync("HUR");
        await page.GetByTestId("catalog-currency").Locator("input").FillAsync("EUR");
        await page.GetByTestId("catalog-save").ClickAsync();

        await page.WaitForURLAsync(new Regex("/account/(denied|login|lockout)"));
    }

    [Fact]
    public async Task A_row_written_through_an_interactive_page_carries_the_signed_in_user()
    {
        // Review C5: inside a circuit IHttpContextAccessor is stale or null, which would
        // record "system".
        IPage page = await app.SignInAsAdministratorAsync(browser);
        await page.GotoAsync(app.Url("/catalog"));
        await page.GetByTestId("catalog-number").Locator("input").FillAsync("PROVENANCE-1");
        await page.GetByTestId("catalog-name").Locator("input").FillAsync("Provenance probe");
        await page.GetByTestId("catalog-unit").Locator("input").FillAsync("HUR");
        await page.GetByTestId("catalog-price").Locator("input").FillAsync("1");
        await page.GetByTestId("catalog-currency").Locator("input").FillAsync("EUR");
        await page.GetByTestId("catalog-vat-rate").Locator("input").FillAsync("19");
        await page.GetByTestId("catalog-save").ClickAsync();
        await Expect(page.GetByTestId("catalog-row").Filter(new() { HasText = "PROVENANCE-1" })).ToBeVisibleAsync();

        (await app.ReadCatalogItemCreatedByAsync("PROVENANCE-1")).Should().Be(app.AdminEmail);
    }
```

`UiAccount.Email` and `CreateEnrolledUserAsync`/`SignInAsync`/`LockUserAsync` exist in the fixture; check their exact signatures first. Add `ReadCatalogItemCreatedByAsync(string number)` to the fixture: resolve `CatalogDbContext` from `Services` and return `CreatedBy` of the item with that `CatalogItemNumber`. The 65-second wait follows the precedent of E02a's `Locking_a_user_stops_their_existing_session`; the interval is `SecurityStampValidatorOptions.ValidationInterval`, one minute.

- [ ] **Step 11: Run every suite**

```bash
dotnet build --configuration Release --no-incremental
dotnet format --verify-no-changes
for p in Fakturenn.UnitTests Fakturenn.Modules.Identity.UnitTests Fakturenn.Web.UnitTests Fakturenn.ArchitectureTests Fakturenn.ComplianceTests Fakturenn.IntegrationTests Fakturenn.UiTests; do
  DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project "tests/$p" --configuration Release
done
```

Expected: all PASS, including the four `SharedResourceTests` facts.

- [ ] **Step 12: Mutations**

Copy aside, apply, run, expect FAIL, restore, `sha256sum --check`:

- `MainLayout.razor` + `OrganizationPage.razor`: move `<MudPopoverProvider />` back into the layout and remove `<InteractiveProviders />` from the organization page. Run the journey test. Expected: FAIL at the scope select.
- `IdentityConfiguration.cs`: remove the `AuthenticationStateProvider` registration line. Run the lock test. Expected: FAIL — the save succeeds.
- `HttpContextCurrentUserAccessor.cs`: change `UserName` back to `NameOf(httpContextAccessor.HttpContext?.User)`. Run the provenance test. Expected: FAIL with `system` or a stale name.
- `CatalogPage.razor`: delete the `@rendermode` line. Run the provenance test. Expected: FAIL — nothing is interactive, the save button does nothing.

- [ ] **Step 13: Commit**

```bash
git add src/Fakturenn.Web tests/Fakturenn.UiTests
git commit -m "feat(web): interactive pages for organization, master data and invoices"
```

---

### Task 9: Previous-state migration, deployment, backup, and the documents this change amends

**Files:**
- Test: `tests/Fakturenn.IntegrationTests/MigrateEntrypointTests.cs`
- Modify: `docs/operations/DEPLOYMENT-BASELINE.md`, `docs/domain/DOMAIN-MODEL-v0.1.md`, `docs/architecture/adr/ADR-003.md`, `docs/architecture/adr/ADR-005.md`, `docs/architecture/adr/ADR-006.md`, `docs/planning/WALKING-SKELETON.md`, `docs/planning/BACKLOG.md`, `docs/planning/PLAN-v0.1.md`, `.claude/CLAUDE.md`, `docs/architecture/IMPLEMENTATION-NOTES.md`, `CHANGELOG.md`, `docs/superpowers/specs/2026-09-16-m1-stage1-invoice-finalization-design.md`, `src/Fakturenn.Modules.Identity/Domain/Role.cs`, `src/Fakturenn.Modules.Identity/Domain/UserRole.cs`, `src/Fakturenn.Modules.Identity/Persistence/IdentityDbContext.cs`

- [ ] **Step 1: Migration from main's schema**

Add to `MigrateEntrypointTests`, reusing its existing way of running `--migrate` as a subprocess (read the class first):

```csharp
    [Fact]
    public async Task Migrate_upgrades_a_database_at_the_previous_release()
    {
        // Definition of Done: "migrations work from clean and previous states" (review C11).
        // The previous state is main before Stage 1: Data Protection and Identity migrated,
        // Invoices at its InitialCreate, the four new schemas absent. The class fixture's
        // database is shared with tests that migrate everything, so this one works in its own.
        NpgsqlConnectionStringBuilder builder = new(postgres.ConnectionString) { Database = "previous_release" };
        await using (NpgsqlConnection administration = new(postgres.ConnectionString))
        {
            await administration.OpenAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand drop = administration.CreateCommand();
            drop.CommandText = "DROP DATABASE IF EXISTS previous_release WITH (FORCE)";
            await drop.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand create = administration.CreateCommand();
            create.CommandText = "CREATE DATABASE previous_release";
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        string connectionString = builder.ConnectionString;
        await using (DataProtectionDbContext dataProtection = new(
            new DbContextOptionsBuilder<DataProtectionDbContext>().UseNpgsql(connectionString).Options))
        {
            await dataProtection.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (IdentityDbContext identity = new(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options,
            DataProtectionProvider.Create("Fakturenn.Tests")))
        {
            await identity.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (InvoicesDbContext invoices = new(
            new DbContextOptionsBuilder<InvoicesDbContext>().UseNpgsql(connectionString).Options))
        {
            await invoices.GetService<IMigrator>().MigrateAsync("20260807133735_InitialCreate", cancellationToken: TestContext.Current.CancellationToken);
        }

        (int exitCode, string output) = await HostProcess.RunAsync(
            connectionString, ["--migrate"], standardInput: null, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, output);
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand schemas = connection.CreateCommand();
        schemas.CommandText =
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name IN "
            + "('organizations', 'customers', 'projects', 'catalog', 'invoices', 'messaging')";
        Convert.ToInt64(await schemas.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture)
            .Should().Be(6);
        await using NpgsqlCommand organizations = connection.CreateCommand();
        organizations.CommandText = "SELECT count(*) FROM organizations.\"Organizations\"";
        Convert.ToInt64(await organizations.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture)
            .Should().Be(1);
    }
```

Add the usings the class lacks: `System.Globalization`, `Fakturenn.Infrastructure.DataProtection`, `Fakturenn.Modules.Identity.Persistence`, `Fakturenn.Modules.Invoices.Persistence`, `Microsoft.AspNetCore.DataProtection`, `Microsoft.EntityFrameworkCore.Infrastructure`, `Microsoft.EntityFrameworkCore.Migrations`, `Npgsql`. Check `IMigrator.MigrateAsync`'s parameter names in EF Core 10 before relying on the named `cancellationToken:`. Run it. Expected: PASS.

- [ ] **Step 2: Deployment and backup**

In `docs/operations/DEPLOYMENT-BASELINE.md`, under its replica or Kubernetes section, add (spec section 12, review C10):

```markdown
**Interactive pages hold server-side state per browser tab.** A Blazor circuit lives
in one replica's memory: the negotiate request and the WebSocket must reach the same
replica, and the circuit never moves. Above one replica, configure ingress session
affinity, and forward WebSockets (`Upgrade`) at every proxy in front of the app. A
rolling update ends open circuits — anything typed and not yet saved is lost, and the
page reconnects or reloads.
```

Under its backup section, beside the existing warning that a restore replays queued messages (review C11):

```markdown
**A restore rewinds the invoice counter.** Restoring a dump older than the last
numbered invoice makes the next finalizations issue numbers that already exist. After
any restore, before anyone finalizes, compare `invoices."NumberCounters"` with the
highest number issued — the last invoice sent, or the archive — and raise the counter
row for each affected scope to it.

**The snapshot hash detects corruption, not tampering.** Whoever can rewrite a
snapshot's bytes can rewrite its hash in the same row.
```

- [ ] **Step 3: The documents the spec says this change amends** (spec section 13, review M5)

- `DOMAIN-MODEL-v0.1.md`: remove `OrganizationId` from `Customer`, `Project`, `CatalogItem`, `Invoice` and every other aggregate that carries it; "numbering is scoped by organization and document type" → "numbering is per scope key, configured on the one organization"; in §15 mark "Whether number sequences belong to Organizations or Documents module" answered — configuration in Organizations, counter in Invoices; `CatalogItem`'s `TaxCategoryId` → `VatRate`; add the `Draft → Numbered → Locked` states to the invoice aggregate.
- `ADR-003.md`: "Use JSONB selectively for snapshots" → snapshots are stored as raw bytes with their SHA-256, because `jsonb` reorders and normalizes keys and the bytes read back would not be the bytes hashed.
- `ADR-005.md`: Status `Accepted`; add one paragraph — the snapshot is the single production-time source of every artifact, read until the invoice locks and never after; reprinting returns the archived files.
- `ADR-006.md`: Status `Accepted`; one paragraph naming Invoice as the first separate aggregate, with corrections a separate document.
- `WALKING-SKELETON.md`: IBAN `DE00 0000 0000 0000 0000 00` → `DE89 3704 0044 0532 0130 00`, which passes mod-97.
- `BACKLOG.md`, the MudBlazor entry: narrow it to the static pages — the business pages are interactive and fixed; the floating-label symptom remains on the sign-in, authenticator and `/admin/users` forms.
- `PLAN-v0.1.md`: one line under E02 recording that organization-scoped roles are moot with one organization per instance.
- `.claude/CLAUDE.md`: under "Architecture invariants", after "No document binary data in PostgreSQL", add "— the invoice snapshot is structured data, not a rendered document, and is stored as bytes"; in "Adding a new module", add a checklist item: register the context with `AuditSaveChangesInterceptor` (through `ModuleConfiguration.AddModuleContext`) and add an `AssertAudited<TContext>()` fact to `ModuleCompositionTests`, because a missing interceptor saves empty provenance with no error.
- `Role.cs`, `UserRole.cs`, `IdentityDbContext.cs`: the three comments saying E02b adds an `OrganizationId` → state that with one organization per instance no such column is planned (spec section 2).
- The Stage 1 spec: §8 — permission constants stay in `Fakturenn.Modules.Identity`, because enforcement lives in `Fakturenn.Web`'s `CircuitOperations` and no module checks a permission; §3 — `CatalogItem` stores no type in Stage 1.
- `IMPLEMENTATION-NOTES.md`: a short "Circuits" section — the provider location rule (C9), `CircuitOperations` as the only door, prerendering off, and why `IDbContextFactory` was not used.

- [ ] **Step 4: Changelog**

Under `[Unreleased]` → `### Added` in `CHANGELOG.md`:

```markdown
- **Organization, customers, projects and catalog.** Enter your company once, then
  your customers with their projects and their own numbers for your services, and
  your catalog with prices and VAT rates.
- **Invoice numbering.** Draft an invoice and give it a number from your own scheme —
  a prefix, a per-day, per-month or per-year counter, or a running one continued
  from another tool. Numbers never repeat and never skip, and they carry the invoice's
  own date, so an invoice written today for last month gets last month's number.
  Nothing is sent yet; PDF and e-invoice follow.
```

and under `### Changed`:

```markdown
- **Signed-out visitors reach only the sign-in pages.** Every other page now
  requires signing in, including any added later without an explicit exception.
```

- [ ] **Step 5: Run every suite, format, build** — Task 8 Step 11's commands. Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add docs .claude/CLAUDE.md CHANGELOG.md src/Fakturenn.Modules.Identity tests/Fakturenn.IntegrationTests/MigrateEntrypointTests.cs
git commit -m "docs: record what Stage 1 changes in the deployment, the domain model and the ADRs"
```

---

## Human test

Spec section 14, unchanged. Run it after Task 9, from a clean checkout.
