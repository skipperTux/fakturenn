# Implementation Notes

Facts discovered the hard way while building the testing-and-release harness
(SDK quirks, analyzer edge cases, ArchUnitNET traps, container behaviour).
Each note states the reasoning, not just the rule, so a later epic can tell
whether the reasoning still applies before changing anything. Verified
against the merged `main` branch; nothing here duplicates `CLAUDE.md` without
adding the underlying *why*.

## Environment and SDK

- `dotnet test <directory>` is rejected on SDK 10.0.110. Use
  `dotnet test --project <directory>` (or `--solution`) — see `CLAUDE.md`'s
  Commands section for the exact per-project invocations.
- `global.json` sets `"test": { "runner": "Microsoft.Testing.Platform" }`.
  Without it, `dotnet test` fails with "Testing with VSTest target is no
  longer supported." Do not remove it.
- `nuget.config` at the repo root clears inherited package sources and maps
  everything to nuget.org. Without it, restore fails with NU1507/NU1900
  because a machine-level NuGet config can expose an unreachable private feed
  that Central Package Management then rejects. New packages still resolve
  from nuget.org normally with the file in place.
- `dotnet new xunit3` generates `xunit.runner.json` wired through
  `<Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />`
  in the csproj. The file is required at build time — a Release build fails
  with MSB3030 (missing content file) without it. Every test project in the
  repo carries this pairing; keep it when creating a new one.
- `dotnet new` templates emit some generated files with a UTF-8 BOM, but the
  repo's `.editorconfig` declares `charset = utf-8` (no BOM) under `[*]`.
  `dotnet format` only reformats files inside a project's compile items, so
  it does not catch a BOM (or a missing trailing newline) on a project-root
  file like a `.csproj` or `global.json`-style file. Check generated files by
  hand after scaffolding a new project.

## Build and analyzer behaviour

`Directory.Build.props` sets `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`
and `AnalysisLevel=latest-recommended` repo-wide. Three things about what that
combination does and does not catch:

