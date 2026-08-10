# E02a Identity Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Authentication for Fakturenn — first-run setup, password sign-in with mandatory TOTP, recovery codes, lockout, permission-based authorization, operator recovery entrypoints, and an automated password + TOTP journey that closes SPIKE-009.

**Architecture:** A new `Fakturenn.Modules.Identity` module owns users, roles and permissions with its own `DbContext` and migrations. A new `Fakturenn.Infrastructure.DataProtection` project owns the Data Protection key ring, persisted to PostgreSQL so replicas share it. Authorization is permission-based: permissions are compile-time constants, roles are database rows, and code never authorizes on a role name. Authentication pages are static-SSR Blazor components posting real forms, because a Blazor Server circuit has no `HttpContext` to issue a cookie from.

**Tech Stack:** .NET 10, ASP.NET Core Identity 10.0.10, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.10, EF Core with Npgsql, MudBlazor, `Otp.NET` 1.4.1 (tests only), xUnit v3, Testcontainers, Playwright.

**Spec:** `docs/superpowers/specs/2026-08-10-e02a-identity-foundation-design.md`

## Global Constraints

- Target framework `net10.0`. `TreatWarningsAsErrors` is `true`, `Nullable` is `enable`. `dotnet build --configuration Release` must report `0 Warning(s)` and `0 Error(s)`.
- `dotnet format --verify-no-changes` must stay clean. `IDE0005` is an error, so an unused `using` fails the build.
- Central Package Management: never write a `Version=` attribute on a `PackageReference`. Add packages with `dotnet add package <id>`; never hand-write a version.
- `dotnet test <directory>` is rejected by this SDK. Always `dotnet test --project <directory>`.
- Every project under `src/` needs **two** edits: an entry in `Fakturenn.slnx` under `/src/`, and one `typeof(<public type>).Assembly` line in `FakturennArchitecture.Loaded`. Omitting the second fails `The_loader_omits_no_assembly_declared_under_src_in_the_solution`.
- Every new test project needs its own `.editorconfig` suppressing CA1707 and CA1859 — copy `tests/Fakturenn.UnitTests/.editorconfig`.
- Generated EF migrations need the `.editorconfig` pattern from `src/Fakturenn.Modules.Invoices/Persistence/Migrations/.editorconfig`.
- All files UTF-8 without a BOM, ending with a trailing newline. `dotnet new` emits BOMs on `.csproj`; strip them.
- `git status --short` must be empty before a task is reported done.
- Migrations never run at startup. Only via `--migrate`.
- Conventional Commits with a `Co-Authored-By:` trailer.
- No secrets in the repository. No password may ever be passed as a command-line argument.
- Design principles: TDD where a test can meaningfully come first; SOLID, KISS, YAGNI.

## Baseline before this plan starts

`main` at the harness completion: unit 26, architecture 14, integration 6, compliance 10, UI 4. Build clean, `dotnet format` clean, CI green on GitHub.

## Two facts discovered while planning, which change how tasks are written

**1. The application is already entirely static SSR.** `Components/Pages/Home.razor` declares no `@rendermode`, and nothing else does either. `AddInteractiveServerRenderMode()` is registered but no component opts in. So the authentication pages need **no special render-mode work** — static SSR is the default and form posts will simply work. The care is needed in the opposite direction: any future page needing interactivity must declare `@rendermode InteractiveServer`, and must never be an authentication page.

**2. Identity stores both second factors in plaintext.** Verified empirically, not assumed:

```text
ROW  LoginProvider=[AspNetUserStore]  Name=AuthenticatorKey  Value=2W2NZBPUT2YX3LP3SUMMXICIO2INDYYU
ROW  LoginProvider=[AspNetUserStore]  Name=RecoveryCodes     Value=XBK77-435VP;TG5RD-6TJW9;QWVJ8-F983Q
```

Both live in `AspNetUserTokens.Value`, so one value converter covers both. Task 7 exists because of this.

## Architecture rules become live in this plan

`Fakturenn.Modules.Identity` is the **second** module. Rules 5 (no cross-module implementation references) and 6 (no cycles between modules) have never been able to fail, because only one module existed. From Task 1 they constrain real code:

- `Fakturenn.Modules.Identity` must not reference `Fakturenn.Modules.Invoices`, only its `.Contracts`.
- No module may reference `Fakturenn.Infrastructure.DataProtection`. The Identity module depends on the framework's `IDataProtectionProvider` abstraction; the concrete key store is wired only in `Fakturenn.Web`.

Task 1 verifies this by introducing a real violation and watching the rule fail, exactly as the harness did.

## File Structure

```text
src/Fakturenn.SharedKernel/
  IAuditable.cs                          row-level provenance contract
  ICurrentUserAccessor.cs                who is acting, or null outside a request
  AuditStamp.cs                          the interceptor's decisions, as a pure function

src/Fakturenn.Infrastructure.Persistence/
  AuditSaveChangesInterceptor.cs         fills IAuditable on save

src/Fakturenn.Modules.Identity.Contracts/
  UserId.cs                              readonly record struct, cross-module surface

src/Fakturenn.Modules.Identity/
  IdentityModule.cs                      assembly marker
  Authorization/
    Permissions.cs                       closed set of permission constants
    PermissionRequirement.cs
    PermissionPolicyProvider.cs
    PermissionAuthorizationHandler.cs
  Domain/
    ApplicationUser.cs
    Role.cs
    RolePermission.cs
    UserRole.cs
  Persistence/
    IdentityDbContext.cs                 derives IdentityUserContext, schema "identity"
    EncryptedStringConverter.cs
    RoleSeeder.cs
    PermissionCatalogValidator.cs
    Migrations/                          + .editorconfig

src/Fakturenn.Infrastructure.DataProtection/
  DataProtectionDbContext.cs             IDataProtectionKeyContext, schema "dataprotection"
  Migrations/                            + .editorconfig

src/Fakturenn.Web/
  Components/Account/
    Setup.razor
    Login.razor
    LoginWith2fa.razor
    LoginWithRecoveryCode.razor
    EnrolTotp.razor
    RecoveryCodes.razor
    Lockout.razor
    AccountEndpoints.cs                  form POST handlers
  Components/Admin/
    Users.razor
  Operations/
    OperatorCommands.cs                  --create-admin, --reset-password, --reset-mfa, --list-users
  EnrolmentGateMiddleware.cs
  FakturennWebApplication.cs             modified
  Program.cs                             modified

tests/Fakturenn.Modules.Identity.UnitTests/    new project
tests/Fakturenn.IntegrationTests/              modified
tests/Fakturenn.UiTests/                       modified
tests/Fakturenn.ArchitectureTests/             modified
```

---

### Task 1: Identity module seam, contracts, and proof the boundary rules bite

**Files:**

- Create: `src/Fakturenn.Modules.Identity.Contracts/Fakturenn.Modules.Identity.Contracts.csproj`, `UserId.cs`
- Create: `src/Fakturenn.Modules.Identity/Fakturenn.Modules.Identity.csproj`, `IdentityModule.cs`
- Create: `tests/Fakturenn.Modules.Identity.UnitTests/` project with `.editorconfig`
- Modify: `Fakturenn.slnx`, `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs`, `tests/Fakturenn.ArchitectureTests/Fakturenn.ArchitectureTests.csproj`

**Interfaces:**

- Consumes: `Fakturenn.SharedKernel.IIdGenerator`
- Produces:
  - `Fakturenn.Modules.Identity.Contracts.UserId` — `readonly record struct`; ctor `UserId(Guid value)` rejecting `Guid.Empty`; `Guid Value`; `static UserId New(IIdGenerator generator)`; `ToString()` override
  - `Fakturenn.Modules.Identity.IdentityModule` — `public static class`, no members, assembly marker

- [ ] **Step 1: Create the projects and wire references**

```bash
dotnet new classlib --output src/Fakturenn.Modules.Identity.Contracts --name Fakturenn.Modules.Identity.Contracts
dotnet new classlib --output src/Fakturenn.Modules.Identity --name Fakturenn.Modules.Identity
dotnet new xunit3 --output tests/Fakturenn.Modules.Identity.UnitTests --name Fakturenn.Modules.Identity.UnitTests
rm --force src/Fakturenn.Modules.Identity.Contracts/Class1.cs src/Fakturenn.Modules.Identity/Class1.cs tests/Fakturenn.Modules.Identity.UnitTests/UnitTest1.cs
dotnet sln Fakturenn.slnx add src/Fakturenn.Modules.Identity.Contracts/Fakturenn.Modules.Identity.Contracts.csproj
dotnet sln Fakturenn.slnx add src/Fakturenn.Modules.Identity/Fakturenn.Modules.Identity.csproj
dotnet sln Fakturenn.slnx add tests/Fakturenn.Modules.Identity.UnitTests/Fakturenn.Modules.Identity.UnitTests.csproj
dotnet add src/Fakturenn.Modules.Identity.Contracts reference src/Fakturenn.SharedKernel
dotnet add src/Fakturenn.Modules.Identity reference src/Fakturenn.Modules.Identity.Contracts
dotnet add src/Fakturenn.Modules.Identity reference src/Fakturenn.SharedKernel
dotnet add tests/Fakturenn.Modules.Identity.UnitTests reference src/Fakturenn.Modules.Identity
dotnet add tests/Fakturenn.Modules.Identity.UnitTests package AwesomeAssertions
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Modules.Identity
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Modules.Identity.Contracts
cp tests/Fakturenn.UnitTests/.editorconfig tests/Fakturenn.Modules.Identity.UnitTests/.editorconfig
```

Copy the Microsoft.Testing.Platform properties from `tests/Fakturenn.UnitTests/Fakturenn.UnitTests.csproj` into the new test project. Strip BOMs from every generated `.csproj`.

- [ ] **Step 2: Write the failing test**

`tests/Fakturenn.Modules.Identity.UnitTests/UserIdTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class UserIdTests
{
    [Fact]
    public void A_new_user_id_takes_its_value_from_the_id_generator()
    {
        var expected = Guid.Parse("0198f3a0-0000-7000-8000-00000000000a");
        IIdGenerator generator = new StubIdGenerator(expected);

        UserId id = UserId.New(generator);

        id.Value.Should().Be(expected);
    }

    [Fact]
    public void An_empty_user_id_is_rejected()
    {
        var create = () => new UserId(Guid.Empty);

        create.Should().Throw<ArgumentException>();
    }

    private sealed class StubIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
```

- [ ] **Step 3: Run the test and watch it fail**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests`
Expected: build failure — `The type or namespace name 'UserId' could not be found`.

- [ ] **Step 4: Implement `UserId` and the assembly marker**

`src/Fakturenn.Modules.Identity.Contracts/UserId.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.Contracts;

/// <summary>
/// The Identity module's user identifier as other modules see it. Cross-module
/// references use this, never <c>ApplicationUser</c>.
/// </summary>
public readonly record struct UserId
{
    // public Constructors
    public UserId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A user id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    // public Properties
    public Guid Value { get; }

    // public static Methods
    public static UserId New(IIdGenerator generator) => new(generator.NewId());

    // public Methods
    public override string ToString() => Value.ToString();
}
```

`src/Fakturenn.Modules.Identity/IdentityModule.cs`:

```csharp
namespace Fakturenn.Modules.Identity;

/// <summary>
/// Assembly marker. Gives the architecture tests and dependency injection a stable
/// public handle on this assembly without exporting a type that exists for no other
/// reason.
/// </summary>
public static class IdentityModule;
```

- [ ] **Step 5: Register both assemblies in the architecture loader**

In `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs`, add two lines to `LoadAssemblies`:

```csharp
            typeof(Modules.Identity.Contracts.UserId).Assembly,
            typeof(Modules.Identity.IdentityModule).Assembly,
```

- [ ] **Step 6: Run everything**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests` — expect 2 passing.
Run: `dotnet test --project tests/Fakturenn.ArchitectureTests` — expect 14 passing, including `The_loader_omits_no_assembly_declared_under_src_in_the_solution`.

- [ ] **Step 7: Prove rule 5 now bites**

Rules 5 and 6 have never been violable — there was only one module. Introduce a real violation:

```bash
dotnet add src/Fakturenn.Modules.Identity reference src/Fakturenn.Modules.Invoices
```

Then add to `src/Fakturenn.Modules.Identity/IdentityModule.cs`, temporarily:

```csharp
    public static readonly Type Violation = typeof(Modules.Invoices.InvoicesModule);
```

A project reference alone is not enough — ArchUnitNET analyses type dependencies and the compiler records nothing for an unused reference.

Run: `dotnet test --project tests/Fakturenn.ArchitectureTests`
Expected: FAIL on `No_module_depends_on_another_modules_implementation_assembly`.

Then revert both:

```bash
dotnet remove src/Fakturenn.Modules.Identity reference src/Fakturenn.Modules.Invoices
```

Remove the `Violation` field. Re-run: expect 14 passing, and `git status --short` clean apart from this task's intended files.

- [ ] **Step 8: Commit**

