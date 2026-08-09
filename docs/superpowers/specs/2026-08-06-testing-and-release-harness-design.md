# Design — Testing and Release Harness (E01 + E17 plumbing)

**Date:** 2026-08-06
**Status:** Approved
**Scope source:** `docs/SPEC-v0.1.md` §10 Testing, §11 Release target
**Supporting docs:** `docs/testing/TEST-STRATEGY.md`, `docs/planning/PLAN-v0.1.md`, `docs/planning/WALKING-SKELETON.md`, `docs/architecture/MODULE-OWNERSHIP.md`, `docs/operations/DEPLOYMENT-BASELINE.md`

## 1. Problem

The repository is documentation only: 37 Markdown files, no solution, no project, no CI. `SPEC-v0.1.md` §10 prescribes a six-tier test stack and §11 declares v0.1 a runnable alpha. Neither can exist until a solution, a test harness, and a release mechanism exist.

## 2. Goal of this cycle

Deliver the **test and release harness**: the scaffold that every later epic lands on. Concretely, epic E01 (Repository and foundation) plus the release plumbing portion of E17 (Packaging and human-test release).

Success criteria:

- `dotnet build` succeeds with zero compiler and zero nullable warnings.
- All five test projects exist, run, and pass — each exercising something real, not a placeholder.
- All six architecture rules from `TEST-STRATEGY.md` are machine-enforced.
- `docker compose up` yields a reachable application answering `/health`.
- A tag push produces a published, signed-for-integrity container image and a GitHub Release.
- `CLAUDE.md` states the project contract accurately, with no claim that outruns the code.

## 3. Non-goals for this cycle

Explicitly out of scope, each belonging to a named epic:

- Wolverine durable messaging (E15)
- ASP.NET Core Identity, TOTP, recovery codes, OIDC (E02)
- S3 document provider, immutable archive (E11)
- MimeKit/MailKit, S/MIME, OpenPGP (E14)
- PDFsharp/MigraDoc rendering (E11)
- E-Invoice-EU adapter, Factur-X and XRechnung generation (E12)
- Kimai time import (E13)
- Kustomize bases and overlays (E17 proper)
- The 15 modules other than the `Invoices` exemplar (E02–E16)

## 4. Honest limits

This design fulfils §10 **structurally** and §11's **release mechanism**. It does not fulfil §11's "runnable alpha", which spans E01–E17.

The `TEST-STRATEGY.md` release gate — independent verification of at least one Factur-X, one XRechnung, one S/MIME message and one OpenPGP message — cannot be satisfied until E12 and E14 land, and `docs/operations/RELEASE-CHECKLIST-v0.1.md` says so plainly.

The enforcement is a GitHub Environment, not the checklist itself. `release.yml`'s `publish` job declares `environment: release`, so nothing reaches GHCR until a configured reviewer approves the deployment. The checklist is what that reviewer consults; the environment is what actually blocks. **This gate is inert until required reviewers are armed once in the repository's GitHub settings** — a step that cannot be performed from the repository contents, and without which the job runs straight through.

One further limit, added after the branch was built: **nothing under `.github/` has ever executed.** The repository has no remote, so the CI and release workflows are syntactically valid and composed of commands verified locally, but unproven. The first push is their first real run.

## 5. Decisions

| Decision | Choice | Reason |
| --- | --- | --- |
| Target framework | `net10.0` | LTS; SPEC names no version; matches the installed SDK |
| Solution format | `Fakturenn.slnx` | XML solution format, supported by the .NET 10 CLI |
| Layout strategy | Seam + convention rules | Scaffold only what the harness needs; architecture rules match `Fakturenn.Modules.*` by convention, so future modules inherit enforcement without editing tests |
| Test framework | xUnit v3 (`xunit.v3`) on Microsoft.Testing.Platform | Mandated by SPEC §10 |
| Assertions | AwesomeAssertions | FluentAssertions v8 is commercially licensed; incompatible with AGPL-3.0-or-later |
| Architecture tests | ArchUnitNET via `TngTech.ArchUnitNET.xUnitV3` | Type-level dependency analysis, xUnit v3 support, and `Slices().Should().BeFreeOfCycles()` for the cycle rule |
| Design principles | TDD where a test can come first; SOLID, KISS, YAGNI | Restated in `CLAUDE.md` so later epics inherit them |
| Mocking | NSubstitute | Mandated by SPEC §10, and only where interaction is the behaviour under test |
| Integration infra | Testcontainers.PostgreSql | Mandated by SPEC §10 |
| UI | Microsoft.Playwright | Mandated by SPEC §10 |
| Container build | `Microsoft.NET.Build.Containers` | SDK-native; avoids a Dockerfile that can drift from the project file |
| CI/CD | GitHub Actions, GHCR | Free Linux runners where Testcontainers works unprivileged; CodeQL and Dependabot included |
| Versioning | `bump-my-version`, SemVer, start `0.1.0-alpha.1` | Project tooling standard |
| Package management | Central Package Management via `Directory.Packages.props` | One version per package across the solution |
| Coverage | Reported, not gated | `TEST-STRATEGY.md` names no threshold; a threshold on a five-file codebase measures nothing |

