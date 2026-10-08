# Design — M1 Stage 1: invoice finalization

**Date:** 2026-09-16
**Status:** Approved
**Milestone:** M1 Walking skeleton
**Epic:** Stage 1 of four. Touches E04, E05, E09.
**Supporting docs:** `docs/planning/WALKING-SKELETON.md`,
`docs/domain/DOMAIN-MODEL-v0.1.md`, `docs/architecture/MODULE-OWNERSHIP.md`,
`docs/architecture/adr/ADR-005.md`, `docs/architecture/adr/ADR-006.md`,
`docs/architecture/IMPLEMENTATION-NOTES.md`, `docs/planning/BACKLOG.md`

## 1. What this delivers, and what it does not

M1 proves the Fakturenn differentiator end to end:

```text
organization → customer → one-line invoice → finalization
→ PDF → e-invoice → validation → S/MIME → SMTP → archive
```

That is four specs, cut **by pipeline stage** rather than by subsystem. Each
stage extends the same thread further down the pipe and ends with it working end
to end as far as it reaches. A subsystem cut — "master data spec", "rendering
spec", "mail spec" — would rebuild M1 as four verticals and defer the
end-to-end proof to the end, which is the failure a walking skeleton exists to
prevent.

- **Stage 1 (this spec):** organization, customer, project, catalog item,
  one-line invoice, finalization, immutable snapshot.
- Stage 2: PDF.
- Stage 3: Factur-X and validation.
- Stage 4: S/MIME, SMTP, immutable archive.

`WALKING-SKELETON.md`'s fixed example is the test data throughout all four:
Example Consulting invoicing Example Client GmbH, customer `C-4711`, project
`PRJ-2026-083`, catalog item `DEV-BACKEND`, 8 hours at 100.00 EUR, 19% VAT,
net 800.00, VAT 152.00, gross 952.00.

**Stage 1 produces no document.** Nothing renders, nothing is sent, nothing is
archived. It ends with a finalized invoice carrying a number, authoritative
totals, and a hashed snapshot — the input every later stage consumes.

Completing Stage 1 is what makes **ADR-005** (canonical finalized snapshot) and
**ADR-006** (separate document aggregates) decidable. Both are `Proposed` today
and are promoted or corrected by this work, not before it.

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
mechanism this project decided against — machinery that is hard to remove once
finalized snapshots reference it.

The Definition of Done's *"organization isolation is tested"* is satisfied
concretely: exactly one organization exists, and creating a second is refused.

## 3. Modules

Four new modules, each `Fakturenn.Modules.<Name>` plus
`Fakturenn.Modules.<Name>.Contracts`. All eight assemblies go into
`Fakturenn.slnx` and into `FakturennArchitecture.Loaded` — the second is
enforced by `The_loader_omits_no_assembly_declared_under_src_in_the_solution`,
and a forgotten line silently exempts an assembly from every architecture rule.

| Module | Owns in Stage 1 | Deferred, and to where |
|---|---|---|
| Organizations | `Organization`: legal name, country, VAT ID, IBAN, currency. Number-scheme configuration. `DocumentDateDefault`. | `MailIdentityReference`, `SigningIdentityReference` → Stage 4. `OrganizationAddress` → E04. |
| Customers | `Customer`: legal name, country, **VAT ID**, customer number, delivery email, e-invoice profile, PDF template name, signing policy. | Contacts, multiple addresses, electronic address → E04. |
| Projects | `Project`: `CustomerProjectReference`. | External mappings → E13. |
| Catalog | `CatalogItem`: `CatalogItemNumber`, `CustomerCatalogItemNumber`, type, unit, unit price, VAT rate. | Translations, activity mappings → E05. |

`Fakturenn.Modules.Invoices` exists and is schema-only today — one migration
creating a schema, zero entities. It gains `Invoice`, `InvoiceLine`,
`InvoiceSnapshot` and `NumberCounter`.

Each module owns its `DbContext` and its migrations. Cross-module access is
through `.Contracts` only; architecture rules 4, 5 and 6 apply automatically by
name pattern.

The per-module checklist in `CLAUDE.md` under "Adding a new module" applies to
all four. Two of its items are test-enforced; the rest fail silently. In
particular each new `DbContext` must be added to
`MessagingStartupTests.CreateEfMigratedDatabaseAsync`, and each must be asserted
in `MessagingCompositionTests` **by its `WolverineEnabled` model annotation**,
never by resolving `IDbContextOutbox<T>` — that is registered as an open generic
and resolves for every context whether enrolled or not.