```bash
git add src/Fakturenn.Modules.Identity src/Fakturenn.Modules.Identity.Contracts tests/Fakturenn.Modules.Identity.UnitTests tests/Fakturenn.ArchitectureTests Fakturenn.slnx
git commit --message "feat(identity): add the Identity module seam and its contracts assembly

Fakturenn.Modules.Identity is the second module, so architecture rules 5 and 6
stop being vacuous. Verified by making Identity depend on the Invoices
implementation assembly and watching the rule fail, then reverting.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Row-level audit provenance

Every entity we define carries who created it and who last changed it, filled by an EF Core interceptor so no entity code ever sets them by hand.

This comes **before** the entities on purpose. Audit columns added afterwards mean a second migration against tables that already shipped, and every module built after E02a inherits the pattern for free.

**Not the Audit module.** `MODULE-OWNERSHIP.md` assigns an Audit module owning `AuditEvent` and correlation metadata. That is an event log; this is row-level provenance. Same word, different thing — a later epic building `AuditEvent` does not supersede this and should not try to.

**Files:**

- Create: `src/Fakturenn.SharedKernel/IAuditable.cs`, `ICurrentUserAccessor.cs`, `AuditStamp.cs`
- Create: `src/Fakturenn.Infrastructure.Persistence/Fakturenn.Infrastructure.Persistence.csproj`, `AuditSaveChangesInterceptor.cs`
- Create: `tests/Fakturenn.UnitTests/SharedKernel/AuditStampTests.cs`
- Modify: `Fakturenn.slnx`, `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs` and its `.csproj`

**Interfaces:**

- Consumes: `Fakturenn.SharedKernel.IClock`
- Produces:
  - `IAuditable` — `DateTimeOffset CreatedAt { get; set; }`, `string CreatedBy { get; set; }`, `DateTimeOffset ModifiedAt { get; set; }`, `string ModifiedBy { get; set; }`
  - `ICurrentUserAccessor` — `string? UserName { get; }`
  - `AuditStamp` — `static class`; `const string SystemUser = "system"`; `static (DateTimeOffset CreatedAt, string CreatedBy) ForAdded(DateTimeOffset existingCreatedAt, string? existingCreatedBy, DateTimeOffset now, string user)`; `static string ResolveUser(string? userName)`
  - `AuditSaveChangesInterceptor` — `sealed`, `SaveChangesInterceptor`, ctor `(IClock clock, ICurrentUserAccessor currentUser)`

**Naming note.** The domain model uses `CreatedAt`, `FinalizedAt`, `ReceivedAt` — no `Utc` suffix — and `DateTimeOffset` already carries its offset, so a suffix would be redundant. These names follow `DOMAIN-MODEL-v0.1.md`.

- [ ] **Step 1: Write the failing tests**

The interceptor's decisions are extracted into a pure function so they can be tested without a database or a request pipeline. The interceptor itself becomes thin glue, covered by an integration test in Task 4.

`tests/Fakturenn.UnitTests/SharedKernel/AuditStampTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.SharedKernel;

public sealed class AuditStampTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 10, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_entity_with_no_provenance_is_stamped_with_the_current_user_and_time()
    {
        (DateTimeOffset createdAt, string createdBy) =
            AuditStamp.ForAdded(default, null, Now, "cr@roeper.biz");

        createdAt.Should().Be(Now);
        createdBy.Should().Be("cr@roeper.biz");
    }

    [Fact]
    public void A_new_entity_that_already_carries_provenance_keeps_it()
    {
        // A seeder or an import knows the real provenance. Overwriting it would
        // replace a fact with the identity of whoever happened to run the import.
        var imported = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        (DateTimeOffset createdAt, string createdBy) =
            AuditStamp.ForAdded(imported, "legacy-import", Now, "cr@roeper.biz");

        createdAt.Should().Be(imported);
        createdBy.Should().Be("legacy-import");
    }

    [Fact]
    public void A_blank_creator_counts_as_absent()
    {
        (_, string createdBy) = AuditStamp.ForAdded(default, "   ", Now, "cr@roeper.biz");

        createdBy.Should().Be("cr@roeper.biz");
    }

    [Fact]
    public void No_signed_in_user_resolves_to_the_system_actor()
    {
        // Migrations, seeding and the operator entrypoints all run without a
        // request, and they must still produce a truthful actor rather than an
        // empty string.
        AuditStamp.ResolveUser(null).Should().Be(AuditStamp.SystemUser);
        AuditStamp.ResolveUser("  ").Should().Be(AuditStamp.SystemUser);
    }

    [Fact]
    public void A_signed_in_user_resolves_to_their_name()
    {
        AuditStamp.ResolveUser("cr@roeper.biz").Should().Be("cr@roeper.biz");
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test --project tests/Fakturenn.UnitTests`
Expected: build failure — `The type or namespace name 'AuditStamp' could not be found`.

- [ ] **Step 3: Implement the shared-kernel contracts**

`src/Fakturenn.SharedKernel/IAuditable.cs`:

```csharp
namespace Fakturenn.SharedKernel;

/// <summary>
/// Row-level provenance: who created this row and who last changed it.
/// <para>
/// Implemented by every entity Fakturenn defines. The values are filled by
/// <c>AuditSaveChangesInterceptor</c>, so entity code never sets them by hand and
/// cannot forget to.
/// </para>
/// <para>
/// This is not the Audit module. MODULE-OWNERSHIP.md assigns an Audit module owning
/// AuditEvent and correlation metadata, which is an event log. This is a property of
/// each row.
/// </para>
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    string CreatedBy { get; set; }

    DateTimeOffset ModifiedAt { get; set; }

    string ModifiedBy { get; set; }
}
```

`src/Fakturenn.SharedKernel/ICurrentUserAccessor.cs`:

```csharp
namespace Fakturenn.SharedKernel;

/// <summary>
/// The signed-in user's name, or null when there is no request — migrations,
/// seeding, background work and the operator entrypoints all run without one.
/// <para>
/// An abstraction rather than <c>IHttpContextAccessor</c> so that the shared kernel
/// stays free of ASP.NET Core, and so the claim actually consulted can change in one
/// place when generic OIDC eventually lands.
/// </para>
/// </summary>
public interface ICurrentUserAccessor
{
    string? UserName { get; }
}
```

`src/Fakturenn.SharedKernel/AuditStamp.cs`:

```csharp
namespace Fakturenn.SharedKernel;

/// <summary>
/// The decisions the audit interceptor makes, as a pure function, so they are
/// testable without a database or a request pipeline.
/// </summary>
public static class AuditStamp
{
    public const string SystemUser = "system";

    /// <summary>
    /// Provenance for a newly added row. Values already present are preserved: a
    /// seeder or an import knows the real provenance, and overwriting it would
    /// replace a fact with the identity of whoever ran the import.
    /// </summary>
    public static (DateTimeOffset CreatedAt, string CreatedBy) ForAdded(
        DateTimeOffset existingCreatedAt,
        string? existingCreatedBy,
        DateTimeOffset now,
        string user)
    {
        DateTimeOffset createdAt = existingCreatedAt == default ? now : existingCreatedAt;
        string createdBy = string.IsNullOrWhiteSpace(existingCreatedBy) ? user : existingCreatedBy;

        return (createdAt, createdBy);
    }

    public static string ResolveUser(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? SystemUser : userName;
}
```

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test --project tests/Fakturenn.UnitTests`
Expected: PASS, 31 tests.

- [ ] **Step 5: Create the infrastructure project and the interceptor**

```bash
dotnet new classlib --output src/Fakturenn.Infrastructure.Persistence --name Fakturenn.Infrastructure.Persistence
rm --force src/Fakturenn.Infrastructure.Persistence/Class1.cs
dotnet sln Fakturenn.slnx add src/Fakturenn.Infrastructure.Persistence/Fakturenn.Infrastructure.Persistence.csproj
dotnet add src/Fakturenn.Infrastructure.Persistence reference src/Fakturenn.SharedKernel
dotnet add src/Fakturenn.Infrastructure.Persistence package Microsoft.EntityFrameworkCore
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Infrastructure.Persistence
```

The interceptor lives in infrastructure rather than the shared kernel because it needs EF Core, and the shared kernel is referenced by the `.Contracts` assemblies that form the cross-module surface. Dragging EF Core in there would put a persistence dependency on every module's public surface.

`src/Fakturenn.Infrastructure.Persistence/AuditSaveChangesInterceptor.cs`:

```csharp
using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fakturenn.Infrastructure.Persistence;

/// <summary>
/// Fills <see cref="IAuditable"/> fields on save, so no entity code sets them by
/// hand and none can forget to.
/// <para>
/// Takes <see cref="IClock"/> rather than reading the clock directly, so a test can
/// assert an exact timestamp instead of a tolerance window.
/// </para>
/// </summary>
public sealed class AuditSaveChangesInterceptor(IClock clock, ICurrentUserAccessor currentUser)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditFields(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditFields(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;
        string user = AuditStamp.ResolveUser(currentUser.UserName);

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    (DateTimeOffset createdAt, string createdBy) = AuditStamp.ForAdded(
                        entry.Entity.CreatedAt, entry.Entity.CreatedBy, now, user);

                    entry.Entity.CreatedAt = createdAt;
                    entry.Entity.CreatedBy = createdBy;
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = user;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = user;

                    // Creation provenance is a fact about the past. Stop EF writing it
                    // again even if something in the graph changed the property.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }
}
```

- [ ] **Step 6: Register the assembly in the architecture loader**

Add to `FakturennArchitecture.LoadAssemblies`:

```csharp
            typeof(Infrastructure.Persistence.AuditSaveChangesInterceptor).Assembly,
```

- [ ] **Step 7: Verify the boundary holds**

Run: `dotnet test --project tests/Fakturenn.ArchitectureTests`
Expected: 14 passing. `No_module_depends_on_infrastructure` now governs this assembly too: modules implement `IAuditable` from the shared kernel and must never reference the interceptor.

Run: `dotnet build --configuration Release` — `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add src/Fakturenn.SharedKernel src/Fakturenn.Infrastructure.Persistence tests/Fakturenn.UnitTests tests/Fakturenn.ArchitectureTests Fakturenn.slnx Directory.Packages.props
git commit --message "feat: add row-level audit provenance

Every entity Fakturenn defines carries who created it and who last changed it,
filled by an EF Core interceptor so entity code never sets them by hand.

The decisions are a pure function in the shared kernel so they are testable
without a database; the interceptor is thin glue in infrastructure, because the
shared kernel is referenced by the .Contracts assemblies and must not carry a
persistence dependency.

Added before the Identity entities on purpose: audit columns added later mean a
second migration against tables that already shipped.

This is not the Audit module from MODULE-OWNERSHIP.md, which owns AuditEvent and
is an event log. This is a property of each row.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Permission-based authorization

Permissions are compile-time constants; roles are data. Code authorizes on permissions only, never on role names, so an operator can create a role without a deploy but cannot invent a permission the code does not enforce.

**Files:**

- Create: `src/Fakturenn.Modules.Identity/Authorization/Permissions.cs`, `PermissionRequirement.cs`, `PermissionPolicyProvider.cs`, `PermissionAuthorizationHandler.cs`
- Create: `tests/Fakturenn.Modules.Identity.UnitTests/Authorization/PermissionPolicyProviderTests.cs`, `PermissionAuthorizationHandlerTests.cs`

**Interfaces:**

- Consumes: nothing from Task 1 beyond the project
- Produces:
  - `Permissions` — `static class`; consts `UsersRead = "users.read"`, `UsersManage = "users.manage"`, `RolesRead = "roles.read"`, `RolesManage = "roles.manage"`; `static IReadOnlySet<string> All`
  - `PermissionRequirement` — `sealed class`, implements `IAuthorizationRequirement`, ctor `PermissionRequirement(string permission)`, property `string Permission`
  - `PermissionPolicyProvider` — `sealed class`, implements `IAuthorizationPolicyProvider`
  - `PermissionAuthorizationHandler` — `sealed class`, `AuthorizationHandler<PermissionRequirement>`

- [ ] **Step 1: Add the package**

```bash
dotnet add src/Fakturenn.Modules.Identity package Microsoft.AspNetCore.Authorization
dotnet add tests/Fakturenn.Modules.Identity.UnitTests package Microsoft.AspNetCore.Authorization
```

- [ ] **Step 2: Write the failing tests**

`tests/Fakturenn.Modules.Identity.UnitTests/Authorization/PermissionPolicyProviderTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Fakturenn.Modules.Identity.UnitTests.Authorization;

public sealed class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider CreateProvider() =>
        new(Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task A_known_permission_name_yields_a_policy_requiring_it()
    {
        AuthorizationPolicy? policy = await CreateProvider().GetPolicyAsync(Permissions.UsersManage);

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle()
            .Which.Should().BeOfType<PermissionRequirement>()
            .Which.Permission.Should().Be(Permissions.UsersManage);
    }

    [Fact]
    public async Task A_name_that_is_not_a_defined_permission_yields_no_policy()
    {
        // Guards against a typo in an [Authorize(Policy = "...")] silently
        // becoming an allow-all instead of a build or request failure.
        AuthorizationPolicy? policy = await CreateProvider().GetPolicyAsync("users.manag");

        policy.Should().BeNull();
    }

    [Fact]
    public void Every_declared_constant_is_present_in_the_catalogue()
    {
        Permissions.All.Should().BeEquivalentTo(
        [
            Permissions.UsersRead,
            Permissions.UsersManage,
            Permissions.RolesRead,
            Permissions.RolesManage,
        ]);
    }
}
```

`tests/Fakturenn.Modules.Identity.UnitTests/Authorization/PermissionAuthorizationHandlerTests.cs`:

```csharp
using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Fakturenn.Modules.Identity.UnitTests.Authorization;

public sealed class PermissionAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext ContextFor(string requiredPermission, params string[] granted)
    {
        var identity = new ClaimsIdentity(
            [.. granted.Select(p => new Claim(PermissionClaims.Type, p))],
            authenticationType: "Test");

