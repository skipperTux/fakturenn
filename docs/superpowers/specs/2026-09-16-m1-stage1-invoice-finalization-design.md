# Design — M1 Stage 1: invoice finalization

**Date:** 2026-09-16
**Revised:** 2026-10-08, after the spec review
`docs/superpowers/reviews/2026-10-08-m1-stage1-invoice-finalization-spec-review.md`.
Finding identifiers in brackets — [S1], [C3] — point there.
**Status:** Approved
**Milestone:** M1 Walking skeleton
**Epic:** Stage 1 of four. Touches E04, E05, E09.
**Supporting docs:** `docs/planning/WALKING-SKELETON.md`,
`docs/domain/DOMAIN-MODEL-v0.1.md`, `docs/architecture/MODULE-OWNERSHIP.md`,
`docs/architecture/adr/ADR-003.md`, `docs/architecture/adr/ADR-005.md`,
`docs/architecture/adr/ADR-006.md`, `docs/architecture/IMPLEMENTATION-NOTES.md`,
`docs/operations/DEPLOYMENT-BASELINE.md`, `docs/planning/BACKLOG.md`

## 1. What this delivers, and what it does not

M1 proves the Fakturenn differentiator end to end:

```text
organization → customer → one-line invoice → numbering
→ PDF → e-invoice → validation → S/MIME → SMTP → archive
```

That is four specs, cut **by pipeline stage** rather than by subsystem. Each
stage extends the same thread further down the pipe and ends with it working end
to end as far as it reaches. A subsystem cut — "master data spec", "rendering
spec", "mail spec" — would rebuild M1 as four verticals and defer the
end-to-end proof to the end, which is the failure a walking skeleton exists to
prevent.

- **Stage 1 (this spec):** organization, customer, project, catalog item,
  one-line invoice, number allocation, snapshot.
- Stage 2: PDF.
- Stage 3: Factur-X and validation.
- Stage 4: S/MIME, SMTP, immutable archive.

`WALKING-SKELETON.md`'s fixed example is the test data throughout all four:
Example Consulting invoicing Example Client GmbH, customer `C-4711`, project
`PRJ-2026-083`, catalog item `DEV-BACKEND` with customer reference `SI-9001`,
8 hours at 100.00 EUR, 19% VAT, net 800.00, VAT 152.00, gross 952.00.

**Stage 1 produces no document and locks nothing.** Nothing renders, nothing is
sent, nothing is archived. It ends with a *numbered* invoice carrying a number,
authoritative totals and a hashed snapshot — the input Stage 2 consumes. Because
an invoice locks only once its artifacts exist (§4), every Stage 1 invoice stays
editable, and **a database holding only Stage 1 invoices is disposable**: it is
test and demonstration data, never a set of issued invoices. [C3]

Completing Stage 1 is what makes **ADR-005** (canonical finalized snapshot) and
**ADR-006** (separate document aggregates) decidable. Both are `Proposed` today;
the implementation plan's last task promotes or corrects them. [M5]

## 2. One organization per instance

`SPEC-v0.1.md` §2 previously listed "multiple organizations" as a v0.1
capability. It no longer does, and the reasoning matters here because it
removes machinery this design would otherwise carry.

A self-hosted instance serves **exactly one organization**. Someone needing
several legal entities runs several stacks. Hosted multi-tenancy, if it is ever
built, is schema- or database-per-tenant — recorded in `BACKLOG.md` — which
means the tenant key lives in the **connection**, never in the row.

So: no `OrganizationId` filtering, no global query filters, no tenant
resolution, no multi-tenancy dependency. `Organization` is ordinary seller
master data. Row-level tenancy is the obvious default that every .NET tutorial
teaches, and adding tenant columns "just in case" would build machinery for a
mechanism this project decided against.

**Where the row comes from.** `--migrate` inserts it, once, under a fixed key,
with every field empty. A check constraint pins the primary key to that fixed
value, so a second row is impossible at the database level and creation never
sits on a request path. The organization page edits the row; finalization
refuses until it is complete (§5). The Definition of Done's *"organization
isolation is tested"* means exactly this: the test inserts a second row through
the context and asserts the constraint violation. [C2]

## 3. Modules

