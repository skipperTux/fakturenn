# Fakturenn — Specification v0.1

**Status:** Working draft  
**License:** AGPL-3.0-or-later  
**Project:** Greenfield, open source, self-hosted  
**Primary audience:** Freelancers and small service businesses

## 1. Vision

Fakturenn is an open-source, self-hosted document and invoicing workflow for service businesses.

It gives users control over:

- customer, project, and document data;
- their own SMTP and IMAP infrastructure;
- S/MIME and OpenPGP signing identities;
- document storage;
- identity management;
- deployment, backup, and export.

> Your invoices, your identity, your infrastructure.

## 2. v0.1 scope

### Documents

- Quotation
- Order
- Order confirmation
- Invoice
- Invoice correction
- Payment reminder

Delivery notes are deferred because v0.1 focuses on services.

### Core capabilities

- multiple organizations;
- customers, contacts, and multiple addresses;
- customer-specific e-invoice profile;
- customer-specific PDF template;
- customer-specific required references and fields;
- projects;
- service catalog;
- Kimai time import through provider abstraction;
- PDF and timesheet rendering;
- ZUGFeRD/Factur-X and XRechnung;
- SMTP and optional IMAP integration;
- S/MIME and OpenPGP signing;
- immutable archive;
- filesystem or S3-compatible document storage;
- local Identity with TOTP and recovery codes;
- optional generic OIDC;
- English and German localization;
- Docker Compose reference deployment;
- Kubernetes-compatible operation.

## 3. Explicit non-goals

- general ledger;
- double-entry bookkeeping;
- tax declarations;
- payroll;
- inventory;
- physical-goods workflows;
- supplier invoice processing;
- bank synchronization and reconciliation;
- public multi-tenant SaaS;
- Peppol transport;
- arbitrary executable templates.

## 4. Architecture

- Modular monolith
- Vertical slices
- Selective domain modelling
- PostgreSQL
- Blazor Interactive Server with MudBlazor
- Wolverine with PostgreSQL-backed durable messaging
- PDFsharp and MigraDoc
- E-Invoice-EU behind a Fakturenn-owned adapter
- MimeKit and MailKit
- Filesystem and S3 document providers

## 5. Business document chain

```text
Quotation
  → Order
    → Order confirmation
      → Invoice
        → Invoice correction

Invoice
  → Reminder
  → Second reminder
  → Final reminder
```

Each conversion creates a new document with its own identity, number, snapshot, and source reference.

## 6. Customer-specific document requirements

Each customer may configure:

- electronic-invoice profile and syntax;
- PDF template;
- document language;
- delivery email;
- timesheet inclusion;
- signing policy;
- payment terms;
- required header fields;
- required line fields;
- XML mappings supported by the selected e-invoice profile.

Common first-class references include:

- BuyerReference
- PurchaseOrderReference
- ContractReference
- ProjectReference
- CustomerAccountReference
- AccountingCostCentre
- SellerAssignedCatalogItemNumber
- BuyerAssignedCatalogItemNumber

Customer-specific catalog references are modelled as:

```text
CustomerCatalogItemReference
- CustomerId
- CatalogItemId
- CustomerCatalogItemNumber
- CustomerDescription
```

Custom XML fragments are not allowed. All XML output must use explicit semantic mappings supported by the target standard/profile.

## 7. Internationalization

- English source and fallback
- German complete for v0.1
- Native `.resx` and `IStringLocalizer`
- Weblate-ready
- UI culture, document language, formatting culture, currency, and e-invoice profile are separate concepts

The canonical e-invoice model is based on EN 16931 and must not be hard-wired to Germany.

## 8. Storage

No document binary data is stored in PostgreSQL.

Supported providers:

- Filesystem
- S3-compatible storage

PostgreSQL stores metadata, hashes, relations, retention data, and storage keys.

## 9. Identity

Default:

- ASP.NET Core Identity
- Password
- TOTP
- Recovery codes
- Lockout
- Password reset

Optional:

- Generic OIDC

## 10. Testing

- xUnit v3
- real domain objects first
- fakes/nullables second
- NSubstitute when interaction verification is the behavior under test
- Testcontainers for infrastructure integration tests
- Playwright for critical UI journeys
- architecture tests
- compliance and golden-file tests

Every epic must leave all applicable tests green and the main branch runnable.

## 11. Release target

v0.1 is a runnable alpha ready for structured human testing, not a production-readiness declaration.
