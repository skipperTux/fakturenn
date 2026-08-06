# Testing and Release Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn a documentation-only repository into a buildable, testable, releasable ASP.NET Core solution that satisfies `SPEC-v0.1.md` §10 structurally and provides the §11 release mechanism.

**Architecture:** Modular monolith on .NET 10. Only the seams the harness needs are scaffolded — a Blazor Interactive Server host, a shared kernel, one exemplar module with a contracts assembly, and one exemplar infrastructure adapter. The six architecture rules from `TEST-STRATEGY.md` are enforced as conventions over assembly-name patterns, so modules added by later epics inherit enforcement without editing the test project.

**Tech Stack:** .NET 10 (`net10.0`), Blazor Interactive Server, MudBlazor, EF Core with Npgsql, Serilog, xUnit v3, AwesomeAssertions, NSubstitute, Testcontainers, Microsoft.Playwright, GitHub Actions, GHCR, `Microsoft.NET.Build.Containers`, `bump-my-version`.

**Spec:** `docs/superpowers/specs/2026-08-06-testing-and-release-harness-design.md`

## Global Constraints

- Target framework is `net10.0` for every project. SDK pinned to `10.0.110` with `rollForward: latestFeature`.
- `TreatWarningsAsErrors` is `true` and `Nullable` is `enable` everywhere. `PLAN-v0.1.md` Definition of Done requires no unresolved compiler or nullable warnings.
- Central Package Management is on. Never write a `Version=` attribute on a `PackageReference`; versions live only in `Directory.Packages.props`.
- Never hand-write a package version number. Always add packages with `dotnet add package <id>` so the resolved version is recorded, then move it to `Directory.Packages.props` if the SDK has not already done so.
- Licence is **AGPL-3.0-or-later**. Do not introduce a dependency under a licence incompatible with it. This is why FluentAssertions v8 is excluded in favour of AwesomeAssertions.
- Product name is **Fakturenn**. Canonical terms, used verbatim: `CatalogItem`, `CatalogItemNumber`, `CustomerCatalogItemNumber`.
- Migrations never run automatically at startup. `DEPLOYMENT-BASELINE.md` requires an explicit migration Job.
- `InvariantGlobalization` must be `false`; German formatting and localization depend on ICU data.
- Commit messages follow Conventional Commits.
- Each task ends green: `dotnet build` clean and `dotnet test` passing before the commit.
- TDD where a test can meaningfully come first; SOLID, KISS and YAGNI throughout. See **Design principles** below.

## Deviations from the spec, decided while planning

One decision in the spec is revised, noted here rather than silently applied:

1. **`NullDocumentStore` is dropped from the fakes set.** `MODULE-OWNERSHIP.md` assigns document-store contracts to the Documents module, which this cycle does not scaffold. Defining that contract here would pre-empt E11. The fakes set is `FakeClock` and `FakeIdGenerator`; the exemplar infrastructure adapter (Task 4) demonstrates the NSubstitute boundary instead.