Four new modules, each `Fakturenn.Modules.<Name>` plus
`Fakturenn.Modules.<Name>.Contracts`. All eight assemblies go into
`Fakturenn.slnx` and into `FakturennArchitecture.Loaded` — the second is
enforced by `The_loader_omits_no_assembly_declared_under_src_in_the_solution`,
and a forgotten line silently exempts an assembly from every architecture rule.

| Module | Owns in Stage 1 | Deferred, and to where |
| --- | --- | --- |
| Organizations | `Organization`: legal name, country, VAT ID, IBAN, currency, time zone, payment terms in days, issue-date default. Number-scheme configuration. | `MailIdentityReference`, `SigningIdentityReference` → Stage 4. `OrganizationAddress` → E04. |
| Customers | `Customer`: legal name, country, VAT ID (nullable), customer number. `CustomerCatalogItemReference`: per customer and catalog item, the `CustomerCatalogItemNumber`. | Delivery email, e-invoice profile, PDF template, signing policy → the stage that reads each. Contacts, multiple addresses, electronic address → E04. |
| Projects | `Project`: belongs to one customer; `CustomerProjectReference`. | External mappings → E13. |
| Catalog | `CatalogItem`: `CatalogItemNumber`, type, unit, unit price, VAT rate. | Translations, activity mappings → E05. |

**`CustomerCatalogItemNumber` lives in Customers, not on the catalog item.** It is
a per-customer reference — `SI-9001` is Example Client GmbH's name for
`DEV-BACKEND`, and another customer has its own — which is how `MODULE-OWNERSHIP`
already models it (`CustomerCatalogItemReference`). It is resolved at finalization
for the invoice's customer. [C7]

**The customer carries only what Stage 1 reads.** Delivery email, e-invoice
profile, PDF template and signing policy each have one possible value and no
reader until Stages 2–4; they arrive with the stage that reads them. [M11]

`Fakturenn.Modules.Invoices` exists and is schema-only today — one migration
creating a schema, zero entities. It gains `Invoice`, `InvoiceLine`,
`InvoiceSnapshot` and `NumberCounter`.

Each module owns its `DbContext` and its migrations. Cross-module access is
through `.Contracts` only; architecture rules 4, 5 and 6 apply automatically by
name pattern.

The per-module checklist in `CLAUDE.md` under "Adding a new module" applies to
all four, plus two items this stage adds to it:

- **Row provenance.** Every new entity implements `IAuditable`, and every new
  context registration adds `AuditSaveChangesInterceptor`. Today only
  `IdentityDbContext` has it; `InvoicesDbContext` is registered without it, and
  an entity copying Identity's `CreatedBy = string.Empty` saves empty provenance
  and a `0001-01-01` timestamp with no constraint objecting. A host-composition
  test in `Fakturenn.Web.UnitTests` asserts the interceptor on each context, and
  the checklist gains the item. [C5]
- **Messaging.** Stage 1 publishes nothing, so the four new contexts are
  **unenrolled** and belong in
  `The_unenrolled_contexts_carry_no_wolverine_model_annotation`.
  `InvoicesDbContext` stays enrolled, as it is today. [M9]

## 4. Invoice lifecycle, the snapshot, and why nothing re-renders

**Reprinting a locked invoice hands back the archived PDF and XML. Neither is
ever regenerated.** Identical data does not produce an identical document:
layout changes, font versions differ, PDF producer metadata moves. A re-render
is therefore a *new* document claiming to be an old one, and GoBD's
Unveränderbarkeit is exactly what that breaks. The XML is stated separately
because it is the part most tempting to regenerate — it looks like "just data",
and a fixed mapping bug or a newer Factur-X profile looks like a reason to emit
it again. The PDF and the XML sent together are **one** archived record, and a
correction to either is an `InvoiceCorrection`, never a replacement file.

**What locks is the issued document, not the data at number allocation.** Three
states:

| State | Entered by | May change |
| --- | --- | --- |
| `Draft` | Creating it | Everything. No number exists. |
| `Numbered` | Finalization (§5) | Everything **except the number and the issue date**. Each edit re-runs finalization under the same number, overwriting the snapshot. |
| `Locked` | Stage 2–4: PDF and XML rendered, validated and archived | Nothing, ever. |

The number and the issue date are frozen at allocation because the number is
derived from the date (§6): changing the date would either release a number — a
gap — or leave an invoice numbered in the wrong period. Everything else stays
editable until lock because artifacts are produced **after** finalization
commits, durably through Wolverine, and a Stage 3 validation failure is
typically a data error that a retry with the same data would only repeat. A
superseded snapshot is overwritten: nothing was ever issued from it. A send
failure in Stage 4 retries the send and never re-renders. [C3, C4]