- **CA1707** (underscores in identifiers) becomes a build error under this
  combination. The test naming convention deliberately uses underscores
  (`CLAUDE.md`'s Testing section), so every test project needs a local
  `.editorconfig` setting `dotnet_diagnostic.CA1707.severity = none`. The same
  file also disables **CA1859** ("prefer concrete type"), because tests
  declare collaborators by their interface type on purpose, to exercise the
  same abstraction production code depends on. Copy
  `tests/Fakturenn.UnitTests/.editorconfig` into any new test project; all
  five existing test projects carry an identical copy of it.
- **Unused `using` directives are not caught by warnings-as-errors alone.**
  CS8019 (the compiler's own unused-using diagnostic) is not emitted by the
  command-line compiler, and IDE0005 (the analyzer equivalent) needs two
  things to actually fire during a build: `EnforceCodeStyleInBuild=true`, and
  `GenerateDocumentationFile=true` (IDE0005 does not run without a
  documentation file being generated). `Directory.Build.props` sets both, and
  the repo `.editorconfig` explicitly raises
  `dotnet_diagnostic.IDE0005.severity` to `error`, since it defaults to a
  non-blocking severity even when the diagnostic does run.
  `GenerateDocumentationFile=true` in turn activates CS1591 (missing XML doc
  comment) on every public member repo-wide, which is out of scope here, so
  `Directory.Build.props` suppresses it explicitly with `NoWarn`.
- **Naming rules in `.editorconfig` are evaluated in file order, first match
  wins.** The `private_const_fields_pascal_case` rule (matches
  `required_modifiers = const`) is declared before the general
  `private_fields_underscore` rule. If the order were reversed, a private
  `const` field would be flagged for not having an underscore prefix instead
  of being correctly recognized as PascalCase per StyleCop SA1303. Keep the
  more specific rule first when adding another naming rule.
- Generated EF Core migrations always emit block-scoped namespaces, which
  conflicts with the repo-wide `csharp_style_namespace_declarations =
  file_scoped:error`. Each module's `Persistence/Migrations/` folder carries
  its own `.editorconfig` that sets `csharp_style_namespace_declarations =
  file_scoped:none` for that folder only, so generated files are exempted
  instead of hand-edited after every `dotnet ef migrations add`. Apply the
  same pattern to a new module's migrations rather than editing generated
  code.
- `Directory.Packages.props` pins `Microsoft.EntityFrameworkCore.Relational`
  explicitly even though nothing references it directly, to resolve a real
  MSB3277 version conflict between the transitive version
  `Npgsql.EntityFrameworkCore.PostgreSQL` pulls in and the one the rest of
  the EF Core package set uses. Removing the pin reintroduces the conflict.

## Architecture-test pitfalls

`tests/Fakturenn.ArchitectureTests` (14 tests) enforces the six rules listed
in `CLAUDE.md`. The rules themselves, and which are live versus vacuous
today, are described there — the notes below are the ArchUnitNET mechanics
that make the rules actually work, none of which are obvious from the fluent
API's shape.

- **`NotDependOnAny(Types().That().ResideInAssemblyMatching(pattern))` is a
  silent no-op for any pattern that targets a third-party package.**
  `Types()` only matches types belonging to assemblies passed to
  `ArchLoader.LoadAssemblies`. MudBlazor, MimeKit, MailKit, PDFsharp and
  MigraDoc are never loaded, so that target set is always empty and
  `NotDependOnAny(<empty set>)` passes unconditionally — not vacuous in the
  usual "nothing to check yet" sense, but permanently dead, even after the
  dependency it is supposed to catch actually exists. This form silently
  killed all three of the technology-containment rules (MudBlazor, Mail,
  Documents) during development. The fix is
  `.Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(pattern)`,
  which evaluates the predicate against each dependency's target type as
  resolved from the *referencing* assembly's own metadata — not limited to
  what was loaded. `tests/Fakturenn.ArchitectureTests/TechnologyContainmentTests.cs`
  carries the full explanation next to the three rules that depend on it.
- **Never add a third-party package to `LoadAssemblies` to make a containment
  rule "see" it.** Loading MudBlazor directly to test against it was tried
  and produced 332 violations, because the package's own internals then
  become dependency sources subject to every rule. `FakturennArchitecture.Loaded`
  only ever loads first-party assemblies; third-party technology is matched
  by name pattern against unloaded dependency targets instead (the mechanism
  above).
- **`ResideInAssemblyMatching` matches the assembly's full CLR name**
  (`Fakturenn.Modules.Invoices, Version=0.1.0.0, Culture=neutral,
  PublicKeyToken=null`), not the short name, for any assembly that was not
  itself loaded. Patterns that need to match a bare short name must allow for
  the version suffix — e.g. `^Fakturenn\.Web(,.*)?$`, not `^Fakturenn\.Web$`.
  The distinction also cuts the other way: excluding a module's own
  `.Contracts` assembly from a "module implementations" selector needs
  `(?!.*\.Contracts(,|$))`, not a bare `\.Contracts$`, or the exclusion never
  matches a loaded assembly's full name and ends up excluding nothing.
- **A rule expressed as `NotDependOnAny(<a type-set that overlaps the
  subject>)` produces false positives from a type's dependency on itself.**
  ArchUnitNET records a constructor field assignment (`this._field = value`)
  or a static field initializer as a dependency from the type to itself. The
  module-to-module boundary rule's subject set (`Modules`) and its forbidden
  target set (`ModuleImplementations`) overlap for every implementation
  assembly, so a naive `Types().That().Are(Modules).Should().NotDependOnAny(ModuleImplementations)`
  flags almost every non-trivial class as depending on "another module" —
  verified with a minimal repro (`class C { public C(int v) { _v = v; } }`
  fails with "C does depend on C"). The actual rule
  (`ModuleBoundaryTests.No_module_depends_on_another_modules_implementation_assembly`)
  walks the same `Dependencies` collection by hand and compares owning module
  names instead of raw type identity, so a self-reference or an in-module
  reference is correctly excluded while a genuine cross-module dependency
  still fails.
- **The loader-omission test assumes a project's `.csproj` file name equals
  its assembly's short name.** `ModuleBoundaryTests.The_loader_omits_no_assembly_declared_under_src_in_the_solution`
  cross-checks `FakturennArchitecture.Loaded` against every project listed
  under `Fakturenn.slnx`'s `/src/` folder by comparing file names to loaded
  assembly names. A project that sets `<AssemblyName>` to something other
  than its file name fails this test loudly (it is a real assertion failure,
  not a silent gap) — but avoid the situation by not setting `<AssemblyName>`
  on `src/` projects.
- `ModuleBoundaryTests.RepositoryRoot` resolves the repository root through
  `[CallerFilePath]`, a compile-time absolute path baked into the assembly.
  Enabling `ContinuousIntegrationBuild`, `DeterministicSourcePaths`, or
  SourceLink rewrites that path to something like `/_/tests/...` on the build
  agent, and the loader-omission test then fails on `File.Exists`. Both
  `ci.yml` and `release.yml` carry a comment explaining this and deliberately
  do not set any of the three. Do not add `ContinuousIntegrationBuild=true`
  "by reflex" to a build or publish step.
- The subject-side exclusion for the MudBlazor rule is anchored on the exact
  short name `Fakturenn.Web` (`^Fakturenn\.Web(,.*)?$`). A future Blazor
  split — e.g. `Fakturenn.Web.Client` or `Fakturenn.Web.Components`, a common
  pattern for interactive-render-mode separation — would be treated as a
  MudBlazor violator under the current pattern, because it would no longer
  match the exclusion. Decide the intended pattern before splitting the web
  project, and remember the new assembly needs both a `Fakturenn.slnx` entry
  and a loader line regardless.

## Adding a module or test project

`CLAUDE.md`'s "Adding a new module" section lists the edit sites and marks
which two are enforced by a test versus which are conventions with no test
behind them — that list is authoritative and is not repeated here. Two
additions not covered there:

- A new **test project** (independent of adding a module) needs the same
  `.editorconfig` (CA1707/CA1859 suppressions) as every other test project,
  plus its own `xunit.runner.json` wired through the `<Content Include>` seen
  in the Build section above — `dotnet new xunit3` generates both, but check
  the BOM/trailing-newline hygiene noted in Environment and SDK before
  committing them.
- If the project sets `<AssemblyName>` to anything other than the `.csproj`
  file name, the loader-omission architecture test fails (see the
  Architecture-test pitfalls note above). Simplest fix: do not set
  `<AssemblyName>` on `src/` projects.

## Domain and storage primitives

- `GuidV7IdGenerator` uses `Guid.CreateVersion7()`, which orders ids to
  millisecond resolution only — .NET does not implement RFC 9562's optional
  monotonic-counter methods, so ordering within a single millisecond is
  unspecified. Never sort by id, or use one as a creation-order key; use an
  explicit timestamp or sequence instead.
- `FilesystemBlobWriter`'s root containment guard
  (`src/Fakturenn.Infrastructure.Storage/FilesystemBlobWriter.cs`) is lexical
  only: it resolves the target path with `Path.GetFullPath` and checks it
  starts with `root + DirectorySeparatorChar`. This does **not** resolve
  symlinks — a symlink planted inside the storage root can still point
  outside it. This is an accepted, documented limitation, not a sandbox
  guarantee; do not build code elsewhere that assumes the writer prevents a
  symlink escape.

## Persistence and resiliency

`DatabaseMigrator` (the `--migrate` entrypoint, `src/Fakturenn.Web/DatabaseMigrator.cs`)
and the runtime `DbContext` retry policy are two independent self-healing
mechanisms, on purpose:

- `Database:StartupTimeoutSeconds` (default 120) bounds `--migrate` only. It
  is a single wall-clock deadline measured with `Stopwatch` (a monotonic
  clock, immune to system-clock adjustment), not a retry count. A
  count-based budget was rejected because its real duration depends on the
  failure mode: an instantly-refused connection burns roughly
  `N * RetryDelaySeconds`, while a blackholed address burns roughly
  `N * (ConnectTimeout + RetryDelaySeconds)` per attempt — nearly five times
  longer for exactly the failure this feature exists to ride out (a database
  that is booting, not refusing). A wall-clock deadline gives the same
  guarantee regardless of which failure mode is in play.
- `Database:MaxRetries` (default 5) bounds only the runtime EF Core execution
  strategy (`EnableRetryOnFailure`, registered on the DI `DbContext` used
  once the app is serving traffic). It does not apply to `--migrate`. The
  `--migrate` entrypoint builds its own non-retrying `DbContext` deliberately,
  so `StartupTimeoutSeconds` remains the single authority over migration
  waits — nesting `EnableRetryOnFailure` inside the migration loop as well
  would retry each attempt internally before the loop's own deadline check
  ever saw the failure, turning one wall-clock budget into two independently
  enforced ones.
- **Failure classification, and why the catch order matters:** a
  `PostgresException` (the server answered and rejected the operation — a
  genuine migration error) fails immediately and stops the whole run; any
  other `NpgsqlException` (the connection itself could not be established)
  retries until the deadline. `PostgresException` derives from
  `NpgsqlException`, so it must be caught first, or every failure is treated
  as retryable.
- The migration connection gets a default connect timeout of 5 seconds
  (`DatabaseMigrator.ApplyDefaultConnectTimeout`) when the operator's
  connection string does not already set one, so a single attempt against a
  blackholed address cannot consume most of a short startup budget on
  Npgsql's own 15-second default. An explicit operator-supplied `Timeout=` is
  respected and left alone.
- **Npgsql trap:** `NpgsqlConnectionStringBuilder.ContainsKey("Timeout")`
  always returns `true`, because the builder eagerly pre-populates every
  known keyword with its default value on construction — it does not mean
  the keyword was present in the input string. Use `Keys.Contains("Timeout")`
  to test whether the operator actually specified it. CA1841's general
  "prefer `ContainsKey`" advice does not hold for this type; the code
  suppresses it locally with a comment explaining why.
- Migrations are applied in list order across a single shared deadline, not
  a fresh deadline per module — `DatabaseMigrator.RunAsync` takes
  `IReadOnlyList<Func<DbContext>>` for exactly this reason. Giving each
  context its own budget would let the total wait grow unboundedly with the
  number of modules.
- `/health` currently returns 200 against an unmigrated database, because
  the readiness check only verifies connectivity (`AddNpgSql`), and no module
  yet reads data that would fail against a missing schema. This is a known,
  accepted gap, not something to "fix" incidentally — the epic that ships the
  first database-backed page needs to either add a schema-version check to
  readiness, or make migrate-before-traffic a hard documented operational
  requirement.

## Containerisation

- There is no Dockerfile, deliberately — the image is built with
  `dotnet publish src/Fakturenn.Web --configuration Release /t:PublishContainer
  -p:ContainerImageTag=<tag>`. `.dockerignore` exists but is inert for this
  path, since `PublishContainer` packages the publish output directly rather
  than a Docker build context — do not assume it filters anything.
- **The base image must be `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`,
  not plain `-chiseled`.** The plain chiseled tag has no ICU and bakes in
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`, which overrides the project's
  own `InvariantGlobalization=false` (needed for German
  formatting/localization) and crashes `SetDefaultCulture` at startup with
  `CultureNotFoundException`. The cost is real: measured locally, the base
  tags are 124 MB (`chiseled`) versus 163 MB (`chiseled-extra`), a 39 MB
  difference, and there is no intermediate "chiseled + ICU" tag. The
  `-extra` image is still shell-less and non-root.
- **Do not set `<ContainerUser>$APP_UID</ContainerUser>`.** That is
  net8-era Dockerfile guidance; `$APP_UID` is a Dockerfile `ARG` substitution
  that does not exist in the SDK container publish pipeline. Setting it
  writes the literal string `$APP_UID` into the image's `User` field, and
  every container then fails to start ("unable to find user $APP_UID: no
  matching entries in passwd file") because OCI runtimes do not expand env
  vars in that field the way a Dockerfile's `USER $APP_UID` line does at
  build time. The chiseled(-extra) base image already runs as UID 1654
  (non-root) by default; verified via `docker inspect --format '{{.Config.User}}'`
  against the built image.
- **The app service has no Compose healthcheck, deliberately.** Compose
  healthchecks are exec-only, and the chiseled image has no shell and no
  `curl`, so any in-container probe command would be theatre — it would
  report "healthy" based on a command that cannot truthfully check anything.
  Readiness is instead checked externally via `/health`, the same mechanism
  Kubernetes uses via an `httpGet` probe, which also needs no in-container
  shell.
- Migration is a one-shot, profile-gated Compose service:
  `docker compose --profile migrate run --rm migrate`. The `--profile` flag
  is required — without it, Compose reports `missing services [migrate]`
  (verified against `podman-compose`), because a profile-gated service is
  excluded from a plain `docker compose up`. `fakturenn-app` deliberately
  does not `depends_on` the migrate service: Compose refuses to start a
  profile-gated dependency implicitly, so adding it would break a plain
  `docker compose up` rather than merely skipping the migration step.
- Npgsql logs a benign `Cannot load library libgssapi_krb5.so.2` warning at
  startup (verified in container logs) — an optional Kerberos/GSSAPI auth
  probe that is unused here, since this deployment authenticates with a
  password. Not an error to chase.

## Standing rulings

These are binding product decisions, not just implementation details — do
not revisit them without a new decision record:

- **Readiness (`/health`) must never throw and must never retry.** An
  unreachable or unconfigured database reports `Unhealthy` → 503 and answers
  immediately. `AddNpgSql` throws on a null/empty connection string by
  default, which would otherwise surface "not configured yet" as an
  unhandled 500 instead of the 503 a probe expects — the app registers an
  explicit `Unhealthy` check for that case instead of relying on the
  library's guard. Retrying inside the probe would race the orchestrator's
  own probe timeout; retries belong to the `--migrate` entrypoint and the
  runtime execution strategy (see Persistence and resiliency), never to the
  probe itself.
- **Liveness (`/alive`) must never touch the database.** A database outage
  must mark the instance unready, not cause Kubernetes (or any orchestrator)
  to restart it — restarting a healthy process does nothing to fix a
  database outage and only adds churn.
- **No external asset CDN, ever.** No Google Fonts, unpkg, jsdelivr, or any
  other external script/style/font host. Self-hosted static assets or system
  fonts only. This follows from the project's GDPR posture and its
  self-hosting promise; a scan for
  `fonts.googleapis|fonts.gstatic|cdn.|unpkg|jsdelivr` across the web
  project must stay empty.
