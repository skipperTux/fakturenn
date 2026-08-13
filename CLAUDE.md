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

Before touching the build, the test suites, the architecture tests, or the
container/CI setup, also read `docs/architecture/IMPLEMENTATION-NOTES.md` —
verified, topic-organised facts about SDK quirks, analyzer edge cases,
ArchUnitNET traps, and container behaviour that are easy to silently undo.

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
6. No dependency cycles between **namespace slices** under `Fakturenn.Modules.*`.
   The rule is `Slices().Matching("Fakturenn.Modules.(*)")`, and `(*)` captures the
   whole remaining namespace — so `Fakturenn.Modules.Identity.Authorization` and
   `Fakturenn.Modules.Identity.Persistence` are two separate slices. It therefore
   constrains namespaces *within* one module as well as across modules.

Rules 2 and 3 are **live and binding now**, not vacuous: their subject
selector is `DoNotResideInAssemblyMatching(<Mail|Documents pattern>)`, i.e.
"every assembly that is NOT Mail/Documents" — today that is all five loaded
assemblies. Task 6 proved this by making `Fakturenn.Modules.Invoices` depend
on real MimeKit and watching the rule fail. When `Fakturenn.Infrastructure.Mail*`
or `.Documents*` eventually appears, the rule does not newly switch on — it
gets **narrower**, carving out an exemption for the one assembly now allowed
to use the library.

Rules 5 and 6 are binding too. Rule 5 compares
`ModuleNameOf(origin) != ModuleNameOf(target)`, which needs a second module to
be satisfiable; `Fakturenn.Modules.Identity` supplied one in E02a Task 1, so it
has constrained real code ever since. Rule 6 was **never** vacuous:
`Fakturenn.Modules.Invoices` has carried three namespaces — and therefore three
slices — since it existed.

Rule 6 fired for the first time in E02a Task 8, on
`Identity.Authorization` ↔ `Identity.Persistence`: the planned layout put the
pure `PermissionCatalogValidator` under `Persistence/` while
`PermissionClaimsPrincipalFactory`, which needs `IdentityDbContext`, sat under
`Authorization/`. It was a real cycle, not a false positive, and it was resolved
by moving the validator into `Authorization/` and the claims factory into
`Persistence/` — leaving `Persistence → Authorization` in one direction only,
with `Authorization` as the pure policy vocabulary. Do not delete a rule because
it currently matches nothing.

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

## CI/CD status

The remote is `git@github.com:skipperTux/fakturenn.git`. Two of the three
workflows under `.github/` have run there; one has not.

- **`ci.yml`** — has run on `push` and on `pull_request`. Every run to date
  succeeded.
- **`codeql.yml`** — has run on `push`, on `pull_request` and on `schedule`.
  Every run to date succeeded.
- **`release.yml`** — **has never executed.** It triggers on a tag and no tag
  exists yet. It is still unproven, including the multi-arch container publish
  that has no local equivalent. Do not describe it as known-working.

**The green history covers `main` only.** The most recent CI run was against
`main` at `c861376`, which is the commit *before* the E02a identity foundation
branched. Everything E02a added — the 200-odd extra tests, Testcontainers at
this scale (the integration suite starts several PostgreSQL containers), and
the Playwright journeys — has only ever run on one developer workstation. Do
not assume the branch is green in CI because it is green locally; the first
push of `feat/e02a-identity-foundation` is the first time CI sees any of it.
Two known local-only workarounds are the likeliest sources of a first-run
surprise: `DOTNET_USE_POLLING_FILE_WATCHER=1` (an inotify limit on that one
host — see `IMPLEMENTATION-NOTES.md`; `ci.yml` now sets it on the `integration`
and `ui` jobs as insurance, not because a runner is known to need it) and the
browser-install command, which CI runs through `pwsh` and the dev host does not.

**`ci.yml` ran three of the five in-process suites until the final E02a review.**
`Fakturenn.Web.UnitTests` and `Fakturenn.Modules.Identity.UnitTests` were added
to the solution by this branch and never added to the workflow, so they compiled
in CI — catching a build break — while not one of their assertions ever ran.
That silently disarmed `The_claims_principal_factory_is_the_permission_factory`,
which exists precisely because a unit test over a class passes whether or not the
class is registered. Both are in the `build-test` job now. **When a new test
project is added, add it to `ci.yml` in the same change**; nothing fails if you
forget.

## Commands

Requires the .NET 10 SDK, Docker (or a Docker-compatible engine), and the
`Microsoft.Playwright.CLI` global tool for local Playwright browser installs.