Producing artifacts outside the finalization transaction is deliberate.
E-Invoice-EU and the validator are external services; inside the transaction
they would hold the counter row lock across HTTP calls and make their
availability gate every finalization. This is the work the M0 Wolverine
foundation exists for.

**The aggregate enforces the table.** `Invoice`'s mutating methods refuse a
number or issue-date change in `Numbered` and every change in `Locked`. There is
no database trigger on the snapshot table: it would catch a bug, not an
operator, and the aggregate tests catch the bug. Stage 1 cannot reach `Locked`
through the UI, so `Locked` is covered by aggregate unit tests. [C4]

**The snapshot's job is bounded:**

- It is the **single source** the artifacts are produced from, so the PDF and the
  XML cannot disagree. ADR-005 says exactly this — *"PDF, XML, timesheet, and
  email artifacts derive from one immutable finalized snapshot"* — a statement
  about the artifacts agreeing **with each other**, not about re-rendering later.
- Its SHA-256 is stored beside it, and each later artifact's metadata records
  the snapshot hash it was produced from. [M6]

**The snapshot must deserialize until its invoice locks, and only until then.** A
`Numbered` invoice's artifacts are produced by a queued message that survives a
restart and therefore an upgrade, so the version that renders may not be the
version that wrote the snapshot. The reader is strict:
`JsonUnmappedMemberHandling.Disallow` and required members, so a renamed or
missing member fails loudly instead of rendering an old invoice as 0.00 with no
tax category. After lock nothing reads the bytes except hash verification, so
there is no long-term format obligation: no format-version field, no golden
snapshot fixtures. [C3]

**Nothing else reads the snapshot.** `Invoice` carries the number, issue date,
service period, due date, totals and tax category as columns, and the detail page
reads those. A page that read the snapshot would put every snapshot ever written
under the obligation the previous paragraph limits. [C3]

**Storage.** System.Text.Json with a source-generated context and fixed options,
serialized once per finalization and stored as raw bytes. Not `jsonb`: a `jsonb`
column reorders and normalizes keys, so the bytes read back are not the bytes
hashed. Nothing re-serializes for verification — it hashes the stored bytes — so
no canonical form is needed and none is claimed. This does not conflict with
`CLAUDE.md`'s "no document binary data in PostgreSQL": the snapshot is a few
kilobytes of structured data, not a rendered document, and ADR-003's "JSONB
selectively for snapshots" is amended to say bytes. Accepted cost: the snapshot
is opaque to SQL. [M5, M6]

**What the hash proves.** Corruption, not tampering: whoever can rewrite the
bytes can rewrite the hash in the same row. A later stage must not cite it as
integrity evidence against an operator. [C11]

## 5. Finalization

Finalization takes a `Draft` to `Numbered`, or re-runs on a `Numbered` invoice
after an edit. One unit of work on `InvoicesDbContext`, run **inside the
execution strategy**, because the context uses `EnableRetryOnFailure` and EF Core
requires a user transaction to be retried as a whole: every load happens inside
the delegate, so a retry starts from the database, not from a change tracker
left over from the failed attempt. [S2]

1. **Lock the invoice.** Read it with `SELECT … FOR UPDATE` and re-check its
   state there. A `Locked` invoice is refused. A `Numbered` invoice keeps its
   number: finalizing it again re-snapshots and never allocates. Double-clicks,
   two tabs and the execution strategy re-running after a commit-time failure all
   reduce to this case. [S2]
2. **Validate the draft.** Issue date, service period and exactly one line with
   a quantity greater than zero.
3. **Resolve** seller, customer, project, catalog item and the customer's
   catalog-item reference through `.Contracts` providers, and check completeness:
   - *Seller complete:* legal name, country, VAT ID, IBAN, currency, time zone,
     payment terms, number scheme. The VAT ID is mandatory while it is the only
     seller tax identifier modelled (EN 16931 BR-S-02).
   - *Customer complete:* legal name, country, customer number.
   - *Project:* belongs to the invoice's customer.
   - *Line:* the catalog item exists, its currency equals the organization's.
   Each failure is a named refusal. [C6, C8]