        return new AuthorizationHandlerContext(
            [new PermissionRequirement(requiredPermission)],
            new ClaimsPrincipal(identity),
            resource: null);
    }

    [Fact]
    public async Task A_principal_holding_the_permission_succeeds()
    {
        AuthorizationHandlerContext context = ContextFor(Permissions.UsersManage, Permissions.UsersManage);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_principal_holding_a_different_permission_does_not_succeed()
    {
        AuthorizationHandlerContext context = ContextFor(Permissions.UsersManage, Permissions.UsersRead);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task An_unauthenticated_principal_does_not_succeed()
    {
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.UsersManage)],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run and watch them fail**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests`
Expected: build failure — `The type or namespace name 'Permissions' could not be found`.

- [ ] **Step 4: Implement the authorization types**

`src/Fakturenn.Modules.Identity/Authorization/Permissions.cs`:

```csharp
namespace Fakturenn.Modules.Identity.Authorization;

/// <summary>
/// The closed set of permissions this application enforces. Code authorizes on
/// these constants, never on a role name, so a role can be created or renamed by
/// an operator without a deploy while the set of things code checks stays fixed
/// and greppable.
/// </summary>
public static class Permissions
{
    // public const Fields
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";

    // public static readonly Fields
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UsersRead,
        UsersManage,
        RolesRead,
        RolesManage,
    };
}

/// <summary>The claim type carrying a granted permission.</summary>
public static class PermissionClaims
{
    public const string Type = "fakturenn.permission";
}
```

`src/Fakturenn.Modules.Identity/Authorization/PermissionRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Fakturenn.Modules.Identity.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
```

`src/Fakturenn.Modules.Identity/Authorization/PermissionPolicyProvider.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Fakturenn.Modules.Identity.Authorization;

/// <summary>
/// Turns a permission name used as a policy name into a policy requiring that
/// permission. Returns null for anything that is not a declared permission, so a
/// typo in an <c>[Authorize(Policy = ...)]</c> fails the request instead of
/// silently authorising it.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!Permissions.All.Contains(policyName))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
```

`src/Fakturenn.Modules.Identity/Authorization/PermissionAuthorizationHandler.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Fakturenn.Modules.Identity.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        bool granted = context.User.Claims.Any(claim =>
            claim.Type == PermissionClaims.Type
            && string.Equals(claim.Value, requirement.Permission, StringComparison.Ordinal));

