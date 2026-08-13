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
- **The integration suite can exhaust this host's inotify instances.**
  `fs.inotify.max_user_instances` is 128 and a desktop session spends most of
  it (editors, Docker/Podman, chat clients). Every `WebApplication.CreateBuilder`
  adds a configuration file watcher, and the suite builds several hosts — the
  fixtures plus every `MigrateEntrypointTests` subprocess. Over the limit,
  `FileSystemWatcher.StartRaisingEvents` throws `IOException("The configured
  user limit (128) on the number of inotify instances has been reached")` from
  inside `FakturennWebApplication.Build`, and whole classes fail for a reason
  that has nothing to do with them. Run the integration suite with
  `DOTNET_USE_POLLING_FILE_WATCHER=1`, which uses polling instead of inotify.
  This is the first thing to check when the integration suite fails
  inexplicably in bulk; it is the likeliest explanation for the intermittent
  `MigrateEntrypointTests` failures recorded during Task 9, and it is a
  property of this workstation, not of CI.

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
  seven existing test projects carry an identical copy of it.
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

## Logging

- **Serilog configuration resolves types by assembly-qualified name, and does
  it at runtime.** `Serilog.Settings.Configuration` accepts
  `"Namespace.Type, Assembly"` for any argument typed as an interface it can
  construct — `MessageFieldJsonFormatter` is selected that way — and also
  `"Namespace.Type::StaticMember, Assembly"` to use an existing instance, which
  is how `tests/Fakturenn.IntegrationTests/HostLogCapture.cs` attaches an
  in-memory sink to the running host through `Serilog:WriteTo` rather than
  through a test-only hook in `FakturennWebApplication`. A name that does not
  resolve throws from `ReadFrom.Configuration` (measured: dropping one letter
  from the formatter's type name produced "Type ... was not found"), so the
  failure is loud — but only in the deployment that selected that sink.
  `MessageFieldJsonFormatterTests` exists because nothing else would notice.
- **`src/Fakturenn.Web` references `Fakturenn.Infrastructure.Logging` with no
  C# in the project naming a type from it.** The reference is what puts the
  assembly next to the host so `Type.GetType` can find the formatter. Removing
  it leaves the build green; `MessageFieldJsonFormatterTests` reddens instead,
  because that test project references only `Fakturenn.Web` and reaches the
  formatter through this chain. Do not "clean up" the reference.
- **`ILogger<T>` cannot name a static class** (CS0718), so a
  `[LoggerMessage]` holder cannot be its own logger category. `AuthEventLog`
  takes an `ILoggerFactory` and resolves the fixed category
  `Fakturenn.Auth` instead — which is better anyway: the category is what an
  operator filters on, so it should not change when a call site moves to
  another class.
- **CA1873 rejects a method call as an argument to a logging method**,
  including a source-generated `[LoggerMessage]` delegate, because the argument
  would be evaluated even when the level is disabled. `Account(LoggerFor(f), …)`
  is an error; resolving the logger into a local first is not.
- **Every log event written inside a request carries `RequestId`,
  `RequestPath` and `ConnectionId`** from ASP.NET Core's own logging scope, on
  top of Serilog's `SourceContext` and `EventId`. A test that asserts on an
  event's property set has to allow for them. They are correlation handles, not
  credentials: `RequestPath` is `Request.Path` with no query string, and
  `ConnectionId` names a Kestrel connection rather than a session.
- **`SignInManager.GetTwoFactorAuthenticationUserAsync` must be called before
  the two-factor exchange, not after.** A successful
  `TwoFactorAuthenticatorSignInAsync` or `TwoFactorRecoveryCodeSignInAsync`
  deletes the two-factor cookie it reads, so afterwards there is no challenged
  user left to name and every successful second factor would be logged against
  "unknown".

## Browser tests

- **The shared UI fixture's first-run journey is order-sensitive, and the
  collection has to protect it.** `/setup` closes permanently the moment the
  first user row exists, so `AuthenticatedWebAppFixture.EnsureAdministratorAsync`
  can only ever succeed while no other test has created a user.
  `CreateEnrolledUserAsync` writes a user straight through `UserManager`, which
  closes it. Task 16 hit this deterministically: with the test-case order that
  build produced, `AuthorizationJourneyTests`' two user-creating tests ran
  first, and four tests in the collection then failed waiting for a
  `setup-email` field on a page that had already redirected. The fix is
  `await app.EnsureAdministratorAsync(_browser)` in that class's
  `InitializeAsync`, so the journey is always the first thing the collection
  does. A new test class in `SharedIdentityHost` that creates users needs the
  same line — and the failure it prevents does not point at its own cause.

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
- `dotnet ef migrations remove` needs `--force` in this repository. Without it
  the command opens the design-time connection to check whether the migration
  has already been applied, and the design-time string points at
  `localhost:5432`, where nothing is listening. The failure looks like a
  database problem; it is not. Regenerating a migration is
  `remove --force` followed by `add`.
- Regenerated migration files come back with a UTF-8 BOM every time.
  `dotnet format` strips it from the migration itself but **not** from the
  `.Designer.cs` or the model snapshot, because it skips files marked
  auto-generated. Those two need stripping by hand after every regenerate, or
  the next `dotnet format --verify-no-changes` fails on CHARSET. Verify with
  `head -c3 <file> | od -An -tx1` — `ef bb bf` means the BOM is still there.
- **A unique index serialises only rows that collide on the indexed value, so it
  is not a general check-then-act guard.** E02a Task 9's `POST /account/setup`
  shipped a comment claiming Identity's unique index on `NormalizedUserName`
  serialised concurrent first-run posts. Measured: four concurrent posts with
  **distinct** e-mail addresses produced **four administrators** (reproduced in an
  integration test and with concurrent `curl` against real PostgreSQL), while the
  same four posts sharing **one** address produced exactly one user. The index was
  doing real work in the case nobody attacks and none in the case that matters.
  When a guard has to cover "at most one row in this table, whatever its
  contents", the mechanism is a lock, not an index — Task 9 uses
  `pg_advisory_xact_lock` on a fixed key as the first statement inside the
  transaction, because it is transaction-scoped (released on commit or rollback,
  no cleanup path) and records no state, so an instance that ends up with zero
  users correctly reopens `/setup` instead of staying bricked behind a marker row.
- **An explicit transaction on a `DbContext` configured with
  `EnableRetryOnFailure` must go through `Database.CreateExecutionStrategy()`.**
  `BeginTransactionAsync` otherwise throws `InvalidOperationException` telling you
  to use the configured execution strategy. Both `IdentityDbContext` and
  `InvoicesDbContext` enable the retry, so every explicit transaction in the
  application needs the `strategy.ExecuteAsync(async token => { ... })` wrapper.
  Do not remove the retry to avoid the wrapper — it is a deliberate feature (see
  the runtime/`--migrate` split above). Note the delegate can re-run: build the
  entities *inside* it rather than closing over objects a first attempt mutated.
- **Wrapping a handler in a transaction can serialise it by accident, which
  manufactures a false green.** Task 9's race test passed with the advisory lock
  deleted, until the cause was found: the racers were also the first writers to
  `RoleSeeder`, so the first one's uncommitted unique-index entry on the role name
  blocked the others, then failed them with a duplicate key — rolling back their
  *user* inserts too. The fix was to make the fixture seed the roles first, which
  is what `--migrate` does before the instance ever serves traffic. When a
  concurrency test goes green, delete the guard and confirm it goes red; if it
  stays green, the serialisation is coming from somewhere you did not intend.
- A change to keys, schema name or column facets is caught only by EF's
  `PendingModelChangesWarning`, which fails *every* test in the suite on the
  migration guard rather than the one test that cares. That makes it useless
  as a mutation signal: regenerating the migration silences it, which is
  exactly what a careless change would do. When proving such a guard is
  load-bearing, regenerate against the mutated model first, then observe which
  test actually fails. Task 4 did this for `RolePermission`'s composite key.

## Host and forwarded headers

- `IPNetwork` is ambiguous. `System.Net.IPNetwork` is the real one;
  `Microsoft.AspNetCore.HttpOverrides.IPNetwork` is obsolete and still resolves.
  Use a `using` alias rather than hoping the right one wins.
- `ForwardedHeadersOptions.KnownIPNetworks` is backed by `DualIPNetworkList`,
  which implements the generic enumerable for **both** `IPNetwork` types plus
  the non-generic one. An assertion library reaches the non-generic path and
  compares against the *obsolete* type, so a direct assertion fails for reasons
  that have nothing to do with the values. Call `.ToArray()` before asserting.
- `CA1848` and `CA1873` are build errors here, so log calls in the host go
  through `[LoggerMessage]` partial methods — see `DatabaseMigrator` for the
  shape. An `IsEnabled` guard does **not** satisfy CA1873; hoist any
  `string.Join` or similar argument into a local instead.
- The state of the RFC 7239 ecosystem, checked against primary sources. Do not
  re-research this, and do not repeat the corrected claims:
  - **ASP.NET Core cannot consume `Forwarded`.** `ForwardedHeaders` covers only
    `XForwardedFor|XForwardedHost|XForwardedProto|XForwardedPrefix`, and
    renaming the header via `ForwardedForHeaderName` does not help, because the
    parser still expects X-Forwarded-For's comma-separated list rather than
    `for=…;proto=…` parameter syntax. dotnet/aspnetcore#5978 ("Support the
    `Forwarded` header") was filed 2016-01-27 and is still open, milestone
    Backlog, labelled `severity-minor`. The shim is permanent, not a stopgap.
  - **HAProxy ≥ 2.8 (June 2023) is the only first-class emitter** among widely
    deployed open-source proxies. Opt-in via `option forwarded`; the bare form
    expands to `proto for` and emits `forwarded: proto=http;for=127.0.0.1` — a
    **real address**. Set in `defaults`/`listen`/`backend`, ignored in
    `frontend`, and **independent of `option forwardfor`**: enabling only the
    standards-compliant one sends no `X-Forwarded-*` at all.
  - nginx, ingress-nginx, Traefik, Caddy, Apache httpd and Envoy do **not**
    emit it first-class. Traefik specifically does not — corrected against the
    `traefik v3.3.0` source after the opposite was asserted here.
  - **No proxy consumes `Forwarded`** for client-IP determination. HAProxy is
    the partial exception: it ships `rfc7239_field`, `rfc7239_is_valid`,
    `rfc7239_n2nn` and `rfc7239_n2np` converters for explicit parsing, but does
    not use the header for its own client IP automatically.
  - The obfuscation hazard is therefore **implementation-specific, not
    general**. RFC 7239 section 8.3 recommends an obfuscated default and YARP
    follows it (`ForFormat` defaults to `Random`); HAProxy does not. Neither
    one generalises to "emitters obfuscate by default".
- `ForwardedHeaderNormalizer` translates `Forwarded` into the `X-Forwarded-*`
  headers and lets the built-in middleware evaluate trust unchanged. It grants
  nothing: trust is still anchored on the connection's peer address.
- In `ForwardedHeaderNormalizer.TryReadNode`, only the length check is
  load-bearing. `IPAddress.TryParse` already rejects RFC 7239's obfuscated
  (`_gazonk`) and `unknown` node forms — measured by deleting each predicate
  separately and watching nothing go red. The predicates are kept as
  documentation; do not cite them as the mechanism.
- Measured against the RFC's node grammar (`nodename [":" node-port]`, either
  half possibly obfuscated), anchored on HAProxy's real output —
  `tests/Fakturenn.Web.UnitTests/ForwardedHeaderNodeFormTests.cs`:
  - Address-bearing nodenames translate in both families, with or without a
    port, and with an obfuscated *port* (`for="[::1]:_jDw5Cf3tQ"`) — the
    bracket parse cuts everything after `]`, so an obfuscated port is discarded
    like any other port.
  - Obfuscated nodenames (section 6.3) and the literal `unknown` (section 6.2)
    produce no `X-Forwarded-For`, with or without a port. Correctly so — none
    of them contains an address. The operational consequence is recorded in
    `docs/operations/DEPLOYMENT-BASELINE.md`: `Connection.RemoteIpAddress`
    stays at the proxy, and the account rate limiter partitions on that.
  - Parameter order within an element is irrelevant; `proto=…;host=…;for=…;by=…`
    reads all three we want. `by=` is ignored — trust is anchored on the peer
    address, so a self-reported identity adds nothing.