Architecture tests use **ArchUnitNET** via [`TngTech.ArchUnitNET.xUnitV3`](https://www.nuget.org/packages/TngTech.ArchUnitNET.xUnitV3/0.13.3), which supports xUnit v3 directly — latest 0.13.3. Its type-level dependency analysis is finer-grained than assembly references, and `Slices().Should().BeFreeOfCycles()` handles the cycle rule without hand-rolled graph traversal.

## Design principles

Applied throughout, and restated in `CLAUDE.md` so later epics inherit them:

- **TDD where a test can meaningfully come first.** Write the failing test, make it pass, refactor. Not everything qualifies — a `.csproj` property, a workflow file or a `.resx` entry has no sensible unit test. For those, the plan substitutes an explicit verification step with a command and an expected result. What is never acceptable is writing implementation code that *could* have been driven by a test and skipping the test.
- **SOLID**, in the places it pays: interfaces stay narrow and consumer-defined (`IFileSystem` exposes one method, not a file-system API); infrastructure depends on module-owned abstractions, never the reverse; each type has one reason to change.
- **KISS.** No abstraction without a second caller. No configurability that nothing configures.
- **YAGNI.** Build the harness, not the product. If a type only exists to be useful in E09, it belongs in E09.

## File Structure

```text
CLAUDE.md                                       agent contract (Task 1, completed Task 15)
LICENSE                                         AGPL-3.0-or-later (Task 1)
README.md                                       root readme (Task 1)
CHANGELOG.md                                    Keep a Changelog (Task 1)
global.json                                     SDK pin (Task 2)
Directory.Build.props                           shared MSBuild properties (Task 2)
Directory.Packages.props                        central package versions (Task 2)
.editorconfig                                   C# style (Task 2)
Fakturenn.slnx                                  solution (Task 2)
compose.yaml                                    reference deployment (Task 12)
.bumpversion.toml                               version bumping (Task 14)

src/Fakturenn.SharedKernel/
  Money.cs                                      currency-safe amount (Task 2)
  Percentage.cs                                 tax-rate percentage (Task 2)
  IClock.cs, SystemClock.cs                     time abstraction (Task 3)
  IIdGenerator.cs, GuidV7IdGenerator.cs         id abstraction (Task 3)

src/Fakturenn.Infrastructure.Storage/
  IFileSystem.cs, PhysicalFileSystem.cs         filesystem seam (Task 4)
  FilesystemBlobWriter.cs, StoredBlob.cs        write + SHA-256 (Task 4)

src/Fakturenn.Modules.Invoices.Contracts/
  InvoiceId.cs                                  cross-module surface (Task 5)

src/Fakturenn.Modules.Invoices/
  Persistence/InvoicesDbContext.cs              module-owned context (Task 8)
  Persistence/Migrations/                       module-owned migrations (Task 8)

src/Fakturenn.Web/
  Program.cs                                    entrypoint + --migrate (Task 7, 8)
  FakturennWebApplication.cs                    composable host builder (Task 7)
  Components/                                   Blazor shell (Task 7)
  Resources/SharedResource.resx, .de.resx       localization (Task 7)

tests/Fakturenn.UnitTests/                      Tasks 2, 3, 4
tests/Fakturenn.ArchitectureTests/              Task 6
tests/Fakturenn.IntegrationTests/               Task 9
tests/Fakturenn.ComplianceTests/                Task 10
tests/Fakturenn.UiTests/                        Task 11

.github/workflows/ci.yml                        Task 13
.github/workflows/codeql.yml                    Task 13
.github/dependabot.yml                          Task 13
.github/workflows/release.yml                   Task 14
docs/operations/RELEASE-CHECKLIST-v0.1.md       Task 14
```

---

### Task 1: Repository contract documents

Written first so every later task — and every subagent — reads the same contract. The commands section of `CLAUDE.md` is deliberately marked as unwritten; Task 15 fills it with commands that have actually been run.

**Files:**

- Create: `CLAUDE.md`
- Create: `LICENSE`
- Create: `README.md`
- Create: `CHANGELOG.md`
- Modify: `.gitignore` (append a Playwright block)

**Interfaces:**

- Consumes: nothing
- Produces: `CLAUDE.md`, read by every later task

- [ ] **Step 1: Write `CLAUDE.md`**

````markdown
# CLAUDE.md

Guidance for AI coding agents working in this repository.

## What this is

Fakturenn is an open-source, self-hosted document and invoicing workflow for
service businesses. Outgoing service documents and electronic invoicing are the
primary domain. Licence: **AGPL-3.0-or-later**.

Greenfield. There are no compatibility requirements with any existing
application, database, API, or user interface. If a change would be simpler
without backwards compatibility, take the simpler path.

## Documentation is the source of truth

Read in this order before making design decisions:

1. `docs/SPEC-v0.1.md` — scope, architecture, testing, release target
2. `docs/planning/PLAN-v0.1.md` — milestones, epics, Definition of Ready/Done
3. `docs/domain/DOMAIN-MODEL-v0.1.md`
4. `docs/architecture/MODULE-OWNERSHIP.md` — which module owns which entity
5. `docs/planning/WALKING-SKELETON.md` — the fixed end-to-end example
6. `docs/architecture/adr/` — ADR-001 through ADR-010
7. `docs/testing/TEST-STRATEGY.md`
8. `docs/security/SECURITY-BASELINE.md`
9. `docs/operations/DEPLOYMENT-BASELINE.md`

If the code and the documentation disagree, **say so and stop**. Do not
silently pick one. The disagreement is the finding.

## Canonical terminology

Use these exact terms. Do not introduce synonyms.

- Product: **Fakturenn**
- Service/product master data: **CatalogItem**
- Internal number: **CatalogItemNumber**
- Customer-specific item reference: **CustomerCatalogItemNumber**

## Architecture invariants

- Modular monolith with vertical slices (ADR-001, ADR-002).
- A module owns its write model **and its migrations**.
- A module must not reference another module's EF entities. Cross-module
  communication uses contracts, commands, events, identifiers, or read models.
- Dependency direction: UI → feature slices → module domain/contracts →
  infrastructure implementations. Infrastructure implements module-owned
  interfaces; modules never reference infrastructure.
- No document binary data in PostgreSQL. PostgreSQL stores metadata, hashes,
  relations, retention data, and storage keys.
- Migrations never run automatically at startup. Use the explicit
  `--migrate` entrypoint.

## Machine-enforced architecture rules

These are enforced by `tests/Fakturenn.ArchitectureTests`. Breaking one fails
the build, not the review:

1. Only `Fakturenn.Web` may reference MudBlazor.
2. Only `Fakturenn.Infrastructure.Mail*` may reference MimeKit or MailKit.
3. Only `Fakturenn.Infrastructure.Documents*` may reference PDFsharp or MigraDoc.
4. No `Fakturenn.Modules.*` assembly may reference any `Fakturenn.Infrastructure.*`
   assembly. This is what keeps E-Invoice-EU adapter types out of the domain.
5. `Fakturenn.Modules.X` must not reference `Fakturenn.Modules.Y`. It may
   reference `Fakturenn.Modules.Y.Contracts`.
6. No dependency cycles between `Fakturenn.Modules.*` assemblies.

Rules 2 and 3 name assemblies that do not exist yet. They are vacuously true
today and become binding the moment those assemblies appear. Do not delete a
rule because it currently matches nothing.

## Design principles

- **TDD where a test can meaningfully come first.** Write the failing test, see
  it fail for the right reason, make it pass, refactor. Not everything
  qualifies: a `.csproj` property, a CI workflow or a `.resx` entry has no
  sensible unit test — verify those with an explicit command and a stated
  expected result instead. What is never acceptable is writing code that
  *could* have been driven by a test and skipping the test.
- **SOLID**, where it pays. Interfaces are narrow and defined by the consumer,
  not the implementer. Infrastructure depends on module-owned abstractions,
  never the reverse. A type should have one reason to change.
- **KISS.** No abstraction without a second caller. No configurability that
  nothing configures. No interface with one implementation and no test double.
- **YAGNI.** Build what the current epic needs. If a type only becomes useful
  in a later epic, it belongs in that epic. Deleting speculative code later
  costs more than adding it when it is actually needed.

These are aids, not rules to satisfy for their own sake. If applying one makes
the code harder to read, say so rather than contorting the design around it.

## Testing

Test approach per `SPEC-v0.1.md` §10, in strict order of preference:

1. Real domain objects.
2. Fakes and nullables (`tests/Fakturenn.UnitTests/Fakes/`).
3. NSubstitute — **only** where collaborator interaction is the behaviour under
   test, e.g. "the signer was called", "SMTP was not called after a validation
   failure". Never as a shortcut for constructing a real object.

Testcontainers for real infrastructure. Playwright for critical journeys.

## Commands

Filled in at the end of the harness cycle, after each command has been run.

## Definition of Done

From `docs/planning/PLAN-v0.1.md`. Check before opening a pull request:

- [ ] Functional acceptance criteria pass
- [ ] Unit, integration, architecture, compliance and applicable Playwright tests pass
- [ ] No unresolved compiler or nullable warnings
- [ ] Authorization and organization isolation are tested
- [ ] Retries are idempotent where applicable
- [ ] Migrations work from clean and previous states
- [ ] English and German resources are complete
- [ ] Compose remains runnable
- [ ] Kubernetes compatibility is not broken
- [ ] Security and backup implications are documented
- [ ] Human-test instructions are included

## Non-goals — do not add these

From `docs/SPEC-v0.1.md` §3. Do not implement them, and do not add
dependencies that only make sense for them:

general ledger; double-entry bookkeeping; tax declarations; payroll;
inventory; physical-goods workflows; supplier invoice processing; bank
synchronization and reconciliation; public multi-tenant SaaS; Peppol
transport; arbitrary executable templates.

## Project-specific style

The global style rules apply. Only the deltas are listed here:

- Central Package Management is on. Never put a `Version=` attribute on a
  `PackageReference`; add versions to `Directory.Packages.props`.
- Never hand-write a package version. Use `dotnet add package <id>`.
- Public API surface stays minimal — `internal` by default, `public` only where
  another assembly genuinely consumes it.
- Prefer `readonly record struct` for value objects.
- Every new module assembly is named `Fakturenn.Modules.<Name>`, and its
  cross-module surface `Fakturenn.Modules.<Name>.Contracts`. The architecture
  tests match on these names.
````

- [ ] **Step 2: Write `LICENSE`**

Run: `curl --location --silent --output LICENSE https://www.gnu.org/licenses/agpl-3.0.txt`

Expected: a file of roughly 34 KB whose first non-blank line is the centred
title `GNU AFFERO GENERAL PUBLIC LICENSE`.

If the machine is offline, copy the AGPL-3.0 text from a local source instead. Do not write a summary or a stub — an inaccurate licence file is worse than none.

- [ ] **Step 3: Write `README.md`**

```markdown
# Fakturenn

Open-source, self-hosted document and invoicing workflow for service
businesses.

> Your invoices, your identity, your infrastructure.

Fakturenn gives service businesses control over their customer, project and
document data, their own SMTP and IMAP infrastructure, their S/MIME and
OpenPGP signing identities, their document storage, their identity management,
and their deployment, backup and export.

## Status

**Pre-alpha.** The v0.1 milestone is a runnable alpha intended for structured
human testing. It is not a production-readiness declaration. See
[docs/SPEC-v0.1.md](docs/SPEC-v0.1.md).

## Scope

Quotations, orders, order confirmations, invoices, invoice corrections and
payment reminders. Multiple organizations, customers with multiple addresses
and contacts, projects, a service catalog, time import through a provider
abstraction, PDF and timesheet rendering, ZUGFeRD/Factur-X and XRechnung,
SMTP with optional IMAP, S/MIME and OpenPGP signing, an immutable archive,
filesystem or S3-compatible storage, local identity with TOTP and optional
generic OIDC, English and German.

Not in scope: general ledger, double-entry bookkeeping, tax declarations,
payroll, inventory, physical-goods workflows, supplier invoice processing,
bank reconciliation, public multi-tenant SaaS, Peppol transport.

## Installation

Requires Docker and Docker Compose.

    docker compose up

The application listens on http://localhost:8080.

## Development

Requires the .NET 10 SDK.

    dotnet build
    dotnet test

## Documentation

Start at [docs/README.md](docs/README.md).

## Contributing

Read [CLAUDE.md](CLAUDE.md) for the architecture invariants and the Definition
of Done, then [docs/planning/PLAN-v0.1.md](docs/planning/PLAN-v0.1.md).

## Licence

AGPL-3.0-or-later. See [LICENSE](LICENSE).
```

- [ ] **Step 4: Write `CHANGELOG.md`**

```markdown
# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Repository contract documents: `CLAUDE.md`, `README.md`, `LICENSE`, `CHANGELOG.md`.
```

- [ ] **Step 5: Add the Playwright ignores via the template source, not `.gitignore`**

`.gitignore` is **generated**. `gitignore_templates.json` lists upstream GitHub
templates plus `Project.gitignore` as the final `Project` block, and `dotgit`
concatenates them. Editing `.gitignore` directly is lost on the next
regeneration.

.NET build output is already covered by the upstream `VisualStudio.gitignore`
template. Only Playwright needs adding.

Insert into `Project.gitignore`, keeping the existing alphabetical section
order — between the `# Defensive secret catch-alls` block and `# Portainer`:

```gitignore
# Playwright
playwright-report/
playwright/.cache/
.playwright/

```

Then regenerate:

```bash
dotgit gitignore --config gitignore_templates.json --output .gitignore --working-dir .
```

This fetches the upstream templates over the network. If the machine is
offline, leave `.gitignore` untouched and commit only `Project.gitignore`; the
next online regeneration picks the block up.

- [ ] **Step 5a: Verify the regenerated file kept everything**

```bash
git diff --stat .gitignore
grep --fixed-strings 'playwright-report/' .gitignore
grep --fixed-strings '.decrypted~*' .gitignore
```

Expected: the diff adds the Playwright lines only, `playwright-report/` is
present, and the pre-existing SOPS entry survived. A large diff means an
upstream template changed — review it before committing rather than accepting
it silently.

- [ ] **Step 6: Verify the licence file is the real text**

Run: `head --lines=1 LICENSE && wc --lines LICENSE`
Expected: first line contains `GNU AFFERO GENERAL PUBLIC LICENSE`, and at least 600 lines.

- [ ] **Step 7: Commit**

```bash
git add CLAUDE.md LICENSE README.md CHANGELOG.md .gitignore
git commit --message "docs: add repository contract documents

CLAUDE.md states the architecture invariants, the six machine-enforced
architecture rules, canonical terminology and the Definition of Done. The
commands section is left unwritten until the harness cycle ends, so it can
only ever contain commands that have actually been run.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Solution scaffold, shared kernel, first real unit tests

**Files:**

- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `Fakturenn.slnx`
- Create: `src/Fakturenn.SharedKernel/Fakturenn.SharedKernel.csproj`, `src/Fakturenn.SharedKernel/Money.cs`, `src/Fakturenn.SharedKernel/Percentage.cs`
- Create: `tests/Fakturenn.UnitTests/Fakturenn.UnitTests.csproj`, `tests/Fakturenn.UnitTests/SharedKernel/MoneyTests.cs`, `tests/Fakturenn.UnitTests/SharedKernel/PercentageTests.cs`

**Interfaces:**

- Consumes: nothing
- Produces:
  - `Fakturenn.SharedKernel.Money` — `readonly record struct`, ctor `Money(decimal amount, string currency)`, properties `decimal Amount`, `string Currency`, `static Money operator +(Money, Money)`, `Money Round()`
  - `Fakturenn.SharedKernel.Percentage` — `readonly record struct`, ctor `Percentage(decimal value)`, property `decimal Value`, method `Money Of(Money money)`

- [ ] **Step 1: Create the SDK pin**

`global.json`:

```json
{
  "sdk": {
    "version": "10.0.110",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 2: Create shared MSBuild properties**

`Directory.Build.props`:

```xml
<Project>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <!-- German formatting and localization need ICU data. -->
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>

  <PropertyGroup Label="Product">
    <Product>Fakturenn</Product>
    <Version>0.1.0-alpha.1</Version>
    <Authors>Fakturenn contributors</Authors>
    <PackageLicenseExpression>AGPL-3.0-or-later</PackageLicenseExpression>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Create central package management**

`Directory.Packages.props`:

```xml
<Project>

  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup />

</Project>
```

- [ ] **Step 4: Create `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
indent_style = space
insert_final_newline = true
trim_trailing_whitespace = true

[*.{cs,csproj,props,targets,slnx}]
indent_size = 4

[*.{json,yml,yaml,md,razor,resx,xml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false

[*.cs]
csharp_using_directive_placement = outside_namespace:error
csharp_style_namespace_declarations = file_scoped:error
csharp_prefer_braces = true:error
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
dotnet_style_require_accessibility_modifiers = for_non_interface_members:error
dotnet_style_readonly_field = true:error
dotnet_diagnostic.IDE0055.severity = error

dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel_case
dotnet_naming_rule.private_fields_underscore.severity = error
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel_case.required_prefix = _
dotnet_naming_style.underscore_camel_case.capitalization = camel_case
```

- [ ] **Step 5: Create the solution and both projects**

```bash
dotnet new sln --name Fakturenn --format slnx
dotnet new classlib --output src/Fakturenn.SharedKernel --name Fakturenn.SharedKernel
dotnet new xunit3 --output tests/Fakturenn.UnitTests --name Fakturenn.UnitTests
rm --force src/Fakturenn.SharedKernel/Class1.cs tests/Fakturenn.UnitTests/UnitTest1.cs
dotnet sln Fakturenn.slnx add src/Fakturenn.SharedKernel/Fakturenn.SharedKernel.csproj
dotnet sln Fakturenn.slnx add tests/Fakturenn.UnitTests/Fakturenn.UnitTests.csproj
dotnet add tests/Fakturenn.UnitTests reference src/Fakturenn.SharedKernel
dotnet add tests/Fakturenn.UnitTests package AwesomeAssertions
```

If `dotnet new sln --format slnx` is rejected by this SDK, run `dotnet new sln --name Fakturenn` followed by `dotnet sln migrate`, then delete the generated `Fakturenn.sln`.

If `dotnet new xunit3` is not installed, run `dotnet new install xunit.v3.templates` first.

- [ ] **Step 6: Configure the test project for Microsoft.Testing.Platform**

Ensure `tests/Fakturenn.UnitTests/Fakturenn.UnitTests.csproj` contains these properties, adding any that the template omitted:

```xml
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
  </PropertyGroup>
```

- [ ] **Step 7: Write the failing tests**

`tests/Fakturenn.UnitTests/SharedKernel/MoneyTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.SharedKernel;

public sealed class MoneyTests
{
    [Fact]
    public void Adding_two_amounts_in_the_same_currency_sums_them()
    {
        var sum = new Money(800.00m, "EUR") + new Money(152.00m, "EUR");

        sum.Should().Be(new Money(952.00m, "EUR"));
    }

    [Fact]
    public void Adding_two_amounts_in_different_currencies_throws()
    {
        var add = () => new Money(1m, "EUR") + new Money(1m, "CHF");

        add.Should().Throw<InvalidOperationException>()
            .WithMessage("*EUR*CHF*");
    }

    [Fact]
    public void Rounding_uses_commercial_rounding_away_from_zero()
    {
        new Money(0.125m, "EUR").Round().Amount.Should().Be(0.13m);
        new Money(-0.125m, "EUR").Round().Amount.Should().Be(-0.13m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void A_currency_that_is_not_three_uppercase_letters_is_rejected(string currency)
    {
        var create = () => new Money(1m, currency);

        create.Should().Throw<ArgumentException>();
    }
}
```

`tests/Fakturenn.UnitTests/SharedKernel/PercentageTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.SharedKernel;

public sealed class PercentageTests
{
    [Fact]
    public void Nineteen_percent_of_the_walking_skeleton_net_amount_is_the_documented_tax()
    {
        // docs/planning/WALKING-SKELETON.md: net 800.00, VAT 19%, VAT 152.00.
        var tax = new Percentage(19m).Of(new Money(800.00m, "EUR"));

        tax.Should().Be(new Money(152.00m, "EUR"));
    }

    [Fact]
    public void The_result_keeps_the_currency_of_the_base_amount()
    {
        new Percentage(19m).Of(new Money(100m, "CHF")).Currency.Should().Be("CHF");
    }

    [Fact]
    public void The_result_is_rounded_to_two_decimal_places()
    {
        new Percentage(19m).Of(new Money(0.99m, "EUR")).Amount.Should().Be(0.19m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void A_percentage_outside_zero_to_one_hundred_is_rejected(decimal value)
    {
        var create = () => new Percentage(value);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 8: Run the tests to verify they fail**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: build failure — `The type or namespace name 'Money' could not be found`.

- [ ] **Step 9: Implement `Money`**

`src/Fakturenn.SharedKernel/Money.cs`:

```csharp
namespace Fakturenn.SharedKernel;

/// <summary>An amount bound to an ISO 4217 currency code.</summary>
public readonly record struct Money
{
    // public Constructors
    public Money(decimal amount, string currency)
    {
        if (!IsIso4217Code(currency))
        {
            throw new ArgumentException(
                $"'{currency}' is not a three-letter uppercase ISO 4217 currency code.",
                nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    // public Properties
    public decimal Amount { get; }

    public string Currency { get; }

    // public static Methods
    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot add {left.Currency} to {right.Currency}.");
        }

        return new Money(left.Amount + right.Amount, left.Currency);
    }

    // public Methods
    /// <summary>
    /// Rounds to two decimal places away from zero. Commercial rounding is used
    /// rather than banker's rounding because invoice totals must match what a
    /// human arrives at with the same figures.
    /// </summary>
    public Money Round() =>
        new(Math.Round(Amount, 2, MidpointRounding.AwayFromZero), Currency);

    public override string ToString() => $"{Amount} {Currency}";

    // private static Methods
    private static bool IsIso4217Code(string currency)
    {
        if (currency is not { Length: 3 })
        {
            return false;
        }

        foreach (char character in currency)
        {
            if (character is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
```

- [ ] **Step 10: Implement `Percentage`**

`src/Fakturenn.SharedKernel/Percentage.cs`:

```csharp
namespace Fakturenn.SharedKernel;

/// <summary>A percentage expressed in whole percent, so 19 means 19%.</summary>
public readonly record struct Percentage
{
    // public Constructors
    public Percentage(decimal value)
    {
        if (value is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "A percentage must be between 0 and 100.");
        }

        Value = value;
    }

    // public Properties
    public decimal Value { get; }

    // public Methods
    public Money Of(Money money) =>
        new Money(money.Amount * Value / 100m, money.Currency).Round();

    public override string ToString() => $"{Value}%";
}
```

- [ ] **Step 11: Run the tests to verify they pass**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: PASS, 11 tests.

- [ ] **Step 12: Verify the build is warning-free**

Run: `dotnet build --configuration Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 13: Commit**

```bash
git add global.json Directory.Build.props Directory.Packages.props .editorconfig Fakturenn.slnx src tests
git commit --message "feat: add solution scaffold and shared kernel value objects

Pins the SDK to 10.0.110, enables central package management, and turns on
warnings-as-errors so the Definition of Done requirement for zero compiler
and nullable warnings is enforced by the build rather than by review.

Money and Percentage are tested against the figures in WALKING-SKELETON.md:
800.00 EUR net at 19% is 152.00 EUR tax, totalling 952.00 EUR.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Clock and id abstractions with their fakes

`SPEC-v0.1.md` §10 ranks fakes and nullables above mocks. This task establishes the `Fakes/` folder that later epics copy from.

**Files:**

- Create: `src/Fakturenn.SharedKernel/IClock.cs`, `src/Fakturenn.SharedKernel/SystemClock.cs`, `src/Fakturenn.SharedKernel/IIdGenerator.cs`, `src/Fakturenn.SharedKernel/GuidV7IdGenerator.cs`
- Create: `tests/Fakturenn.UnitTests/Fakes/FakeClock.cs`, `tests/Fakturenn.UnitTests/Fakes/FakeIdGenerator.cs`
- Create: `tests/Fakturenn.UnitTests/Fakes/FakeClockTests.cs`, `tests/Fakturenn.UnitTests/Fakes/FakeIdGeneratorTests.cs`

**Interfaces:**

- Consumes: nothing from earlier tasks
- Produces:
  - `Fakturenn.SharedKernel.IClock` — `DateTimeOffset UtcNow { get; }`
  - `Fakturenn.SharedKernel.SystemClock` — `sealed`, implements `IClock`
  - `Fakturenn.SharedKernel.IIdGenerator` — `Guid NewId()`
  - `Fakturenn.SharedKernel.GuidV7IdGenerator` — `sealed`, implements `IIdGenerator`
  - `Fakturenn.UnitTests.Fakes.FakeClock` — ctor `FakeClock(DateTimeOffset now)`, settable `UtcNow`, `void Advance(TimeSpan by)`
  - `Fakturenn.UnitTests.Fakes.FakeIdGenerator` — ctor `FakeIdGenerator(params Guid[] ids)`, `Guid NewId()`

- [ ] **Step 1: Write the failing tests**

`tests/Fakturenn.UnitTests/Fakes/FakeClockTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.SharedKernel;
using Fakturenn.UnitTests.Fakes;

namespace Fakturenn.UnitTests.FakeTests;

public sealed class FakeClockTests
{
    [Fact]
    public void The_fake_clock_returns_the_time_it_was_given()
    {
        var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        IClock clock = new FakeClock(now);

        clock.UtcNow.Should().Be(now);
    }

    [Fact]
    public void Advancing_the_fake_clock_moves_time_forward()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));

        clock.Advance(TimeSpan.FromDays(30));

        clock.UtcNow.Should().Be(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
    }
}
```

`tests/Fakturenn.UnitTests/Fakes/FakeIdGeneratorTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.SharedKernel;
using Fakturenn.UnitTests.Fakes;

namespace Fakturenn.UnitTests.FakeTests;

public sealed class FakeIdGeneratorTests
{
    [Fact]
    public void The_fake_generator_hands_out_the_ids_it_was_given_in_order()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        IIdGenerator generator = new FakeIdGenerator(first, second);

        generator.NewId().Should().Be(first);
        generator.NewId().Should().Be(second);
    }

    [Fact]
    public void Asking_for_more_ids_than_were_supplied_throws_rather_than_repeating()
    {
        IIdGenerator generator = new FakeIdGenerator(Guid.Empty);
        generator.NewId();

        var next = () => generator.NewId();

        next.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_real_generator_produces_distinct_sortable_version_seven_ids()
    {
        IIdGenerator generator = new GuidV7IdGenerator();

        Guid[] ids = [generator.NewId(), generator.NewId(), generator.NewId()];

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeInAscendingOrder();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: build failure — `The type or namespace name 'IClock' could not be found`.

- [ ] **Step 3: Implement the abstractions**

`src/Fakturenn.SharedKernel/IClock.cs`:

```csharp
namespace Fakturenn.SharedKernel;

/// <summary>
/// Supplies the current instant. Everything that needs the time takes this,
/// so tests can drive due dates and reminder levels deterministically.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

`src/Fakturenn.SharedKernel/SystemClock.cs`:

```csharp
namespace Fakturenn.SharedKernel;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

`src/Fakturenn.SharedKernel/IIdGenerator.cs`:

```csharp
namespace Fakturenn.SharedKernel;

public interface IIdGenerator
{
    Guid NewId();
}
```

`src/Fakturenn.SharedKernel/GuidV7IdGenerator.cs`:

```csharp
namespace Fakturenn.SharedKernel;

/// <summary>
/// Produces UUID version 7 values, which sort by creation time. Random v4 keys
/// fragment PostgreSQL B-tree indexes; time-ordered keys do not.
/// </summary>
public sealed class GuidV7IdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}
```

- [ ] **Step 4: Implement the fakes**

`tests/Fakturenn.UnitTests/Fakes/FakeClock.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.Fakes;

/// <summary>
/// A controllable clock. Prefer this over mocking <see cref="IClock"/>: the time
/// a test needs is data, not an interaction worth asserting.
/// </summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
```

`tests/Fakturenn.UnitTests/Fakes/FakeIdGenerator.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.Fakes;

/// <summary>
/// Hands out a fixed sequence of ids and throws when exhausted, so a test that
/// silently starts allocating more ids than it declared fails instead of drifting.
/// </summary>
public sealed class FakeIdGenerator(params Guid[] ids) : IIdGenerator
{
    private readonly Queue<Guid> _remaining = new(ids);

    public Guid NewId() =>
        _remaining.Count > 0
            ? _remaining.Dequeue()
            : throw new InvalidOperationException(
                $"FakeIdGenerator was primed with {ids.Length} id(s) and has run out.");
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: PASS, 16 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Fakturenn.SharedKernel tests/Fakturenn.UnitTests
git commit --message "feat: add clock and id abstractions with their fakes

Establishes the Fakes/ folder that later epics copy from. SPEC-v0.1.md
section 10 ranks fakes above mocks, so the clock and the id generator are
faked, not substituted.

GuidV7IdGenerator uses UUID v7 because random v4 keys fragment PostgreSQL
B-tree indexes.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Exemplar infrastructure adapter and the NSubstitute boundary

Demonstrates where NSubstitute is legitimate: the behaviour under test is *that a collaborator was called*, which no fake can assert as directly. The adapter also produces the SHA-256 hash that `WALKING-SKELETON.md` requires of every artifact.

**Files:**

- Create: `src/Fakturenn.Infrastructure.Storage/Fakturenn.Infrastructure.Storage.csproj`, `IFileSystem.cs`, `PhysicalFileSystem.cs`, `StoredBlob.cs`, `FilesystemBlobWriter.cs`
- Create: `tests/Fakturenn.UnitTests/Infrastructure/FilesystemBlobWriterTests.cs`

**Interfaces:**

- Consumes: nothing from earlier tasks
- Produces:
  - `Fakturenn.Infrastructure.Storage.IFileSystem` — `Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)`
  - `Fakturenn.Infrastructure.Storage.PhysicalFileSystem` — `sealed`, implements `IFileSystem`
  - `Fakturenn.Infrastructure.Storage.StoredBlob` — `sealed record StoredBlob(string Path, string Sha256, int SizeInBytes)`
  - `Fakturenn.Infrastructure.Storage.FilesystemBlobWriter` — ctor `FilesystemBlobWriter(IFileSystem fileSystem, string rootPath)`, method `Task<StoredBlob> WriteAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)`

- [ ] **Step 1: Create the project and wire it up**

```bash
dotnet new classlib --output src/Fakturenn.Infrastructure.Storage --name Fakturenn.Infrastructure.Storage
rm --force src/Fakturenn.Infrastructure.Storage/Class1.cs
dotnet sln Fakturenn.slnx add src/Fakturenn.Infrastructure.Storage/Fakturenn.Infrastructure.Storage.csproj
dotnet add tests/Fakturenn.UnitTests reference src/Fakturenn.Infrastructure.Storage
dotnet add tests/Fakturenn.UnitTests package NSubstitute
```

- [ ] **Step 2: Write the failing tests**

`tests/Fakturenn.UnitTests/Infrastructure/FilesystemBlobWriterTests.cs`:

```csharp
using System.Text;
using AwesomeAssertions;
using Fakturenn.Infrastructure.Storage;
using NSubstitute;

namespace Fakturenn.UnitTests.Infrastructure;

public sealed class FilesystemBlobWriterTests
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("fakturenn");

    // "fakturenn" hashed with SHA-256, verified independently with:
    //   printf 'fakturenn' | sha256sum
    private const string ExpectedHash =
        "0f6f0b0e50e33b0dcbd0e2ec8a7cf3b0c2cbcd6c5aa5eb9bd2b3d95c07d8b4c1";

    [Fact]
    public async Task Writing_a_blob_reports_its_sha256_hash()
    {
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        StoredBlob blob = await writer.WriteAsync("invoices/invoice.pdf", Content, TestContext.Current.CancellationToken);

        blob.Sha256.Should().Be(ExpectedHash);
        blob.SizeInBytes.Should().Be(Content.Length);
    }

    [Fact]
    public async Task Writing_a_blob_places_it_under_the_configured_root()
    {
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        StoredBlob blob = await writer.WriteAsync("invoices/invoice.pdf", Content, TestContext.Current.CancellationToken);

        blob.Path.Should().Be(Path.Combine("/srv/fakturenn", "invoices/invoice.pdf"));
    }

    [Fact]
    public async Task The_underlying_file_system_is_written_to_exactly_once()
    {
        // Interaction is the behaviour under test here, which is what NSubstitute
        // is for. A fake could record the call, but not assert "exactly once"
        // as the contract itself.
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        var writer = new FilesystemBlobWriter(fileSystem, "/srv/fakturenn");

        await writer.WriteAsync("invoices/invoice.pdf", Content, TestContext.Current.CancellationToken);

        await fileSystem.Received(1).WriteAsync(
            Path.Combine("/srv/fakturenn", "invoices/invoice.pdf"),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_absolute_relative_path_is_rejected_so_a_blob_cannot_escape_the_root()
    {
        var writer = new FilesystemBlobWriter(Substitute.For<IFileSystem>(), "/srv/fakturenn");

        Func<Task> write = () => writer.WriteAsync("../../etc/passwd", Content, TestContext.Current.CancellationToken);

        await write.Should().ThrowAsync<ArgumentException>();
    }
}
```

Note on the hash constant: the value above is a placeholder for the real digest and **must be replaced**. Before running the tests, compute it with `printf 'fakturenn' | sha256sum` and paste the actual value. Do not adjust the implementation to match a wrong constant.

- [ ] **Step 3: Replace the hash constant with the real digest**

Run: `printf 'fakturenn' | sha256sum`
Copy the 64-character hex value into `ExpectedHash`.

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: build failure — `The type or namespace name 'FilesystemBlobWriter' could not be found`.

- [ ] **Step 5: Implement the storage types**

`src/Fakturenn.Infrastructure.Storage/IFileSystem.cs`:

```csharp
namespace Fakturenn.Infrastructure.Storage;

/// <summary>
/// The narrow slice of the file system this adapter needs. Exists so writing
/// logic can be tested without touching a disk.
/// </summary>
public interface IFileSystem
{
    Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
```

`src/Fakturenn.Infrastructure.Storage/PhysicalFileSystem.cs`:

```csharp
namespace Fakturenn.Infrastructure.Storage;

public sealed class PhysicalFileSystem : IFileSystem
{
    public async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, content, cancellationToken);
    }
}
```

`src/Fakturenn.Infrastructure.Storage/StoredBlob.cs`:

```csharp
namespace Fakturenn.Infrastructure.Storage;

/// <summary>
/// The record of a stored artifact. WALKING-SKELETON.md requires a SHA-256 hash
/// for every artifact, so the hash is part of the write result rather than
/// something a caller has to remember to compute.
/// </summary>
public sealed record StoredBlob(string Path, string Sha256, int SizeInBytes);
```

`src/Fakturenn.Infrastructure.Storage/FilesystemBlobWriter.cs`:

```csharp
using System.Security.Cryptography;

namespace Fakturenn.Infrastructure.Storage;

public sealed class FilesystemBlobWriter(IFileSystem fileSystem, string rootPath)
{
    public async Task<StoredBlob> WriteAsync(
        string relativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        string fullPath = ResolveWithinRoot(relativePath);
        string hash = Convert.ToHexStringLower(SHA256.HashData(content.Span));

        await fileSystem.WriteAsync(fullPath, content, cancellationToken);

        return new StoredBlob(fullPath, hash, content.Length);
    }

    private string ResolveWithinRoot(string relativePath)
    {
        string combined = Path.Combine(rootPath, relativePath);
        string normalizedRoot = Path.GetFullPath(rootPath);

        if (!Path.GetFullPath(combined).StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{relativePath}' resolves outside the storage root.", nameof(relativePath));
        }

        return combined;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: PASS, 20 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Fakturenn.Infrastructure.Storage tests/Fakturenn.UnitTests
git commit --message "feat: add filesystem blob writer as the exemplar infrastructure adapter

Shows where NSubstitute is legitimate: 'the file system was written to exactly
once' is an interaction contract, not state a fake can assert as directly.

Every write returns a SHA-256 hash, which WALKING-SKELETON.md requires of all
artifacts. Paths that resolve outside the storage root are rejected.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Module seam projects

Creates the assemblies whose *names* the architecture rules in Task 6 match on. Deliberately near-empty — the module gains real content in E09.

**Files:**

- Create: `src/Fakturenn.Modules.Invoices.Contracts/Fakturenn.Modules.Invoices.Contracts.csproj`, `src/Fakturenn.Modules.Invoices.Contracts/InvoiceId.cs`
- Create: `src/Fakturenn.Modules.Invoices/Fakturenn.Modules.Invoices.csproj`, `src/Fakturenn.Modules.Invoices/InvoicesModule.cs`
- Create: `tests/Fakturenn.UnitTests/Modules/InvoiceIdTests.cs`

**Interfaces:**

- Consumes: `Fakturenn.SharedKernel` (Task 2, 3)
- Produces:
  - `Fakturenn.Modules.Invoices.Contracts.InvoiceId` — `readonly record struct`, ctor `InvoiceId(Guid value)`, property `Guid Value`, `static InvoiceId New(IIdGenerator generator)`
  - `Fakturenn.Modules.Invoices.InvoicesModule` — `static class`, assembly marker consumed by Task 6

- [ ] **Step 1: Create both projects**

```bash
dotnet new classlib --output src/Fakturenn.Modules.Invoices.Contracts --name Fakturenn.Modules.Invoices.Contracts
dotnet new classlib --output src/Fakturenn.Modules.Invoices --name Fakturenn.Modules.Invoices
rm --force src/Fakturenn.Modules.Invoices.Contracts/Class1.cs src/Fakturenn.Modules.Invoices/Class1.cs
dotnet sln Fakturenn.slnx add src/Fakturenn.Modules.Invoices.Contracts/Fakturenn.Modules.Invoices.Contracts.csproj
dotnet sln Fakturenn.slnx add src/Fakturenn.Modules.Invoices/Fakturenn.Modules.Invoices.csproj
dotnet add src/Fakturenn.Modules.Invoices.Contracts reference src/Fakturenn.SharedKernel
dotnet add src/Fakturenn.Modules.Invoices reference src/Fakturenn.Modules.Invoices.Contracts
dotnet add src/Fakturenn.Modules.Invoices reference src/Fakturenn.SharedKernel
dotnet add tests/Fakturenn.UnitTests reference src/Fakturenn.Modules.Invoices
```

- [ ] **Step 1a: Add the assembly marker**

`src/Fakturenn.Modules.Invoices/InvoicesModule.cs`:

```csharp
namespace Fakturenn.Modules.Invoices;

/// <summary>
/// Assembly marker. Gives the architecture tests and, later, dependency
/// injection a stable public handle on this assembly without exporting a type
/// that exists for no other reason.
/// </summary>
public static class InvoicesModule;
```

- [ ] **Step 2: Write the failing test**

`tests/Fakturenn.UnitTests/Modules/InvoiceIdTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Contracts;
using Fakturenn.UnitTests.Fakes;

namespace Fakturenn.UnitTests.Modules;

public sealed class InvoiceIdTests
{
    [Fact]
    public void A_new_invoice_id_takes_its_value_from_the_id_generator()
    {
        var expected = Guid.Parse("0198f3a0-0000-7000-8000-000000000001");

        InvoiceId id = InvoiceId.New(new FakeIdGenerator(expected));

        id.Value.Should().Be(expected);
    }

    [Fact]
    public void An_empty_invoice_id_is_rejected()
    {
        var create = () => new InvoiceId(Guid.Empty);

        create.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: build failure — `The type or namespace name 'InvoiceId' could not be found`.

- [ ] **Step 4: Implement `InvoiceId`**

`src/Fakturenn.Modules.Invoices.Contracts/InvoiceId.cs`:

```csharp
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Contracts;

/// <summary>
/// The Invoices module's identifier as other modules see it. Cross-module
/// references use this, never the module's EF entity.
/// </summary>
public readonly record struct InvoiceId
{
    // public Constructors
    public InvoiceId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An invoice id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    // public Properties
    public Guid Value { get; }

    // public static Methods
    public static InvoiceId New(IIdGenerator generator) => new(generator.NewId());

    // public Methods
    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Fakturenn.UnitTests`
Expected: PASS, 22 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Fakturenn.Modules.Invoices src/Fakturenn.Modules.Invoices.Contracts tests/Fakturenn.UnitTests
git commit --message "feat: add the Invoices module seam and its contracts assembly

These assemblies exist mainly so the architecture rules in the next task have
real names to match on. InvoiceId is the module's cross-module surface;
MODULE-OWNERSHIP.md forbids other modules touching the EF entity.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---
### Task 6: Architecture tests with ArchUnitNET

The six rules from `TEST-STRATEGY.md`, expressed as conventions over assembly-name patterns so modules added by later epics inherit enforcement without editing this project. Rules are proven to bite in Step 8 by introducing a real violation and watching the suite fail — a rule that has never been seen to fail is not evidence of anything.

TDD applies loosely here: the rules are written before they can pass in one case (Step 8's deliberate violation), but the primary failing-first signal is the compile error, since the rule *is* the assertion.

**Files:**

- Create: `tests/Fakturenn.ArchitectureTests/Fakturenn.ArchitectureTests.csproj`
- Create: `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs`
- Create: `tests/Fakturenn.ArchitectureTests/ModuleBoundaryTests.cs`
- Create: `tests/Fakturenn.ArchitectureTests/TechnologyContainmentTests.cs`

**Interfaces:**

- Consumes: every `src/` assembly, by project reference
- Produces:
  - `FakturennArchitecture.Loaded` — `static readonly ArchUnitNET.Domain.Architecture`
  - `FakturennArchitecture.Modules` — `static readonly IObjectProvider<IType>`, matching `Fakturenn.Modules.*` including `.Contracts`
  - `FakturennArchitecture.ModuleImplementations` — `static readonly IObjectProvider<IType>`, matching `Fakturenn.Modules.*` excluding `.Contracts`
  - `FakturennArchitecture.Infrastructure` — `static readonly IObjectProvider<IType>`, matching `Fakturenn.Infrastructure.*`

- [ ] **Step 1: Create the project and reference every source assembly**

```bash
dotnet new xunit3 --output tests/Fakturenn.ArchitectureTests --name Fakturenn.ArchitectureTests
rm --force tests/Fakturenn.ArchitectureTests/UnitTest1.cs
dotnet sln Fakturenn.slnx add tests/Fakturenn.ArchitectureTests/Fakturenn.ArchitectureTests.csproj
dotnet add tests/Fakturenn.ArchitectureTests package AwesomeAssertions
dotnet add tests/Fakturenn.ArchitectureTests package TngTech.ArchUnitNET.xUnitV3
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.SharedKernel
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Infrastructure.Storage
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Modules.Invoices
dotnet add tests/Fakturenn.ArchitectureTests reference src/Fakturenn.Modules.Invoices.Contracts
```

`TngTech.ArchUnitNET.xUnitV3` brings `TngTech.ArchUnitNET` transitively and supplies the `.Check(Architecture)` extension that reports failures as xUnit v3 assertion failures.

Apply the same Microsoft.Testing.Platform properties as Task 2 Step 6.

- [ ] **Step 2: Write the shared architecture and the object providers**

`tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs`:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

/// <summary>
/// Loaded once per run because building the type graph is the expensive part.
/// The providers below match on assembly-name patterns rather than a list of
/// assemblies, so a module added by a later epic is governed the moment it
/// exists and needs no new rule.
/// </summary>
public static class FakturennArchitecture
{
    public static readonly Architecture Loaded = new ArchLoader()
        .LoadAssemblies(
            typeof(SharedKernel.Money).Assembly,
            typeof(Infrastructure.Storage.FilesystemBlobWriter).Assembly,
            typeof(Modules.Invoices.Contracts.InvoiceId).Assembly,
            typeof(Modules.Invoices.InvoicesModule).Assembly)
        .Build();

    /// <summary>Every module assembly, contracts included.</summary>
    public static readonly IObjectProvider<IType> Modules =
        Types().That().ResideInAssembly(@"^Fakturenn\.Modules\..*$", useRegularExpressions: true)
            .As("module assemblies");

    /// <summary>Module implementation assemblies, contracts excluded.</summary>
    public static readonly IObjectProvider<IType> ModuleImplementations =
        Types().That().ResideInAssembly(@"^Fakturenn\.Modules\.(?!.*\.Contracts$).*$", useRegularExpressions: true)
            .As("module implementation assemblies");

    public static readonly IObjectProvider<IType> Infrastructure =
        Types().That().ResideInAssembly(@"^Fakturenn\.Infrastructure\..*$", useRegularExpressions: true)
            .As("infrastructure assemblies");
}
```

`typeof(...).Assembly` is used instead of a directory scan so that a missing project reference is a compile error rather than a silently smaller architecture that passes every rule by having nothing to check.

Each new module assembly is added here as one more `typeof(...).Assembly` line. That is the only per-module edit this project ever needs; the rules themselves stay untouched.

- [ ] **Step 3: Write the anti-vacuity guard**

Add to `tests/Fakturenn.ArchitectureTests/ModuleBoundaryTests.cs`:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.xUnitV3;
using AwesomeAssertions;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    [Fact]
    public void The_architecture_contains_the_assemblies_the_rules_govern()
    {
        // Without this, every rule below would pass vacuously if assembly
        // loading broke, and the suite would go green while enforcing nothing.
        IEnumerable<string> assemblies = FakturennArchitecture.Loaded.Assemblies
            .Select(assembly => assembly.Name.Split(',')[0]);

        assemblies.Should().Contain([
            "Fakturenn.SharedKernel",
            "Fakturenn.Infrastructure.Storage",
            "Fakturenn.Modules.Invoices",
            "Fakturenn.Modules.Invoices.Contracts",
        ]);
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet test tests/Fakturenn.ArchitectureTests`
Expected: build failure — `The type or namespace name 'FakturennArchitecture' could not be found`, until Step 2's file compiles; then PASS for this one test.

- [ ] **Step 5: Write the module boundary rules**

Append to `tests/Fakturenn.ArchitectureTests/ModuleBoundaryTests.cs`:

```csharp
    [Fact]
    public void No_module_depends_on_infrastructure()
    {
        // Infrastructure implements module-owned interfaces, never the reverse.
        // This is also what keeps E-Invoice-EU adapter types out of the domain.
        Types().That().Are(FakturennArchitecture.Modules)
            .Should().NotDependOnAny(FakturennArchitecture.Infrastructure)
            .Because("MODULE-OWNERSHIP.md fixes the direction UI -> slices -> module contracts -> infrastructure")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void No_module_depends_on_another_modules_implementation_assembly()
    {
        Types().That().Are(FakturennArchitecture.Modules)
            .Should().NotDependOnAny(FakturennArchitecture.ModuleImplementations)
            .Because("cross-module access goes through Fakturenn.Modules.<Name>.Contracts, never the owner's entities")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void There_are_no_dependency_cycles_between_modules()
    {
        SliceRuleDefinition.Slices()
            .Matching("Fakturenn.Modules.(*)")
            .Should().BeFreeOfCycles()
            .Check(FakturennArchitecture.Loaded);
    }
```

Add `using ArchUnitNET.Fluent;` for `SliceRuleDefinition`.

The second rule is expressed as "no module depends on any module implementation assembly". A type depending on its own assembly is not a dependency ArchUnitNET reports across the slice boundary, so this reads as the cross-module rule it is. If the rule proves too coarse once a second module exists, narrow it then — not before, per YAGNI.

- [ ] **Step 6: Write the technology containment rules**

`tests/Fakturenn.ArchitectureTests/TechnologyContainmentTests.cs`:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Fakturenn.ArchitectureTests;

/// <summary>
/// Keeps each third-party technology inside the one layer allowed to know about
/// it. The Mail and Documents rules name assemblies that do not exist yet: they
/// are vacuously true today and become binding the moment E11 or E14 creates
/// them. Do not delete a rule because it currently matches nothing.
/// </summary>
public sealed class TechnologyContainmentTests
{
    [Fact]
    public void Only_the_web_assembly_depends_on_MudBlazor()
    {
        Types().That().DoNotResideInAssembly("Fakturenn.Web")
            .Should().NotDependOnAny(
                Types().That().ResideInAssembly(@"^MudBlazor.*$", useRegularExpressions: true))
            .Because("MudBlazor is a UI concern and must not leak into modules or infrastructure")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_mail_infrastructure_depends_on_MimeKit_or_MailKit()
    {
        Types().That().DoNotResideInAssembly(
                @"^Fakturenn\.Infrastructure\.Mail.*$", useRegularExpressions: true)
            .Should().NotDependOnAny(
                Types().That().ResideInAssembly(@"^(MimeKit|MailKit).*$", useRegularExpressions: true))
            .Because("MIME composition and signing belong behind the Mail module's contracts")
            .Check(FakturennArchitecture.Loaded);
    }

    [Fact]
    public void Only_document_infrastructure_depends_on_PdfSharp_or_MigraDoc()
    {
        Types().That().DoNotResideInAssembly(
                @"^Fakturenn\.Infrastructure\.Documents.*$", useRegularExpressions: true)
            .Should().NotDependOnAny(
                Types().That().ResideInAssembly(@"^(PdfSharp|MigraDoc).*$", useRegularExpressions: true))
            .Because("rendering belongs behind the Documents module's rendering contracts")
            .Check(FakturennArchitecture.Loaded);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Fakturenn.ArchitectureTests`
Expected: PASS, 7 tests.

If a rule fails with "no types found matching", the regular expression did not match any loaded assembly. Confirm against the anti-vacuity test's assembly list before changing the pattern.

- [ ] **Step 8: Prove the rules bite**

A rule that has never failed is not evidence. Introduce a real violation and confirm the suite catches it:

```bash
dotnet add src/Fakturenn.Modules.Invoices reference src/Fakturenn.Infrastructure.Storage
```

Add to `src/Fakturenn.Modules.Invoices/InvoicesModule.cs`, temporarily:

```csharp
    public static readonly Type Violation = typeof(Infrastructure.Storage.FilesystemBlobWriter);
```

A project reference alone is not enough: ArchUnitNET analyses type dependencies, and the compiler records nothing for an unused reference.

```bash
dotnet test tests/Fakturenn.ArchitectureTests
```

Expected: FAIL on `No_module_depends_on_infrastructure`.

Revert both changes:

```bash
dotnet remove src/Fakturenn.Modules.Invoices reference src/Fakturenn.Infrastructure.Storage
```

Remove the `Violation` field, then:

```bash
dotnet test tests/Fakturenn.ArchitectureTests
git status --short
```

Expected: PASS, 7 tests, and `git status --short` shows no change to `src/Fakturenn.Modules.Invoices`.

- [ ] **Step 9: Commit**

```bash
git add tests/Fakturenn.ArchitectureTests src/Fakturenn.Modules.Invoices Fakturenn.slnx Directory.Packages.props
git commit --message "test: enforce the six architecture rules with ArchUnitNET

Rules match on assembly-name patterns rather than a hand-maintained list, so
modules added by later epics inherit enforcement without editing this project.
The MimeKit/MailKit and PDFsharp/MigraDoc rules are vacuously true today and
become binding when E11 and E14 create those assemblies.

An anti-vacuity test asserts the loaded architecture actually contains the
assemblies the rules govern, so a loading failure cannot turn the whole suite
green while enforcing nothing.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```
### Task 7: Blazor host with localization and health endpoints

**Files:**

- Create: `src/Fakturenn.Web/Fakturenn.Web.csproj`, `Program.cs`, `FakturennWebApplication.cs`, `SharedResource.cs`
- Create: `src/Fakturenn.Web/Components/App.razor`, `Routes.razor`, `_Imports.razor`, `Layout/MainLayout.razor`, `Pages/Home.razor`
- Create: `src/Fakturenn.Web/Resources/SharedResource.resx`, `src/Fakturenn.Web/Resources/SharedResource.de.resx`
- Create: `src/Fakturenn.Web/appsettings.json`

**Interfaces:**

- Consumes: nothing from earlier tasks
- Produces:
  - `Fakturenn.Web.FakturennWebApplication` — `static WebApplication Build(string[] args)`
  - `Fakturenn.Web.SharedResource` — empty marker class for `IStringLocalizer<SharedResource>`
  - Endpoints `GET /alive` and `GET /health`
  - Resource key `AppTagline`, English `Your invoices, your identity, your infrastructure.`, German `Ihre Rechnungen, Ihre Identität, Ihre Infrastruktur.`

- [ ] **Step 1: Create the project**

```bash
dotnet new blazor --output src/Fakturenn.Web --name Fakturenn.Web --interactivity Server --empty
dotnet sln Fakturenn.slnx add src/Fakturenn.Web/Fakturenn.Web.csproj
dotnet add src/Fakturenn.Web package MudBlazor
dotnet add src/Fakturenn.Web package Serilog.AspNetCore
dotnet add src/Fakturenn.Web package AspNetCore.HealthChecks.NpgSql
```

- [ ] **Step 2: Write the host builder**

`src/Fakturenn.Web/FakturennWebApplication.cs`:

```csharp
using System.Globalization;
using Fakturenn.Web.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;
using Serilog;

namespace Fakturenn.Web;

public static class FakturennWebApplication
{
    private static readonly string[] SupportedCultures = ["en", "de"];

    /// <summary>
    /// Builds the application without starting it, so tests can host it on a
    /// real socket instead of reimplementing composition.
    /// </summary>
    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration));

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(SupportedCultures[0]);
            options.AddSupportedCultures(SupportedCultures);
            options.AddSupportedUICultures(SupportedCultures);
        });

        // The liveness probe must not depend on PostgreSQL: a database outage
        // should mark the instance unready, not have Kubernetes restart it.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddNpgSql(
                connectionStringFactory: _ =>
                    builder.Configuration.GetConnectionString("Fakturenn") ?? string.Empty,
                name: "postgres",
                tags: ["ready"]);

        WebApplication app = builder.Build();

        app.UseSerilogRequestLogging();
        app.UseRequestLocalization();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        });

        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return app;
    }
}
```

- [ ] **Step 3: Write the entrypoint**

`src/Fakturenn.Web/Program.cs`:

```csharp
using Fakturenn.Web;

WebApplication app = FakturennWebApplication.Build(args);

await app.RunAsync();
```

- [ ] **Step 4: Add the localization marker and resources**

`src/Fakturenn.Web/SharedResource.cs`:

```csharp
namespace Fakturenn.Web;

/// <summary>
/// Marker type for <c>IStringLocalizer&lt;SharedResource&gt;</c>. Resource files
/// live in Resources/SharedResource.resx and Resources/SharedResource.de.resx.
/// </summary>
public sealed class SharedResource;
```

`src/Fakturenn.Web/Resources/SharedResource.resx` — a standard `.resx` header followed by:

```xml
  <data name="AppTagline" xml:space="preserve">
    <value>Your invoices, your identity, your infrastructure.</value>
  </data>
```

`src/Fakturenn.Web/Resources/SharedResource.de.resx` — same header, with:

```xml
  <data name="AppTagline" xml:space="preserve">
    <value>Ihre Rechnungen, Ihre Identität, Ihre Infrastruktur.</value>
  </data>
```

Create the files with `dotnet new resx --output src/Fakturenn.Web/Resources --name SharedResource` if the template is available; otherwise copy the standard `.resx` schema header from any existing .NET resource file. The header must be present and well-formed or the resource will not compile.

- [ ] **Step 5: Write the Blazor shell**

`src/Fakturenn.Web/Components/_Imports.razor`:

```razor
@using System.Net.Http
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.Extensions.Localization
@using MudBlazor
@using Fakturenn.Web
@using Fakturenn.Web.Components
@using Fakturenn.Web.Components.Layout
```

`src/Fakturenn.Web/Components/Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase

<MudThemeProvider />
<MudPopoverProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudText Typo="Typo.h6">Fakturenn</MudText>
    </MudAppBar>
    <MudMainContent>
        <MudContainer MaxWidth="MaxWidth.Medium" Class="mt-8">
            @Body
        </MudContainer>
    </MudMainContent>
</MudLayout>
```

`src/Fakturenn.Web/Components/Pages/Home.razor`:

```razor
@page "/"
@inject IStringLocalizer<SharedResource> Localizer

<PageTitle>Fakturenn</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Fakturenn</MudText>
<MudText Typo="Typo.body1" data-testid="app-tagline">@Localizer["AppTagline"]</MudText>
```

Update `App.razor` to reference the MudBlazor stylesheet and `Routes.razor` to use `MainLayout` as the default layout, following the structure the `blazor` template generated.

- [ ] **Step 6: Configure Serilog and the connection string**

`src/Fakturenn.Web/appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" }
    ]
  },
  "ConnectionStrings": {
    "Fakturenn": ""
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 7: Run the application and verify the endpoints**

```bash
dotnet run --project src/Fakturenn.Web --urls http://127.0.0.1:5099 &
sleep 8
curl --silent --fail http://127.0.0.1:5099/alive
curl --silent --header 'Accept-Language: de' http://127.0.0.1:5099/ | grep --fixed-strings 'Ihre Rechnungen'
kill %1
```

Expected: `/alive` returns `Healthy`; the German tagline appears in the rendered HTML.

- [ ] **Step 8: Verify the build is warning-free**

Run: `dotnet build --configuration Release`
Expected: `0 Warning(s)`.

- [ ] **Step 9: Commit**

```bash
git add src/Fakturenn.Web Fakturenn.slnx Directory.Packages.props
git commit --message "feat: add Blazor Interactive Server host with health and localization

Composition lives in FakturennWebApplication.Build so tests can host the real
application on a socket rather than reimplementing it.

Liveness is deliberately independent of PostgreSQL: a database outage should
mark the instance unready, not cause Kubernetes to restart it.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Module-owned persistence and the explicit migration entrypoint

The migration creates the `invoices` schema and nothing else. Inventing invoice tables here would pre-empt E09; the point is to prove that a module owns its migrations and that they apply from clean.

**Files:**

- Create: `src/Fakturenn.Modules.Invoices/Persistence/InvoicesDbContext.cs`
- Create: `src/Fakturenn.Modules.Invoices/Persistence/InvoicesDbContextFactory.cs`
- Create: `src/Fakturenn.Modules.Invoices/Persistence/Migrations/` (generated)
- Modify: `src/Fakturenn.Web/FakturennWebApplication.cs`
- Modify: `src/Fakturenn.Web/Program.cs`

**Interfaces:**

- Consumes: `Fakturenn.Modules.Invoices` (Task 5), `Fakturenn.Web.FakturennWebApplication` (Task 7)
- Produces:
  - `Fakturenn.Modules.Invoices.Persistence.InvoicesDbContext` — `sealed`, ctor `InvoicesDbContext(DbContextOptions<InvoicesDbContext> options)`, const `string SchemaName = "invoices"`
  - `Fakturenn.Web.Program` — supports `--migrate`, which applies migrations and exits with code 0

- [ ] **Step 1: Add the packages**

```bash
dotnet add src/Fakturenn.Modules.Invoices package Microsoft.EntityFrameworkCore
dotnet add src/Fakturenn.Modules.Invoices package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Fakturenn.Modules.Invoices package Microsoft.EntityFrameworkCore.Design
dotnet add src/Fakturenn.Web reference src/Fakturenn.Modules.Invoices
dotnet tool install --global dotnet-ef
```

- [ ] **Step 2: Write the DbContext**

`src/Fakturenn.Modules.Invoices/Persistence/InvoicesDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// The Invoices module owns this context and its migrations. No other module
/// may reference the entities it maps.
/// </summary>
public sealed class InvoicesDbContext(DbContextOptions<InvoicesDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "invoices";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 3: Write the design-time factory**

`src/Fakturenn.Modules.Invoices/Persistence/InvoicesDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. The connection string is never read at
/// design time because migrations are generated, not applied, here.
/// </summary>
public sealed class InvoicesDbContextFactory : IDesignTimeDbContextFactory<InvoicesDbContext>
{
    public InvoicesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
```

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add InitialCreate \
  --project src/Fakturenn.Modules.Invoices \
  --output-dir Persistence/Migrations
```

- [ ] **Step 5: Verify the migration creates the schema**

Open the generated migration under `src/Fakturenn.Modules.Invoices/Persistence/Migrations/`. Its `Up` method must contain:

```csharp
            migrationBuilder.EnsureSchema(
                name: "invoices");
```

If EF omitted it because the model has no entities, add that call to `Up` and add the matching `migrationBuilder.DropSchema(name: "invoices");` note is **not** required — leave `Down` empty, because dropping a schema that later epics populate is destructive.

- [ ] **Step 6: Register the context and the migration entrypoint**

In `FakturennWebApplication.Build`, after `AddHealthChecks`, add:

```csharp
        builder.Services.AddDbContext<InvoicesDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("Fakturenn")));
```

with `using Fakturenn.Modules.Invoices.Persistence;` and `using Microsoft.EntityFrameworkCore;` at the top.

Replace `src/Fakturenn.Web/Program.cs` with:

```csharp
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web;
using Microsoft.EntityFrameworkCore;

WebApplication app = FakturennWebApplication.Build(args);

// Migrations never run as a side effect of serving traffic. DEPLOYMENT-BASELINE.md
// requires an explicit migration Job, and auto-migrating on startup races when
// more than one replica starts at once.
if (args.Contains("--migrate"))
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    InvoicesDbContext database = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
    await database.Database.MigrateAsync();

    return;
}

await app.RunAsync();
```

- [ ] **Step 7: Verify the architecture tests still pass**

Run: `dotnet test tests/Fakturenn.ArchitectureTests`
Expected: PASS, 12 tests. The Invoices module now references EF Core and Npgsql, which no rule forbids; it must still not reference `Fakturenn.Infrastructure.*`.

- [ ] **Step 8: Commit**

```bash
git add src/Fakturenn.Modules.Invoices src/Fakturenn.Web Directory.Packages.props
git commit --message "feat: add module-owned persistence and an explicit migration entrypoint

The Invoices module owns its DbContext and its migrations, per
MODULE-OWNERSHIP.md. The initial migration creates the invoices schema and
nothing else; inventing tables here would pre-empt E09.

Migrations run only via 'dotnet run --project src/Fakturenn.Web -- --migrate'.
DEPLOYMENT-BASELINE.md requires an explicit migration Job, and auto-migrating
on startup races when multiple replicas start together.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Integration tests with Testcontainers

**Files:**

- Create: `tests/Fakturenn.IntegrationTests/Fakturenn.IntegrationTests.csproj`
- Create: `tests/Fakturenn.IntegrationTests/PostgresFixture.cs`
- Create: `tests/Fakturenn.IntegrationTests/InvoicesMigrationTests.cs`

**Interfaces:**

- Consumes: `Fakturenn.Modules.Invoices.Persistence.InvoicesDbContext` (Task 8)
- Produces: `PostgresFixture` — implements `IAsyncLifetime`, property `string ConnectionString`, method `InvoicesDbContext CreateContext()`

- [ ] **Step 1: Create the project**

```bash
dotnet new xunit3 --output tests/Fakturenn.IntegrationTests --name Fakturenn.IntegrationTests
rm --force tests/Fakturenn.IntegrationTests/UnitTest1.cs
dotnet sln Fakturenn.slnx add tests/Fakturenn.IntegrationTests/Fakturenn.IntegrationTests.csproj
dotnet add tests/Fakturenn.IntegrationTests reference src/Fakturenn.Modules.Invoices
dotnet add tests/Fakturenn.IntegrationTests package AwesomeAssertions
dotnet add tests/Fakturenn.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/Fakturenn.IntegrationTests package Npgsql
```

Apply the same Microsoft.Testing.Platform properties as Task 2 Step 6.

- [ ] **Step 2: Write the fixture**

`tests/Fakturenn.IntegrationTests/PostgresFixture.cs`:

```csharp
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// A real PostgreSQL instance per test class. SPEC-v0.1.md section 10 requires
/// real infrastructure through Testcontainers rather than an in-memory provider,
/// because schemas, sequences and concurrency behaviour are the point.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("fakturenn")
        .WithUsername("fakturenn")
        .WithPassword("fakturenn")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public InvoicesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
}
```

- [ ] **Step 3: Write the failing test**

`tests/Fakturenn.IntegrationTests/InvoicesMigrationTests.cs`:

```csharp
using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fakturenn.IntegrationTests;

public sealed class InvoicesMigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migrations_apply_to_a_clean_database()
    {
        await using InvoicesDbContext context = postgres.CreateContext();

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        IEnumerable<string> applied = await context.Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        applied.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Applying_migrations_creates_the_invoices_schema()
    {
        await using InvoicesDbContext context = postgres.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @name";
        command.Parameters.AddWithValue("name", InvoicesDbContext.SchemaName);

        object? count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Convert.ToInt64(count).Should().Be(1);
    }

    [Fact]
    public async Task Applying_migrations_twice_is_idempotent()
    {
        // Migrations must work from clean and from previous states, per the
        // Definition of Done in PLAN-v0.1.md.
        await using InvoicesDbContext context = postgres.CreateContext();

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        Func<Task> second = () => context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await second.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail, then pass**

Run: `dotnet test tests/Fakturenn.IntegrationTests`

If Docker is not running, this fails with a Testcontainers connection error rather than an assertion failure. Start Docker and rerun. Expected on success: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add tests/Fakturenn.IntegrationTests Fakturenn.slnx Directory.Packages.props
git commit --message "test: add Testcontainers integration tests for module migrations

Asserts migrations apply to a clean database, create the invoices schema, and
are idempotent on re-application. The Definition of Done requires migrations to
work from clean and from previous states.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Compliance tests and the golden-file comparer

No e-invoice generator exists until E12, so this task ships the comparer that the golden-file corpus will be checked with, and proves the comparer detects both equality and difference. A comparer that has only ever returned "equal" is worthless.

**Files:**

- Create: `tests/Fakturenn.ComplianceTests/Fakturenn.ComplianceTests.csproj`
- Create: `tests/Fakturenn.ComplianceTests/XmlNormalizer.cs`
- Create: `tests/Fakturenn.ComplianceTests/XmlComparison.cs`
- Create: `tests/Fakturenn.ComplianceTests/NormalizingXmlComparer.cs`
- Create: `tests/Fakturenn.ComplianceTests/NormalizingXmlComparerTests.cs`
- Create: `tests/Fakturenn.ComplianceTests/corpus/README.md`

**Interfaces:**

- Consumes: nothing from earlier tasks
- Produces:
  - `XmlNormalizer` — `static XElement Normalize(XElement element)`
  - `XmlComparison` — `sealed record XmlComparison(bool IsMatch, IReadOnlyList<string> Differences)`
  - `NormalizingXmlComparer` — `static XmlComparison Compare(string expectedXml, string actualXml)`

- [ ] **Step 1: Create the project**

```bash
dotnet new xunit3 --output tests/Fakturenn.ComplianceTests --name Fakturenn.ComplianceTests
rm --force tests/Fakturenn.ComplianceTests/UnitTest1.cs
dotnet sln Fakturenn.slnx add tests/Fakturenn.ComplianceTests/Fakturenn.ComplianceTests.csproj
dotnet add tests/Fakturenn.ComplianceTests package AwesomeAssertions
```

Apply the same Microsoft.Testing.Platform properties as Task 2 Step 6.

- [ ] **Step 2: Write the failing tests**

`tests/Fakturenn.ComplianceTests/NormalizingXmlComparerTests.cs`:

```csharp
using AwesomeAssertions;

namespace Fakturenn.ComplianceTests;

public sealed class NormalizingXmlComparerTests
{
    [Fact]
    public void Identical_documents_match()
    {
        const string xml = "<Invoice><Total currency=\"EUR\">952.00</Total></Invoice>";

        NormalizingXmlComparer.Compare(xml, xml).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Insignificant_whitespace_and_indentation_are_ignored()
    {
        const string expected = "<Invoice><Total currency=\"EUR\">952.00</Total></Invoice>";
        const string actual = """
            <Invoice>
                <Total currency="EUR">952.00</Total>
            </Invoice>
            """;

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Attribute_order_is_ignored()
    {
        const string expected = "<Total currency=\"EUR\" scheme=\"EN16931\">952.00</Total>";
        const string actual = "<Total scheme=\"EN16931\" currency=\"EUR\">952.00</Total>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Comments_are_ignored()
    {
        const string expected = "<Invoice><Total>952.00</Total></Invoice>";
        const string actual = "<Invoice><!-- generated --><Total>952.00</Total></Invoice>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void A_different_value_is_reported_as_a_difference()
    {
        const string expected = "<Invoice><Total>952.00</Total></Invoice>";
        const string actual = "<Invoice><Total>952.01</Total></Invoice>";

        XmlComparison comparison = NormalizingXmlComparer.Compare(expected, actual);

        comparison.IsMatch.Should().BeFalse();
        comparison.Differences.Should().ContainSingle()
            .Which.Should().Contain("952.00").And.Contain("952.01");
    }

    [Fact]
    public void A_missing_element_is_reported_as_a_difference()
    {
        const string expected = "<Invoice><Total>952.00</Total><BuyerReference>C-4711</BuyerReference></Invoice>";
        const string actual = "<Invoice><Total>952.00</Total></Invoice>";

        XmlComparison comparison = NormalizingXmlComparer.Compare(expected, actual);

        comparison.IsMatch.Should().BeFalse();
        comparison.Differences.Should().NotBeEmpty();
    }

    [Fact]
    public void A_different_attribute_value_is_reported_as_a_difference()
    {
        const string expected = "<Total currency=\"EUR\">952.00</Total>";
        const string actual = "<Total currency=\"CHF\">952.00</Total>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Element_order_is_significant_because_EN_16931_sequences_are_ordered()
    {
        const string expected = "<Invoice><A>1</A><B>2</B></Invoice>";
        const string actual = "<Invoice><B>2</B><A>1</A></Invoice>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Fakturenn.ComplianceTests`
Expected: build failure — `The name 'NormalizingXmlComparer' does not exist`.

- [ ] **Step 4: Implement the normalizer**

`tests/Fakturenn.ComplianceTests/XmlNormalizer.cs`:

```csharp
using System.Xml.Linq;

namespace Fakturenn.ComplianceTests;

/// <summary>
/// Removes differences that carry no semantics: comments, insignificant
/// whitespace, and attribute order. Element order is preserved, because
/// EN 16931 syntax bindings define ordered sequences.
/// </summary>
public static class XmlNormalizer
{
    public static XElement Normalize(XElement element)
    {
        var normalized = new XElement(element.Name);

        foreach (XAttribute attribute in element.Attributes()
                     .Where(attribute => !attribute.IsNamespaceDeclaration)
                     .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal))
        {
            normalized.SetAttributeValue(attribute.Name, attribute.Value.Trim());
        }

        XElement[] children = [.. element.Elements()];

        if (children.Length == 0)
        {
            normalized.Value = CollapseWhitespace(element.Value);
            return normalized;
        }

        foreach (XElement child in children)
        {
            normalized.Add(Normalize(child));
        }

        return normalized;
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
```

- [ ] **Step 5: Implement the comparison result**

`tests/Fakturenn.ComplianceTests/XmlComparison.cs`:

```csharp
namespace Fakturenn.ComplianceTests;

public sealed record XmlComparison(bool IsMatch, IReadOnlyList<string> Differences);
```

- [ ] **Step 6: Implement the comparer**

`tests/Fakturenn.ComplianceTests/NormalizingXmlComparer.cs`:

```csharp
using System.Xml.Linq;

namespace Fakturenn.ComplianceTests;

public static class NormalizingXmlComparer
{
    public static XmlComparison Compare(string expectedXml, string actualXml)
    {
        XElement expected = XmlNormalizer.Normalize(XElement.Parse(expectedXml, LoadOptions.None));
        XElement actual = XmlNormalizer.Normalize(XElement.Parse(actualXml, LoadOptions.None));

        List<string> differences = [];
        CompareElements(expected, actual, expected.Name.LocalName, differences);

        return new XmlComparison(differences.Count == 0, differences);
    }

    private static void CompareElements(XElement expected, XElement actual, string path, List<string> differences)
    {
        if (expected.Name != actual.Name)
        {
            differences.Add($"{path}: expected element '{expected.Name}' but found '{actual.Name}'");
            return;
        }

        CompareAttributes(expected, actual, path, differences);

        XElement[] expectedChildren = [.. expected.Elements()];
        XElement[] actualChildren = [.. actual.Elements()];

        if (expectedChildren.Length == 0 && actualChildren.Length == 0)
        {
            if (expected.Value != actual.Value)
            {
                differences.Add($"{path}: expected value '{expected.Value}' but found '{actual.Value}'");
            }

            return;
        }

        if (expectedChildren.Length != actualChildren.Length)
        {
            differences.Add(
                $"{path}: expected {expectedChildren.Length} child element(s) but found {actualChildren.Length}");
        }

        int shared = Math.Min(expectedChildren.Length, actualChildren.Length);
        for (int index = 0; index < shared; index++)
        {
            CompareElements(
                expectedChildren[index],
                actualChildren[index],
                $"{path}/{expectedChildren[index].Name.LocalName}[{index}]",
                differences);
        }
    }

    private static void CompareAttributes(XElement expected, XElement actual, string path, List<string> differences)
    {
        foreach (XAttribute attribute in expected.Attributes())
        {
            string? actualValue = actual.Attribute(attribute.Name)?.Value;

            if (actualValue is null)
            {
                differences.Add($"{path}: missing attribute '{attribute.Name}'");
            }
            else if (actualValue != attribute.Value)
            {
                differences.Add(
                    $"{path}@{attribute.Name}: expected '{attribute.Value}' but found '{actualValue}'");
            }
        }

        foreach (XAttribute attribute in actual.Attributes()
                     .Where(attribute => expected.Attribute(attribute.Name) is null))
        {
            differences.Add($"{path}: unexpected attribute '{attribute.Name}'");
        }
    }
}
```

- [ ] **Step 7: Write the corpus README**

`tests/Fakturenn.ComplianceTests/corpus/README.md`:

```markdown
# Compliance corpus

Golden files for electronic-invoice output, compared with
`NormalizingXmlComparer`.

## Layout

    corpus/
      facturx/<profile>/<case>.expected.xml
      xrechnung-cii/<profile>/<case>.expected.xml
      xrechnung-ubl/<profile>/<case>.expected.xml

## Rules

- Every file records its provenance and the exact standard version it was
  produced against, in an XML comment at the top of the file.
- A golden file is never edited to make a failing test pass. Either the
  generator is wrong, or the standard changed — and a standard change gets its
  own file under a new version directory, so the old expectation stays testable.
- Files are compared after normalization: comments, insignificant whitespace
  and attribute order are ignored. Element order is significant, because
  EN 16931 syntax bindings define ordered sequences.

## Status

Empty. No electronic-invoice generator exists until epic E12. The comparer and
its tests ship first so that the corpus has something trustworthy to be checked
with when the generator arrives.

Planned coverage, from `docs/testing/TEST-STRATEGY.md`: Factur-X/ZUGFeRD,
XRechnung CII, XRechnung UBL if supported, multiple tax cases, references,
allowances and charges, corrections, service periods, and rounding edges.
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Fakturenn.ComplianceTests`
Expected: PASS, 8 tests.

- [ ] **Step 9: Commit**

```bash
git add tests/Fakturenn.ComplianceTests Fakturenn.slnx Directory.Packages.props
git commit --message "test: add the golden-file XML comparer for compliance tests

Ships the comparer before the generator it will check, with tests proving it
detects value, attribute, cardinality and ordering differences. A comparer that
has only ever returned 'equal' proves nothing.

Element order is significant because EN 16931 syntax bindings define ordered
sequences; comments, whitespace and attribute order are normalized away.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: Playwright UI tests

**Files:**

- Create: `tests/Fakturenn.UiTests/Fakturenn.UiTests.csproj`
- Create: `tests/Fakturenn.UiTests/WebAppFixture.cs`
- Create: `tests/Fakturenn.UiTests/HomePageTests.cs`

**Interfaces:**

- Consumes: `Fakturenn.Web.FakturennWebApplication.Build` (Task 7)
- Produces: `WebAppFixture` — implements `IAsyncLifetime`, property `string BaseAddress`

- [ ] **Step 1: Create the project**

```bash
dotnet new xunit3 --output tests/Fakturenn.UiTests --name Fakturenn.UiTests
rm --force tests/Fakturenn.UiTests/UnitTest1.cs
dotnet sln Fakturenn.slnx add tests/Fakturenn.UiTests/Fakturenn.UiTests.csproj
dotnet add tests/Fakturenn.UiTests reference src/Fakturenn.Web
dotnet add tests/Fakturenn.UiTests package AwesomeAssertions
dotnet add tests/Fakturenn.UiTests package Microsoft.Playwright
```

Apply the same Microsoft.Testing.Platform properties as Task 2 Step 6.

- [ ] **Step 2: Install the browsers**

```bash
dotnet build tests/Fakturenn.UiTests --configuration Debug
pwsh tests/Fakturenn.UiTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
```

If `pwsh` is unavailable, install it or run `dotnet tool install --global Microsoft.Playwright.CLI` and use `playwright install --with-deps chromium`.

- [ ] **Step 3: Write the fixture**

`tests/Fakturenn.UiTests/WebAppFixture.cs`:

```csharp
using Fakturenn.Web;

namespace Fakturenn.UiTests;

/// <summary>
/// Hosts the real application on a real socket. Blazor Interactive Server needs
/// a WebSocket circuit, which an in-memory test server cannot provide, and port
/// 0 lets the OS pick a free port so parallel runs do not collide.
/// </summary>
public sealed class WebAppFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public string BaseAddress { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        await _app.StartAsync();

        BaseAddress = _app.Urls.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
```

- [ ] **Step 4: Write the failing tests**

`tests/Fakturenn.UiTests/HomePageTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Fakturenn.UiTests;

public sealed class HomePageTests(WebAppFixture app) : IClassFixture<WebAppFixture>, IAsyncLifetime
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
    public async Task The_home_page_renders_the_english_tagline_by_default()
    {
        IPage page = await NewPageAsync("en-GB");

        await page.GotoAsync(app.BaseAddress);

        string? tagline = await page.GetByTestId("app-tagline").TextContentAsync();
        tagline.Should().Be("Your invoices, your identity, your infrastructure.");
    }

    [Fact]
    public async Task A_german_browser_gets_the_german_tagline()
    {
        // Proves resources, the localization middleware and the Accept-Language
        // provider are wired together, not merely present.
        IPage page = await NewPageAsync("de-DE");

        await page.GotoAsync(app.BaseAddress);

        string? tagline = await page.GetByTestId("app-tagline").TextContentAsync();
        tagline.Should().Be("Ihre Rechnungen, Ihre Identität, Ihre Infrastruktur.");
    }

    [Fact]
    public async Task The_liveness_endpoint_reports_healthy_without_a_database()
    {
        IPage page = await NewPageAsync("en-GB");

        IResponse? response = await page.GotoAsync($"{app.BaseAddress}/alive");

        response!.Status.Should().Be(200);
        (await response.TextAsync()).Should().Be("Healthy");
    }

    private async Task<IPage> NewPageAsync(string locale)
    {
        IBrowserContext context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = locale,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Accept-Language"] = locale },
        });

        return await context.NewPageAsync();
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Fakturenn.UiTests`
Expected: PASS, 3 tests.

If the German test fails with the English string, the localization middleware order is wrong: `UseRequestLocalization` must run before `MapRazorComponents`.

- [ ] **Step 6: Commit**

```bash
git add tests/Fakturenn.UiTests Fakturenn.slnx Directory.Packages.props
git commit --message "test: add Playwright UI tests for the home page and liveness

The fixture hosts the real application on a real socket because Blazor
Interactive Server needs a WebSocket circuit, which an in-memory test server
cannot provide. Port 0 avoids collisions between parallel runs.

The German test proves the resources, the localization middleware and the
Accept-Language provider are wired together rather than merely present.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 12: Container image and Compose reference deployment

**Files:**

- Modify: `src/Fakturenn.Web/Fakturenn.Web.csproj`
- Create: `compose.yaml`
- Create: `.dockerignore`

**Interfaces:**

- Consumes: `Fakturenn.Web` (Task 7), `--migrate` entrypoint (Task 8)
- Produces: image `fakturenn:dev` built by `dotnet publish /t:PublishContainer`

- [ ] **Step 1: Configure container publishing**

Add to `src/Fakturenn.Web/Fakturenn.Web.csproj`:

```xml
  <PropertyGroup Label="Container">
    <ContainerRepository>fakturenn</ContainerRepository>
    <ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled</ContainerBaseImage>
    <ContainerRuntimeIdentifiers>linux-x64;linux-arm64</ContainerRuntimeIdentifiers>
    <ContainerUser>$APP_UID</ContainerUser>
    <ContainerPort>8080</ContainerPort>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
```

The chiseled base image ships no shell, which removes an entire class of container escape tooling. `SECURITY-BASELINE.md` favours a minimal runtime surface. `$APP_UID` runs the process as a non-root user.

- [ ] **Step 2: Write `.dockerignore`**

```gitignore
**/bin/
**/obj/
**/.vs/
.git/
.github/
docs/
tests/
```

- [ ] **Step 3: Build the image**

```bash
dotnet publish src/Fakturenn.Web --configuration Release /t:PublishContainer -p:ContainerImageTag=dev
docker image inspect fakturenn:dev --format '{{.Config.User}} {{.Config.ExposedPorts}}'
```

Expected: the image exists and the user is not `root`.

- [ ] **Step 4: Write `compose.yaml`**

```yaml
name: fakturenn

services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: fakturenn
      POSTGRES_USER: fakturenn
      POSTGRES_PASSWORD: fakturenn
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready --username=fakturenn --dbname=fakturenn"]
      interval: 5s
      timeout: 5s
      retries: 20

  # Run once before starting the app: docker compose run --rm migrate
  # DEPLOYMENT-BASELINE.md requires an explicit migration step, so this is not
  # part of the default 'up'.
  migrate:
    image: fakturenn:dev
    command: ["--migrate"]
    profiles: ["migrate"]
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__Fakturenn: "Host=postgres;Port=5432;Database=fakturenn;Username=fakturenn;Password=fakturenn"

  fakturenn-app:
    image: fakturenn:dev
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ASPNETCORE_URLS: "http://+:8080"
      ConnectionStrings__Fakturenn: "Host=postgres;Port=5432;Database=fakturenn;Username=fakturenn;Password=fakturenn"
    ports:
      - "8080:8080"
    healthcheck:
      test: ["CMD", "/app/Fakturenn.Web", "--help"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 20s

volumes:
  postgres-data:
```

The application container has no shell and no `curl`, so its healthcheck cannot use HTTP. External readiness is checked through `/health` from outside the container; the Compose healthcheck only confirms the process responds.

- [ ] **Step 5: Verify the stack comes up**

```bash
docker compose up --detach
docker compose run --rm migrate
timeout 60 bash -c 'until curl --silent --fail http://localhost:8080/health; do sleep 2; done'
curl --silent --fail http://localhost:8080/alive
docker compose down --volumes
```

Expected: `/health` returns `Healthy` after the migration has run, and `/alive` returns `Healthy`.

- [ ] **Step 6: Commit**

```bash
git add src/Fakturenn.Web/Fakturenn.Web.csproj compose.yaml .dockerignore
git commit --message "feat: publish a container image and add the Compose reference deployment

Uses the SDK's built-in container support rather than a Dockerfile, so the
image cannot drift from the project file. The chiseled base image ships no
shell and the process runs as a non-root user.

Migration is a separate one-shot service behind a Compose profile, matching the
explicit migration Job that DEPLOYMENT-BASELINE.md requires for Kubernetes.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 13: Continuous integration

**Files:**

- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/codeql.yml`
- Create: `.github/dependabot.yml`

**Interfaces:**

- Consumes: every test project and `compose.yaml`
- Produces: the checks that gate a pull request

- [ ] **Step 1: Write `.github/workflows/ci.yml`**

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  format:
    name: Format
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Verify formatting
        run: dotnet format --verify-no-changes --verbosity diagnostic

  build-test:
    name: Build and unit tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --configuration Release --no-restore
      - name: Unit tests
        run: dotnet test tests/Fakturenn.UnitTests --configuration Release --no-build
      - name: Architecture tests
        run: dotnet test tests/Fakturenn.ArchitectureTests --configuration Release --no-build
      - name: Compliance tests
        run: dotnet test tests/Fakturenn.ComplianceTests --configuration Release --no-build

  integration:
    name: Integration tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Integration tests
        run: dotnet test tests/Fakturenn.IntegrationTests --configuration Release

  ui:
    name: UI tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Build UI tests
        run: dotnet build tests/Fakturenn.UiTests --configuration Release
      - name: Install browsers
        run: pwsh tests/Fakturenn.UiTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
      - name: UI tests
        run: dotnet test tests/Fakturenn.UiTests --configuration Release --no-build

  compose-smoke:
    name: Compose smoke test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Build image
        run: dotnet publish src/Fakturenn.Web --configuration Release /t:PublishContainer -p:ContainerImageTag=dev
      - name: Start stack
        run: docker compose up --detach
      - name: Apply migrations
        run: docker compose run --rm migrate
      - name: Wait for readiness
        run: timeout 120 bash -c 'until curl --silent --fail http://localhost:8080/health; do sleep 3; done'
      - name: Dump logs on failure
        if: failure()
        run: docker compose logs
      - name: Stop stack
        if: always()
        run: docker compose down --volumes
```

- [ ] **Step 2: Write `.github/workflows/codeql.yml`**

```yaml
name: CodeQL

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  schedule:
    - cron: "17 4 * * 1"

permissions:
  contents: read
  security-events: write

jobs:
  analyze:
    name: Analyze C#
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - uses: github/codeql-action/init@v3
        with:
          languages: csharp
      - name: Build
        run: dotnet build --configuration Release
      - uses: github/codeql-action/analyze@v3
```

- [ ] **Step 3: Write `.github/dependabot.yml`**

```yaml
version: 2

updates:
  - package-ecosystem: nuget
    directory: "/"
    schedule:
      interval: weekly
    open-pull-requests-limit: 10
    groups:
      dotnet:
        patterns: ["Microsoft.*", "System.*"]

  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: weekly

  - package-ecosystem: docker
    directory: "/"
    schedule:
      interval: weekly
```

- [ ] **Step 4: Verify the workflows are valid YAML and the commands run locally**

```bash
python3 -c "import yaml,sys;[yaml.safe_load(open(f)) for f in sys.argv[1:]]" .github/workflows/ci.yml .github/workflows/codeql.yml .github/dependabot.yml
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test
```

Expected: no YAML errors, no formatting changes required, `0 Warning(s)`, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add .github
git commit --message "ci: add build, test, security and Compose smoke workflows

Splits fast checks from slow ones so a formatting mistake fails in under a
minute. The compose-smoke job enforces the Definition of Done item that Compose
remains runnable, which no unit test can cover.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 14: Release pipeline, versioning and the human-verification gate

**Files:**

- Create: `.bumpversion.toml`
- Create: `docs/operations/RELEASE-CHECKLIST-v0.1.md`
- Create: `.github/workflows/release.yml`
- Modify: `CHANGELOG.md`

**Interfaces:**

- Consumes: `Directory.Build.props` `<Version>` (Task 2), the container configuration (Task 12), the CI jobs (Task 13)
- Produces: a tag-triggered release publishing `ghcr.io/<owner>/fakturenn`

- [ ] **Step 1: Write `.bumpversion.toml`**

```toml
[tool.bumpversion]
current_version = "0.1.0-alpha.1"
parse = "(?P<major>\\d+)\\.(?P<minor>\\d+)\\.(?P<patch>\\d+)(?:-(?P<pre_label>[a-z]+)\\.(?P<pre_number>\\d+))?"
serialize = [
    "{major}.{minor}.{patch}-{pre_label}.{pre_number}",
    "{major}.{minor}.{patch}",
]
search = "{current_version}"
replace = "{new_version}"
commit = true
tag = true
tag_name = "v{new_version}"
message = "chore(release): bump version to {new_version}"
allow_dirty = false

[tool.bumpversion.parts.pre_label]
values = ["alpha", "beta", "rc", "final"]
optional_value = "final"

[[tool.bumpversion.files]]
filename = "Directory.Build.props"
search = "<Version>{current_version}</Version>"
replace = "<Version>{new_version}</Version>"

[[tool.bumpversion.files]]
filename = "CHANGELOG.md"
search = "## [Unreleased]"
replace = "## [Unreleased]\n\n## [{new_version}]"
```

- [ ] **Step 2: Write the human-verification checklist**

`docs/operations/RELEASE-CHECKLIST-v0.1.md`:

```markdown
# Release checklist — v0.1 human-test release

`docs/testing/TEST-STRATEGY.md` states:

> v0.1 human-test release requires all automated suites green and successful
> independent verification of at least one Factur-X, one XRechnung, one S/MIME
> message, and one OpenPGP message.

Automated suites are gated by CI. The four verifications below cannot be
automated away — "independent" means verified by a tool that is not part of
this codebase. This checklist fails closed: an unticked box blocks the release.

## Automated gates

- [ ] `format`, `build-test`, `integration`, `ui` and `compose-smoke` are green
      on the tagged commit
- [ ] CodeQL reports no new alerts
- [ ] The published image runs as a non-root user

## Independent verification

- [ ] **Factur-X** — one generated invoice validated by an external EN 16931
      validator not maintained in this repository. Record tool, version and
      report.
- [ ] **XRechnung** — one generated invoice validated by the official KoSIT
      validator. Record the validator version, the scenario configuration
      version, and the report.
- [ ] **S/MIME** — one signed message whose signature verifies in a mail client
      that has never seen this codebase. Record client and version.
- [ ] **OpenPGP** — one signed message whose signature verifies with GnuPG.
      Record the GnuPG version and the key fingerprint used.

## Operational verification

- [ ] `docker compose up` from a clean checkout reaches `/health` healthy
- [ ] The migration Job applies from an empty database
- [ ] A backup taken per `DEPLOYMENT-BASELINE.md` restores with matching
      artifact hashes

## Status for the current release

**Not satisfiable yet.** Factur-X and XRechnung generation arrive with epic
E12; S/MIME and OpenPGP signing with epic E14. Until both land, this checklist
cannot be completed truthfully, and v0.1 cannot be declared a human-test
release. This is the intended behaviour, not an oversight.
```

- [ ] **Step 3: Write `.github/workflows/release.yml`**

```yaml
name: Release

on:
  push:
    tags: ["v*"]

permissions:
  contents: write
  packages: write

env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  verify:
    name: Verify
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Build
        run: dotnet build --configuration Release
      - name: All tests
        run: dotnet test --configuration Release

  publish:
    name: Publish image and release
    runs-on: ubuntu-latest
    needs: verify
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Derive version from tag
        id: version
        run: echo "value=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"

      - name: Log in to GHCR
        run: echo "${{ secrets.GITHUB_TOKEN }}" | docker login ghcr.io --username "${{ github.actor }}" --password-stdin

      - name: Publish multi-architecture image
        run: |
          dotnet publish src/Fakturenn.Web \
            --configuration Release \
            /t:PublishContainer \
            -p:ContainerRegistry=ghcr.io \
            -p:ContainerRepository=${{ github.repository_owner }}/fakturenn \
            -p:ContainerImageTags='"${{ steps.version.outputs.value }};latest"' \
            -p:Version=${{ steps.version.outputs.value }}

      - name: Generate SBOM
        run: |
          dotnet tool install --global CycloneDX
          dotnet CycloneDX Fakturenn.slnx --out ./artifacts --json
          mv ./artifacts/bom.json ./artifacts/fakturenn-${{ steps.version.outputs.value }}-sbom.json

      - name: Checksum artifacts
        working-directory: ./artifacts
        run: sha256sum * > SHA256SUMS

      - name: Extract the changelog section
        run: |
          awk '/^## \[${{ steps.version.outputs.value }}\]/{flag=1; next} /^## \[/{flag=0} flag' \
            CHANGELOG.md > release-notes.md
          if [ ! -s release-notes.md ]; then
            echo "No CHANGELOG section for ${{ steps.version.outputs.value }}." >&2
            exit 1
          fi
          {
            echo ""
            echo "---"
            echo ""
            echo "Human verification: see [docs/operations/RELEASE-CHECKLIST-v0.1.md](docs/operations/RELEASE-CHECKLIST-v0.1.md)."
          } >> release-notes.md

      - name: Create the GitHub release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          gh release create "${GITHUB_REF_NAME}" \
            --title "${GITHUB_REF_NAME}" \
            --notes-file release-notes.md \
            --prerelease \
            ./artifacts/*
```

The release is marked `--prerelease` unconditionally. v0.1 is an alpha for structured human testing, and the release checklist cannot be completed until E12 and E14 land.

- [ ] **Step 4: Verify the release workflow parses and the changelog extraction works**

```bash
python3 -c "import yaml;yaml.safe_load(open('.github/workflows/release.yml'))"
printf '## [Unreleased]\n\n## [0.1.0-alpha.1]\n\n### Added\n\n- Harness.\n' > /tmp/changelog-probe.md
awk '/^## \[0.1.0-alpha.1\]/{flag=1; next} /^## \[/{flag=0} flag' /tmp/changelog-probe.md
```

Expected: no YAML error; the awk output contains `### Added` and `- Harness.`.

- [ ] **Step 5: Verify `bump-my-version` performs the intended edits**

```bash
bump-my-version bump --dry-run --verbose pre_number
```

Expected: reports changing `Directory.Build.props` and `CHANGELOG.md` from `0.1.0-alpha.1` to `0.1.0-alpha.2`, and no other file.

- [ ] **Step 6: Record the release plumbing in the changelog**

Replace the `## [Unreleased]` section of `CHANGELOG.md` with:

```markdown
## [Unreleased]

### Added

- Repository contract documents: `CLAUDE.md`, `README.md`, `LICENSE`, `CHANGELOG.md`.
- .NET 10 solution scaffold with central package management and warnings as errors.
- Shared kernel value objects: `Money`, `Percentage`, `IClock`, `IIdGenerator`.
- Filesystem blob writer with SHA-256 hashing.
- Invoices module seam with a module-owned `DbContext` and an explicit
  `--migrate` entrypoint.
- Blazor Interactive Server host with MudBlazor, English and German
  localization, and `/health` and `/alive` endpoints.
- Test harness: unit, architecture, integration (Testcontainers), compliance
  (golden-file comparer) and Playwright UI suites.
- Container image published with the .NET SDK container tooling and a Compose
  reference deployment.
- GitHub Actions CI, CodeQL, Dependabot, and a tag-triggered release pipeline
  publishing to GHCR with an SBOM and SHA-256 checksums.
- `docs/operations/RELEASE-CHECKLIST-v0.1.md`, the fail-closed human
  verification gate for the v0.1 release.
```

- [ ] **Step 7: Commit**

```bash
git add .bumpversion.toml .github/workflows/release.yml docs/operations/RELEASE-CHECKLIST-v0.1.md CHANGELOG.md
git commit --message "ci: add the tag-triggered release pipeline and version bumping

Releases trigger on tags only, so no push can publish by accident. Every
release is marked prerelease: v0.1 is an alpha for structured human testing.

RELEASE-CHECKLIST-v0.1.md is the fail-closed gate for the four independent
verifications TEST-STRATEGY.md requires. It cannot be completed until epics E12
and E14 land, which is the intended behaviour.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 15: Complete `CLAUDE.md` and verify the whole harness

Fills the commands section with commands that have actually been run in this cycle, then checks that every path and name `CLAUDE.md` mentions really exists.

**Files:**

- Modify: `CLAUDE.md`
- Modify: `docs/README.md`

**Interfaces:**

- Consumes: everything from Tasks 1–14
- Produces: the finished agent contract

- [ ] **Step 1: Run every command before writing it down**

```bash
dotnet build --configuration Release
dotnet test
dotnet format --verify-no-changes
dotnet test tests/Fakturenn.UnitTests
dotnet test tests/Fakturenn.ArchitectureTests
dotnet test tests/Fakturenn.IntegrationTests
dotnet test tests/Fakturenn.ComplianceTests
dotnet test tests/Fakturenn.UiTests
```

Record the actual pass counts. Any command that fails is fixed before it is documented.

- [ ] **Step 2: Replace the Commands section of `CLAUDE.md`**

Replace the placeholder section written in Task 1 with:

````markdown
## Commands

Requires the .NET 10 SDK, Docker, and PowerShell (`pwsh`) for Playwright
browser installation.

```bash
# Build everything, warnings are errors
dotnet build --configuration Release

# Every test suite
dotnet test

# One suite at a time
dotnet test tests/Fakturenn.UnitTests           # domain, fakes, NSubstitute boundary
dotnet test tests/Fakturenn.ArchitectureTests   # the six architecture rules
dotnet test tests/Fakturenn.IntegrationTests    # Testcontainers PostgreSQL, needs Docker
dotnet test tests/Fakturenn.ComplianceTests     # golden-file XML comparer
dotnet test tests/Fakturenn.UiTests             # Playwright, needs browsers installed

# Formatting, checked in CI
dotnet format --verify-no-changes

# Install Playwright browsers once
dotnet build tests/Fakturenn.UiTests
pwsh tests/Fakturenn.UiTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium

# Run the app locally
dotnet run --project src/Fakturenn.Web --urls http://127.0.0.1:5099

# Apply migrations — never happens automatically
dotnet run --project src/Fakturenn.Web -- --migrate

# Add a migration to a module (the module owns its migrations)
dotnet ef migrations add <Name> \
  --project src/Fakturenn.Modules.<Module> \
  --output-dir Persistence/Migrations

# Reference deployment
dotnet publish src/Fakturenn.Web --configuration Release /t:PublishContainer -p:ContainerImageTag=dev
docker compose up --detach
docker compose run --rm migrate
docker compose down --volumes

# Version bump; releases trigger on the resulting tag
bump-my-version bump pre_number
```

## Adding a new module

1. `dotnet new classlib --output src/Fakturenn.Modules.<Name> --name Fakturenn.Modules.<Name>`
2. `dotnet new classlib --output src/Fakturenn.Modules.<Name>.Contracts --name Fakturenn.Modules.<Name>.Contracts`
3. Add both to `Fakturenn.slnx` and reference them from
   `tests/Fakturenn.ArchitectureTests` so the rules see them.
4. The architecture rules apply automatically — they match on the
   `Fakturenn.Modules.*` name pattern. Do not add a rule per module.
````

- [ ] **Step 3: Add the new documents to the docs index**

Append to the reading order in `docs/README.md`:

```markdown
11. `operations/RELEASE-CHECKLIST-v0.1.md`
12. `superpowers/specs/` and `superpowers/plans/`
```

- [ ] **Step 4: Verify every path named in `CLAUDE.md` exists**

```bash
grep --only-matching --extended-regexp '(src|tests|docs)/[A-Za-z0-9./_-]+' CLAUDE.md \
  | grep --invert-match '<' \
  | sort --unique \
  | while read -r path; do
      if [ ! -e "$path" ]; then
        echo "MISSING: $path"
      fi
    done
```

Expected: no output. Any `MISSING:` line is a documentation bug — fix the path or create the file.

- [ ] **Step 5: Verify the architecture rules are not vacuous**

Run: `dotnet test tests/Fakturenn.ArchitectureTests --configuration Release`
Expected: PASS including `The_graph_is_not_empty`.

- [ ] **Step 6: Final full verification**

```bash
dotnet format --verify-no-changes
dotnet build --configuration Release 2>&1 | grep --extended-regexp 'Warning\(s\)|Error\(s\)'
dotnet test --configuration Release
git status --short
```

Expected: no formatting changes, `0 Warning(s)` and `0 Error(s)`, every suite green, and a clean working tree apart from the `CLAUDE.md` and `docs/README.md` edits about to be committed.

- [ ] **Step 7: Commit**

```bash
git add CLAUDE.md docs/README.md
git commit --message "docs: complete CLAUDE.md with verified commands

Every command in the commands section was run before it was written down. A
path check confirms every src/, tests/ and docs/ path the file names exists.

Adds the module-creation recipe, which points out that the architecture rules
match on a name pattern, so a new module needs no new rule.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage.** Every section of the design document maps to at least one task: §6 build order → Tasks 1 and 15; §7 scaffold → Task 2; §8 test architecture → Tasks 2, 3, 4, 6, 9, 10, 11; §8.1 rules → Task 6; §8.2 corpus → Task 10; §9 runnable shell → Tasks 7, 8, 12; §10.1 CI → Task 13; §10.2 CodeQL and Dependabot → Task 13; §10.3 release → Task 14; §11 testing the design → Task 6 Step 8, Task 10 Step 2, Task 9, Task 12 Step 5.

**Known gaps, stated rather than hidden.**

- The design named `NullDocumentStore`; the plan drops it. Reasoned above.
- `docs/operations/RELEASE-CHECKLIST-v0.1.md` was not in the design's file list. It is the concrete form of design §4's "documented manual checklist that fails closed", so it is an addition, not a change of scope.
- The SHA-256 constant in Task 4 Step 2 is explicitly marked as needing replacement, with the command that produces the real value in Step 3. This is the one deliberate fill-in in the plan, because fabricating a digest would be worse than requiring one command.
- Task 6's rules are enforced at type-dependency level by ArchUnitNET. A module could still declare an unused project reference to infrastructure without failing a rule, because the compiler records no type dependency for it. That is acceptable: an unused reference is not a dependency, and the moment anything uses it the rule fires.

**Type consistency.** `Money`, `Percentage`, `IClock`, `IIdGenerator`, `FakeClock`, `FakeIdGenerator`, `IFileSystem`, `StoredBlob`, `FilesystemBlobWriter`, `InvoiceId`, `InvoicesModule`, `FakturennArchitecture`, `InvoicesDbContext`, `FakturennWebApplication`, `SharedResource`, `WebAppFixture`, `PostgresFixture`, `XmlNormalizer`, `XmlComparison` and `NormalizingXmlComparer` are each defined in exactly one task and used with the same signature everywhere later.