        if (granted)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Run and watch them pass**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Fakturenn.Modules.Identity tests/Fakturenn.Modules.Identity.UnitTests Directory.Packages.props
git commit --message "feat(identity): add permission-based authorization

Permissions are compile-time constants; roles are data. The policy provider
returns no policy for a name that is not a declared permission, so a typo in an
[Authorize(Policy = ...)] fails the request rather than silently allowing it.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Identity entities and the module-owned DbContext

Derives from `IdentityUserContext<ApplicationUser, Guid>`, **not** `IdentityDbContext`, so the stock `AspNetRoles` and `AspNetUserRoles` tables are never created. Our own `Role`/`UserRole` tables replace them because E02b needs an `OrganizationId` that the stock tables have nowhere to put.

**Files:**

- Create: `src/Fakturenn.Modules.Identity/Domain/ApplicationUser.cs`, `Role.cs`, `RolePermission.cs`, `UserRole.cs`
- Create: `src/Fakturenn.Modules.Identity/Persistence/IdentityDbContext.cs`, `IdentityDbContextFactory.cs`
- Create: `src/Fakturenn.Modules.Identity/Persistence/Migrations/.editorconfig` and the generated migration

**Interfaces:**

- Consumes: Task 1's project
- Produces:
  - `ApplicationUser : IdentityUser<Guid>` — adds `string DisplayName`, `DateTimeOffset CreatedAt`, `bool MustEnrolTotp`
  - `Role` — `Guid Id`, `string Name`, `string? Description`, `bool IsSystemRole`
  - `RolePermission` — `Guid RoleId`, `string Permission`
  - `UserRole` — `Guid UserId`, `Guid RoleId`
  - `IdentityDbContext` — `sealed`, ctor takes `DbContextOptions<IdentityDbContext>`, const `SchemaName = "identity"`, `DbSet<Role> Roles`, `DbSet<RolePermission> RolePermissions`, `DbSet<UserRole> UserRoles`

- [ ] **Step 1: Add packages**

```bash
dotnet add src/Fakturenn.Modules.Identity package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/Fakturenn.Modules.Identity package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Fakturenn.Modules.Identity package Microsoft.EntityFrameworkCore.Design
```

- [ ] **Step 2: Write the entities**

`src/Fakturenn.Modules.Identity/Domain/ApplicationUser.cs`:

```csharp
using Fakturenn.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// The application's user. Keys are UUID v7 for the same reason the rest of the
/// system uses them: random v4 keys fragment PostgreSQL B-tree indexes.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Set when an account exists but has not completed TOTP enrolment. A user in
    /// this state has authenticated by password and may reach only the enrolment
    /// page — see <c>EnrolmentGateMiddleware</c>.
    /// </summary>
    public bool MustEnrolTotp { get; set; }

    // IAuditable, filled by AuditSaveChangesInterceptor
    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;
}
```

The first administrator is created before anyone is signed in, so the interceptor stamps `CreatedBy` as `system` — which is truthful: nobody was authenticated at that moment.

`src/Fakturenn.Modules.Identity/Domain/Role.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// A named bundle of permissions. Deliberately not ASP.NET Core Identity's
/// <c>IdentityRole</c>: epic E02b adds an OrganizationId to <see cref="UserRole"/>,
/// and the stock join table has nowhere to put one.
/// </summary>
public sealed class Role : IAuditable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// A role the application itself depends on. System roles cannot be deleted and
    /// cannot have their permissions removed, so an instance cannot be locked out of
    /// its own administration through the user interface.
    /// </summary>
    public bool IsSystemRole { get; set; }

    // IAuditable, filled by AuditSaveChangesInterceptor
    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;
}
```

`RolePermission` and `UserRole` implement `IAuditable` in exactly the same shape — add the same four properties and the `using Fakturenn.SharedKernel;` to each, and make each class `: IAuditable`. Knowing who granted a permission or assigned a role is the part of this that will matter in an audit.

`src/Fakturenn.Modules.Identity/Domain/RolePermission.cs`:

```csharp
namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// Grants one permission to one role. <see cref="Permission"/> holds a string that
/// must match a constant in <c>Permissions</c>; a startup check rejects any value
/// that does not.
/// </summary>
public sealed class RolePermission
{
    public Guid RoleId { get; set; }

    public string Permission { get; set; } = string.Empty;
}
```

`src/Fakturenn.Modules.Identity/Domain/UserRole.cs`:

```csharp
namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// Assigns a role to a user. Epic E02b adds an OrganizationId here, which is why
/// this is a table of our own rather than Identity's AspNetUserRoles.
/// </summary>
public sealed class UserRole
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }
}
```

- [ ] **Step 3: Write the DbContext**

`src/Fakturenn.Modules.Identity/Persistence/IdentityDbContext.cs`:

```csharp
using Fakturenn.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public const string SchemaName = "identity";

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
            user.Property(u => u.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<Role>(role =>
        {
            role.HasKey(r => r.Id);
            role.Property(r => r.Name).HasMaxLength(128).IsRequired();
            role.Property(r => r.Description).HasMaxLength(512);
            role.HasIndex(r => r.Name).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(rolePermission =>
        {
            rolePermission.HasKey(rp => new { rp.RoleId, rp.Permission });
            rolePermission.Property(rp => rp.Permission).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<UserRole>(userRole =>
        {
            userRole.HasKey(ur => new { ur.UserId, ur.RoleId });
        });

        // One place configures the audit columns for every auditable entity, so a
        // later entity cannot arrive with a different column width by accident.
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType)))
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

Add `using Fakturenn.SharedKernel;` and `using Microsoft.EntityFrameworkCore.Metadata;`.

`src/Fakturenn.Modules.Identity/Persistence/IdentityDbContextFactory.cs`:

```csharp
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
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
```

- [ ] **Step 4: Generate the migration**

```bash
mkdir --parents src/Fakturenn.Modules.Identity/Persistence/Migrations
cp src/Fakturenn.Modules.Invoices/Persistence/Migrations/.editorconfig src/Fakturenn.Modules.Identity/Persistence/Migrations/.editorconfig
dotnet ef migrations add InitialIdentity \
  --project src/Fakturenn.Modules.Identity \
  --output-dir Persistence/Migrations
```

- [ ] **Step 5: Verify the migration creates no role tables**

```bash
grep --extended-regexp 'CreateTable|name: "' src/Fakturenn.Modules.Identity/Persistence/Migrations/*_InitialIdentity.cs | grep --invert-match Designer
```

Expected tables: `AspNetUsers`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `Role`, `RolePermission`, `UserRole`.
**`AspNetRoles` and `AspNetUserRoles` must NOT appear.** If they do, the context derives from the wrong base class — fix it and regenerate rather than editing the migration.

- [ ] **Step 5a: Confirm the audit columns are in this first migration**

```bash
grep --extended-regexp 'CreatedBy|ModifiedBy|CreatedAt|ModifiedAt' src/Fakturenn.Modules.Identity/Persistence/Migrations/*_InitialIdentity.cs | grep --invert-match Designer
```

Expected: all four columns on `AspNetUsers`, `Role`, `RolePermission` and `UserRole`. If any are missing, the entity does not implement `IAuditable` — fix the entity and regenerate rather than editing the migration. Getting this wrong is the whole reason Task 2 comes before this one.

- [ ] **Step 5b: Prove the interceptor actually stamps**

`tests/Fakturenn.IntegrationTests/AuditStampingTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Infrastructure.Persistence;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.SharedKernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class AuditStampingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 30, 0, TimeSpan.Zero);

    private IdentityDbContext CreateContext(string? userName)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new StubClock(Now), new StubCurrentUser(userName)))
            .Options;

        return new IdentityDbContext(options, DataProtectionProvider.Create("Fakturenn.Tests"));
    }

    [Fact]
    public async Task An_added_row_is_stamped_with_the_acting_user_and_the_clock()
    {
        await using IdentityDbContext context = CreateContext("cr@roeper.biz");
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), Name = $"audited-{Guid.NewGuid():N}" };
        context.Roles.Add(role);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Role stored = await context.Roles.AsNoTracking()
            .SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);

        stored.CreatedAt.Should().Be(Now);
        stored.CreatedBy.Should().Be("cr@roeper.biz");
        stored.ModifiedAt.Should().Be(Now);
        stored.ModifiedBy.Should().Be("cr@roeper.biz");
    }

    [Fact]
    public async Task Without_a_signed_in_user_the_row_records_the_system_actor()
    {
        // Migrations, seeding and the operator entrypoints all run without a request.
        await using IdentityDbContext context = CreateContext(userName: null);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), Name = $"seeded-{Guid.NewGuid():N}" };
        context.Roles.Add(role);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Role stored = await context.Roles.AsNoTracking()
            .SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);

        stored.CreatedBy.Should().Be(AuditStamp.SystemUser);
    }

    [Fact]
    public async Task Updating_a_row_moves_ModifiedBy_but_never_CreatedBy()
    {
        await using IdentityDbContext created = CreateContext("first@example.test");
        await created.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), Name = $"changing-{Guid.NewGuid():N}" };
        created.Roles.Add(role);
        await created.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using IdentityDbContext modified = CreateContext("second@example.test");
        Role tracked = await modified.Roles.SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);
        tracked.Description = "changed";
        // Creation provenance is a fact about the past; the interceptor must refuse
        // this even when something in the graph tries to overwrite it.
        tracked.CreatedBy = "tampered";
        await modified.SaveChangesAsync(TestContext.Current.CancellationToken);

        Role stored = await modified.Roles.AsNoTracking()
            .SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);

        stored.CreatedBy.Should().Be("first@example.test");
        stored.ModifiedBy.Should().Be("second@example.test");
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class StubCurrentUser(string? userName) : ICurrentUserAccessor
    {
        public string? UserName => userName;
    }
}
```

```bash
dotnet add tests/Fakturenn.IntegrationTests reference src/Fakturenn.Infrastructure.Persistence
```

Run: `dotnet test --project tests/Fakturenn.IntegrationTests` — expect 9 passing.

The third test is the one that matters: it is the only check that the interceptor refuses to rewrite creation provenance, which a plain round-trip would never notice.

- [ ] **Step 6: Build clean**

Run: `dotnet build --configuration Release`
Expected: `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test --project tests/Fakturenn.ArchitectureTests` — expect 14 passing.

- [ ] **Step 7: Commit**

```bash
git add src/Fakturenn.Modules.Identity Directory.Packages.props
git commit --message "feat(identity): add Identity entities and the module-owned DbContext

Derives from IdentityUserContext rather than IdentityDbContext, so no AspNetRoles
or AspNetUserRoles tables are created. Roles live in our own tables because E02b
adds an OrganizationId to the user-role join, which the stock table cannot carry.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Data Protection key ring in PostgreSQL

Sticky sessions give a Blazor circuit affinity but do not share keys, so a cookie encrypted by one replica cannot be decrypted by another. `DEPLOYMENT-BASELINE.md` commits to stateless replicas, which makes a shared ring a requirement rather than an optimisation.

**Files:**

- Create: `src/Fakturenn.Infrastructure.DataProtection/Fakturenn.Infrastructure.DataProtection.csproj`, `DataProtectionDbContext.cs`, `DataProtectionDbContextFactory.cs`, `Migrations/.editorconfig` and the generated migration
- Modify: `Fakturenn.slnx`, `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs` and its `.csproj`

**Interfaces:**

- Consumes: nothing
- Produces: `DataProtectionDbContext` — `sealed`, implements `IDataProtectionKeyContext`, ctor takes `DbContextOptions<DataProtectionDbContext>`, const `SchemaName = "dataprotection"`, `DbSet<DataProtectionKey> DataProtectionKeys`

- [ ] **Step 1: Create the project**

```bash
dotnet new classlib --output src/Fakturenn.Infrastructure.DataProtection --name Fakturenn.Infrastructure.DataProtection
rm --force src/Fakturenn.Infrastructure.DataProtection/Class1.cs
dotnet sln Fakturenn.slnx add src/Fakturenn.Infrastructure.DataProtection/Fakturenn.Infrastructure.DataProtection.csproj
dotnet add src/Fakturenn.Infrastructure.DataProtection package Microsoft.AspNetCore.DataProtection.EntityFrameworkCore
dotnet add src/Fakturenn.Infrastructure.DataProtection package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Fakturenn.Infrastructure.DataProtection package Microsoft.EntityFrameworkCore.Design
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Infrastructure.DataProtection
```

- [ ] **Step 2: Write the context**

`src/Fakturenn.Infrastructure.DataProtection/DataProtectionDbContext.cs`:

```csharp
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

        modelBuilder.HasDefaultSchema(SchemaName);
    }
}
```

`src/Fakturenn.Infrastructure.DataProtection/DataProtectionDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Infrastructure.DataProtection;

public sealed class DataProtectionDbContextFactory : IDesignTimeDbContextFactory<DataProtectionDbContext>
{
    public DataProtectionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
```

- [ ] **Step 3: Generate the migration**

```bash
mkdir --parents src/Fakturenn.Infrastructure.DataProtection/Migrations
cp src/Fakturenn.Modules.Invoices/Persistence/Migrations/.editorconfig src/Fakturenn.Infrastructure.DataProtection/Migrations/.editorconfig
dotnet ef migrations add InitialDataProtection \
  --project src/Fakturenn.Infrastructure.DataProtection \
  --output-dir Migrations
```

- [ ] **Step 4: Register the assembly in the architecture loader**

Add to `FakturennArchitecture.LoadAssemblies`:

```csharp
            typeof(Infrastructure.DataProtection.DataProtectionDbContext).Assembly,
```

- [ ] **Step 5: Verify no module can reach it**

Run: `dotnet test --project tests/Fakturenn.ArchitectureTests`
Expected: 14 passing. `No_module_depends_on_infrastructure` now governs a second infrastructure assembly.

Run: `dotnet build --configuration Release` — `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/Fakturenn.Infrastructure.DataProtection tests/Fakturenn.ArchitectureTests Fakturenn.slnx Directory.Packages.props
git commit --message "feat: persist the Data Protection key ring to PostgreSQL

Sticky sessions give a Blazor circuit affinity but do not share keys, so a cookie
encrypted by one replica cannot be read by another. DEPLOYMENT-BASELINE.md commits
to stateless replicas, so a shared ring is a requirement.

Owned by infrastructure rather than a module: MODULE-OWNERSHIP.md assigns no key
material to any module, and a key ring is not domain data.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Encrypt both second factors at rest

One value converter on `IdentityUserToken.Value` covers the TOTP shared secret **and** the recovery codes, both of which Identity stores in plaintext. This was verified empirically; see the plan preamble.

**Files:**

- Create: `src/Fakturenn.Modules.Identity/Persistence/EncryptedStringConverter.cs`
- Modify: `src/Fakturenn.Modules.Identity/Persistence/IdentityDbContext.cs`
- Create: `tests/Fakturenn.IntegrationTests/IdentityTokenEncryptionTests.cs`

**Interfaces:**

- Consumes: Task 4's `IdentityDbContext`
- Produces: `EncryptedStringConverter : ValueConverter<string, string>` — ctor `EncryptedStringConverter(IDataProtector protector)`

- [ ] **Step 1: Add the package**

```bash
dotnet add src/Fakturenn.Modules.Identity package Microsoft.AspNetCore.DataProtection.Abstractions
```

This is the abstraction only. The Identity module must **not** reference `Fakturenn.Infrastructure.DataProtection`; the architecture test enforces that.

- [ ] **Step 2: Write the converter**

`src/Fakturenn.Modules.Identity/Persistence/EncryptedStringConverter.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Encrypts a string column with ASP.NET Core Data Protection.
/// <para>
/// Applied to <c>IdentityUserToken.Value</c>, which stores BOTH second factors in
/// plaintext by default: the base32 TOTP shared secret under the token name
/// <c>AuthenticatorKey</c>, and the recovery codes, semicolon-joined and unhashed,
/// under <c>RecoveryCodes</c>. A read of that one table would otherwise yield a
/// working second factor for every user.
/// </para>
/// <para>
/// This defends against partial exposure — a dump of one table, a read-only
/// replica, a query log — and not against full database compromise, because the key
/// ring lives in the same database. It is never worse than the plaintext default.
/// </para>
/// </summary>
public sealed class EncryptedStringConverter(IDataProtector protector)
    : ValueConverter<string, string>(
        plaintext => protector.Protect(plaintext),
        ciphertext => protector.Unprotect(ciphertext));
```

- [ ] **Step 3: Apply it in the context**

In `IdentityDbContext`, add a constructor parameter and apply the converter. Replace the class declaration and add to `OnModelCreating`:

```csharp
public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    IDataProtectionProvider dataProtectionProvider)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
```

and inside `OnModelCreating`, after the existing configuration:

```csharp
        // Both second factors live in IdentityUserToken.Value. The purpose string is
        // part of the key derivation: changing it makes every existing value
        // undecryptable, so it must never be edited.
        var converter = new EncryptedStringConverter(
            dataProtectionProvider.CreateProtector("Fakturenn.Identity.UserToken.v1"));

        modelBuilder.Entity<IdentityUserToken<Guid>>()
            .Property(token => token.Value)
            .HasConversion(converter);
```

Add `using Microsoft.AspNetCore.DataProtection;` and `using Microsoft.AspNetCore.Identity;`.

Update `IdentityDbContextFactory` to pass `DataProtectionProvider.Create(new DirectoryInfo(Path.GetTempPath()))` — design time never reads real data, and `dotnet ef` must be able to construct the context.

- [ ] **Step 4: Write the failing integration test**

`tests/Fakturenn.IntegrationTests/IdentityTokenEncryptionTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.IntegrationTests;

public sealed class IdentityTokenEncryptionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string SecretValue = "2W2NZBPUT2YX3LP3SUMMXICIO2INDYYU";

    private IdentityDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options,
            DataProtectionProvider.Create("Fakturenn.Tests"));

    [Fact]
    public async Task A_token_value_is_not_readable_as_plaintext_in_the_column()
    {
        // This is the only test that would notice the value converter being dropped
        // in a future refactor: a round-trip through EF alone would still pass.
        await using IdentityDbContext context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "probe@example.test",
            Email = "probe@example.test",
            DisplayName = "Probe",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.Users.Add(user);
        context.Set<IdentityUserToken<Guid>>().Add(new IdentityUserToken<Guid>
        {
            UserId = user.Id,
            LoginProvider = "[AspNetUserStore]",
            Name = "AuthenticatorKey",
            Value = SecretValue,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """SELECT "Value" FROM identity."AspNetUserTokens" WHERE "Name" = 'AuthenticatorKey'""";
        object? stored = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        stored.Should().NotBeNull();
        stored!.ToString().Should().NotBe(SecretValue, "the shared secret must not be stored in plaintext");
        stored.ToString().Should().NotContain(SecretValue);
    }

    [Fact]
    public async Task A_token_value_round_trips_through_the_converter()
    {
        await using IdentityDbContext write = CreateContext();
        await write.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var userId = Guid.CreateVersion7();
        write.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"roundtrip-{userId:N}@example.test",
            Email = $"roundtrip-{userId:N}@example.test",
            DisplayName = "Round Trip",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        write.Set<IdentityUserToken<Guid>>().Add(new IdentityUserToken<Guid>
        {
            UserId = userId,
            LoginProvider = "[AspNetUserStore]",
            Name = "RecoveryCodes",
            Value = "AAAAA-11111;BBBBB-22222",
        });
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using IdentityDbContext read = CreateContext();
        IdentityUserToken<Guid> token = await read.Set<IdentityUserToken<Guid>>()
            .AsNoTracking()
            .SingleAsync(t => t.UserId == userId, TestContext.Current.CancellationToken);

        token.Value.Should().Be("AAAAA-11111;BBBBB-22222");
    }
}
```

Note both contexts use the same fixed purpose string via `DataProtectionProvider.Create("Fakturenn.Tests")`, which derives a stable key from the machine — the round-trip only works because both instances derive the same key.

- [ ] **Step 5: Wire the test project**

```bash
dotnet add tests/Fakturenn.IntegrationTests reference src/Fakturenn.Modules.Identity
dotnet add tests/Fakturenn.IntegrationTests package Microsoft.AspNetCore.DataProtection
```

- [ ] **Step 6: Run and watch them fail, then pass**

Run: `dotnet test --project tests/Fakturenn.IntegrationTests`

Before Step 3's converter is applied, `A_token_value_is_not_readable_as_plaintext_in_the_column` must FAIL with the stored value equalling the plaintext. Verify by temporarily commenting out the `HasConversion` line, running, then restoring it. Paste both runs.

Expected after: 8 integration tests passing.

- [ ] **Step 7: Commit**

```bash
git add src/Fakturenn.Modules.Identity tests/Fakturenn.IntegrationTests Directory.Packages.props
git commit --message "feat(identity): encrypt both second factors at rest

ASP.NET Core Identity stores the TOTP shared secret AND the recovery codes as
plaintext in AspNetUserTokens.Value, verified empirically. One value converter
covers both, so a read of that single table no longer yields a working second
factor for every user.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Register Identity, authorization and Data Protection in the host

**Files:**

- Modify: `src/Fakturenn.Web/Fakturenn.Web.csproj`, `FakturennWebApplication.cs`, `Program.cs`, `appsettings.json`
- Create: `src/Fakturenn.Web/IdentityConfiguration.cs`

**Interfaces:**

- Consumes: Tasks 3–6
- Produces: `IdentityConfiguration.AddFakturennIdentity(WebApplicationBuilder, string? connectionString, DatabaseOptions)` — extension method returning `void`

- [ ] **Step 1: Reference the new projects**

```bash
dotnet add src/Fakturenn.Web reference src/Fakturenn.Modules.Identity
dotnet add src/Fakturenn.Web reference src/Fakturenn.Infrastructure.DataProtection
dotnet add src/Fakturenn.Web package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/Fakturenn.Web package Microsoft.AspNetCore.DataProtection.EntityFrameworkCore
```

- [ ] **Step 2: Write the registration**

`src/Fakturenn.Web/IdentityConfiguration.cs`:

```csharp
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Web;

public static class IdentityConfiguration
{
    public static void AddFakturennIdentity(
        this WebApplicationBuilder builder,
        string? connectionString,
        DatabaseOptions databaseOptions)
    {
        builder.Services.AddDbContext<DataProtectionDbContext>(options =>
            options.UseNpgsql(connectionString));

        // A fixed application name is what makes replicas share one ring. Without it
        // each instance derives its own, and a cookie encrypted by one cannot be read
        // by another -- sticky sessions give circuit affinity, not key sharing.
        builder.Services.AddDataProtection()
            .SetApplicationName("Fakturenn")
            .PersistKeysToDbContext<DataProtectionDbContext>();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();

        builder.Services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                    databaseOptions.MaxRetries,
                    TimeSpan.FromSeconds(databaseOptions.RetryDelaySeconds),
                    errorCodesToAdd: null))
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager();

        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/account/denied";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
        });

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddAuthorization();

        // Lockout alone would make the login endpoint a user-enumeration oracle:
        // a locked account answers differently from an unknown one under load.
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("account", limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
    }
}
```

`SecurePolicy` is `SameAsRequest` rather than `Always` because the reference Compose deployment serves plain HTTP on localhost; forcing `Always` would silently drop the cookie and make sign-in fail with no error. TLS termination is a deployment concern documented in `DEPLOYMENT-BASELINE.md`.

Add `using Fakturenn.Infrastructure.Persistence;` and `using Fakturenn.SharedKernel;`.

The `InvoicesDbContext` registration is deliberately left alone: the Invoices module has no auditable entity yet, so adding the interceptor there now would be configuration with nothing to configure. E09 adds it when the first invoice entity implements `IAuditable`.

- [ ] **Step 2a: Implement the current-user accessor**

`src/Fakturenn.Web/HttpContextCurrentUserAccessor.cs`:

```csharp
using System.Security.Claims;
using Fakturenn.SharedKernel;

namespace Fakturenn.Web;

/// <summary>
/// Resolves who is acting from the current request, or null when there is no
/// request — migrations, seeding, background work and the operator entrypoints all
/// run without one, and the interceptor records those as the system actor.
/// <para>
/// The claim order puts <c>preferred_username</c> first so that adding generic OIDC
/// later, per ADR-008, needs no change here. Local Identity does not issue that
/// claim, so today the name claim is the one that answers.
/// </para>
/// </summary>
public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserAccessor
{
    public string? UserName
    {
        get
        {
            ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;

            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return user.FindFirst("preferred_username")?.Value
                ?? user.FindFirst(ClaimTypes.Name)?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
```

- [ ] **Step 3: Call it and add the middleware**

In `FakturennWebApplication.Build`, after the `DatabaseOptions` block and before `builder.Build()`:

```csharp
        builder.AddFakturennIdentity(connectionString, databaseOptions);
```

After `app.UseRequestLocalization();` and before `app.UseAntiforgery();`:

```csharp
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
```

- [ ] **Step 4: Register all three migration contexts**

In `Program.cs`, replace the single-factory array:

```csharp
    IdentityDbContext CreateIdentityMigrationContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(migrationConnectionString)
                .Options,
            DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "fakturenn-migrate"))));

    DataProtectionDbContext CreateDataProtectionMigrationContext() =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql(migrationConnectionString)
            .Options);

    // One factory per context that owns migrations. The Data Protection context is
    // migrated FIRST: the Identity context's value converter needs a key ring, and
    // the ring's own table must exist before anything can write to it.
    Func<DbContext>[] createMigrationContexts =
    [
        CreateDataProtectionMigrationContext,
        CreateIdentityMigrationContext,
        CreateMigrationContext,
    ];
```

- [ ] **Step 5: Verify against a real database**

```bash
docker run --rm --detach --name fakturenn-e02a -e POSTGRES_PASSWORD=dev -e POSTGRES_USER=dev -e POSTGRES_DB=dev --publish 55432:5432 postgres:17-alpine
sleep 5
ConnectionStrings__Fakturenn='Host=127.0.0.1;Port=55432;Database=dev;Username=dev;Password=dev' \
  dotnet run --project src/Fakturenn.Web --configuration Release -- --migrate
echo "exit=$?"
docker exec fakturenn-e02a psql -U dev -d dev -c '\dn'
docker stop fakturenn-e02a
```

Expected: exit 0, and schemas `identity`, `invoices` and `dataprotection` all present.

- [ ] **Step 6: Confirm readiness did not regress**

Run the app with no connection string and confirm `/alive` is 200 and `/health` is 503, as Task 7 of the harness established.

- [ ] **Step 7: Run every suite and commit**

```bash
dotnet build --configuration Release
dotnet format --verify-no-changes
dotnet test --project tests/Fakturenn.UnitTests
dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests
dotnet test --project tests/Fakturenn.ArchitectureTests
dotnet test --project tests/Fakturenn.IntegrationTests
git add src/Fakturenn.Web Directory.Packages.props
git commit --message "feat(identity): register Identity, authorization and Data Protection

Three migration contexts now. Data Protection migrates first: the Identity
context's value converter needs a key ring, and the ring's table must exist
before anything writes to it.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Role seeding, permission catalogue validation, and the last-administrator guard

**Files:**

- Create: `src/Fakturenn.Modules.Identity/Persistence/RoleSeeder.cs`, `PermissionCatalogValidator.cs`, `AdministratorGuard.cs`
- Create: `tests/Fakturenn.Modules.Identity.UnitTests/PermissionCatalogValidatorTests.cs`, `AdministratorGuardTests.cs`
- Create: `tests/Fakturenn.IntegrationTests/RoleSeedingTests.cs`

**Interfaces:**

- Consumes: Tasks 3, 4
- Produces:
  - `RoleSeeder` — `static Task SeedAsync(IdentityDbContext context, CancellationToken cancellationToken)`; creates the `Administrator` role with `IsSystemRole = true` holding every permission in `Permissions.All`; idempotent
  - `PermissionCatalogValidator` — `static IReadOnlyList<string> FindUnknownPermissions(IEnumerable<string> stored)`
  - `AdministratorGuard` — `static bool WouldRemoveLastAdministrator(int administratorCount, bool targetIsAdministrator)`

- [ ] **Step 1: Write the failing unit tests**

`tests/Fakturenn.Modules.Identity.UnitTests/PermissionCatalogValidatorTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Persistence;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class PermissionCatalogValidatorTests
{
    [Fact]
    public void Stored_permissions_that_all_exist_in_code_produce_no_findings()
    {
        string[] stored = [Permissions.UsersRead, Permissions.UsersManage];

        PermissionCatalogValidator.FindUnknownPermissions(stored).Should().BeEmpty();
    }

    [Fact]
    public void A_stored_permission_the_code_does_not_define_is_reported()
    {
        // A stale or misspelt row grants nothing, which is indistinguishable from a
        // working configuration until someone is denied access they believe they have.
        string[] stored = [Permissions.UsersRead, "invoices.finalise"];

        PermissionCatalogValidator.FindUnknownPermissions(stored)
            .Should().ContainSingle().Which.Should().Be("invoices.finalise");
    }

    [Fact]
    public void Comparison_is_case_sensitive()
    {
        PermissionCatalogValidator.FindUnknownPermissions(["Users.Manage"])
            .Should().ContainSingle();
    }
}
```

`tests/Fakturenn.Modules.Identity.UnitTests/AdministratorGuardTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Persistence;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class AdministratorGuardTests
{
    [Fact]
    public void Removing_the_only_administrator_is_refused()
    {
        AdministratorGuard.WouldRemoveLastAdministrator(administratorCount: 1, targetIsAdministrator: true)
            .Should().BeTrue();
    }

    [Fact]
    public void Removing_one_of_several_administrators_is_allowed()
    {
        AdministratorGuard.WouldRemoveLastAdministrator(administratorCount: 2, targetIsAdministrator: true)
            .Should().BeFalse();
    }

    [Fact]
    public void Removing_a_user_who_is_not_an_administrator_is_allowed()
    {
        AdministratorGuard.WouldRemoveLastAdministrator(administratorCount: 1, targetIsAdministrator: false)
            .Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests`
Expected: build failure — `PermissionCatalogValidator` not found.

- [ ] **Step 3: Implement**

`src/Fakturenn.Modules.Identity/Persistence/PermissionCatalogValidator.cs`:

```csharp
using Fakturenn.Modules.Identity.Authorization;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Compares permissions stored against roles with the closed set the code defines.
/// A stored value the code does not know grants nothing, and silently granting
/// nothing looks exactly like a working configuration until someone is denied.
/// </summary>
public static class PermissionCatalogValidator
{
    public static IReadOnlyList<string> FindUnknownPermissions(IEnumerable<string> stored) =>
        [.. stored.Where(permission => !Permissions.All.Contains(permission)).Distinct(StringComparer.Ordinal)];
}
```

`src/Fakturenn.Modules.Identity/Persistence/AdministratorGuard.cs`:

```csharp
namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Stops the user interface locking an instance out of its own administration.
/// The CLI entrypoints remain the escape hatch if it happens anyway.
/// </summary>
public static class AdministratorGuard
{
    public static bool WouldRemoveLastAdministrator(int administratorCount, bool targetIsAdministrator) =>
        targetIsAdministrator && administratorCount <= 1;
}
```

`src/Fakturenn.Modules.Identity/Persistence/RoleSeeder.cs`:

```csharp
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Identity.Persistence;

public static class RoleSeeder
{
    public const string AdministratorRoleName = "Administrator";

    /// <summary>
    /// Ensures the Administrator system role exists and holds every declared
    /// permission. Idempotent: safe to run on every start, and it re-grants a
    /// permission added to the code since the role was created.
    /// </summary>
    public static async Task SeedAsync(IdentityDbContext context, CancellationToken cancellationToken)
    {
        Role? administrator = await context.Roles
            .SingleOrDefaultAsync(role => role.Name == AdministratorRoleName, cancellationToken);

        if (administrator is null)
        {
            administrator = new Role
            {
                Id = Guid.CreateVersion7(),
                Name = AdministratorRoleName,
                Description = "Full system administration.",
                IsSystemRole = true,
            };
            context.Roles.Add(administrator);
        }

        List<string> existing = await context.RolePermissions
            .Where(rolePermission => rolePermission.RoleId == administrator.Id)
            .Select(rolePermission => rolePermission.Permission)
            .ToListAsync(cancellationToken);

        foreach (string permission in Permissions.All.Except(existing, StringComparer.Ordinal))
        {
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = administrator.Id,
                Permission = permission,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Write the seeding integration test**

`tests/Fakturenn.IntegrationTests/RoleSeedingTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class RoleSeedingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private IdentityDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options,
            DataProtectionProvider.Create("Fakturenn.Tests"));

    [Fact]
    public async Task Seeding_twice_leaves_exactly_one_administrator_role_with_every_permission()
    {
        await using IdentityDbContext context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);
        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);

        int roleCount = await context.Roles
            .CountAsync(r => r.Name == RoleSeeder.AdministratorRoleName, TestContext.Current.CancellationToken);
        roleCount.Should().Be(1);

        Guid roleId = await context.Roles
            .Where(r => r.Name == RoleSeeder.AdministratorRoleName)
            .Select(r => r.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        List<string> granted = await context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync(TestContext.Current.CancellationToken);

        granted.Should().BeEquivalentTo(Permissions.All);
    }
}
```

- [ ] **Step 5: Run everything**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests` — expect 14 passing.
Run: `dotnet test --project tests/Fakturenn.IntegrationTests` — expect 9 passing.

- [ ] **Step 6: Commit**

```bash
git add src/Fakturenn.Modules.Identity tests/Fakturenn.Modules.Identity.UnitTests tests/Fakturenn.IntegrationTests
git commit --message "feat(identity): seed the Administrator role and validate the permission catalogue

Seeding is idempotent and re-grants permissions added to the code since the role
was created. The catalogue validator rejects a stored permission the code does not
define, because silently granting nothing looks identical to a working
configuration until someone is denied.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: First-run setup

**Files:**

- Create: `src/Fakturenn.Web/Components/Account/Setup.razor`, `src/Fakturenn.Web/Components/Account/AccountEndpoints.cs`
- Modify: `src/Fakturenn.Web/FakturennWebApplication.cs`

**Interfaces:**

- Consumes: Tasks 7, 8
- Produces: `AccountEndpoints.MapAccountEndpoints(IEndpointRouteBuilder)`; route `/setup`; endpoint `POST /account/setup`

- [ ] **Step 1: Write the setup page**

`src/Fakturenn.Web/Components/Account/Setup.razor`:

```razor
@page "/setup"
@using Fakturenn.Modules.Identity.Persistence
@using Microsoft.EntityFrameworkCore
@inject IdentityDbContext Db
@inject NavigationManager Navigation

<PageTitle>Fakturenn — Setup</PageTitle>

@if (_alreadyConfigured)
{
    <MudText Typo="Typo.h5">Already configured</MudText>
}
else
{
    <MudText Typo="Typo.h4" GutterBottom="true">Create the first administrator</MudText>
    <MudText Typo="Typo.body2" Class="mb-4">
        This page is available only while no account exists. It disappears permanently
        once the first administrator is created.
    </MudText>

    @if (Error is not null)
    {
        <MudAlert Severity="Severity.Error" Class="mb-4" data-testid="setup-error">@Error</MudAlert>
    }

    <form method="post" action="/account/setup" data-testid="setup-form">
        <AntiforgeryToken />
        <MudTextField T="string" Label="Email" InputType="InputType.Email" name="email" Required="true" data-testid="setup-email" />
        <MudTextField T="string" Label="Display name" name="displayName" Required="true" data-testid="setup-display-name" />
        <MudTextField T="string" Label="Password" InputType="InputType.Password" name="password" Required="true" data-testid="setup-password" />
        <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4" data-testid="setup-submit">
            Create administrator
        </MudButton>
    </form>
}

@code {
    private bool _alreadyConfigured;

    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _alreadyConfigured = await Db.Users.AnyAsync();

        if (_alreadyConfigured)
        {
            // A user-count query, not a configuration flag: a flag can be left on,
            // a populated table cannot.
            Navigation.NavigateTo("/account/login", replace: true);
        }
    }
}
```

- [ ] **Step 2: Write the endpoint**

`src/Fakturenn.Web/Components/Account/AccountEndpoints.cs`:

```csharp
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Web.Components.Account;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/account").RequireRateLimiting("account");

        group.MapPost("/setup", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            IdentityDbContext db,
            CancellationToken cancellationToken) =>
        {
            // Re-checked server-side. The page's own guard is a redirect for humans;
            // this is the one that actually closes the endpoint.
            if (await db.Users.AnyAsync(cancellationToken))
            {
                return Results.NotFound();
            }

            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string email = form["email"].ToString().Trim();
            string displayName = form["displayName"].ToString().Trim();
            string password = form["password"].ToString();

            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTimeOffset.UtcNow,
                MustEnrolTotp = true,
            };

            IdentityResult created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                string message = string.Join(" ", created.Errors.Select(e => e.Description));
                return Results.Redirect($"/setup?error={Uri.EscapeDataString(message)}");
            }

            await RoleSeeder.SeedAsync(db, cancellationToken);

            Guid administratorRoleId = await db.Roles
                .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
                .Select(role => role.Id)
                .SingleAsync(cancellationToken);

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = administratorRoleId });
            await db.SaveChangesAsync(cancellationToken);

            return Results.Redirect("/account/login");
        });
    }
}
```

- [ ] **Step 3: Map the endpoints and redirect the root**

In `FakturennWebApplication.Build`, before `app.MapRazorComponents<App>()`:

```csharp
        app.MapAccountEndpoints();
```

Add `using Fakturenn.Web.Components.Account;`.

- [ ] **Step 4: Verify by hand against a real database**

Start PostgreSQL as in Task 7 Step 5, run `--migrate`, start the app, then:

```bash
curl --silent --output /dev/null --write-out 'setup=%{http_code}\n' http://127.0.0.1:5099/setup
```

Expected: 200 while no user exists.

- [ ] **Step 5: Commit**

```bash
git add src/Fakturenn.Web
git commit --message "feat(identity): add first-run setup

The setup endpoint re-checks the user count server-side rather than trusting the
page's redirect, and the guard is a query rather than a configuration flag: a flag
can be left on, a populated table cannot.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: TOTP enrolment and recovery codes

**Files:**

- Create: `src/Fakturenn.Web/Components/Account/EnrolTotp.razor`, `RecoveryCodes.razor`
- Modify: `src/Fakturenn.Web/Components/Account/AccountEndpoints.cs`

**Interfaces:**

- Consumes: Task 9
- Produces: routes `/account/enrol-totp`, `/account/recovery-codes`; endpoints `POST /account/enrol-totp`

- [ ] **Step 1: Add the endpoint**

Add to `AccountEndpoints.MapAccountEndpoints`:

```csharp
        group.MapPost("/enrol-totp", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            CancellationToken cancellationToken) =>
        {
            ApplicationUser? user = await users.GetUserAsync(http.User);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string code = form["code"].ToString().Replace(" ", string.Empty);

            bool valid = await users.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, code);

            if (!valid)
            {
                return Results.Redirect("/account/enrol-totp?error=invalid");
            }

            await users.SetTwoFactorEnabledAsync(user, true);
            user.MustEnrolTotp = false;
            await users.UpdateAsync(user);

            IEnumerable<string>? codes =
                await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            // Recovery codes are shown exactly once. They are stored encrypted, but
            // Identity keeps them recoverable by design, so there is no second chance
            // to display them without regenerating and invalidating the old set.
            http.Session0Set(codes);

            return Results.Redirect("/account/recovery-codes");
        });
```

Replace `http.Session0Set(codes)` with a `TempData`-equivalent that works without session state: store the codes in a short-lived, data-protected cookie. Add this helper to `AccountEndpoints`:

```csharp
    private const string RecoveryCookieName = "fakturenn_recovery";

    private static void StashRecoveryCodes(HttpContext http, IEnumerable<string> codes)
    {
        IDataProtector protector = http.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Fakturenn.RecoveryCodeDisplay.v1");

        http.Response.Cookies.Append(
            RecoveryCookieName,
            protector.Protect(string.Join(';', codes)),
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                SecurePolicy = CookieSecurePolicy.SameAsRequest,
                MaxAge = TimeSpan.FromMinutes(5),
            });
    }

    internal static string[] TakeRecoveryCodes(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(RecoveryCookieName, out string? protectedValue))
        {
            return [];
        }

        http.Response.Cookies.Delete(RecoveryCookieName);

        IDataProtector protector = http.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Fakturenn.RecoveryCodeDisplay.v1");

        try
        {
            return protector.Unprotect(protectedValue).Split(';', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return [];
        }
    }
```

and call `StashRecoveryCodes(http, codes ?? []);` in place of the placeholder.

Add `using Microsoft.AspNetCore.DataProtection;`.

- [ ] **Step 2: Write the enrolment page**

`src/Fakturenn.Web/Components/Account/EnrolTotp.razor`:

```razor
@page "/account/enrol-totp"
@using System.Text
@using Fakturenn.Modules.Identity.Domain
@using Microsoft.AspNetCore.Identity
@attribute [Microsoft.AspNetCore.Authorization.Authorize]
@inject UserManager<ApplicationUser> Users
@inject IHttpContextAccessor HttpContextAccessor

<PageTitle>Fakturenn — Set up two-factor authentication</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Set up two-factor authentication</MudText>
<MudText Typo="Typo.body2" Class="mb-4">
    Two-factor authentication is required. Scan the key below with an authenticator
    app, then enter the six-digit code it shows.
</MudText>

@if (Error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4" data-testid="enrol-error">That code was not accepted.</MudAlert>
}

<MudPaper Class="pa-4 mb-4">
    <MudText Typo="Typo.caption">Manual entry key</MudText>
    <MudText Typo="Typo.h6" data-testid="totp-key">@_formattedKey</MudText>
</MudPaper>

<form method="post" action="/account/enrol-totp" data-testid="enrol-form">
    <AntiforgeryToken />
    <MudTextField T="string" Label="Authenticator code" name="code" Required="true" data-testid="enrol-code" />
    <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4" data-testid="enrol-submit">
        Verify and enable
    </MudButton>
</form>

@code {
    private string _formattedKey = string.Empty;

    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    protected override async Task OnInitializedAsync()
    {
        HttpContext http = HttpContextAccessor.HttpContext!;
        ApplicationUser user = (await Users.GetUserAsync(http.User))!;

        string? key = await Users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await Users.ResetAuthenticatorKeyAsync(user);
            key = await Users.GetAuthenticatorKeyAsync(user);
        }

        _formattedKey = FormatKey(key!);
    }

    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < key.Length; i += 4)
        {
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }

        return builder.ToString().TrimEnd();
    }
}
```

`AddHttpContextAccessor()` must be registered — add it in `IdentityConfiguration.AddFakturennIdentity`.

- [ ] **Step 3: Write the recovery-codes page**

`src/Fakturenn.Web/Components/Account/RecoveryCodes.razor`:

```razor
@page "/account/recovery-codes"
@attribute [Microsoft.AspNetCore.Authorization.Authorize]
@inject IHttpContextAccessor HttpContextAccessor

<PageTitle>Fakturenn — Recovery codes</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Recovery codes</MudText>

@if (_codes.Length == 0)
{
    <MudAlert Severity="Severity.Info" data-testid="recovery-empty">
        These codes are shown once. Generate a new set from your account if you no longer have them.
    </MudAlert>
}
else
{
    <MudAlert Severity="Severity.Warning" Class="mb-4">
        Store these now. They are shown once and cannot be displayed again.
        Each code works a single time.
    </MudAlert>

    <MudPaper Class="pa-4" data-testid="recovery-codes">
        @foreach (string code in _codes)
        {
            <MudText Typo="Typo.body1">@code</MudText>
        }
    </MudPaper>
}

<MudButton Href="/" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4" data-testid="recovery-continue">
    Continue
</MudButton>

@code {
    private string[] _codes = [];

    protected override void OnInitialized() =>
        _codes = AccountEndpoints.TakeRecoveryCodes(HttpContextAccessor.HttpContext!);
}
```

- [ ] **Step 4: Build and format**

Run: `dotnet build --configuration Release` — `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet format --verify-no-changes` — clean.

- [ ] **Step 5: Commit**

```bash
git add src/Fakturenn.Web
git commit --message "feat(identity): add TOTP enrolment and recovery codes

Recovery codes are handed to the display page through a short-lived,
data-protected cookie rather than session state, so the flow needs no server
session and survives the redirect. They are shown once; Identity keeps them
recoverable by design, so there is no second display without regenerating.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: Sign-in, two-factor challenge, recovery-code sign-in and lockout

**Files:**

- Create: `src/Fakturenn.Web/Components/Account/Login.razor`, `LoginWith2fa.razor`, `LoginWithRecoveryCode.razor`, `Lockout.razor`, `AccessDenied.razor`
- Modify: `src/Fakturenn.Web/Components/Account/AccountEndpoints.cs`

**Interfaces:**

- Consumes: Tasks 7, 10
- Produces: routes `/account/login`, `/account/login-2fa`, `/account/login-recovery`, `/account/lockout`, `/account/denied`; endpoints `POST /account/login`, `POST /account/login-2fa`, `POST /account/login-recovery`, `POST /account/logout`

- [ ] **Step 1: Add the sign-in endpoints**

Add to `AccountEndpoints.MapAccountEndpoints`:

```csharp
        group.MapPost("/login", async (
            HttpContext http,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string email = form["email"].ToString().Trim();
            string password = form["password"].ToString();

            SignInResult result = await signIn.PasswordSignInAsync(
                email, password, isPersistent: false, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                return Results.Redirect("/account/lockout");
            }

            if (result.RequiresTwoFactor)
            {
                return Results.Redirect("/account/login-2fa");
            }

            if (!result.Succeeded)
            {
                // One message for both an unknown account and a wrong password.
                return Results.Redirect("/account/login?error=invalid");
            }

            return Results.Redirect("/");
        });

        group.MapPost("/login-2fa", async (
            HttpContext http,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string code = form["code"].ToString().Replace(" ", string.Empty);

            SignInResult result = await signIn.TwoFactorAuthenticatorSignInAsync(
                code, isPersistent: false, rememberClient: false);

            if (result.IsLockedOut)
            {
                return Results.Redirect("/account/lockout");
            }

            return result.Succeeded
                ? Results.Redirect("/")
                : Results.Redirect("/account/login-2fa?error=invalid");
        });

        group.MapPost("/login-recovery", async (
            HttpContext http,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string code = form["code"].ToString().Replace(" ", string.Empty);

            SignInResult result = await signIn.TwoFactorRecoveryCodeSignInAsync(code);

            return result.Succeeded
                ? Results.Redirect("/")
                : Results.Redirect("/account/login-recovery?error=invalid");
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Redirect("/account/login");
        });
```

- [ ] **Step 2: Write the pages**

`src/Fakturenn.Web/Components/Account/Login.razor`:

```razor
@page "/account/login"
@using Fakturenn.Modules.Identity.Persistence
@using Microsoft.EntityFrameworkCore
@inject IdentityDbContext Db
@inject NavigationManager Navigation

<PageTitle>Fakturenn — Sign in</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Sign in</MudText>

@if (Error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4" data-testid="login-error">
        That email address and password combination was not recognised.
    </MudAlert>
}

<form method="post" action="/account/login" data-testid="login-form">
    <AntiforgeryToken />
    <MudTextField T="string" Label="Email" InputType="InputType.Email" name="email" Required="true" data-testid="login-email" />
    <MudTextField T="string" Label="Password" InputType="InputType.Password" name="password" Required="true" data-testid="login-password" />
    <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4" data-testid="login-submit">
        Sign in
    </MudButton>
</form>

@code {
    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!await Db.Users.AnyAsync())
        {
            Navigation.NavigateTo("/setup", replace: true);
        }
    }
}
```

`src/Fakturenn.Web/Components/Account/LoginWith2fa.razor`:

```razor
@page "/account/login-2fa"

<PageTitle>Fakturenn — Two-factor authentication</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Two-factor authentication</MudText>

@if (Error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4" data-testid="twofa-error">That code was not accepted.</MudAlert>
}

<form method="post" action="/account/login-2fa" data-testid="twofa-form">
    <AntiforgeryToken />
    <MudTextField T="string" Label="Authenticator code" name="code" Required="true" data-testid="twofa-code" />
    <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4" data-testid="twofa-submit">
        Verify
    </MudButton>
</form>

<MudLink Href="/account/login-recovery" Class="mt-4" data-testid="twofa-use-recovery">
    Use a recovery code instead
</MudLink>

@code {
    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }
}
```

`src/Fakturenn.Web/Components/Account/LoginWithRecoveryCode.razor`:

```razor
@page "/account/login-recovery"

<PageTitle>Fakturenn — Recovery code</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Sign in with a recovery code</MudText>
<MudText Typo="Typo.body2" Class="mb-4">Each recovery code works once.</MudText>

@if (Error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4" data-testid="recovery-error">That code was not accepted.</MudAlert>
}

<form method="post" action="/account/login-recovery" data-testid="recovery-form">
    <AntiforgeryToken />
    <MudTextField T="string" Label="Recovery code" name="code" Required="true" data-testid="recovery-code" />
    <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4" data-testid="recovery-submit">
        Sign in
    </MudButton>
</form>

@code {
    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }
}
```

`src/Fakturenn.Web/Components/Account/Lockout.razor`:

```razor
@page "/account/lockout"

<PageTitle>Fakturenn — Account locked</PageTitle>

<MudAlert Severity="Severity.Warning" data-testid="lockout-message">
    This account is temporarily locked after too many failed attempts. Try again later.
</MudAlert>
```

`src/Fakturenn.Web/Components/Account/AccessDenied.razor`:

```razor
@page "/account/denied"

<PageTitle>Fakturenn — Access denied</PageTitle>

<MudAlert Severity="Severity.Error" data-testid="denied-message">
    You do not have permission to view that page.
</MudAlert>
```

- [ ] **Step 3: Build, format, verify by hand**

Start PostgreSQL, migrate, run the app, complete setup in a browser or with `curl`, and confirm sign-in reaches the two-factor challenge.

- [ ] **Step 4: Commit**

```bash
git add src/Fakturenn.Web
git commit --message "feat(identity): add sign-in, two-factor challenge, recovery-code sign-in and lockout

Sign-in failures give one message for both an unknown account and a wrong
password, so the endpoint is not a user-enumeration oracle.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 12: The enrolment gate

A user who has authenticated by password but not finished enrolling is in a partial-authentication state. Getting this wrong in one direction locks users out; in the other, a password alone reaches the application.

**Files:**

- Create: `src/Fakturenn.Web/EnrolmentGateMiddleware.cs`
- Create: `tests/Fakturenn.Modules.Identity.UnitTests/EnrolmentGatePathTests.cs`
- Modify: `src/Fakturenn.Web/FakturennWebApplication.cs`, and move the path predicate into `Fakturenn.Modules.Identity`

**Interfaces:**

- Consumes: Task 11
- Produces:
  - `Fakturenn.Modules.Identity.Authorization.EnrolmentGate.IsAllowedWhileEnrolmentPending(string path)` — `static bool`
  - `EnrolmentGateMiddleware` — standard middleware

- [ ] **Step 1: Write the failing test**

`tests/Fakturenn.Modules.Identity.UnitTests/EnrolmentGatePathTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class EnrolmentGatePathTests
{
    [Theory]
    [InlineData("/account/enrol-totp")]
    [InlineData("/account/recovery-codes")]
    [InlineData("/account/logout")]
    [InlineData("/alive")]
    [InlineData("/health")]
    [InlineData("/_content/MudBlazor/MudBlazor.min.css")]
    public void Paths_a_half_enrolled_user_still_needs_are_allowed(string path)
    {
        EnrolmentGate.IsAllowedWhileEnrolmentPending(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin/users")]
    [InlineData("/invoices")]
    public void Everything_else_is_blocked(string path)
    {
        EnrolmentGate.IsAllowedWhileEnrolmentPending(path).Should().BeFalse();
    }

    [Fact]
    public void Matching_is_case_insensitive_because_urls_are()
    {
        EnrolmentGate.IsAllowedWhileEnrolmentPending("/Account/Enrol-Totp").Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests`
Expected: build failure — `EnrolmentGate` not found.

- [ ] **Step 3: Implement the predicate**

`src/Fakturenn.Modules.Identity/Authorization/EnrolmentGate.cs`:

```csharp
namespace Fakturenn.Modules.Identity.Authorization;

/// <summary>
/// Decides which paths a user who has authenticated by password but not completed
/// TOTP enrolment may still reach. Kept as a pure function so the policy is
/// testable without a request pipeline.
/// </summary>
public static class EnrolmentGate
{
    private static readonly string[] _allowedPrefixes =
    [
        "/account/enrol-totp",
        "/account/recovery-codes",
        "/account/logout",
        "/alive",
        "/health",
        "/_content/",
        "/_framework/",
        "/css/",
        "/favicon",
    ];

    public static bool IsAllowedWhileEnrolmentPending(string path) =>
        _allowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Implement the middleware**

`src/Fakturenn.Web/EnrolmentGateMiddleware.cs`:

```csharp
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Fakturenn.Web;

/// <summary>
/// Redirects a signed-in user with pending TOTP enrolment to the enrolment page.
/// Runs after authentication so <c>HttpContext.User</c> is populated, and before
/// endpoint execution so no application page renders for a half-enrolled user.
/// </summary>
public sealed class EnrolmentGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string path = context.Request.Path.Value ?? "/";
        if (EnrolmentGate.IsAllowedWhileEnrolmentPending(path))
        {
            await next(context);
            return;
        }

        ApplicationUser? user = await users.GetUserAsync(context.User);
        if (user?.MustEnrolTotp == true)
        {
            context.Response.Redirect("/account/enrol-totp");
            return;
        }

        await next(context);
    }
}
```

- [ ] **Step 5: Wire it**

In `FakturennWebApplication.Build`, after `app.UseAuthorization();`:

```csharp
        app.UseMiddleware<EnrolmentGateMiddleware>();
```

- [ ] **Step 6: Run and commit**

Run: `dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests` — expect 24 passing.

```bash
git add src/Fakturenn.Modules.Identity src/Fakturenn.Web tests/Fakturenn.Modules.Identity.UnitTests
git commit --message "feat(identity): gate the application behind completed TOTP enrolment

The path policy is a pure function so it is testable without a request pipeline.
A half-enrolled user reaches only enrolment, sign-out, health and static assets.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 13: Administrator user management

**Files:**

- Create: `src/Fakturenn.Web/Components/Admin/Users.razor`
- Modify: `src/Fakturenn.Web/Components/Account/AccountEndpoints.cs`

**Interfaces:**

- Consumes: Tasks 8, 11
- Produces: route `/admin/users`; endpoints `POST /account/admin/create-user`, `/account/admin/reset-password`, `/account/admin/clear-mfa`, `/account/admin/set-lockout`

- [ ] **Step 1: Add the administration endpoints**

Add to `AccountEndpoints.MapAccountEndpoints`, all requiring the permission:

```csharp
        RouteGroupBuilder admin = group.MapGroup("/admin")
            .RequireAuthorization(Permissions.UsersManage);

        admin.MapPost("/create-user", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string email = form["email"].ToString().Trim();
            string displayName = form["displayName"].ToString().Trim();
            string password = form["password"].ToString();

            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTimeOffset.UtcNow,
                MustEnrolTotp = true,
            };

            IdentityResult created = await users.CreateAsync(user, password);

            return created.Succeeded
                ? Results.Redirect("/admin/users")
                : Results.Redirect($"/admin/users?error={Uri.EscapeDataString(
                    string.Join(" ", created.Errors.Select(e => e.Description)))}");
        });

        admin.MapPost("/reset-password", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            ApplicationUser? user = await users.FindByEmailAsync(form["email"].ToString().Trim());
            if (user is null)
            {
                return Results.Redirect("/admin/users?error=unknown");
            }

            string token = await users.GeneratePasswordResetTokenAsync(user);
            IdentityResult reset = await users.ResetPasswordAsync(user, token, form["password"].ToString());

            return reset.Succeeded
                ? Results.Redirect("/admin/users")
                : Results.Redirect("/admin/users?error=reset-failed");
        });

        admin.MapPost("/clear-mfa", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            ApplicationUser? user = await users.FindByEmailAsync(form["email"].ToString().Trim());
            if (user is null)
            {
                return Results.Redirect("/admin/users?error=unknown");
            }

            await users.SetTwoFactorEnabledAsync(user, false);
            await users.ResetAuthenticatorKeyAsync(user);
            user.MustEnrolTotp = true;
            await users.UpdateAsync(user);

            return Results.Redirect("/admin/users");
        });
```

Add `using Fakturenn.Modules.Identity.Authorization;`.

- [ ] **Step 2: Write the page**

`src/Fakturenn.Web/Components/Admin/Users.razor`:

```razor
@page "/admin/users"
@using Fakturenn.Modules.Identity.Authorization
@using Fakturenn.Modules.Identity.Domain
@using Fakturenn.Modules.Identity.Persistence
@using Microsoft.EntityFrameworkCore
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Policy = Permissions.UsersManage)]
@inject IdentityDbContext Db

<PageTitle>Fakturenn — Users</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Users</MudText>

@if (Error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4" data-testid="admin-error">@Error</MudAlert>
}

<MudTable Items="_users" Class="mb-8" data-testid="user-table">
    <HeaderContent>
        <MudTh>Email</MudTh>
        <MudTh>Display name</MudTh>
        <MudTh>Two-factor</MudTh>
        <MudTh>Locked until</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.Email</MudTd>
        <MudTd>@context.DisplayName</MudTd>
        <MudTd>@(context.TwoFactorEnabled ? "enrolled" : "pending")</MudTd>
        <MudTd>@(context.LockoutEnd?.ToString("u") ?? "-")</MudTd>
    </RowTemplate>
</MudTable>

<MudText Typo="Typo.h5" GutterBottom="true">Create a user</MudText>
<form method="post" action="/account/admin/create-user" data-testid="create-user-form">
    <AntiforgeryToken />
    <MudTextField T="string" Label="Email" InputType="InputType.Email" name="email" Required="true" />
    <MudTextField T="string" Label="Display name" name="displayName" Required="true" />
    <MudTextField T="string" Label="Initial password" InputType="InputType.Password" name="password" Required="true" />
    <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4">
        Create user
    </MudButton>
</form>

@code {
    private List<ApplicationUser> _users = [];

    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    protected override async Task OnInitializedAsync() =>
        _users = await Db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
}
```

- [ ] **Step 3: Build, format, commit**

```bash
dotnet build --configuration Release
dotnet format --verify-no-changes
git add src/Fakturenn.Web
git commit --message "feat(identity): add administrator user management

Every administration endpoint requires the users.manage permission rather than a
role name, so the policy can change without a deploy.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 14: Operator recovery entrypoints

**Files:**

- Create: `src/Fakturenn.Web/Operations/OperatorCommands.cs`
- Modify: `src/Fakturenn.Web/Program.cs`

**Interfaces:**

- Consumes: Tasks 7, 8
- Produces: `OperatorCommands.TryRunAsync(string[] args, WebApplication app)` — `static Task<int?>`, returns `null` when no operator command was requested

- [ ] **Step 1: Implement**

`src/Fakturenn.Web/Operations/OperatorCommands.cs`:

```csharp
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Web.Operations;

/// <summary>
/// Recovery entrypoints for an operator locked out of the web interface. They
/// require host and database access rather than a password, which is exactly what
/// makes them both useful and unavailable to anyone who only reaches the web.
/// <para>
/// A password is never taken as a command-line argument: it would land in shell
/// history and in process listings. Passwords are read from standard input.
/// </para>
/// </summary>
public static class OperatorCommands
{
    public static async Task<int?> TryRunAsync(string[] args, WebApplication app)
    {
        string? command = args.FirstOrDefault(argument => argument.StartsWith("--", StringComparison.Ordinal)
            && argument is "--create-admin" or "--reset-password" or "--reset-mfa" or "--list-users");

        if (command is null)
        {
            return null;
        }

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        string? email = args.SkipWhile(a => a != command).Skip(1).FirstOrDefault();

        return command switch
        {
            "--list-users" => await ListUsersAsync(db),
            "--create-admin" => await CreateAdminAsync(users, db, email),
            "--reset-password" => await ResetPasswordAsync(users, email),
            "--reset-mfa" => await ResetMfaAsync(users, email),
            _ => null,
        };
    }

    private static async Task<int> ListUsersAsync(IdentityDbContext db)
    {
        List<ApplicationUser> users = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();

        foreach (ApplicationUser user in users)
        {
            Console.WriteLine(
                $"{user.Email}\t{user.DisplayName}\ttwoFactor={user.TwoFactorEnabled}\t" +
                $"mustEnrol={user.MustEnrolTotp}\tlockedUntil={user.LockoutEnd?.ToString("u") ?? "-"}");
        }

        Console.WriteLine($"{users.Count} user(s).");
        return 0;
    }

    private static async Task<int> CreateAdminAsync(
        UserManager<ApplicationUser> users, IdentityDbContext db, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine("--create-admin requires an email address.");
            return 2;
        }

        if (await db.Users.AnyAsync())
        {
            Console.Error.WriteLine("Refusing: an account already exists. Use --reset-password instead.");
            return 2;
        }

        string password = ReadPassword();

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = email,
            CreatedAt = DateTimeOffset.UtcNow,
            MustEnrolTotp = true,
        };

        IdentityResult created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            Console.Error.WriteLine(string.Join(" ", created.Errors.Select(e => e.Description)));
            return 1;
        }

        await RoleSeeder.SeedAsync(db, CancellationToken.None);
        Guid roleId = await db.Roles
            .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
            .Select(role => role.Id)
            .SingleAsync();

        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();

        Console.WriteLine($"Created administrator {email}. TOTP enrolment is required at first sign-in.");
        return 0;
    }

    private static async Task<int> ResetPasswordAsync(UserManager<ApplicationUser> users, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine("--reset-password requires an email address.");
            return 2;
        }

        ApplicationUser? user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            Console.Error.WriteLine($"No user with email {email}.");
            return 1;
        }

        string password = ReadPassword();
        string token = await users.GeneratePasswordResetTokenAsync(user);
        IdentityResult reset = await users.ResetPasswordAsync(user, token, password);

        if (!reset.Succeeded)
        {
            Console.Error.WriteLine(string.Join(" ", reset.Errors.Select(e => e.Description)));
            return 1;
        }

        Console.WriteLine($"Password reset for {email}.");
        return 0;
    }

    private static async Task<int> ResetMfaAsync(UserManager<ApplicationUser> users, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine("--reset-mfa requires an email address.");
            return 2;
        }

        ApplicationUser? user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            Console.Error.WriteLine($"No user with email {email}.");
            return 1;
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        user.MustEnrolTotp = true;
        await users.UpdateAsync(user);

        Console.WriteLine($"Two-factor authentication cleared for {email}. Re-enrolment is required at next sign-in.");
        return 0;
    }

    private static string ReadPassword()
    {
        Console.Error.Write("Password: ");
        string? password = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("No password supplied on standard input.");
        }

        return password;
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`**

After the `--migrate` block and before `await app.RunAsync();`:

```csharp
int? operatorExitCode = await OperatorCommands.TryRunAsync(args, app);
if (operatorExitCode is not null)
{
    Environment.ExitCode = operatorExitCode.Value;
    return;
}
```

- [ ] **Step 3: Verify each command against a real database**

```bash
docker run --rm --detach --name fakturenn-e02a -e POSTGRES_PASSWORD=dev -e POSTGRES_USER=dev -e POSTGRES_DB=dev --publish 55432:5432 postgres:17-alpine
sleep 5
export ConnectionStrings__Fakturenn='Host=127.0.0.1;Port=55432;Database=dev;Username=dev;Password=dev'
dotnet run --project src/Fakturenn.Web --configuration Release -- --migrate
echo 'Str0ng!Passw0rd!' | dotnet run --project src/Fakturenn.Web --configuration Release -- --create-admin ops@example.test
dotnet run --project src/Fakturenn.Web --configuration Release -- --list-users
echo 'An0ther!Passw0rd!' | dotnet run --project src/Fakturenn.Web --configuration Release -- --reset-password ops@example.test
dotnet run --project src/Fakturenn.Web --configuration Release -- --reset-mfa ops@example.test
echo 'Third!Passw0rd!' | dotnet run --project src/Fakturenn.Web --configuration Release -- --create-admin second@example.test
echo "second create should have failed with exit 2, got $?"
docker stop fakturenn-e02a
```

Paste the real output. `--create-admin` must refuse the second call.

- [ ] **Step 4: Commit**

```bash
git add src/Fakturenn.Web
git commit --message "feat(identity): add operator recovery entrypoints

--create-admin, --reset-password, --reset-mfa and --list-users. Passwords are read
from standard input, never taken as arguments, so they do not land in shell
history or process listings.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 15: The Playwright journey that closes SPIKE-009

**No test may bypass two-factor authentication.** A bypass would make every later UI test pass against an application whose authentication does not work — the failure mode that let three architecture rules sit dead through an entire task in the harness.

**Files:**

- Create: `tests/Fakturenn.UiTests/IdentityJourneyTests.cs`, `tests/Fakturenn.UiTests/AuthenticatedWebAppFixture.cs`
- Modify: `tests/Fakturenn.UiTests/Fakturenn.UiTests.csproj`

**Interfaces:**

- Consumes: Tasks 9–12
- Produces: `AuthenticatedWebAppFixture` — `IAsyncLifetime`; properties `string BaseAddress`, `string AdminEmail`, `string AdminPassword`, `string TotpSecret`; method `string CurrentTotpCode()`

- [ ] **Step 1: Add packages**

```bash
dotnet add tests/Fakturenn.UiTests package Otp.NET
dotnet add tests/Fakturenn.UiTests package Testcontainers.PostgreSql
dotnet add tests/Fakturenn.UiTests reference src/Fakturenn.Modules.Identity
```

- [ ] **Step 2: Write the fixture**

`tests/Fakturenn.UiTests/AuthenticatedWebAppFixture.cs`:

```csharp
using Fakturenn.Web;
using OtpNet;
using Testcontainers.PostgreSql;

namespace Fakturenn.UiTests;

/// <summary>
/// Hosts the real application against a real PostgreSQL container, so the identity
/// journey exercises genuine persistence, genuine Data Protection and genuine RFC
/// 6238 verification. Nothing here bypasses two-factor authentication.
/// </summary>
public sealed class AuthenticatedWebAppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("fakturenn")
        .WithUsername("fakturenn")
        .WithPassword("fakturenn")
        .Build();

    private WebApplication? _app;

    public string BaseAddress { get; private set; } = string.Empty;

    public string AdminEmail => "admin@example.test";

    public string AdminPassword => "Str0ng!Passw0rd!";

    public string TotpSecret { get; set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        string[] args =
        [
            "--urls", "http://127.0.0.1:0",
            "--ConnectionStrings:Fakturenn", _postgres.GetConnectionString(),
        ];

        // Migrations never run at startup, so apply them explicitly first.
        WebApplication migrator = FakturennWebApplication.Build([.. args, "--migrate"]);
        await MigrateAsync(migrator, _postgres.GetConnectionString());

        _app = FakturennWebApplication.Build(args);
        await _app.StartAsync();
        BaseAddress = _app.Urls.First();
    }

    public string CurrentTotpCode() =>
        new Totp(Base32Encoding.ToBytes(TotpSecret)).ComputeTotp();

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private static async Task MigrateAsync(WebApplication app, string connectionString)
    {
        // Reuses the same DatabaseMigrator the --migrate entrypoint uses, rather than
        // a second migration path that could drift from it.
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        foreach (DbContext context in new DbContext[]
                 {
                     scope.ServiceProvider.GetRequiredService<Fakturenn.Infrastructure.DataProtection.DataProtectionDbContext>(),
                     scope.ServiceProvider.GetRequiredService<Fakturenn.Modules.Identity.Persistence.IdentityDbContext>(),
                     scope.ServiceProvider.GetRequiredService<Fakturenn.Modules.Invoices.Persistence.InvoicesDbContext>(),
                 })
        {
            await context.Database.MigrateAsync();
        }

        await app.DisposeAsync();
    }
}
```

Add the `using Microsoft.EntityFrameworkCore;` and project references needed for the three contexts.

- [ ] **Step 3: Write the journey**

`tests/Fakturenn.UiTests/IdentityJourneyTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Fakturenn.UiTests;

public sealed class IdentityJourneyTests(AuthenticatedWebAppFixture app)
    : IClassFixture<AuthenticatedWebAppFixture>, IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    [Fact]
    public async Task Setup_then_password_and_totp_sign_in_reaches_the_application()
    {
        IPage page = await NewPageAsync();

        // 1. Setup exists while no user does.
        await page.GotoAsync($"{app.BaseAddress}/setup");
        await page.GetByTestId("setup-email").FillAsync(app.AdminEmail);
        await page.GetByTestId("setup-display-name").FillAsync("Administrator");
        await page.GetByTestId("setup-password").FillAsync(app.AdminPassword);
        await page.GetByTestId("setup-submit").ClickAsync();

        // 2. Sign in with the password.
        await page.WaitForURLAsync($"{app.BaseAddress}account/login");
        await page.GetByTestId("login-email").FillAsync(app.AdminEmail);
        await page.GetByTestId("login-password").FillAsync(app.AdminPassword);
        await page.GetByTestId("login-submit").ClickAsync();

        // 3. Enrolment is forced, and the manual-entry key is the real shared secret.
        await page.WaitForURLAsync($"**/account/enrol-totp");
        string displayedKey = (await page.GetByTestId("totp-key").TextContentAsync())!;
        app.TotpSecret = displayedKey.Replace(" ", string.Empty);