4. **Decide the tax category** (§7).
5. **Compute totals** from resolved data — never from anything the UI
   submitted. VAT is computed **per category on the sum of the line net
   amounts**, rounded once, as EN 16931 does; summing separately rounded
   per-line VAT can differ by a cent per line. Money uses the shared kernel's
   `Money` and `Percentage`, whose rounding is commercial (away from zero);
   no second `Money` is introduced. The due date is the issue date plus the
   organization's payment terms. [M4, M13, C6]
6. **Allocate the number** (§6) — only when entering `Numbered`.
7. **Serialize** the snapshot, hash it, and upsert it keyed one-to-one by the
   invoice. Write the denormalized columns on `Invoice`.
8. **Set state `Numbered`.** Commit.

Everything in one transaction, so a failure anywhere returns the number.

**What the user confirms is what finalizes.** Finalization is irreversible in
its number, so the page shows a confirmation with the totals computed by the
same code path, and finalization receives the gross amount the user saw. If
resolution in step 5 produces a different amount — a price or rate changed
between preview and click — finalization refuses and the page shows the new
totals for a second confirmation. [M12]

Resolution happens **at finalization**, not when a line is added. A draft holds
only references — customer id, project id, catalog item id, quantity, issue
date, service period — and no copied master data. Copying into the draft as
lines are added means a draft opened weeks ago carries stale prices with
nothing signalling it. Accepted cost of resolving late: a draft can display one
address and finalize with another if the customer changed in between, which is
arguably correct — the invoice should carry current billing data.

Event-maintained read models were rejected for Stage 1: they need the events,
the projections, and an answer for what finalization does when a projection is
behind.

Value objects are `readonly record struct`: `InvoiceNumber`, `Quantity`, and
the shared kernel's existing `Money` and `Percentage`.

## 6. Numbering

**Configuration lives in Organizations, the counter lives in Invoices.**
`MODULE-OWNERSHIP` assigns Organizations "NumberSequence **configuration**", and
that word resolves `DOMAIN-MODEL` §15's open question. The scheme is read at
finalization step 3 and **copied into the snapshot**, so the snapshot records
which scheme produced its number. The mutable counter row lives in the Invoices
schema, because allocation and the snapshot insert must commit together and
modules own separate `DbContext`s.

**Allocation is a single statement that also handles the first occurrence:**

```sql
INSERT INTO invoices.number_counter (scope_key, value) VALUES (@key, @start)
ON CONFLICT (scope_key) DO UPDATE SET value = number_counter.value + 1
RETURNING value;
```

A plain `SELECT … FOR UPDATE` locks nothing on the first invoice of each new day,
month or year, because the row does not exist yet: two concurrent first
finalizations both find nothing and both issue number 1. This was executed
during the review against `postgres:17-alpine` — without a unique key both
committed 1; with the statement above, three concurrent transactions on a
missing row received 1, 2 and 3, and a rolled-back fourth returned its number.
It stays transactional, so a failed finalization still returns its number,
unlike a PostgreSQL sequence, whose `nextval` survives `ROLLBACK`. [S1]

**Two unique indexes back it:** one on the counter's scope key, one on the
formatted invoice number. The second is the backstop for anything the scheme
rules below miss. [S1]

**The scheme is a closed configuration, not a template language:**

| Field | Values |
| --- | --- |
| Prefix | Empty, or 1–9 ASCII letters and digits. |
| Reset scope | `PerDay`, `PerMonth`, `PerYear`, `Continuous` |
| Counter padding | Minimum digits, zero-padded. |
| Next number | The number the next finalization issues in a fresh scope. |

The formatted number is the prefix, the date part, then the padded counter, with
no separators. The date part is fixed per scope, taken from the issue date:
`PerDay` `yyMMdd`, `PerMonth` `yyMM`, `PerYear` `yyyy`, `Continuous` none. So
`R{yyMMdd}{counter}` is `PerDay` with prefix `R`; migrating from a tool that has
issued 450 invoices is `Continuous` with next number 451. [M2]

**The scheme locks once the first number is allocated.** Changing prefix or
scope later could format a number already issued, and the counter rows written
under one scope mean nothing under another. A user who needs a new scheme
starts a new instance — consistent with one organization per instance. [M2]

A parsed template string was rejected: it is a mini-language needing a parser,
validation and error messages, and an invalid template would be discovered at
finalization — the worst possible moment.

