# Fakturenn v0.1 Implementation Plan

## Sequence

1. Initial domain model
2. Module ownership map
3. Walking-skeleton sample invoice
4. Blocking ADRs
5. Technical spikes
6. Repository bootstrap
7. Walking-skeleton implementation
8. Full v0.1 epics

Steps 1–5 are documented in this package. Steps 6 onward are the starting point for implementation in a coding agent.

## Milestones

### M0 — Foundation

Repository, Compose, PostgreSQL, Blazor shell, Identity/TOTP, localization, Wolverine persistence.

### M1 — Walking skeleton

Minimal organization and customer → one-line invoice → PDF → e-invoice → validation → S/MIME → SMTP → archive.

### M2 — Invoice alpha

Master data, invoice core, document storage, e-invoice, signed mail.

### M3 — Time billing alpha

Kimai provider, mappings, duplicate protection, timesheet generation.

### M4 — Document chain alpha

Quotation, order, order confirmation, conversions.

### M5 — Post-invoice alpha

Payments, corrections, reminders.

### M6 — Human-test release candidate

Import/export, backup/restore, packaging, Kubernetes manifests, acceptance checklist.

## Epic list

- E01 Repository and foundation
- E02 Identity and organizations
- E03 Localization
- E04 Company and customer data
- E05 Projects and catalog
- E06 Shared document foundation
- E07 Quotations
- E08 Orders and order confirmations
- E09 Invoice core
- E10 Corrections and reminders
- E11 Document rendering and storage
- E12 Electronic invoicing
- E13 Kimai integration
- E14 Email and signatures
- E15 Durable background processing
- E16 Import, export, backup and restore
- E17 Packaging and human-test release

## Definition of Ready

A feature is ready when:

- business outcome is clear;
- acceptance criteria are testable;
- affected modules are known;
- organization isolation is addressed;
- localization impact is known;
- audit and document implications are known;
- external dependencies are identified;
- security considerations are documented;
- migrations are understood;
- test approach is specified;
- unresolved questions are assigned to a spike or ADR.

## Definition of Done

A feature is done when:

- functional acceptance criteria pass;
- unit, integration, architecture, compliance, and applicable Playwright tests pass;
- no unresolved compiler or nullable warnings remain;
- authorization and organization isolation are tested;
- retries are idempotent where applicable;
- migrations work from clean and previous states;
- English and German resources are complete;
- Compose remains runnable;
- Kubernetes compatibility is not broken;
- security and backup implications are documented;
- human-test instructions are included.