## 4. The snapshot, and why nothing re-renders

**Reprinting a finalized invoice hands back the archived PDF and XML. Neither
is ever regenerated.**

This is the ruling that shapes the rest. Identical data does not produce an
identical document: layout changes, font versions differ, PDF producer metadata
moves. A re-render is therefore a *new* document claiming to be an old one, and
GoBD's Unveränderbarkeit is exactly what that breaks. An invoice once written
and sent **is** the document to archive.

The same holds for the XML, and stating it separately matters because the XML
is the part most tempting to regenerate: it is "just data", and a fixed mapping
bug or a newer Factur-X profile looks like a reason to emit it again. It is not.
The PDF and the XML sent together are **one** archived record, both immutable,
and a correction to either is an `InvoiceCorrection`, never a replacement file.

ADR-005 stands unchanged and is not in conflict with this. It says *"PDF, XML,
timesheet, and email artifacts derive from one immutable finalized snapshot"* —
a statement about the artifacts agreeing **with each other** at production time,
not about re-rendering later. An earlier draft of this design claimed ADR-005
would need amending. It does not.

So the snapshot's job is precise and bounded:

- It is the **single source** every artifact is produced from, in one
  transaction, so the PDF and the XML cannot disagree.
- Its **SHA-256** is a link in the hash chain the walking skeleton's acceptance
  criteria require.

It is **not** a re-render source, and therefore carries **no long-term
deserialization obligation**. Consequently: no format-version field, no golden
snapshot fixtures, no additive-only evolution rule. That entire branch of
complexity is deleted.

Storage is **canonical JSON serialized once and stored as raw bytes**, with the
hash beside it. Not `jsonb`: a `jsonb` column reorders and normalizes keys, so
the bytes read back are not the bytes hashed. Accepted cost: the snapshot is
opaque to SQL, so any reporting over finalized data needs projections rather
than queries into the document.

Two consequences follow, and both belong in code rather than in someone's head:

**Finalization is irreversible.** No edit after finalization, ever. A mistake
becomes an `InvoiceCorrection` — a separate document, which `MODULE-OWNERSHIP`
already models as a separate aggregate. This is a Stage 1 invariant.

**The XML is the machine-readable record and the PDF the human-readable one.**
GoBD's Datenzugriff is served by the archived Factur-X XML, so the snapshot
never has to serve an auditor. Stage 3 and 4 territory, stated here so the
snapshot is not later burdened with that role.

## 5. Finalization

One transaction on `InvoicesDbContext`:

1. **Validate the draft.** Document date present, exactly one line, quantity and
   unit price set.
2. **Resolve** seller, customer, project and catalog item through `.Contracts`
   providers. Assert the finalization invariants from `DOMAIN-MODEL` §8: seller
   profile complete, customer billing snapshot complete, line mapping valid.
3. **Compute totals** from line data — never from anything the UI submitted.
4. **Allocate the number** (§6).
5. **Serialize** the snapshot to canonical bytes, hash it, persist both.
6. **Set state Finalized.** Commit.

Everything in one transaction, so a failure anywhere returns the number.

Resolution happens **at finalization**, not when a line is added. A draft holds
only references — customer id, catalog item id, quantity, document date — and no
copied master data. The alternative, copying into the draft as lines are added,
means a draft opened weeks ago carries stale prices with nothing signalling it,
and every draft duplicates master data that may never be finalized. Accepted
cost of resolving late: a draft can display one address and finalize with
another if the customer changed in between, which is arguably correct — the
invoice should carry current billing data.

Event-maintained read models were rejected for Stage 1: they need the events,
the projections, and an answer for what finalization does when a projection is
behind. Wolverine landed two weeks ago with nothing publishing yet.

Value objects are `readonly record struct`: `InvoiceNumber`, `Money`,
`VatRate`, `Quantity`.

## 6. Numbering

**Configuration lives in Organizations, the counter lives in Invoices.**

`MODULE-OWNERSHIP` assigns Organizations "NumberSequence **configuration**", and
that word resolves `DOMAIN-MODEL` §15's open question of whether sequences
belong to Organizations or Documents. The scheme — prefix, reset scope, padding,
starting value — is configuration and stays in Organizations, read at
finalization step 2 and **copied into the snapshot**, so the snapshot records
which scheme produced its number. The mutable counter row lives in the Invoices
schema, because allocation and the snapshot insert must commit together and
modules own separate `DbContext`s. Spanning one transaction across two contexts
would couple their migrations and tangle with the Wolverine outbox enrolment.