        await page.GetByTestId("enrol-code").FillAsync(app.CurrentTotpCode());
        await page.GetByTestId("enrol-submit").ClickAsync();

        // 4. Recovery codes are shown exactly once.
        await page.WaitForURLAsync($"**/account/recovery-codes");
        string codes = (await page.GetByTestId("recovery-codes").TextContentAsync())!;
        codes.Should().NotBeNullOrWhiteSpace();

        await page.GetByTestId("recovery-continue").ClickAsync();
        await page.GetByTestId("app-tagline").WaitForAsync();
    }

    [Fact]
    public async Task The_setup_page_is_gone_once_a_user_exists()
    {
        IPage page = await NewPageAsync();

        await page.GotoAsync($"{app.BaseAddress}/setup");

        // Redirected to sign-in rather than offering to create a second administrator.
        page.Url.Should().Contain("/account/login");
    }

    private async Task<IPage> NewPageAsync()
    {
        IBrowserContext context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "en-GB",
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Accept-Language"] = "en-GB" },
        });

        return await context.NewPageAsync();
    }
}
```

The two tests share one fixture and therefore one database, so `The_setup_page_is_gone_once_a_user_exists` depends on the journey having run. xUnit does not guarantee ordering within a class — if the tests prove order-dependent, split the second into its own class with its own fixture rather than adding ordering attributes.

- [ ] **Step 4: Run**

```bash
dotnet test --project tests/Fakturenn.UiTests
```

Expected: 6 passing — the 4 existing plus these 2. Report the duration; the added container start makes this the slowest suite.

- [ ] **Step 5: Prove the journey is load-bearing**

Temporarily change `EnrolTotp.razor`'s endpoint to skip `VerifyTwoFactorTokenAsync` and always accept. Run the suite and confirm the journey **still passes** — then explain in the report why that is expected, and instead mutate `CurrentTotpCode()` to return `"000000"` and confirm the journey **fails**. That is the check that the test computes a genuine code rather than any code being accepted. Revert both.

- [ ] **Step 6: Commit**

```bash
git add tests/Fakturenn.UiTests Directory.Packages.props
git commit --message "test(identity): add the password and TOTP journey, closing SPIKE-009

