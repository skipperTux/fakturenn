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