**The counter key comes from the invoice's own issue date, never from
`now()`.** This is a correctness constraint, not a preference. An invoice is
often written in a different month from the one being billed, so two invoices
written on 4 September but dated 31 August share the 31-August daily index; an
allocator keyed on the wall clock would give them September numbers. This looks
perfectly correct in any test that dates and finalizes on the same day, which is
why §11's backdating test uses a clock that differs from the issue date.

Gaplessness is therefore **per scope key**. A day with no invoices has no
numbers, which is normal and explainable.

**The issue date** is EN 16931's BT-2, called "document date" in earlier drafts
of this spec; `DOMAIN-MODEL` already says `IssueDate`. It defaults to **today in
the organization's time zone** (default `Europe/Berlin`), taken from `IClock`
once, when the draft is created, and stored on the draft. Users who bill the
preceding month set the organization's issue-date default to
`LastDayOfPreviousMonth` instead. Either way the user may edit it until the
number is allocated, and never after. [M1]

## 7. VAT and tax category

`Customer` carries a nullable `VatId` — private individuals have none — beside
`Country`. This changes VAT from "the catalog item's rate" into a decision.

EN 16931 requires a **tax category code** per line, and the category depends on
seller country, customer country, and whether the customer has a VAT ID:

| Case | Code | Rate |
| --- | --- | --- |
| Domestic | `S` | Standard or reduced |
| Intra-EU B2B with VAT ID | `AE` reverse charge | 0%, mandatory note on the invoice |
| Intra-EU B2C, no VAT ID | `S` | Seller's rate, or OSS |
| Outside the EU | `G` export | 0% |

**Stage 1 implements `S` only and refuses everything else.** Seller country
equals customer country → category `S` at the catalog rate. Three named
refusals:

- any other country combination — cross-border VAT is not implemented;
- `S` at 0% — EN 16931 BR-S-05 requires an `S` rate above zero, and a 0% line
  belongs to a category Stage 1 does not offer; [C6]
- a currency other than the organization's (§5 step 3). [C6]

The refusal is the point. Silently charging 19% to an Austrian business with a
valid VAT ID is a legal problem, not a bug. `AE` additionally needs the
mandatory invoice note, which is real work belonging to M2.

**VAT-ID validation against VIES is optional, not a precondition.** Verifying
that a business partner is a legitimate entity is the user's responsibility when
trading with EU companies. It is recorded in `BACKLOG.md`, and nothing in
finalization waits on it.

**The tax category code is stored explicitly** on the line and in the snapshot
from the first migration — never derived at render time. Stage 3's Factur-X
mapping needs it.

## 8. Authorization

**Three permissions**, added to `Permissions.All` so `--migrate`'s existing
`RoleSeeder` re-sync grants them to Administrator: [S4]

| Permission | Guards |
| --- | --- |
| `organization.manage` | The organization record: legal identity, IBAN, VAT ID, number scheme. Separate because a changed IBAN redirects every future payment. |
| `masterdata.manage` | Customers, projects, catalog items, customer catalog-item references. |
| `invoices.manage` | Drafting and finalizing invoices. |

The constants move from `Fakturenn.Modules.Identity/Authorization/Permissions.cs`
to `Fakturenn.Modules.Identity.Contracts`, so business modules can reference them
under architecture rule 5. [S4]

**Enforced twice: at the page and at the slice.** The page attribute decides
what renders; each mutating slice — save organization, customer, project,
catalog item, reference; draft; finalize — checks its permission against the
circuit's **current** `AuthenticationState`, never against what the page was
rendered with. [S3, S4]

**Authenticated by default.** An authorization fallback policy requires an
authenticated user, so a page that forgets its attribute is refused rather than
anonymous. The account pages already allow anonymous access; `/alive` and
`/health` gain `AllowAnonymous()`, because probes do not sign in. Static files
are served by `UseStaticFiles()` ahead of `UseAuthorization()` and are
unaffected. Whether `_framework/blazor.web.js` reaches an anonymous request
under the fallback policy was not verified during the review; a test settles it.
[S4]

**A circuit is revalidated.** E02a ends a locked or changed user's session
within one minute by validating the security stamp on HTTP requests. An open
circuit makes no new HTTP request: the decompiled `Components.Server` 10.0.12
sets the circuit's user only at start and on reconnect. Without more, a user
locked mid-session keeps editing the IBAN and finalizing until the circuit
drops. A revalidating authentication state provider checks the security stamp
on the same one-minute interval and ends the circuit's authentication when it
fails. [S3]

