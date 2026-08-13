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

Requires the .NET 10 SDK and Docker (or a Docker-compatible engine). There is
no published image yet — `compose.yaml` references `fakturenn:dev`, which
must be built locally first:

    dotnet publish src/Fakturenn.Web --configuration Release /t:PublishContainer \
      -p:ContainerImageTag=dev -p:ContainerRuntimeIdentifiers=linux-x64 -p:RuntimeIdentifier=linux-x64
    docker compose up --detach
    docker compose --profile migrate run --rm migrate

The application listens on http://localhost:8080. See [CLAUDE.md](.claude/CLAUDE.md)
for the full command reference, including `docker compose down --volumes`.

## Development

Requires the .NET 10 SDK. `dotnet build` and the unit/architecture/compliance
suites (`dotnet test --project tests/Fakturenn.UnitTests`, etc.) need nothing
else. The full suite also needs Docker (integration tests, Testcontainers
PostgreSQL) and a local Chromium install (UI tests, Playwright) — see
[CLAUDE.md](.claude/CLAUDE.md) for the exact commands and per-suite test counts.

## Documentation

Start at [docs/README.md](docs/README.md).

## Contributing

Read [CLAUDE.md](.claude/CLAUDE.md) for the architecture invariants and the Definition
of Done, then [docs/planning/PLAN-v0.1.md](docs/planning/PLAN-v0.1.md).

## Licence

AGPL-3.0-or-later. See [LICENSE](LICENSE).
