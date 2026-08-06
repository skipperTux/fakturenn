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