**Provenance inside a circuit.** `HttpContextCurrentUserAccessor` reads
`IHttpContextAccessor.HttpContext`, which inside a circuit is at best the
principal from connection time and at worst null — recorded as `system`. On the
interactive pages `ICurrentUserAccessor` resolves from the circuit's
`AuthenticationState`. Those are the rows provenance exists for: who changed the
IBAN, who finalized. [C5]

## 9. Render mode

**Stage 1 turns on Blazor interactivity for its own pages.**

`SPEC-v0.1.md` §4 specifies "Blazor Interactive Server with MudBlazor". What
shipped is not that: `AddInteractiveServerComponents()` and
`AddInteractiveServerRenderMode()` are both registered, but **no component
declares `@rendermode`**, so no circuit is ever negotiated and every page
renders static.

The business pages declare `InteractiveServer` individually, with
**prerendering off**. Prerendering renders a page twice — over HTTP, then in the
circuit — running `OnInitializedAsync` both times, so a draft page that creates
its draft on initialization would create two; and controls appear before the
circuit attaches, so a Playwright click can land on a button that is not live
yet. Without it the page is blank for the moment the circuit takes to connect,
which an internal tool can afford. `<Routes />` gains no render mode, so every
account page stays static. [M8]

**Each operation owns its `DbContext`.** A circuit lives for as long as the tab
is open, so a context injected into a component would live for hours, track
every entity it ever loaded and be shared by overlapping event handlers. Every
slice called from an interactive page creates a fresh DI scope for that one
operation and resolves its context there. A fresh scope rather than
`IDbContextFactory<T>`, because Wolverine 6.48.1 refuses a handler whose only
route to a context is a factory (`DbContextFactoryRefusalPolicy`, decompiled
during the review), and these slices become handlers when Stage 2 starts
publishing. [C1]

**MudBlazor's providers move.** `MainLayout.razor` renders
`<MudPopoverProvider />` and `<MudSnackbarProvider />`, and under per-page
interactivity the layout stays static. MudBlazor 9.11.0 then logs that the
provider is missing from the active render scope and the popover never opens —
`MudSelect`, `MudAutocomplete` and the date picker all render through it. Adding
a second provider on the page fails too: `MudPopoverProvider` throws "Duplicate
MudPopoverProvider detected" when it finds the layout's container, and a throw
from a component ends the circuit. The providers move out of the static layout
into one interactive component that every business page renders. [C9]

**Numeric input follows the user's culture.** Request localization sets the
culture per request on static pages; a circuit keeps the culture it started
with. Quantity and price parse with that culture — `100,50` in German — and
switching language takes effect on the next circuit. A Playwright check enters a
decimal in German. [M14]

Two constraints from E02a stay met rather than rediscovered:

**Credential forms stay static.** `SignInManager` issues the authentication
cookie by writing `Set-Cookie` to an HTTP *response*. A component inside an
established circuit is handling a WebSocket message, with no response headers
still open. This is why every form in `Components/Account/` posts to a route.

**`EnrolmentGateMiddleware` does not allowlist `/_blazor`, and does not need
to.** Allowlisting the circuit endpoint would let a gated user open a SignalR
connection and render components server-side, bypassing the gate. A gated user
is redirected to the enrolment page before any business page renders, and
enrolment is an account page and therefore static, so no circuit is ever needed
while gated. A test asserts it (§11).

`BACKLOG.md`'s MudBlazor entry stays open: its observed symptom — a
`MudTextField` floating label sitting on its value — is on the sign-in and
authenticator forms, which stay static, and on `/admin/users`. Stage 1 fixes it
for its own pages only. [M7]

## 10. UI

Five pages, MudBlazor, interactive:

- **Organization** — the single record, edit only. Number scheme editable until
  the first number is allocated, then read-only.
- **Customers** — list and form. The form also lists the customer's projects and
  its catalog-item references, each created there; a project belongs to the
  customer it was created on. [C8, C7]
