# Module Ownership Map

## Rules

- A module owns its write model and migrations.
- Another module must not reference the owner's EF entities.
- Cross-module calls use contracts, commands, events, identifiers, or read models.
- Infrastructure libraries may implement module-owned interfaces.

## Organizations

Owns:

- Organization
- OrganizationAddress
- BankAccount
- NumberSequence configuration
- Organization membership policy references

Publishes:

- OrganizationCreated
- OrganizationProfileChanged

## Identity

Owns:

- ApplicationUser
- Membership
- Role and permission assignments
- TOTP state
- Recovery codes

Does not own external IdP users.

## Customers

Owns:

- Customer
- CustomerAddress
- CustomerContact
- CustomerElectronicAddress
- CustomerDocumentProfile
- CustomerCatalogItemReference

Provides immutable customer snapshot contracts.

## Projects

Owns:

- Project
- CustomerProjectReference
- Project external mappings

## Catalog

Owns:

- CatalogItem
- Unit
- TaxCategory and tax-rate reference data
- Catalog translations
- External activity mappings

## Quotations

Owns:

- Quotation
- QuotationLine
- QuotationSnapshot

## Orders

Owns:

- Order
- OrderLine
- OrderSnapshot

## OrderConfirmations

Owns:

- OrderConfirmation
- OrderConfirmationLine
- OrderConfirmationSnapshot

## Invoices

Owns:

- Invoice
- InvoiceLine
- InvoiceSnapshot
- Payment
- invoice state and calculation rules

Does not own PDF/XML/EML binaries.

## Corrections

Owns:

- InvoiceCorrection
- CorrectionSnapshot

## Reminders

Owns:

- Reminder
- ReminderLevel
- ReminderSnapshot

## Documents

Owns:

- PdfTemplate metadata
- DocumentArtifact metadata
- rendering contracts
- document-store contracts

Infrastructure owns concrete filesystem/S3 adapters.

## ElectronicInvoices

Owns:

- ElectronicInvoiceProfile
- canonical mapping contracts
- field mappings
- validation result metadata

Adapters own E-Invoice-EU and validator integration.

## Timesheets

Owns:

- provider contracts
- ImportedTimeEntry
- import sessions
- duplicate-protection records

Kimai is the first adapter.

## Mail

Owns:

- MailAccount metadata
- SigningIdentity metadata
- OutboundMessage
- delivery attempts
- MIME composition/signing contracts

Secrets are references only.

## Audit

Owns:

- AuditEvent
- correlation metadata

## Allowed dependency direction

```text
UI
→ feature slices
→ module domain/contracts
→ infrastructure implementations
```

Cross-module orchestration occurs through messages or explicit query interfaces.
