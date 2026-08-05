# Initial Domain Model v0.1

## 1. Design principles

- Separate aggregates for quotation, order, order confirmation, invoice, correction, and reminder.
- Shared value objects are allowed; one generic mutable `Document` aggregate is not.
- Finalized documents are immutable snapshots.
- Cross-module references use identifiers, contracts, and read models—not foreign EF entities.
- Money uses decimal values and explicit currency.
- Customer-specific requirements are resolved before finalization and copied into snapshots.

## 2. Organization aggregate

### Organization

Owns the invoicing legal entity.

```text
Organization
- Id
- LegalName
- TradingName
- LegalForm
- TaxNumber
- VatId
- DefaultCurrency
- DefaultLanguage
- DefaultFormattingCulture
- Status
```

Child entities:

- OrganizationAddress
- BankAccount
- NumberSequence
- MailIdentityReference
- SigningIdentityReference

Invariants:

- one active legal identity per organization;
- one default currency;
- numbering is scoped by organization and document type;
- private keys are referenced, never stored as ordinary fields.

## 3. Customer aggregate

```text
Customer
- Id
- OrganizationId
- CustomerNumber
- LegalName
- DisplayName
- TaxNumber
- VatId
- DefaultLanguage
- DefaultCurrency
- Status
```

Child entities:

- CustomerAddress
- CustomerContact
- CustomerElectronicAddress
- CustomerDocumentProfile

### CustomerDocumentProfile

```text
CustomerDocumentProfile
- ElectronicInvoiceProfileId
- ElectronicInvoiceSyntax
- PdfTemplateId
- DocumentLanguage
- FormattingCulture
- DeliveryEmail
- TimesheetPolicy
- SigningPolicy
- PaymentTermsId
- RequiredFieldRules[]
```

Invariants:

- required profile fields must be available before finalization;
- unsupported e-invoice mappings fail explicitly;
- profile changes never affect finalized snapshots.

## 4. Project aggregate

```text
Project
- Id
- OrganizationId
- CustomerId
- ProjectNumber
- Name
- Description
- Status
- StartDate
- EndDate
- DefaultDocumentLanguage
- DefaultInvoiceProfileId
```

Child/reference data:

- CustomerProjectReference
- ExternalProviderMapping

```text
CustomerProjectReference
- CustomerId
- ProjectId
- CustomerProjectNumber
- CustomerDescription
```

## 5. Catalog aggregate

```text
CatalogItem
- Id
- OrganizationId
- CatalogItemNumber
- Type
- Name
- Description
- UnitCode
- DefaultPrice
- Currency
- TaxCategoryId
- Active
```

Types:

- Service
- HourlyService
- FixedPriceService
- Expense
- Product

Customer-specific reference:

```text
CustomerCatalogItemReference
- CustomerId
- CatalogItemId
- CustomerCatalogItemNumber
- CustomerDescription
```

External time-tracker mapping:

```text
ExternalEntityMapping
- Provider
- ExternalEntityType
- ExternalId
- LocalEntityId
```

## 6. Shared value objects

### Money

```text
Money
- Amount: decimal
- Currency: ISO 4217 code
```

### Quantity

```text
Quantity
- Value: decimal
- UnitCode: UNECE code
```

### PostalAddress

Immutable value object used inside snapshots.

### ServicePeriod

```text
ServicePeriod
- StartDate
- EndDate
```

### DocumentReference

```text
DocumentReference
- DocumentType
- DocumentId
- DocumentNumber
```

### AllowanceOrCharge

```text
AllowanceOrCharge
- Kind: Allowance | Charge
- Scope: Line | Document
- Amount
- Percentage
- BaseAmount
- Reason
- ReasonCode
- TaxCategory
```

## 7. Document aggregates

Each aggregate owns:

- draft state;
- lines;
- totals;
- lifecycle;
- source references;
- finalization;
- immutable snapshot creation.

### Quotation

States:

```text
Draft → Finalized → Sent → Accepted | Rejected | Expired
```

### Order

States:

```text
Draft → Finalized → Confirmed | Cancelled
```

### OrderConfirmation

States:

```text
Draft → Finalized → Sent
```

### Invoice

States:

```text
Draft → Finalized → Sent → PartiallyPaid → Paid
                         ↘ Overdue → ReminderIssued
Finalized → Corrected
```

### InvoiceCorrection

Separate aggregate referencing the invoice.

### Reminder

Separate aggregate referencing an overdue invoice.

## 8. Invoice aggregate

```text
Invoice
- Id
- OrganizationId
- CustomerId
- ProjectId?
- Status
- IssueDate
- DueDate
- Currency
- DocumentLanguage
- FormattingCulture
- ElectronicInvoiceProfileId
- PdfTemplateId
- SourceDocumentReferences[]
- Lines[]
- AllowancesAndCharges[]
- Payments[]
```

### InvoiceLine

```text
InvoiceLine
- Id
- CatalogItemId?
- Description
- Quantity
- UnitPrice
- TaxCategory
- ServicePeriod?
- CatalogItemNumber?
- CustomerCatalogItemNumber?
- ProjectReference?
- CustomFieldValues[]
```

Finalization invariants:

- seller profile complete;
- customer billing snapshot complete;
- all required customer fields resolved;
- all line mappings valid;
- authoritative totals calculated;
- invoice number allocated once;
- snapshot persisted atomically;
- finalization idempotent.

## 9. InvoiceSnapshot

The canonical source for PDF, XML, timesheet, and email.

```text
InvoiceSnapshot
- InvoiceId
- InvoiceNumber
- SellerSnapshot
- CustomerSnapshot
- CustomerDocumentProfileSnapshot
- ProjectSnapshot?
- Lines[]
- Totals
- TaxSummary[]
- PaymentInstructions
- ServicePeriod
- RequiredReferences
- CustomResolvedFields
- GeneratorContext
- FinalizedAt
```

Snapshot lines include both internal and customer-specific references.

## 10. Custom fields

### CustomFieldDefinition

```text
CustomFieldDefinition
- Id
- OrganizationId
- Key
- DisplayName
- DataType
- Scope
- Required
- ValidationPattern?
- MaximumLength?
- DefaultValue?
- Active
```

Supported scopes:

- Customer
- Project
- CatalogItem
- Quotation
- Order
- OrderConfirmation
- Invoice
- InvoiceLine
- Correction
- Reminder

Resolution precedence:

```text
Document or line override
→ Project
→ CustomerCatalogItemReference / catalog item
→ CustomerDocumentProfile
→ Organization default
```

## 11. Payments

```text
Payment
- Id
- InvoiceId
- Amount
- Currency
- ReceivedAt
- Reference
- Note
- RecordedBy
```

Invoice payment state is derived from recorded payments.

## 12. Documents and archive

```text
DocumentArtifact
- Id
- OrganizationId
- RelatedEntityType
- RelatedEntityId
- ArtifactType
- FileName
- ContentType
- Size
- Sha256
- StorageProvider
- StorageKey
- Generator
- GeneratorVersion
- CreatedAt
- Immutable
```

Artifacts include:

- PDF
- XML
- Validation report
- Timesheet
- EML

## 13. Mail

```text
OutboundMessage
- Id
- OrganizationId
- RelatedDocumentId
- MessageId
- From
- ReplyTo
- EnvelopeSender
- Recipients
- Subject
- SigningMode
- MimeArtifactId
- Status
- Attempts
```

The exact signed MIME is archived before transmission.

## 14. Domain events

Initial events:

- QuotationFinalized
- OrderFinalized
- OrderConfirmationFinalized
- InvoiceFinalized
- InvoiceCorrected
- ReminderIssued
- PaymentRecorded
- DocumentGenerationRequested
- ElectronicInvoiceValidationRequested
- InvoiceDeliveryRequested
- InvoiceDelivered
- DeliveryFailed
- KimaiEntriesImported

## 15. Open modelling questions

- Whether number sequences belong to Organizations or Documents module
- Exact correction semantics for partial corrections
- Required first-class EN 16931 references
- Custom-field storage representation
- Whether payment state is stored or fully derived
- Reminder escalation rules