## 6. Build order

`CLAUDE.md` is written **first**, because every later step — including subagents — should read the same contract. It is written in two passes to avoid stating things that are not yet true:

1. **CLAUDE.md pass A** — the half derivable from existing docs: canonical terminology, doc reading order and source-of-truth rule, architecture invariants, the six architecture rules, dependency direction, Definition of Done, SPEC §3 non-goals, project-specific style delta. The commands section is present but explicitly marked as filled at the end of the cycle.
2. **Repository and solution scaffold** (§7)
3. **Test projects and architecture rules** (§8)
4. **Runnable shell** (§9)
5. **CI and release pipelines** (§10)
6. **CLAUDE.md pass B** — fill the real commands, then verify every path, project name and command named in the file actually exists and runs.

## 7. Repository and solution scaffold

```text
Fakturenn.slnx
global.json                 pin SDK 10.0.x, rollForward latestFeature
Directory.Build.props       Nullable=enable, TreatWarningsAsErrors=true,
                            LangVersion=latest, ImplicitUsings=enable,
                            EnforceCodeStyleInBuild=true,
                            AnalysisLevel=latest-recommended
Directory.Packages.props    Central Package Management
.editorconfig               C# style; member ordering compatible with StyleCop
LICENSE                     AGPL-3.0-or-later (SPEC line 4; currently absent)
README.md                   root readme per Make a README (currently absent)
CHANGELOG.md                Keep a Changelog
CLAUDE.md
.bumpversion.toml

src/
  Fakturenn.Web/                          Blazor Interactive Server + MudBlazor host
  Fakturenn.SharedKernel/                 IClock, IIdGenerator, Result, strongly-typed ids
  Fakturenn.Modules.Invoices/             exemplar module, vertical slices
  Fakturenn.Modules.Invoices.Contracts/   cross-module surface only
  Fakturenn.Infrastructure.Storage/       exemplar adapter, filesystem document provider seam

tests/
  Fakturenn.UnitTests/
  Fakturenn.ArchitectureTests/
  Fakturenn.IntegrationTests/
  Fakturenn.ComplianceTests/
  Fakturenn.UiTests/
```

`TreatWarningsAsErrors` is load-bearing: `PLAN-v0.1.md` Definition of Done requires that no unresolved compiler or nullable warnings remain, so the build must fail on them rather than rely on review.

## 8. Test architecture

Mapping from `SPEC-v0.1.md` §10 to concrete projects:

| §10 clause | Project | Delivered this cycle |
| --- | --- | --- |
| real domain objects first | `Fakturenn.UnitTests` | Money and VAT-grouping value objects in `SharedKernel`, tested through their real public API |
| fakes/nullables second | `Fakturenn.UnitTests` | `Fakes/` folder with `FakeClock`, `FakeIdGenerator`, `NullDocumentStore` — the pattern later epics copy |
| NSubstitute for interaction | `Fakturenn.UnitTests` | NSubstitute referenced plus one genuine interaction assertion, so the boundary between fake and mock is demonstrated |
| architecture tests | `Fakturenn.ArchitectureTests` | All six `TEST-STRATEGY.md` rules |
| Testcontainers | `Fakturenn.IntegrationTests` | `Testcontainers.PostgreSql` fixture; asserts the EF Core migration applies from a clean database |
| Playwright | `Fakturenn.UiTests` | Boots the application and asserts a German-localized string renders |
| compliance and golden-file | `Fakturenn.ComplianceTests` | Corpus directory, normalizing XML comparer, and tests for the comparer itself |

### 8.1 Architecture rules

All six rules from `TEST-STRATEGY.md`, expressed as conventions rather than as a hand-maintained list of assemblies:

1. No MudBlazor types outside `Fakturenn.Web`.
2. No MimeKit or MailKit types outside `Fakturenn.Infrastructure.Mail.*`.
3. No PDFsharp or MigraDoc types outside `Fakturenn.Infrastructure.Documents.*`.
4. No E-Invoice-EU types in any `Fakturenn.Modules.*` assembly.
5. No `Fakturenn.Modules.X` assembly may reference a `Fakturenn.Modules.Y` assembly directly; only `Fakturenn.Modules.Y.Contracts`.
6. No circular references between `Fakturenn.Modules.*` assemblies.