**Allocation is a counter row read with `SELECT … FOR UPDATE` inside the
finalization transaction.** The decisive fact against the obvious alternative: a
PostgreSQL sequence is **non-transactional**. `nextval` does not roll back, so
every failed finalization burns a number and leaves a gap that must later be
explained to an auditor. Cost of the row lock: finalizations serialize per
sequence, which at this scale is not a real cost.

**The scheme is a closed configuration, not a template language:**

| Field | Values |
|---|---|
| Prefix | Empty, or 1–9 alphanumeric characters. User-editable. |
| Reset scope | `PerDay`, `PerMonth`, `PerYear`, `Continuous` |
| Counter padding | Digits |
| Starting value | For migration from another tool |

`R{YYMMDD}{index}` is `PerDay` with prefix `R`. A per-year counter is `PerYear`.
Migrating from a tool that already issued invoices is `Continuous` with the
start set to that tool's last number. All three exist in v0.1 as configuration,
so adding the other variants is a row, not a change.

A parsed template string was rejected: it is a mini-language needing a parser,
validation and error messages, and an invalid template would be discovered at
finalization — the worst possible moment.

**The counter key comes from the invoice's own document date, never from
`now()`.** This is a correctness constraint, not a preference. An invoice is
often written in a different month from the one being billed, so two invoices
written on 4 September but dated 31 August share the 31-August daily index; an
allocator keyed on the wall clock would give them September numbers. This looks
perfectly correct in any test that dates and finalizes on the same day, which is
why it is written down.

Gaplessness is therefore **per scope key**. A day with no invoices has no
numbers, which is normal and explainable.

**Document date** defaults to **today** — the date the invoice is written.
`LastDayOfPreviousMonth` is available as configuration for users who bill the
preceding month, but it is not the default. The date is freely editable before
finalization, when no number exists yet, and immutable after, because the
snapshot is. "The number matches the date" therefore holds by construction.

## 7. VAT and tax category

`Customer` carries a nullable `VatId` — private individuals have none — beside
`Country`. This changes VAT from "the catalog item's rate" into a decision.

EN 16931 requires a **tax category code** per line, and the category depends on
seller country, customer country, and whether the customer has a VAT ID:

| Case | Code | Rate |
|---|---|---|
| Domestic | `S` | Standard or reduced |
| Intra-EU B2B with VAT ID | `AE` reverse charge | 0%, mandatory note on the invoice |
| Intra-EU B2C, no VAT ID | `S` | Seller's rate, or OSS |
| Outside the EU | `G` export | 0% |

**Stage 1 implements `S` only and refuses everything else.** Seller country
equals customer country → category `S` at the catalog rate. Any other
combination fails finalization with a named error stating that cross-border VAT
is not implemented.

The refusal is the point. Silently charging 19% to an Austrian business with a
valid VAT ID is a legal problem, not a bug. `AE` additionally needs the
mandatory invoice note, which is real work belonging to M2.

**VAT-ID validation against VIES is optional, not a precondition.** Verifying
that a business partner is a legitimate entity is the user's responsibility when
trading with EU companies. Validation is a useful add-on, recorded in
`BACKLOG.md`, and nothing in finalization waits on it — reverse charge will be
applied on the strength of the VAT ID the user entered.

**The tax category code is stored explicitly** on the line and in the snapshot
from the first migration — never derived at render time. Stage 3's Factur-X
mapping needs it, and adding it later would mean migrating finalized data that
is by then immutable.

## 8. Render mode

**Stage 1 turns on Blazor interactivity for its own pages.**

`SPEC-v0.1.md` §4 specifies "Blazor Interactive Server with MudBlazor". What
shipped is not that: `AddInteractiveServerComponents()` and
`AddInteractiveServerRenderMode()` are both registered, but **no component
declares `@rendermode`**, so no circuit is ever negotiated and every page
renders static. `BACKLOG.md` records this as an observed defect — the visible
symptom was a `MudTextField` floating label sitting on top of its value, which
looks like CSS and is a missing circuit — and assigns it to "the epic that first
needs an interactive component". Stage 1 is that epic.

`@rendermode InteractiveServer` goes on the **new business pages individually**.
`<Routes />` gains no render mode, so everything else, including every account
page, stays static.

Two constraints, both pre-existing and both met rather than rediscovered:

**Credential forms must stay static.** `SignInManager` issues the authentication
cookie by writing `Set-Cookie` to an HTTP *response*. A component inside an
established circuit is handling a WebSocket message, with no response headers
still open. This is structural, not a tuning problem, and it is why every form
in `Components/Account/` posts to a route rather than binding `@onclick`.
Per-page render mode keeps that untouched.

