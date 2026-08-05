# Walking Skeleton

## Goal

Prove the complete Fakturenn differentiator before broad CRUD development:

```text
Minimal organization
→ minimal customer
→ one-line invoice
→ finalization
→ PDF
→ e-invoice
→ validation
→ S/MIME
→ SMTP
→ immutable archive
```

## Fixed example

### Seller

- Legal name: Example Consulting
- Country: DE
- VAT ID: DE123456789
- IBAN: DE00 0000 0000 0000 0000 00
- Currency: EUR

### Customer

- Legal name: Example Client GmbH
- Country: DE
- Customer number: C-4711
- Customer project number: PRJ-2026-083
- Delivery email: invoice-recipient@example.test
- Electronic invoice: Factur-X EN 16931
- PDF template: StandardServiceInvoice
- Signing policy: RequireSignature

### Catalog item

- CatalogItemNumber: DEV-BACKEND
- CustomerCatalogItemNumber: SI-9001
- Type: HourlyService
- Unit: HUR
- Unit price: 100.00 EUR
- VAT: 19%

### Invoice

- Quantity: 8 hours
- Net: 800.00 EUR
- VAT: 152.00 EUR
- Gross: 952.00 EUR
- Service period: 2026-07-01 to 2026-07-31
- Buyer reference: C-4711
- Project reference: PRJ-2026-083

## Expected artifacts

```text
invoice.pdf
invoice.xml
validation-report.txt or .xml
timesheet.pdf (optional in first slice)
invoice.eml
```

## Acceptance criteria

- Invoice number allocated once under concurrency-safe transaction
- PDF and XML derive from one immutable snapshot
- Customer project and catalog-item references appear in configured PDF positions
- Supported references map to valid XML semantic fields
- Validation succeeds
- S/MIME signature verifies independently
- SMTP test server receives exactly one message
- Archived EML equals transmitted logical message
- All artifacts have SHA-256 hashes
- Retry does not create duplicate invoice number or duplicate send
- End-to-end Testcontainers test passes
- Critical flow passes through Playwright