Rules 2 and 3 are live and binding now, not vacuous. Both are expressed as `Types().That().DoNotResideInAssemblyMatching(<Mail|Documents pattern>).Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(<MimeKit|MailKit|PDFsharp|MigraDoc pattern>)` — the subject selector is "every assembly that is NOT Mail/Documents", which today is all five loaded assemblies, so the rule already governs the whole codebase. Task 6 proved this empirically by making `Fakturenn.Modules.Invoices` depend on real MimeKit and watching the rule fail. When E11 or E14 creates `Fakturenn.Infrastructure.Mail*` or `.Documents*`, the rule does not newly activate — it gets narrower, carving out the one assembly now permitted to reference the library.

Rules 5 and 6 are the genuinely vacuous ones today: rule 5's cross-module check cannot fire while `Fakturenn.Modules.Invoices` is the only module (there is no second module to depend on), and rule 6 needs at least two modules to have a cycle between. Both become binding the moment a second `Fakturenn.Modules.*` assembly exists.

### 8.2 Compliance corpus

The corpus lives at `tests/Fakturenn.ComplianceTests/corpus/`, versioned by profile, with a `README.md` recording provenance and the standard version of each artifact. No generator exists until E12, so this cycle ships the comparer, the normalization rules (whitespace, attribute order, non-semantic ordering) and tests proving the comparer detects both equality and difference.

## 9. Runnable shell

`docker compose up` starts `postgres:17` and `fakturenn-app`, with the application reachable on port 8080.

- Blazor Interactive Server with a MudBlazor theme and one page.
- `/health` readiness endpoint including an Npgsql probe; `/alive` liveness endpoint. `DEPLOYMENT-BASELINE.md` requires readiness, liveness and startup probes.
- EF Core with Npgsql; migration `0001_InitialCreate`.
- Migrations run from an **explicit entrypoint**, never automatically on startup. `DEPLOYMENT-BASELINE.md` mandates an explicit migration Job for Kubernetes, and auto-migrate on startup is unsafe with multiple replicas.
- Serilog structured logging and graceful shutdown.
- Localization through `.resx` and `IStringLocalizer`, English as source and fallback with a complete German resource, one string each.
- Secrets read through file-based configuration providers, per `DEPLOYMENT-BASELINE.md`, not inline environment variables.

## 10. CI and release

### 10.1 `ci.yml` — on push and pull request

| Job | Content |
| --- | --- |
| `format` | `dotnet format --verify-no-changes` |
| `build-test` | Build with warnings as errors; unit and architecture tests; coverage collected and reported |
| `integration` | Testcontainers PostgreSQL tests on `ubuntu-latest` |
| `ui` | Playwright browser install and UI tests |
| `compose-smoke` | `docker compose up`, poll `/health`, `docker compose down` — enforces the Definition of Done item that Compose remains runnable |

### 10.2 `codeql.yml` and `dependabot.yml`

CodeQL for C#. Dependabot for `nuget`, `github-actions` and `docker`.

### 10.3 `release.yml` — on tag `v*`

1. Run the full CI gate set.
2. Publish a multi-architecture container image (`linux/amd64`, `linux/arm64`) to `ghcr.io/<owner>/fakturenn`.
3. Generate a CycloneDX SBOM.
4. Emit `SHA256SUMS` for published artifacts. `WALKING-SKELETON.md` requires SHA-256 hashes for artifacts; the release pipeline establishes the practice.
5. Create a GitHub Release whose body is the matching section of `CHANGELOG.md`.
6. Attach the human-verification checklist described in §4, unticked.

Releases trigger on tags only, so no push can release by accident. Version bumps are performed with `bump-my-version`.

## 11. Testing this design

The harness must not be self-certifying. Each tier is verified by observing it fail as well as pass:

- Architecture rules: temporarily introduce a violating reference locally and confirm the test fails, then revert.
- Compliance comparer: assert both an equal pair and a differing pair.
- Integration: assert the migration applies against a container started from a clean image.
- Compose smoke: assert `/health` returns healthy only after Postgres is reachable.

## 12. Open risks

- **GHCR owner name** is not yet known; the repository has no remote. The release workflow uses `${{ github.repository_owner }}` so it is correct on whichever account the repository lands.
- **Playwright in CI** adds several minutes to the pipeline. If it becomes a bottleneck, the UI job moves to a nightly schedule; it stays on pull requests until there is evidence it needs to move.
- **ADR status.** All ADRs are `Proposed` until accepted in the implementation repository. This cycle creates the implementation repository, so accepting ADR-001 through ADR-010 becomes possible but is not part of this scope.