- **Catalog items** — list and form.
- **Invoice draft** — customer, project (only the customer's), catalog item,
  quantity, issue date, service period; totals shown live; finalization behind a
  confirmation showing the totals that will be used (§5).
- **Invoice detail** — number, state, issue date, due date, totals and tax
  category, read from `Invoice`'s columns, never from the snapshot.

**Field rules.** Country is ISO 3166-1 alpha-2. IBAN passes mod-97. VAT ID is
checked for format only — country prefix and the country's pattern — because
VIES validation is optional (§7). Customer numbers and catalog item numbers are
unique. `WALKING-SKELETON.md`'s fixed IBAN, `DE00 0000 0000 0000 0000 00`, fails
mod-97 and is replaced by a checksum-valid example. [M3]

English and German `.resx` complete. The Definition of Done requires both, and
E02a lost its German resource file once to a careless `git checkout --`, so
completeness is checked rather than assumed.

## 11. Testing

Per `SPEC-v0.1.md` §10's order of preference: real domain objects first, then
fakes, and NSubstitute only where collaborator interaction is itself the
behaviour under test.

**`Fakturenn.UnitTests`**

- Totals against the fixed example (8 × 100.00 = 800.00 net, 152.00 VAT, 952.00
  gross), plus a rounding midpoint (12.5 h at 104.60: net 1307.50, VAT 248.43),
  plus two lines where per-line rounding would differ from per-category. [M4, M13]
- `InvoiceNumber` formatting for each reset scope and padding.
- The issue-date default under both configurations and across a time-zone
  boundary. [M1]
- The tax-category decision, including each named refusal. [C6]
- The `Invoice` aggregate refusing a number or issue-date change in `Numbered`
  and every change in `Locked`. [C4]

**`Fakturenn.IntegrationTests`**

- Finalization end to end against the fixed example.
- Concurrent first allocation in a scope with **no counter row yet**, with the
  interleave forced rather than hoped for: distinct numbers. [S1]
- One invoice finalized twice concurrently: one number, one snapshot, the
  counter advanced once. [S2]
- A rolled-back finalization returns its number.
- A second organization row is refused by the database. [C2]
- The **backdating test**: a clock on 4 September, an invoice dated 31 August,
  the number carries the August date part. [M9]
- A draft whose customer changed between drafting and finalization carries the
  current data; an edit to a `Numbered` invoice keeps its number and replaces
  the snapshot.
- Migrating a database at `main`'s schema to Stage 1's applies cleanly, beside
  the clean-database path. [C11]
- A gated user is redirected from an interactive business page and obtains no
  circuit — beside `EnrolmentGateTests`, which has the database and the sign-in
  this needs. [M9]
- A user without each permission is refused at the page and at the slice. [S4]
- Under the fallback policy, an anonymous request reaches the sign-in page with
  its assets, `/alive` and `/health`, and nothing else. [S4]

**`Fakturenn.ArchitectureTests`** — the eight new assemblies present in
`Loaded`; rules 4, 5 and 6 apply by name pattern with no new rule added.

**`Fakturenn.Web.UnitTests`** — each new context registered, **absent** from
the Wolverine annotation test (§3), and carrying `AuditSaveChangesInterceptor`.
[M9, C5]

**`Fakturenn.UiTests`** — the Playwright journey: complete the organization,
create a customer with project `PRJ-2026-083` and reference `SI-9001`, create a
catalog item, draft an invoice opening a select and the date picker, enter a
decimal in German, confirm and finalize, see the number. Two more: a user locked
while a business page is open is refused on the next mutation from that page
[S3]; a row written through an interactive page carries the signed-in user's
name [C5]. These are the first tests to exercise a live circuit. [C9, M14]

**Each guard is proven by mutation.** Replace the upsert with
`SELECT … FOR UPDATE` and the first-allocation race must redden. Drop the invoice
lock and the double-finalization test must redden. Key the counter on `now()`
and the backdating test must redden. Remove the revalidating provider and the
lock-while-open test must redden. Move the MudBlazor providers back into the
layout and the select step must redden. A mutation that leaves everything green
means the test is decorative, and it is fixed or removed rather than kept.

## 12. Deployment, security and backup

**Circuits change the deployment contract.** `DEPLOYMENT-BASELINE.md` commits to
stateless web replicas. A circuit is in-memory state on one replica: the
negotiate request and the WebSocket must reach the same replica, and the circuit
never moves. So `DEPLOYMENT-BASELINE.md` gains two requirements — ingress
session affinity above one replica, and WebSocket forwarding (`Upgrade`) at the
proxy — and one consequence: a rolling update ends open circuits, so anything
typed and not yet saved is lost. The Definition of Done item at stake is
"Kubernetes compatibility is not broken". [C10]

**Restoring a backup rewinds the counter.** Stage 1 introduces the first state
whose rollback is a legal problem: restoring a dump older than the last numbered
invoice makes the next finalizations issue numbers that already exist — and
once Stage 4 sends, on documents customers hold. `DEPLOYMENT-BASELINE.md` gains
the operator rule beside its existing warning that a restore replays queued
messages: **after a restore, reconcile the counter against the last number
issued before issuing again.** [C11]

**The snapshot hash proves corruption, not tampering** (§4). [C11]

## 13. Documents this change amends

Landing with the code, so a later reader does not trip over the disagreement:
[M5]

- `DOMAIN-MODEL-v0.1.md` — `OrganizationId` removed from `Customer`, `Project`,
  `CatalogItem`, `Invoice` and the rest; "numbering is scoped by organization"
  becomes "per scope key"; §15's sequence question marked answered; `CatalogItem`
  carries a VAT rate where it had `TaxCategoryId`; the `Invoice` states.
- `ADR-003` — snapshots stored as bytes, not JSONB, with the reason.
- `CLAUDE.md` — the snapshot is data, not the "document binary data" kept out of
  PostgreSQL; the module checklist gains the `AuditSaveChangesInterceptor` item.
- `Fakturenn.Modules.Identity` — three comments in `Role.cs`, `UserRole.cs` and
  `IdentityDbContext.cs` say E02b adds an `OrganizationId`. With one
  organization per instance that column is moot and the comments are corrected;
  what remains of E02b is recorded in `PLAN-v0.1.md`.
- `WALKING-SKELETON.md` — the checksum-valid example IBAN. [M3]
- `DEPLOYMENT-BASELINE.md` — §12's additions.
- `BACKLOG.md` — the MudBlazor entry narrowed to the static pages. [M7]
- `ADR-005`, `ADR-006` — promoted or corrected by the plan's last task.

## 14. Human test

Ten minutes, from a clean checkout: [M10]

1. Build the image:
   `dotnet publish src/Fakturenn.Web --configuration Release /t:PublishContainer -p:ContainerImageTag=dev -p:ContainerRuntimeIdentifiers=linux-x64 -p:RuntimeIdentifier=linux-x64`.
2. Start clean: `docker compose down --volumes`, then
   `docker compose --profile migrate run --rm migrate`, then
   `docker compose up --detach`. Migrate first — an app container started against
   an unmigrated database aborts during `StartAsync`.
3. Open `/account/setup`, create the administrator, enrol TOTP.
4. Open the organization page: legal name, country `DE`, VAT ID, IBAN, currency
   `EUR`, time zone, payment terms, prefix `R`, reset scope `PerDay`.
5. Create customer Example Client GmbH, country `DE`, number `C-4711`, with project
   `PRJ-2026-083`. Create catalog item `DEV-BACKEND`, 100.00 EUR, 19%, and on the
   customer the reference `SI-9001` for it.
6. Draft an invoice for 8 hours. Confirm the totals update **without a page
   reload** — that is the circuit working — and that the issue date defaults to
   today. Enter `8,5` with the UI in German and confirm it reads as eight and a
   half.
7. Set the issue date to the last day of last month, finalize through the
   confirmation, and confirm the number carries that date rather than today's.
8. Edit the quantity of the numbered invoice, finalize again, and confirm the
   number is unchanged.
9. Create a second customer in country `AT` with a VAT ID, draft an invoice, and
   confirm finalization is **refused** with the cross-border message.

## 15. Deferred deliberately

- **Custom fields.** `DOMAIN-MODEL` §15 lists their storage representation as
  open and the walking skeleton uses none. → E09 or later.
- **Cross-border VAT** — `AE` reverse charge with its mandatory note,
  intra-EU B2C, `G` export. → **M2**, on the plan, before any non-domestic
  customer is supported.
- **VAT-ID validation against VIES.** Optional add-on; the user's
  responsibility, not a finalization precondition. → `BACKLOG.md`.
- **Multiple lines per invoice.** The skeleton is one line. The aggregate holds a
  collection and VAT is computed per category already (§5); the UI and the
  end-to-end tests exercise one. → E09.
- **Master-data CRUD depth** — contacts, multiple addresses, catalog
  translations. → E04 and E05.
- **Payments, corrections, reminders.** → M5.