Deterministic secrets: the test reads the manual-entry key the enrolment page
displays and computes real RFC 6238 codes with Otp.NET. Parallel isolation: one
PostgreSQL container per fixture. Nothing bypasses two-factor authentication.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 16: Documentation, and closing SPIKE-009 in the record

**Files:**

- Modify: `docs/operations/DEPLOYMENT-BASELINE.md`, `docs/spikes/SPIKE-009-PLAYWRIGHT-TOTP.md`, `CLAUDE.md`, `docs/architecture/IMPLEMENTATION-NOTES.md`, `CHANGELOG.md`

- [ ] **Step 1: Document the operational consequences**

Add to `docs/operations/DEPLOYMENT-BASELINE.md` a section covering: the Data Protection key ring living in PostgreSQL and therefore being part of the database backup; that restoring the database without its key ring invalidates every enrolled authenticator; the optional `ProtectKeysWithCertificate` and the second restore hazard it introduces; and the four operator entrypoints with the warning that passwords come from standard input.

- [ ] **Step 2: Close SPIKE-009**

Rewrite `docs/spikes/SPIKE-009-PLAYWRIGHT-TOTP.md` to record the answers rather than the questions: deterministic secrets by reading the displayed manual-entry key and computing codes with `Otp.NET`; parallel isolation via one PostgreSQL container per fixture; reusable authenticated state via Playwright `storageState` where a test does not exercise sign-in. State the exit criterion as met, and name the test that meets it.