```bash
# Build everything, warnings are errors
dotnet build --configuration Release

# Every test suite
dotnet test

# One suite at a time — SDK 10.0.110 rejects a bare directory; use --project
# Seven test projects. No test counts are listed here on purpose: they moved on
# almost every task of E02a and a stale number is worse than none, because it
# invites "something broke" against a baseline nobody updated. Take the current
# numbers from the run you just did.
dotnet test --project tests/Fakturenn.UnitTests                  # shared kernel and Invoices domain objects, fakes, the NSubstitute boundary
dotnet test --project tests/Fakturenn.Modules.Identity.UnitTests # pure Identity logic: permission handler, policy provider, enrolment-gate predicate, permission-catalogue validation
dotnet test --project tests/Fakturenn.Web.UnitTests              # host composition: forwarded-header parsing, DI registrations, the localization resource guards
dotnet test --project tests/Fakturenn.ArchitectureTests          # the six architecture rules, plus pattern guards, anti-vacuity and loader-omission checks
dotnet test --project tests/Fakturenn.IntegrationTests           # Testcontainers PostgreSQL and real hosts, needs Docker — see the polling note below
dotnet test --project tests/Fakturenn.ComplianceTests            # golden-file XML comparer
dotnet test --project tests/Fakturenn.UiTests                    # Playwright, needs browsers installed

# The integration suite needs this on this workstation. It builds many hosts, each
# adding a configuration file watcher, and the desktop session has already spent
# most of fs.inotify.max_user_instances — over the limit, whole classes fail for a
# reason unrelated to them. See IMPLEMENTATION-NOTES.md, "Environment and SDK".
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --project tests/Fakturenn.IntegrationTests

# Formatting, checked in CI
dotnet format --verify-no-changes

# Install Playwright browsers once (local host workflow — see note below)
dotnet tool install --global Microsoft.Playwright.CLI
playwright -p tests/Fakturenn.UiTests/Fakturenn.UiTests.csproj install chromium
# CI installs the same browser via `pwsh .../playwright.ps1 install --with-deps chromium`
# instead, because ubuntu-latest ships pwsh preinstalled and this dev host does not —
# do not "fix" one workflow to match the other; they are correct for their own host.

# Run the app locally
dotnet run --project src/Fakturenn.Web --urls http://127.0.0.1:5099

# Apply migrations — never happens automatically. Also seeds the system roles and
# refuses (exit 1) if the database stores a permission this version does not define.
dotnet run --project src/Fakturenn.Web -- --migrate

# Operator recovery entrypoints. They bypass authentication, the rate limiter, the
# enrolment gate and every permission policy on purpose — they exist for the case
# where those have locked the operator out — and are safe only because reaching them
# needs a shell on the host. Never map one as an endpoint.
# No flag takes a password as an argument: argv is visible in `ps` and lands in shell
# history, so every password is read from standard input. Passing one positionally or
# via --password exits 2 and changes nothing.
dotnet run --project src/Fakturenn.Web -- --create-admin <email>    # first/replacement administrator; takes the same advisory lock as /account/setup
dotnet run --project src/Fakturenn.Web -- --reset-password <email>  # also clears lockout and forces a change at next sign-in
dotnet run --project src/Fakturenn.Web -- --reset-mfa <email>       # clears the authenticator and the recovery codes; forces re-enrolment
dotnet run --project src/Fakturenn.Web -- --unlock-user <email>     # also rotates the security stamp, ending any session the account still holds
dotnet run --project src/Fakturenn.Web -- --list-users              # five columns, no secrets: no authenticator key, recovery code or password hash

# Add a migration to a module (the module owns its migrations)
dotnet ef migrations add <Name> \
  --project src/Fakturenn.Modules.<Module> \
  --output-dir Persistence/Migrations
# The generated files land in Persistence/Migrations/, which carries its own
# .editorconfig setting csharp_style_namespace_declarations = file_scoped:none,
# because the EF generator always emits block-scoped namespaces. Follow that
# pattern for a new module's migrations rather than hand-editing generated files.

# Reference deployment — pin a single RID locally; a multi-arch index needs a
# containerd image store this host does not have (CONTAINER1020 without it).
# The release workflow (release.yml) is the only place multi-arch is designed
# to be exercised, via a registry push rather than a local load — see the CI/CD
# status section above, though: that workflow has never actually run.
dotnet publish src/Fakturenn.Web --configuration Release /t:PublishContainer \
  -p:ContainerImageTag=dev -p:ContainerRuntimeIdentifiers=linux-x64 -p:RuntimeIdentifier=linux-x64
docker compose up --detach
docker compose --profile migrate run --rm migrate
docker compose down --volumes

# Version bump; releases trigger on the resulting tag
bump-my-version bump pre_number
```

