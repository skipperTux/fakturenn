# Test Strategy

## Principles

- Each epic leaves all applicable tests green.
- Main branch remains runnable.
- Real domain objects first.
- Fakes/nullables before interaction-heavy mocks.
- NSubstitute only where collaborator interaction is the behavior under test.
- Real infrastructure through Testcontainers.
- Critical user journeys through Playwright.

## Test layers

### Unit

- calculations
- rounding
- tax grouping
- state transitions
- document conversions
- field resolution
- signing policy
- due dates and reminders

### Fake/nullable collaborators

- clock
- current user
- document store
- mail outbox
- ID generator
- signing provider

### NSubstitute

Use for explicit interaction assertions such as:

- signer called when signature is required
- SMTP not called after validation failure
- document archive called before delivery

### Integration with Testcontainers

- PostgreSQL
- Garage, RustFS or compatible S3
- SMTP test server
- IMAP test server
- E-Invoice-EU
- validator
- optional OIDC provider

### Playwright

Critical journeys:

- local login with TOTP
- organization setup
- customer/project/catalog setup
- quotation → order → confirmation → invoice
- Kimai import
- invoice finalization
- signed send
- payment
- reminder
- correction

### Architecture

Enforce:

- no MudBlazor outside UI
- no MimeKit/MailKit outside Mail infrastructure
- no PDFsharp/MigraDoc outside Documents infrastructure
- no E-Invoice-EU types in domain
- no direct cross-module EF entity references
- no circular module references

### Compliance

Versioned corpus for:

- Factur-X/ZUGFeRD
- XRechnung CII
- XRechnung UBL if supported
- multiple tax cases
- references
- allowances/charges
- corrections
- service periods
- rounding edges

## Release gate

v0.1 human-test release requires all automated suites green and successful independent verification of at least one Factur-X, one XRechnung, one S/MIME message, and one OpenPGP message.