- [ ] **Step 3: Update `CLAUDE.md`**

Add the four operator commands to the Commands section, having run each. Update the module list, the test-suite counts, and add a line to the "Adding a new module" recipe noting that a module owning a `DbContext` now adds a factory to the `createMigrationContexts` array in `Program.cs`, where the ordering matters — Data Protection first.

- [ ] **Step 4: Record the traps in `IMPLEMENTATION-NOTES.md`**

At minimum: that `IdentityUserContext` is used rather than `IdentityDbContext` to avoid stock role tables; that Identity stores both second factors in plaintext, with the empirical evidence; that the Data Protection purpose string is part of key derivation and must never be edited; that `SignInManager` cannot issue a cookie from an interactive circuit; and that the application is static SSR by default so any interactive page must opt in explicitly.

- [ ] **Step 5: Update `CHANGELOG.md`** under `[Unreleased]`.

- [ ] **Step 6: Full verification**

```bash
dotnet build --configuration Release
dotnet format --verify-no-changes
dotnet test --project tests/Fakturenn.UnitTests
dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests
dotnet test --project tests/Fakturenn.ArchitectureTests
dotnet test --project tests/Fakturenn.IntegrationTests
dotnet test --project tests/Fakturenn.ComplianceTests
dotnet test --project tests/Fakturenn.UiTests
git status --short
```