**`EnrolmentGateMiddleware` does not allowlist `/_blazor`, and does not need
to.** Allowlisting the circuit endpoint would let a gated user open a SignalR
connection and render components server-side, bypassing the gate. It stays
blocked: a gated user is redirected to the enrolment page before any business
page renders, and enrolment is an account page and therefore static, so no
circuit is ever needed while gated. **This must be asserted by a test**, not
assumed — it is exactly the kind of property that silently stops being true.

## 9. UI

Five pages, MudBlazor, interactive:

- Organization — single record, edit only, no create and no delete.
- Customer — list and form.
- Catalog item — list and form.
- Invoice draft — customer, project, catalog item, quantity, document date;
  totals shown live.
- Invoice detail — the finalized number, totals and tax category.

English and German `.resx` complete. The Definition of Done requires both, and
E02a lost its German resource file once to a careless `git checkout --`, so
completeness is checked rather than assumed.

## 10. Testing

Per `SPEC-v0.1.md` §10's order of preference: real domain objects first, then
fakes, and NSubstitute only where collaborator interaction is itself the
behaviour under test.

**`Fakturenn.UnitTests`** — totals against the fixed example (8 × 100.00 =
800.00 net, 152.00 VAT, 952.00 gross); `InvoiceNumber` formatting for each reset
scope; the document-date default rule under both configurations; the
tax-category decision **including the refusal** for each non-domestic case.

**`Fakturenn.IntegrationTests`** — finalization end to end against the fixed
example; concurrent finalization allocates two distinct numbers; a rolled-back
finalization returns the number; a second organization is refused; a draft whose
customer changed between drafting and finalization carries the current data.

**`Fakturenn.ArchitectureTests`** — the eight new assemblies present in
`Loaded`; rules 4, 5 and 6 apply by name pattern with no new rule added.

**`Fakturenn.Web.UnitTests`** — the four new `DbContext`s registered, each
asserted through its `WolverineEnabled` model annotation; a gated user is
redirected from an interactive business page and obtains no circuit.

**`Fakturenn.UiTests`** — Playwright: create a customer, create a catalog item,
draft an invoice, finalize it, see the number. This is the first test to
exercise a live circuit, so it also proves the render-mode change end to end.

**Each guard is proven by mutation.** Replace `FOR UPDATE` with a plain read and
the concurrent-finalization test must redden. Key the counter on `now()` instead
of the document date and the backdating test must redden. Remove the
`@rendermode` line and the Playwright interactivity assertion must redden. A
mutation that leaves everything green means the test is decorative, and it is
fixed or removed rather than kept.

## 11. Human test

Five minutes, against a clean database:

1. `docker compose --profile migrate run --rm migrate`, then
   `docker compose up --detach`. Migrate first — an app container started
   against an unmigrated database aborts during `StartAsync`.
2. Sign in, open the organization page, set legal name, country `DE`, VAT ID,
   IBAN, currency `EUR`, prefix `R`, reset scope `PerDay`.
3. Create customer Example Client GmbH, country `DE`, number `C-4711`. Create
   catalog item `DEV-BACKEND`, 100.00 EUR, 19%.
4. Draft an invoice for 8 hours. Confirm the totals update **without a page
   reload** — that is the circuit working. Confirm the document date defaults to
   today.
5. Set the document date to the last day of last month, finalize, and confirm
   the number carries that date rather than today's.
6. Create a second customer in country `AT` with a VAT ID, draft an invoice, and
   confirm finalization is **refused** with the cross-border message.

## 12. Deferred deliberately

- **Custom fields.** `DOMAIN-MODEL` §15 lists their storage representation as
  open; the walking skeleton uses none; and with no compatibility obligation on
  the snapshot, adding them later is cheap. → E09 or later.
- **Cross-border VAT** — `AE` reverse charge with its mandatory note,
  intra-EU B2C, `G` export. → **M2**, on the plan, before any non-domestic
  customer is supported.
- **VAT-ID validation against VIES.** Optional add-on; the user's
  responsibility, not a finalization precondition. → `BACKLOG.md`.
- **Multiple lines per invoice.** The skeleton is one line. The aggregate
  supports a collection; the UI and the tests exercise one. → E09.
- **Master-data CRUD depth** — contacts, multiple addresses, catalog
  translations. → E04 and E05.
- **Payments, corrections, reminders.** → M5.