## Adding a new module

Creating `src/Fakturenn.Modules.<Name>` and `src/Fakturenn.Modules.<Name>.Contracts`
touches more than two files. Exactly **two** of the edits below are enforced
by a test; the rest are conventions with no test behind them — get them wrong
and the build stays green.

**Enforced by `tests/Fakturenn.ArchitectureTests` (one test,
`The_loader_omits_no_assembly_declared_under_src_in_the_solution`, cross-checks
both together):**

1. Add both new projects to `Fakturenn.slnx` under the `/src/` folder.
2. Add one `typeof(<a public type in it>).Assembly` line to
   `FakturennArchitecture.Loaded` in
   `tests/Fakturenn.ArchitectureTests/FakturennArchitecture.cs` for **each** new
   assembly. There is no compiler error for a forgotten line — it silently
   exempts that assembly from every architecture rule.

**Conventions with no test enforcing them — verified by reading the actual
edit sites, not assumed:**

1. `tests/Fakturenn.ArchitectureTests/Fakturenn.ArchitectureTests.csproj` needs
   a `<ProjectReference>` to each new project, or the enforced step 2 above's
   `typeof(...)` line will not compile. (This one *is* forced, but by the
   compiler, not a dedicated test.)
2. If the module owns an EF Core `DbContext`:
   - `src/Fakturenn.Web/Fakturenn.Web.csproj` — add a `<ProjectReference>`.
   - `src/Fakturenn.Web/FakturennWebApplication.cs` — register the context in
     DI with `AddDbContext<...>`, mirroring `InvoicesDbContext`.
   - `src/Fakturenn.Web/Program.cs` — add one more factory to the
     `createMigrationContexts` array passed to `DatabaseMigrator.RunAsync`.
     `DatabaseMigrator.RunAsync` takes `IReadOnlyList<Func<DbContext>>`
     specifically so this is a one-line addition, not a signature change.
     Position in the array does **not** matter: E02a Task 7C measured it by
     migrating Identity before Data Protection against a clean database and
     everything still applied. The current order is for readability. Do not
     add a note claiming an ordering constraint that does not exist.
3. `tests/Fakturenn.ArchitectureTests/ModuleBoundaryTests.cs`'s
   `The_architecture_contains_the_assemblies_the_rules_govern` hardcodes an
   assembly-name list, but asserts it with `.Should().Contain(...)`, not an
   exact-set match — a forgotten new module name does **not** fail this test.
   Add it anyway so the anti-vacuity guard actually names every assembly it
   claims to guard.
4. Any test project that needs to compile against the new module's types
   directly needs its own `<ProjectReference>` — nothing adds this
   automatically. Today, for reference: `Fakturenn.UnitTests` references
   `Fakturenn.Modules.Invoices` and `.Contracts` for domain-object tests;
   `Fakturenn.Modules.Identity.UnitTests` covers that module's pure logic;
   `Fakturenn.IntegrationTests` references `Fakturenn.Modules.Invoices`,
   `Fakturenn.Modules.Identity` and `Fakturenn.Web`.
5. If the module registers anything in DI that the host must resolve to a
   *specific* concrete type, put the guard in `tests/Fakturenn.Web.UnitTests`.
   That is the host-composition test site, and it exists because a unit test
   over a class passes whether or not the class is registered — which is
   exactly how the missing `AddClaimsPrincipalFactory` call in E02a hid until
   `The_claims_principal_factory_is_the_permission_factory` was written to
   assert the resolved type.
6. If the module gets its own test project, give it a local `.editorconfig`
   suppressing CA1707 (underscore test names) and CA1859 (concrete-type
   preference for test collaborators declared by interface) — copy
   `tests/Fakturenn.UnitTests/.editorconfig`. No test checks for this file's
   existence; its absence just resurfaces CA1707/CA1859 as build errors the
   first time a test in that project uses the naming convention. All seven
   existing test projects carry an identical copy.
7. If the module gets EF Core migrations, copy the `.editorconfig` from
   `src/Fakturenn.Modules.Identity/Persistence/Migrations/` (see the migration
   command above) into its own `Persistence/Migrations/` folder. Use the
   Identity copy rather than the Invoices one: it also silences IDE0005, which
   the generated code needs to stay at zero warnings.
8. The containment and boundary rules (rules 1–6 above) apply automatically —
   they match on the `Fakturenn.Modules.*` name pattern. Do not add a rule per
   module.

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