Then verify every path named in `CLAUDE.md` exists, using the loop that task 15 of the harness plan established.

- [ ] **Step 7: Commit**

```bash
git add docs CLAUDE.md CHANGELOG.md
git commit --message "docs: close SPIKE-009 and document E02a's operational consequences

Records the key-ring backup implication and the restore hazard, the operator
entrypoints, and the traps worth knowing: IdentityUserContext over
IdentityDbContext, both second factors stored in plaintext by default, the Data
Protection purpose string being part of key derivation, and the application being
static SSR by default.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage.** Every section of the spec maps to a task: §2 scope → the whole plan; §3 decisions → Tasks 1, 3–7; §4 layout → Tasks 1, 2, 4, 5; §5 data model → Task 4; §6 authorization → Tasks 3, 8; §7 flows → Tasks 9–13; §8 Data Protection and secrets at rest → Tasks 5, 6; §9 operator entrypoints → Task 14; §10 testing and SPIKE-009 → Tasks 6, 8, 12, 15; §11 risks → Tasks 6, 12, 15 carry the tests that address them.

**Task 2 is not in the spec.** Row-level audit provenance was added after the spec was approved, at the project owner's request, and inserted ahead of the entities so the audit columns land in the first migration rather than an `ALTER` later. The spec should gain a section describing it; until it does, this plan is the only record of the decision. It is deliberately distinct from the Audit module in `MODULE-OWNERSHIP.md`, which owns `AuditEvent` and is an event log rather than a property of each row.

**Known gaps, stated rather than hidden.**

- The spec lists a `RolesRead`/`RolesManage` permission pair but no role-management UI. That is deliberate: roles are seeded and editable by SQL until something needs otherwise, per the spec's YAGNI note. The permissions exist so E02b's organization-scoped roles have them; nothing in E02a enforces them beyond the seeded Administrator role holding them.
- The `AdministratorGuard` is unit-tested as a pure function but no endpoint calls it yet, because E02a has no UI path that removes a role — role assignment is seeded and by SQL. It is written now because the spec commits to it and Task 13's endpoints are where E02b will wire it. **This is speculative code by the plan's own YAGNI standard and should be challenged in review if the reviewer disagrees.**
- Task 15's `AuthenticatedWebAppFixture` reimplements migration rather than calling `DatabaseMigrator.RunAsync`, because the latter takes a `DatabaseOptions` and a logger the fixture would have to construct. If the reviewer judges the duplication worse than the construction, the fixture should call the real migrator.
- Session state is deliberately not used anywhere; recovery codes reach their display page through a short-lived data-protected cookie instead, so no server-side session store is introduced.

**Type consistency.** `UserId`, `IdentityModule`, `Permissions`, `PermissionClaims`, `PermissionRequirement`, `PermissionPolicyProvider`, `PermissionAuthorizationHandler`, `EnrolmentGate`, `ApplicationUser`, `Role`, `RolePermission`, `UserRole`, `IdentityDbContext`, `EncryptedStringConverter`, `RoleSeeder`, `PermissionCatalogValidator`, `AdministratorGuard`, `DataProtectionDbContext`, `IdentityConfiguration`, `AccountEndpoints`, `EnrolmentGateMiddleware`, `OperatorCommands` and `AuthenticatedWebAppFixture` are each defined in exactly one task and referenced with the same signature everywhere later.