- The precedence rule ("`X-Forwarded-For` present ⇒ `Forwarded` ignored
  entirely") is a tie-break, not a precondition. A request carrying only
  `Forwarded` is honoured end to end — verified, and the normal case rather
  than an edge one, because an emitter of `Forwarded` need not send
  `X-Forwarded-*` beside it (HAProxy's independence above; YARP's `Forwarded`
  transform disables its `X-Forwarded` transforms).
- Clearing `KnownProxies` **and** `KnownIPNetworks` and leaving both empty is
  the documented way to disable trust validation entirely and honour
  `X-Forwarded-*` from any source. The code must never clear without adding
  entries back. The guard that enforces this is an early `return`, not the
  `InvalidOperationException` below it — that throw is unreachable by
  construction and is an assertion for the reader, not the mechanism.

## Static SSR pages, account forms and cookies

- **A Blazor static-SSR page endpoint answers POST as well as GET.** Mapping a
  minimal-API `MapPost` on a page's own route therefore produces two candidates
  with identical precedence, and every post fails with
  `AmbiguousMatchException` — not at startup, at request time. The E02a plan
  specified `GET /account/enrol-totp` and `POST /account/enrol-totp`, and that
  pairing cannot work. The convention in this application is that **a form's
  action is never its page route**: `/setup` posts to `/account/setup`, and
  `/account/enrol-totp` posts to `/account/enrol-totp/verify`. Spec section 8's
  flow diagram lists the page routes, not the endpoint routes.
- `CookieOptions` has **no** `SecurePolicy`. That property is on `CookieBuilder`,
  which configures a whole scheme's cookie; a one-off `Response.Cookies.Append`
  takes `CookieOptions`, whose equivalent is the boolean `Secure`. The
  `SameAsRequest` behaviour the application cookie uses is written out by hand as
  `Secure = http.Request.IsHttps`. Setting it unconditionally would silently drop
  the cookie on the reference Compose deployment, which serves plain HTTP.
- `Response.Cookies.Append` URL-escapes the value, so a delimiter inside it
  survives a round trip escaped. Useful when asserting that a value is protected:
  the codes are joined with `;`, and a `;` reaching the cookie means the join was
  written out rather than the ciphertext. Data Protection payloads are base64url,
  whose alphabet contains no `;`, so that assertion cannot false-positive.
- Naming a loop variable `code` in a `.razor` file breaks the build: `@code` in
  markup is parsed as the `@code` directive (RZ2005/RZ1017), wherever it appears
  on the line.
- **A show-once cookie can only be proved with a real cookie store.** The
  mechanism is a `Set-Cookie` deletion, so a test that simply declines to resend
  the cookie passes whether or not the deletion exists. `SetupHostFixture`'s
  `CreateClient(CookieContainer)` overload exists for this; deleting the
  `Cookies.Delete` call reddens the show-once test only because the container
  honours the expiry.
- **Do not assert on a guessed Identity recovery-code alphabet.** An earlier
  version of `EnrolTotpTests` matched `[BCDFGHJKMNPQRTVWXY2346789]{5}-…`, which
  omits `5`, and silently found six of ten codes. The alphabet is an internal
  detail of `UserManager`; extract the codes from the rendered markup instead.

## Antiforgery, the security stamp, and forms that sit open

Everything below was measured against a real instance during the E02a defect
review, after a human hit in ninety seconds what 275 green tests had missed.
The observed failure: first-run setup, sign in, enrolment page rendered, TOTP
code typed and submitted 76 seconds later, `POST /account/enrol-totp/verify`
logged as a 500 with
`AntiforgeryValidationException: The provided antiforgery token was meant for a
different claims-based user than the current user`, and the next page load
already signed out.

- **The cause was a security-stamp rotation on a `GET`, not anything about
  antiforgery's binding.** `EnrolTotp.razor` calls
  `UserManager.ResetAuthenticatorKeyAsync` when the account has no authenticator
  key yet, and that rotates the security stamp — while the cookie in the browser
  still carries the stamp it was issued under. `ValidationInterval` is one
  minute, so for the first minute nothing revalidates and the session looks
  healthy; on the first request after it the validator finds a mismatch,
  **rejects the principal and signs the user out**. The antiforgery token the
  page rendered had been bound to a signed-in caller who is no longer there, so
  the post is refused with a message about a *different* user — which is
  accurate and maximally misleading, because it points at the token rather than
  at the session. The fix is one `SignInManager.RefreshSignInAsync` call beside
  the reset, the mirror image of the one the verify handler has carried since
  Task 10 for `SetTwoFactorEnabledAsync`.
- **Antiforgery in .NET 10 binds the token to the user id alone, and a plausible
  reading of the old sources says otherwise.** `DefaultClaimUidExtractor` looks
  for `sub`, then `ClaimTypes.NameIdentifier`, then `ClaimTypes.Upn`, and returns
  the first it finds; there is **no** hash-every-claim fallback any more. The
  ASP.NET Core 1.x/2.x implementation did have one — it used `NameIdentifier`
  *plus* an `identityprovider` claim, and hashed all claims when both were not
  present — and reasoning from that version predicts that anything regenerating
  the principal changes the binding. It does not. Measured: a
  `POST /account/change-password/submit` two minutes after its page was rendered
  succeeds, on an account whose stamp nothing had rotated.
- **A regenerated principal really does lose the `amr` claim, and it is
  harmless.** `SignInManager` adds `amr=pwd` (password) or `amr=mfa` (second
  factor) when it issues the cookie; `SecurityStampValidator.SecurityStampVerified`
  rebuilds the principal with `CreateUserPrincipalAsync`, which does not — the
  framework's own source carries the comment "REVIEW: note we lost login
  authentication method". Confirmed by dumping both claim sets. Nothing in this
  application reads `amr`, and the claim uid does not depend on it, so no fix is
  warranted; do not "restore" it through `OnRefreshingPrincipal` without a
  reader that needs it.
- **A failed antiforgery validation used to tell the operator and the user two
  different things.** Every handler binds an `IFormCollection` parameter, so
  `RequestDelegateFactory` throws `BadHttpRequestException` wrapping the
  `AntiforgeryValidationException`. Nothing handled it. Under Development the
  wire answer is **400** with the developer exception page's body, while
  `UseSerilogRequestLogging` — which catches, logs and rethrows before that page
  sees the exception — records **"responded 500"**. Under Production it is a bare
  400 with no body. `AntiforgeryFailureMiddleware` now sits immediately behind
  `UseAntiforgery`, reads `IAntiforgeryValidationFeature` (which the framework's
  middleware sets without rejecting anything itself) and redirects to the page
  that drew the form. The post is still refused: nothing downstream runs.
- **Never echo a refused post's fields back to the browser, and the framework
  will stop you.** `FormFeature.Form` throws
  `InvalidOperationException("This form is being accessed with an invalid
  anti-forgery token. Validate the IAntiforgeryValidationFeature on the request
  before reading from the form.")` once that feature reports a failure —
  measured, as a 500 from a first version of `AccountForms` that tried to
  preserve the typed values on this path. The guard is right: a post that failed
  antiforgery is exactly the post that may have been composed by somebody else's
  page. `AccountForms.Rejected` (handler side, token already accepted) carries
  fields; `AccountForms.Expired` (antiforgery side) carries only a sentinel.
- **The `?error=` value reaching a page is a sentinel where the page is
  anonymous.** `FormError` maps `expired` to a localized sentence and otherwise
  shows either a fixed sentence the page supplies or the value itself. The
  sign-in page must be in the first group: it shows one sentence for every
  failure so it cannot become an account oracle, and rendering arbitrary
  query text on a sign-in page would hand whoever composed the URL a phishing
  lever.
- **`AccountForms`' endpoint-to-page table has a test behind it.**
  `AccountFormsTests.Every_account_post_endpoint_names_the_page_that_renders_its_form`
  walks the host's real route table both ways. A new `/account` post without an
  entry would otherwise throw from `AccountForms.Rejected` the first time a real
  user's submission was refused. The scan skips Razor component pages — a
  static-SSR page endpoint answers POST as well as GET, so `/account/denied`
  turns up in a naive POST-route list and needs no entry.
- **Testing this needs an aged ticket, and ageing one is not mocking a clock.**
  `SetupHostFixture.AgeAuthenticationCookie` unprotects the cookie the
  *application* issued, moves its `IssuedUtc` back and re-protects it; the
  validator then does its own real `UtcNow - IssuedUtc` comparison against the
  real one-minute interval. `CreateAuthenticationCookieAsync(user, issuedUtc)`
  is not a substitute here: it mints a fresh principal from the claims factory,
  which is a different claim set from the one a sign-in endpoint writes.
- **Why the whole suite missed it.** Every test and every Playwright journey
  submits its form milliseconds after rendering it, and the failure needs the
  submission to arrive after the interval has elapsed. A green suite is not
  evidence about elapsed-time behaviour unless some test lets time elapse.

## Identity, sign-in and lockout

- **`IdentityDbContext` derives from `IdentityUserContext<ApplicationUser, Guid>`,
  not from Identity's own `IdentityDbContext`.** The stock base class brings
  `AspNetRoles` and `AspNetUserRoles`, and this application owns its own `Roles`,
  `UserRoles` and `RolePermissions` tables because E02b needs an `OrganizationId` on
  the assignment. Switching to the stock base would create a second, parallel role
  system that nothing reads — verified by grepping the migration: no `AspNetRoles`
  table exists anywhere in the schema. The three stock user tables (`AspNetUsers`,
  `AspNetUserClaims`, `AspNetUserTokens`, plus logins) are kept.
- **The application is static SSR by default, and every page relies on that.**
  `AddInteractiveServerComponents()` and `AddInteractiveServerRenderMode()` are both
  registered, but **no component declares `@rendermode`**, so no circuit is ever
  negotiated. Two things depend on it. First, all the account forms post to real
  endpoints (`AccountEndpoints`) rather than handling submission in a component,
  because `SignInManager` issues the authentication cookie by writing a `Set-Cookie`
  header to the HTTP *response*, and a component running inside an established circuit
  is handling a WebSocket message, not a request whose response headers are still
  open. This is a structural constraint, not a tuning problem: it is why every form in
  `Components/Account/` posts to a route rather than binding an `@onclick`. Second,
  `EnrolmentGateMiddleware` does not allowlist `/_blazor` (see the
  enrolment-gate section). Adding `@rendermode InteractiveServer` to a page is
  therefore not a local change; it has to meet both decisions.
- **The `account` rate limiter partitions on identity plus client address, and
  never on the address alone.** `AccountRateLimitPartition.KeyFor` resolves the
  best identity available, in this order: the signed-in user's id claim
  (`UseAuthentication` runs before `UseRateLimiter`, so the application cookie is
  already a principal); the user id inside Identity's two-factor cookie,
  unprotected through `CookieAuthenticationOptions.TicketDataFormat` for the
  `Identity.TwoFactorUserId` scheme; the `email` form field; then nothing.
  **Any endpoint added to the `/account` group inherits this key — do not write a
  new partitioner.**
  - The first version read only the `email` form field, which only
    `/login/submit` and `/setup` carry. `/login-2fa/submit`,
    `/login-recovery/submit`, `/change-password/submit` and `/logout` therefore
    keyed on the address alone. That is a **self-DoS**, and worse than it looks
    because the documented safe default configures no forwarded-header trust: every
    client behind a reverse proxy or a NAT then presents the proxy's address, so one
    ten-per-minute budget covers every user of the instance. A small office behind
    one address would lock itself out of its own second factor, with no
    configuration mistake to point at.
  - Measured both ways. Two users behind one address, six failing
    `/change-password/submit` posts each: with the identity key every response is
    302; reverting the key to `$"|{address}"` turns the second user's six into 429
    and takes eleven other tests with it, which is the same instance-wide
    contention the defect causes in production.
  - The address stays in the key so one compromised account cannot exhaust
    another user's budget, and address-only remains the fallback for a caller with
    no identity at all — that case is genuinely anonymous and the address is the
    only thing left.
  - The two-factor **ticket** is unprotected rather than its ciphertext being used
    as the key: a re-issued cookie is a different string for the same user, so
    keying on the ciphertext would let a caller reset their own budget by posting
    the password again. `SecureDataFormat.Unprotect` answers null for a value it
    cannot read, so a tampered cookie falls through instead of throwing inside the
    limiter — covered by a test, because a throw there would be a 500 on an
    unauthenticated endpoint.
  - The user id lives under **`ClaimTypes.Name`** in the two-factor scheme, not
    under `IdentityOptions.ClaimsIdentity.UserIdClaimType` (`NameIdentifier`), which
    is what the application cookie uses. `SignInManager` writes and reads it that
    way. Verified against a live ticket, not taken from documentation.
- **`AccessFailedCount` is not a running record of failed attempts.**
  `UserManager.AccessFailedAsync` increments it, and when the count reaches
  `MaxFailedAccessAttempts` it sets `LockoutEnd` **and resets the counter to zero in
  the same call**. So a locked-out account reads `AccessFailedCount = 0`, exactly
  when a reader most expects it to read 5. An administration page or an audit view
  that shows "failed attempts" from this column would show `0` at the moment it
  matters; use `LockoutEnd` to answer "is this account locked", and log the
  attempts if a count is genuinely wanted. Measured in E02a Task 11 — an assertion
  of `Be(5)` failed with `found 0`.
- `SignInManager.SignInWithClaimsAsync` assigns `HttpContext.User` as well as
  writing the cookie to the response, so a handler **can** read
  `UserManager.GetUserAsync(http.User)` immediately after a successful
  `TwoFactorAuthenticatorSignInAsync` and get the user back. This is not the
  behaviour the response-cookie mechanism suggests, and it was measured rather than
  assumed: rewriting the handler to capture the two-factor user *before* the sign-in
  left every test green, so both forms work.

## The enrolment gate

`EnrolmentGateMiddleware` (`src/Fakturenn.Web`) plus the pure path predicate
`EnrolmentGate.IsAllowedWhilePendingObligations`
(`src/Fakturenn.Modules.Identity/Authorization`). Added in E02a Task 12.

- **Every destination the gate redirects to must be on the allowlist.** Both
  `MustEnrolTotp` and `MustChangePassword` are enforced, so **both**
  `/account/enrol-totp` and `/account/change-password` are allowed. Dropping either
  one is not a "blocked page" — it is an **infinite redirect loop**, because the gate
  answers its own redirect with the same redirect. Measured by deleting the
  change-password entry: `/ -> /account/change-password -> /account/change-password
  -> /account/change-password -> ...`. A test asserting only "blocked pages redirect"
  stays green through this, which is why
  `EnrolmentGateTests.The_forced_password_change_settles_on_a_page_rather_than_looping`
  follows the chain and fails on a repeated path instead of inspecting one hop.
- **Order when both flags are set: TOTP enrolment first, password change second.**
  An administrator-created account carries both. Enrolling first means the
  replacement password is chosen by an account that already has two factors.
- **`/_blazor` is deliberately NOT allowlisted, and that is a decision, not an
  oversight.** `AddInteractiveServerRenderMode()` is registered and
  `MapRazorComponents` runs after the gate, so circuit requests do reach it and are
  redirected. This is harmless today only because **no component declares a render
  mode** — `<Routes />` has none — so no circuit is ever negotiated. The moment
  somebody adds `@rendermode InteractiveServer`, a gated user's page will load and
  nothing on it will work, which looks like a broken component rather than a gate
  decision. Failing closed is still correct: allowlisting the circuit endpoint would
  let a gated user render any interactive page server-side and bypass the gate
  entirely. Whoever introduces interactivity has to decide what a half-enrolled
  user's circuit may do.
- **Static-asset allowlist entries would be dead code, and this was measured against
  a `dotnet publish` output rather than the test host.** Instrumenting the gate to
  log every path it sees: `/app.css`, `/_framework/blazor.web.js` and
  `/_content/MudBlazor/MudBlazor.min.css` all answered 200 and **never appeared in
  the log** — `UseStaticFiles` runs before `UseAuthentication`, and therefore before
  the gate, and short-circuits them. `/favicon.ico` and `/css/app.css` *do* reach the
  gate, but only because nothing serves them (this application has neither), so
  entries for them would convert a redirect into a 404 and nothing more. All four
  prefixes from the original plan were therefore removed.
- **The integration host is NOT representative for static files.** It is built from
  the test project's content root, which has no `wwwroot`, and it runs as Production
  so `UseStaticWebAssets` never loads the manifest either. Every asset request there
  falls through to the gate and is redirected — the opposite of a deployment. Do not
  write an integration test asserting "a gated user still gets the stylesheet"; it
  will fail, and fixing it by re-adding the allowlist entries would pin the test
  host's artefact instead of the application's behaviour.
- **The `IsAuthenticated` early return is a performance guard, not the correctness
  guard.** Deleting it leaves all 60 integration tests green, and the honest reason is
  that `UserManager.GetUserAsync` answers null for an anonymous principal and the
  gate's null branch then declines to act — something else was providing the
  guarantee. What the early return actually buys is one avoided database round trip
  per anonymous request. The mutation that *does* redden is treating a null user as
  gated, and it reddens **30 of 60**, including every `SignInTests` and
  `SetupEndpointTests` case: a gate that acts on anonymous callers closes sign-in and
  first-run setup, which is the whole application.

## Administrator user management

`POST /account/admin/{create-user,reset-password,clear-mfa,set-lockout}` plus the page
at `/admin/users` (`src/Fakturenn.Web/Components/Admin/Users.razor`). Added in E02a
Task 13.

- **Lockout does not rotate the security stamp, so `set-lockout` does it explicitly,
  and this is the one line the whole endpoint exists for.** `UserManager` rotates the
  stamp on a password change or reset and on a two-factor change; `SetLockoutEndDateAsync`
  does not, and it goes through `UpdateUserAsync`, which does not either. The stamp
  validator checks that the cookie's stamp still **matches** — and it does — so without
  `UpdateSecurityStampAsync` a locked user keeps a working session for as long as the
  cookie lives. Measured: deleting that one call reddens
  `AdminUserManagementTests.Locking_a_user_ends_their_existing_session` and nothing else.
- **`SecurityStampValidatorOptions.ValidationInterval` does not save you here.** It
  decides how *often* the stamp is checked, not what the check finds. A one-minute
  interval on a stamp that never changes is a check that always passes.
- **Testing "the session ended" needs an aged ticket, and one cookie jar is a trap.**
  The validator revalidates only once the interval has elapsed since `IssuedUtc`, so a
  freshly minted cookie sails through the next request no matter what the endpoint did —
  hence `SetupHostFixture.CreateAuthenticationCookieAsync(user, issuedUtc)`. Worse, a
  *successful* revalidation sets `ShouldRenew`, and the handler then reissues the cookie
  with a current `IssuedUtc`. A test that probes the session before locking and after
  locking through **one** `CookieContainer` therefore hands the second request a freshly
  issued ticket that skips revalidation entirely, and fails against correct code. Use a
  fresh container holding the same ticket value for each probe.
- **Neither `SetTwoFactorEnabledAsync` nor `ResetAuthenticatorKeyAsync` touches the
  recovery codes.** Measured: an account with nine unspent codes still read nine after
  both calls. They are unreachable in that state — the recovery endpoint needs the
  two-factor cookie that only a two-factor challenge issues, and `TwoFactorEnabled` is
  now false — and re-enrolment would replace them, because
  `GenerateNewTwoFactorRecoveryCodesAsync` **replaces** the stored set rather than adding
  to it. `clear-mfa` nonetheless calls it with a count of **zero** to wipe them outright:
  an administrator clearing two-factor is often doing so because the old factors may be
  in somebody else's hands, and "unreachable while another flag stays false" is a weaker
  promise than "gone".
- **A missing permission is a 302, not a 403.** `ConfigureApplicationCookie` sets
  `AccessDeniedPath`, so the cookie handler turns the authorization middleware's forbid
  into a redirect to `/account/denied` — as an **absolute** URL with a `ReturnUrl` query,
  unlike the handlers' own relative `Results.Redirect("/admin/users")`. Assert on the
  path, not on the raw `Location` string, or the assertion is about URL formatting.
- **There is no last-administrator guard, and that is deliberate.** An
  `AdministratorGuard.WouldRemoveLastAdministrator` existed through most of E02a,
  unit-tested as a pure function and referenced by nothing but its own tests. It was
  **deleted in the final review**, along with those tests: E02a has no path that removes
  a role or a permission — roles are seeded by `--migrate` and edited by SQL — so the
  guard had nothing to guard, and CLAUDE.md's YAGNI rule says a type only useful in a
  later epic belongs in that epic. E02b adds role management and should reintroduce it
  there, next to its caller. Note what the guard would *not* have covered even then:
  locking the last administrator is **permitted**, which is why `--unlock-user` exists,
  so `set-lockout` must not call it.
- **`Role.IsSystemRole` is write-only in E02a.** `RoleSeeder` sets it; nothing in `src/`
  reads it. It is a marker for E02b's role management, not enforcement, and any comment
  describing it as a guard that "prevents" something is wrong — two such comments shipped
  in E02a and were corrected in the final review.

## Operator recovery entrypoints

`--create-admin`, `--reset-password`, `--reset-mfa`, `--unlock-user` and
`--list-users`, dispatched from `Program.cs` through
`src/Fakturenn.Web/Operations/OperatorCommands.cs`. Added in E02a Task 14.

- **`--create-admin` takes the same advisory lock as `/account/setup`, and the key
  lives in one place (`src/Fakturenn.Web/SetupLock.cs`) precisely because a
  *different* key serialises nothing.** The plan's `if (await db.Users.AnyAsync())`
  is the same check-then-act race Task 9 closed for the web path: measured, four
  concurrent `--create-admin` subprocesses against an empty instance produced **four
  administrators**, three runs out of three, and exactly one after the lock was
  restored. `EnableRetryOnFailure` is on, so the transaction goes through
  `CreateExecutionStrategy().ExecuteAsync` and the user is built *inside* the
  delegate, which can re-run.
- **`--unlock-user` rotates the security stamp, and the reason is not symmetry.**
  An account locked by **failed attempts** never had a rotation at all: Identity's
  automatic lockout writes `LockoutEnd` through `UpdateUserAsync` and does not touch
  the stamp. So a session opened before those failures is still live at the moment an
  operator unlocks the account — and whoever was guessing the password is exactly who
  might be holding it. Spec §8 covers "credentials **or lock state**", §10 requires the
  CLI and `/account/admin/set-lockout` not to diverge, and that endpoint already
  rotates on both edges. Measured: deleting the one `UpdateSecurityStampAsync` call
  reddens `OperatorEntrypointTests.Unlocking_a_user_ends_the_session_they_already_hold`
  and nothing else — the probe answers 200 instead of 302.
- **No flag reads a password from `args`, and a test asserts both shapes.** argv is
  visible in `ps` and lands in shell history. `--create-admin x@y <password>` and
  `--create-admin x@y --password <password>` with nothing on standard input both exit
  2 and create no user.
- **`--list-users` prints no secret** — no authenticator key, no recovery code, no
  password hash. It projects five columns and never materialises
  `AspNetUserTokens`. A diagnostic reached for when an instance is already in trouble
  ends up in terminal scrollback and support tickets.

## The Data Protection provider is part of IdentityDbContext's model cache key

`src/Fakturenn.Modules.Identity/Persistence/UserTokenProtectorModelCacheKeyFactory.cs`,
installed from `IdentityDbContext.OnConfiguring`. Added in E02a Task 14.

- **`OnModelCreating` captures an `IDataProtector` into the value converter, and EF
  caches the compiled model per context type.** Without the factory the **first**
  `IdentityDbContext` built in a process decides which key ring **every** later
  instance encrypts with, whatever provider its own constructor was handed.
- **The symptom is maximally misleading**: `CryptographicException: The key {…} was not
  found in the key ring`. That reads as a Data Protection fault — a lost ring, a
  missing `PersistKeysToDbContext`, an unshared key between replicas — and none of
  those is what happened. The misdirection is most of the cost of this bug.
- **Production was safe only by accident.** One container means one provider, and
  `--migrate` builds its own file-backed provider but exits before serving traffic.
- **Nothing in the suite could see it.** Every in-process test reads a token back
  through the same captured protector that wrote it, so it round-trips regardless. It
  surfaced only when Task 14's `--reset-mfa` **subprocess** became a reader that did
  not share the capture, and only when class scheduling let `PostgresFixture`'s
  `DataProtectionProvider.Create("Fakturenn.Tests")` — an on-disk ring under
  `~/.aspnet/DataProtection-Keys` — win the race against the host's database-backed one.
- **Key on the provider, never on the protector.** `CreateProtector` returns a fresh
  object per call, so a protector-derived key would differ per context instance and
  rebuild the model on every instantiation — a correctness fix turned into a
  performance defect. An `IDataProtectionProvider` is a singleton per DI container, so
  the cache holds **one model per container**; asserted by
  `UserTokenProtectorModelCacheTests.One_provider_yields_one_model_however_many_contexts_are_built`.
- Installed in `OnConfiguring` rather than at each site that builds options — the host,
  the `--migrate` entrypoint, the design-time factory, two test fixtures — because a
  forgotten site would reintroduce the defect with no compiler error and no failing test.
- Removing the `ReplaceService` line reddens
  `UserTokenProtectorModelCacheTests.Two_providers_in_one_process_each_protect_under_their_own_key_ring`
  with "Expected a CryptographicException to be thrown, but no exception was thrown" —
  the second provider silently reading the first's ciphertext, which is the whole defect.
- **The purpose string `"Fakturenn.Identity.UserToken.v1"` (`IdentityDbContext`) is
  part of key derivation, not a label. Never edit it.** Data Protection derives the
  sub-key from it, so a changed string is a different key: every previously written
  `AspNetUserTokens.Value` becomes undecryptable, which means every enrolled
  authenticator and every recovery code in the installation. The failure is a
  `CryptographicException` at read time, not at startup, so it surfaces when a user
  tries to sign in rather than when the deployment goes out. If a re-key is ever
  genuinely needed, it needs a migration that rewrites the column, and the `v1`
  suffix is there so the new string has an obvious form.
- **Why the encryption is ours to do: ASP.NET Core Identity stores both second
  factors in plaintext by default.** `AspNetUserTokens.Value` holds the raw
  authenticator key and the recovery-code set as the framework writes them —
  measured in E02a Task 10 against the real `UserManager` path, not a hand-inserted
  probe: with the converter removed, the column reads the bare base32 key
  (`2DLLRGWUSHJCMXNDCZS4KFQRMM5ZILP7`) and the ten codes as text; with it in place,
  both are `CfDJ8`-prefixed Data Protection payloads. A round-trip test cannot see
  the difference — it round-trips either way — so the guard is
  `A_token_value_is_not_readable_as_plaintext_in_the_column`, which reads the raw
  column.

## Testing the entrypoint

- `Program.cs` is top-level statements, so **anything wired there is unreachable
  from an in-process test.** A test over the class alone stays green when the
  call site is deleted, which is exactly the mutation that matters. The only way
  to cover it is to run the built assembly as a subprocess --
  `dotnet Fakturenn.Web.dll --migrate` -- and assert on the exit code and the log
  output. `tests/Fakturenn.IntegrationTests/MigrateEntrypointTests.cs` is the
  worked example; Task 8 proved it by deleting the
  `PermissionCatalogValidator.FindUnknownPermissions` call and watching that one
  test, and only that test, go red.
- Operator entrypoints added later (`--reset-password` and friends) have the same
  property. Test them the same way rather than extracting the body into a
  testable class and asserting on that -- extracting the body leaves the
  *dispatch* untested, and the dispatch is the part that silently disappears.

## The browser suite

- **`tests/Fakturenn.UiTests` does not run its collections in parallel, and must not
  be switched back.** The assembly hosts three real applications in one process —
  `WebAppFixture` plus two `AuthenticatedWebAppFixture` instances, one for the shared
  identity collection and one for the Content-Security-Policy walk, which needs a
  `/setup` page no user has closed yet. All of them build EF Core models for the same
  three context types out of EF's process-wide internal service provider. With
  parallelism on, two fixtures initialising at once intermittently threw
  `The model must be finalized and its runtime dependencies must be initialized before
  'GetRelationalModel' can be used` from `Migrator.HasPendingModelChanges()` and killed
  the whole shared collection. Measured: twice in eleven runs, with nine green runs in
  between. `AssemblyInfo.cs` carries the reasoning; the cost is about eleven seconds,
  because the suite is dominated by a sixty-second security-stamp wait, not by
  parallelism. This is a harness constraint, not a product defect — a deployed instance
  is one host in one process.
- **Never `WaitForURLAsync` after a click. Wait for an element on the destination and
  assert the path.** `ClickAsync` returns once the click is dispatched, so the navigation
  it causes can finish before or after the next line runs. When it finishes first,
  Playwright sees the URL already matching and falls through to
  `WaitForLoadStateAsync(Load)` — but the new document's `load` event has already fired,
  so it waits for an event that will never come again and times out. Measured: the
  first-run journey hung the full timeout at `WaitForURLAsync("**/account/login")` while
  the server log showed `POST /account/setup responded 302` followed by
  `GET /account/login responded 200` — the navigation being waited for had already
  succeeded — and Playwright's own log reported only `"NetworkIdle" event fired`. Two
  occurrences in thirteen runs, nine green runs in between.
  `AuthenticatedWebAppFixture.ArriveAtAsync(page, path, testId)` is the replacement, and
  it asserts more, not less: a locator wait re-evaluates until the element exists, and the
  path assertion stops an element from standing in for the wrong page.
- **One shared first-run journey needs its failure remembered, not retried.** `/setup`
  closes as soon as the user row exists, so a second attempt after a failed first one
  lands on the sign-in page and times out waiting for `setup-email` — three misleading
  red tests on top of the one real one. `EnsureAdministratorAsync` caches the original
  exception and re-throws it as the inner exception of every later caller's failure.
- **`WebApplication.Urls` reports an address with no trailing slash.** Measured:
  `http://127.0.0.1:34027`. Interpolating a relative path straight onto it produces
  `http://127.0.0.1:34027account/login`, which is a valid-looking URL for a host called
  `127.0.0.1:34027account`. Every navigation in the suite goes through
  `AuthenticatedWebAppFixture.Url(path)`, which composes it with `new Uri(baseUri, path)`.
- **The browser tests need `--environment Development --applicationName Fakturenn.Web`,
  or the pages they look at have no styles and no scripts.** The generic host loads the
  static web assets manifest only in Development, and it looks for
  `{ApplicationName}.staticwebassets.runtime.json` beside the assembly named by
  `ApplicationName` — which in a test process defaults to the *test* assembly. Without
  both switches `/app.css`, `/Fakturenn.Web.styles.css`, `/_content/MudBlazor/*` and
  `/_framework/blazor.web.js` all answer 404, and a Content-Security-Policy check then
  reports "nothing was blocked" about a page on which nothing was ever fetched. That is
  why `ContentSecurityPolicyTests` asserts the required assets were actually received
  before it asserts the violation list is empty.
- **A `Content-Security-Policy` assertion must ask the browser, not the response
  headers.** `Headers.Should().ContainKey("content-security-policy")` passes for a policy
  that blocks every script on the page. Assert on the `securitypolicyviolation` event,
  the console error and the failed request instead. Proven by narrowing `script-src` to
  `'none'`: the console channel named the blocked script and the asset guard reported
  `/_framework/blazor.web.js` never fetched.
- **The application sends two `Content-Security-Policy` headers, and that is correct.**
  Ours, plus a `frame-ancestors 'self'` policy from
  `Microsoft.AspNetCore.Components.Server`. Browsers enforce multiple policies
  independently, so the intersection applies and our `frame-ancestors 'none'` still wins.
  Do not assert a header count — that fails for a reason unrelated to whether the policy
  is right.

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

## Mutation testing: how to revert, and how to read a green

Nearly every task in E02a proved a guard load-bearing by breaking it and
watching a specific test go red. The technique works; these are the ways it
goes wrong.

- **`git checkout -- <file>` is not a mutation revert. It is a revert to
  `HEAD`.** For a tracked file whose current content is *uncommitted*, that
  discards the whole task's work, not the one line just mutated. E02a Task 17
  used it to undo a one-key mutation in
  `src/Fakturenn.Web/Resources/SharedResource.de.resx` and lost all 85 German
  translations, which had to be regenerated from scratch. It was caught only
  because `git status` stopped listing the file as modified. `git checkout --`
  is safe only when the committed state *is* the pre-mutation state. For
  uncommitted work, copy the file aside first, or undo the edit directly.
- **Apply, run and revert a mutation in one command, then confirm
  `git status --short src/` is empty.** A mutation stranded in production code
  by an interrupted run is a live regression that compiles and mostly passes:
  E02a left `[Authorize(Policy = Permissions.UsersRead)]` weakened to a bare
  `[Authorize]` on `src/Fakturenn.Web/Components/Admin/Users.razor` this way.
  Checking that the tree still compiles does not catch it.
- **A mutation that stays green is a question, not a verdict.** The next
  question is "what *else* is providing this guarantee", never "the guard is
  redundant". Three times in E02a the answer was something unintended: the
  enrolment gate's `IsAuthenticated` early return turned out to be a
  performance guard while `GetUserAsync`'s null result was doing the
  correctness work; and, worse, the concurrency test for `/account/setup`
  stayed green with the advisory lock deleted because an unrelated unique
  index on the role name was serialising the racers — the same class of error
  the lock existed to fix, reappearing inside the test meant to prove the fix.
  Accepting that green would have shipped an unverified guard.

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
