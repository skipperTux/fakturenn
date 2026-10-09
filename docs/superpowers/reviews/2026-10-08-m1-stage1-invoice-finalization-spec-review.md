# Spec review — M1 Stage 1, invoice finalization

**Date:** 2026-10-08
**Spec under review:** `docs/superpowers/specs/2026-09-16-m1-stage1-invoice-finalization-design.md`
**Spec status at review time:** Approved
**Review type:** Read-only, pre-plan gate — complete / sound / secure
**Documents consulted:** the spec; `SPEC-v0.1.md`; `PLAN-v0.1.md` (Definition of Ready
and Done); `DOMAIN-MODEL-v0.1.md`; `MODULE-OWNERSHIP.md`; `WALKING-SKELETON.md`;
ADR-001 to ADR-010; `SECURITY-BASELINE.md`; `DEPLOYMENT-BASELINE.md`; `TEST-STRATEGY.md`;
`IMPLEMENTATION-NOTES.md`; `BACKLOG.md`; `.claude/CLAUDE.md`; the E02a spec and its review.
**Claims verified:** against the repository at `ea739da` — the render-mode registration
(`FakturennWebApplication.cs`; no `@rendermode` anywhere in `src/`), `EnrolmentGate`'s
allowlist without `/_blazor`, every account form posting to a separate route, `Invoices`
being schema-only with one migration, the loader-omission test, `MessagingStartupTests` and
`MessagingCompositionTests`, nothing publishing. Against shipped binaries — the open-generic
`IDbContextOutbox<>` registration (WolverineFx.EntityFrameworkCore 6.48.1, decompiled), the
circuit-user lifecycle (`Microsoft.AspNetCore.Components.Server` 10.0.12, decompiled), the
negotiate-first connection (`blazor.web.js` 10.0.12), MudBlazor 9.11.0's provider checks
(decompiled, plus `MudBlazor.min.js`). Executed against `postgres:17-alpine`, the image
`compose.yaml` runs — `jsonb` key reordering, `nextval` surviving a rollback, the
`FOR UPDATE` first-row race (S1). Every statement the spec makes about existing code and
documents is accurate unless a finding below says otherwise.
**Not verified:** the EN 16931 rules cited in C6 and C7 (from the standard's rule set; no
schematron is in the repository); the German VAT ID check digit in M3 (computed with
ISO 7064 MOD 11,10, not checked against an official validator); what
`IHttpContextAccessor.HttpContext` returns inside a circuit event handler (C5 holds either
way); EF Core's change tracker after a rollback, the execution strategy re-running after a
commit-time failure, and Blazor running `OnInitializedAsync` twice under prerendering
(documented framework behaviour, not executed here); §8's statement that `SignInManager`
cannot issue a cookie from a circuit (consistent with the E02a spec, not re-executed); and
every legal statement, which is not legal review.

## Verdict

- **Sound: yes, with one premise to repair.** The decisions argued with the owner hold up
  when checked: the pipeline-stage cut; no re-render; bytes rather than `jsonb` (executed:
  PostgreSQL 17 returns `{"b":1,"a":2.50}` as `{"a": 2.50, "b": 1}`); a counter row rather
  than a sequence (executed: `nextval` returns 2 after a rolled-back 1); the closed scheme;
  `S` only with a named refusal; Interactive Server per page with account pages static.
  The premise that does not hold is §4's "no long-term deserialization obligation": the
  spec's own invoice detail page and `DOMAIN-MODEL` §14's asynchronous generation events
  both read snapshots after the fact (C3).
- **Secure: no.** S1–S4 ship exploitable or broken if followed literally. Numbering and
  finalization each have a concurrency hole the planned tests cannot see; turning on
  circuits silently undoes E02a's session controls on exactly the pages that hold the IBAN;
  and Stage 1 defines no permission at all.
- **Complete: no.** The `DbContext` lifetime inside a circuit, the organization's origin,
  where irreversibility is enforced, row provenance, what "complete" means, how a project
  is created, the customer-specific item number's owner, MudBlazor's providers, Kubernetes
  affinity, and two Definition of Done items.

**Recommendation:** one revision pass on S1–S4 and C1–C5 before the plan is written —
they fix the transaction, context-lifetime and authorization shape every slice in the plan
inherits. C6–C11 need a paragraph each; the minor findings are a sentence each. Several
fixes are small because the project already holds the answer: `BACKLOG.md` names the
invoice-number unique index, `IMPLEMENTATION-NOTES.md` the execution-strategy wrapper,
E02a the permission model and the affinity requirement.

---

## Revision outcome — 2026-10-08

All 29 findings dispositioned: 29 fixed, none rejected. 27 are the reviewer's;
M13 and M14 were added by the spec's author from a list written and hashed
before this review was read. The reviewer independently found 9 of that list's
11 items, and 16 the author had not.

**C3 changed the design, not just the text.** The spec had deleted the
snapshot's format obligation on the premise that nothing reads it after
finalization; its own detail page and the queued artifact generation both did.
The owner's ruling went further than the finding asked: artifacts are produced
after commit, and only a fully rendered, validated and archived invoice locks.
That gives three states, keeps data editable under a frozen number until the
document actually exists, and bounds the deserialization obligation to the
window before lock.

**S1 was demonstrated, not argued.** The reviewer raced two transactions
against PostgreSQL 17 and watched both commit number 1, then executed the
`ON CONFLICT … RETURNING` statement the spec now uses. The planned
concurrency test would have stayed green, because it raced on a counter row
that already existed.

**S3 is the circuit undoing E02a.** Turning on interactivity — a decision this
spec made deliberately — silently removed the session revocation E02a's review
had fought for, and E02a's own lock test could not see it, because it
navigates with fresh HTTP requests.

**C7 was the author's error.** `CustomerCatalogItemNumber` was placed on the
catalog item, contradicting `MODULE-OWNERSHIP`.

**Five owner rulings**: C3, S4, C2, and C7/C8/M11 together. Six smaller
owner's calls were taken by default and stated, open to override: C4, C6's
fields, M1, M2, M3, M12.

---

## Security findings

### S1 — The first allocation in every new scope races, and nothing in the database makes a number unique

- [x] Dispositioned — **Fixed.** §6 allocates with one `INSERT … ON CONFLICT (scope_key) DO UPDATE … RETURNING`, the statement this review executed; unique indexes on the scope key and on the formatted number; §11 races a first allocation on a missing row with a forced interleave.

§6 specifies "a counter row read with `SELECT … FOR UPDATE`". That locks a row that
exists. Under `PerDay` a date's row exists only after that date's first invoice, so the
first finalization of every day — month, year — has nothing to lock, and two concurrent
ones both read no row and both create it. Executed against `postgres:17-alpine`, two
transactions each running `SELECT … WHERE key = '2026-08-31' FOR UPDATE`, pausing, then
inserting the row with value 1:

- no unique key on the counter: **both commit, both hold number 1** — two invoices with
  one number;
- a primary key on the scope key: the second fails with `23505`, so one of two
  concurrent first-of-day finalizations errors out.

The spec states neither key. `BACKLOG.md` ("Invoices per project, from Kimai") already
recorded the risk and its backstop — "**Also needs:** a uniqueness constraint on the
invoice number in the database, so the guarantee does not rest on the allocator alone" —
and recommended a different mechanism (the `SetupLock` advisory lock). The spec chose
`FOR UPDATE` without addressing either.

The planned test cannot see it. "Concurrent finalization allocates two distinct numbers"
is green whenever the row already exists — a fixture that finalizes once first, or a
scope key shared across tests. And §10's mutation, a plain read in place of
`FOR UPDATE`, reddens only if both racers read before either writes; two `Task.WhenAll`
racers on a local database often do not interleave — the false green
`IMPLEMENTATION-NOTES.md` documents for `/account/setup`.

A third path to a duplicate: the scheme is editable. Moving a live installation from
`PerYear` with padding 4 to `PerMonth` with padding 2 can produce `R260813` twice
(`R`+`26`+`0813`, then `R`+`2608`+`13`). Only a unique index on the formatted number
catches that.

**The spec should state:** a unique index on the invoice number; a unique key on the
counter's scope key; an allocation that handles the first occurrence — a single
`INSERT … ON CONFLICT (key) DO UPDATE SET value = counter.value + 1 RETURNING value` does
(executed: three concurrent transactions on a missing row received 1, 2 and 3, and a
rolled-back fourth returned its number), as does the advisory lock `BACKLOG.md` names;
and that the concurrency test races on a scope key with no row yet, with an interleave it
forces rather than hopes for.

### S2 — Finalizing the same invoice twice is not prevented, and the execution strategy will re-run it

- [x] Dispositioned — **Fixed.** §5 locks the invoice with `SELECT … FOR UPDATE` and re-checks its state, runs the unit inside the execution strategy with every load in the delegate, keeps an existing number on re-finalization, and keys the snapshot one-to-one; §11 finalizes one invoice twice concurrently.

`DOMAIN-MODEL` §8 lists "invoice number allocated once" and "finalization idempotent";
`WALKING-SKELETON.md` requires "Retry does not create duplicate invoice number"; the
Definition of Done requires idempotent retries. §5 cites §8's invariants but leaves those
two out, and its steps never re-check under a lock that the invoice is still a draft:
step 1 validates, and the only lock is the counter's in step 4.

Two finalizations of one invoice — a double-click (Blazor dispatches the second click
while the first handler awaits I/O), two tabs, two users — both load it as `Draft`. The
second blocks on the counter until the first commits, reads the advanced counter,
allocates the next number, inserts a second snapshot and updates the invoice. Unless
something the spec does not name stops it, number N sits in an orphaned snapshot and the
invoice carries N+1: a gap, and a number on no invoice.

The context the spec chose makes this routine. `InvoicesDbContext` is registered with
`EnableRetryOnFailure`, so a user-initiated transaction must run inside
`Database.CreateExecutionStrategy().ExecuteAsync(…)`, whose delegate re-runs on a
transient failure — including a connection lost during `COMMIT` after the server
committed. `IMPLEMENTATION-NOTES.md` ("Persistence and resiliency") records the wrapper as
mandatory and warns the delegate can re-run. The spec mentions neither.

**The spec should state:** the invoice is read under a lock (or a concurrency token) inside
the transaction and its state re-checked there; finalizing a finalized invoice returns its
existing number rather than allocating or failing; the snapshot is one-to-one with the
invoice by key; the unit runs inside the execution strategy and loads everything inside the
delegate; and a test finalizes one invoice twice concurrently and asserts one number, one
snapshot, and a counter advanced once.

### S3 — An open circuit escapes E02a's session controls

- [x] Dispositioned — **Fixed.** §8 adds a revalidating authentication state provider on E02a's one-minute stamp interval and requires each mutating slice to check the circuit's current `AuthenticationState`; §11 locks a user while a business page is open.

E02a made lock, password reset and MFA reset bite within a minute — each rotates the
security stamp and `ValidationInterval` is one minute — and the enrolment gate confines a
user who owes TOTP or a password change. All three act per HTTP request. A circuit is one
long-lived WebSocket request.

Verified in `Microsoft.AspNetCore.Components.Server` 10.0.12: the circuit's user is taken
from the hub connection when the circuit starts (`CircuitFactory`) and again only on
reconnect (`ComponentHub.ConnectCircuit`), and the registered provider is the
non-revalidating `ServerAuthenticationStateProvider`; nothing in `src/` registers a
`RevalidatingServerAuthenticationStateProvider`. A user locked, stripped of a role, or
flagged `MustEnrolTotp` while a business page is open keeps a working page — can keep
editing the IBAN and finalizing — for as long as the WebSocket stays up, past the
eight-hour cookie. The page's `[Authorize(Policy = …)]` does not help: it is evaluated on
the HTTP request that rendered the page, `Routes.razor` is a static `RouteView`, and
nothing re-evaluates it in the circuit. §8's planned test covers only a user already gated
when the page loads.

E02a's own guard cannot notice: `Locking_a_user_stops_their_existing_session` polls with
`GotoAsync`, a fresh HTTP request each time, and passes against a build that leaves every
circuit open.

**The spec should state:** a revalidating authentication state provider checking the
security stamp at the same one-minute interval; that every mutating slice — save
organization, customer, catalog item; finalize — checks its permission against the
circuit's current `AuthenticationState` rather than trusting the page attribute; and a
Playwright test that locks a user while a business page is open and asserts the next
mutation from that page is refused.

### S4 — Stage 1 defines no permission, and an unattributed page is anonymous

- [x] Dispositioned — **Fixed.** Owner's ruling: three permissions — `organization.manage`, `masterdata.manage`, `invoices.manage` — in `Permissions.All` so `RoleSeeder` grants them, constants moved to `Identity.Contracts`; an authenticated-by-default fallback policy with `/alive` and `/health` made anonymous (§8).

The spec has no authorization section. E02a established the model: permissions are code
constants in `Fakturenn.Modules.Identity.Authorization.Permissions`, `--migrate`
re-syncs the Administrator role to `Permissions.All`, and its spec notes the Definition of
Done "requires *every* epic to test authorization". `Permissions.All` holds `users.read`
and `users.manage`. Stage 1 adds five pages and four write paths and names no permission.

An implementer has two precedents to copy. `Home.razor` has no attribute, and no fallback
policy exists (`AddAuthorization()` takes no options; `PermissionPolicyProvider` delegates
the fallback to the default, which is none), so an unattributed page is anonymous.
`ChangePassword.razor` has a bare `[Authorize]`, so any signed-in account — including one
an administrator created with no role — may change the seller's IBAN and finalize. The
IBAN is the field a payment-redirection fraud targets: change it, and every invoice from
then on, immutably, asks customers to pay someone else.

There is also a placement question rule 5 decides against the obvious answer:
`Permissions` lives in the Identity *implementation* assembly, so a slice in
`Fakturenn.Modules.Invoices` cannot reference it — and S3's fix needs exactly that.

**The spec should state:** the permissions Stage 1 adds and their enforcement sites (the
split — for example master data versus finalize — is the owner's call); where the
constants live so module slices can reference them (`Fakturenn.Modules.Identity.Contracts`
satisfies rule 5); that the `--migrate` re-sync grants them to Administrator; and tests
that a signed-in user without them is refused at the page and at the slice.

---

## Completeness findings

### C1 — The `DbContext` lifetime inside a circuit is undecided, and the existing precedent is the wrong one

- [x] Dispositioned — **Fixed.** §9: each slice opens a fresh DI scope per operation — not `IDbContextFactory<T>`, because of the `DbContextFactoryRefusalPolicy` this review decompiled.

In an interactive component a scoped service lives as long as the circuit, not the
request. `Users.razor` injects `IdentityDbContext` directly — right for a static page, and
the pattern a Stage 1 page will copy. On an interactive page it gives one
`InvoicesDbContext` per circuit, and:

- two overlapping handlers — S2's double-click, or a live-totals refresh during a save —
  hit one context concurrently; EF Core throws "A second operation was started on this
  context instance", and unhandled in a component that ends the circuit;
- a rollback reverts the database, not the change tracker. After a failed finalization
  the context still holds the attempt's added snapshot and modified counter and invoice —
  or, if `SaveChangesAsync` succeeded and the commit failed, holds them as `Unchanged` with
  values the database never kept, so the page shows a number that does not exist. The next
  save from that circuit re-sends or contradicts them. S1's unique violation and S2's
  refusal make this an ordinary path: both end in a rolled-back finalization the user
  immediately retries;
- tracked entities are not refreshed by later queries, so data edited in another tab stays
  stale for the circuit's lifetime.

§5's "everything in one transaction, so a failure anywhere returns the number" is true of
the database only. Every integration test uses a fresh context and cannot see this.

**The spec should state:** each slice called from an interactive page runs on a context
it owns for that one operation — an `IDbContextFactory<T>` or a fresh DI scope — never on
one injected into the component. If slices become Wolverine handlers, note that 6.48.1
refuses a handler whose only route to a context is a factory (`DbContextFactoryRefusalPolicy`,
decompiled), so the two choices interact.

### C2 — The single organization has no origin, and "a second is refused" has no mechanism

- [x] Dispositioned — **Fixed.** Owner's ruling: `--migrate` seeds the one row under a fixed key pinned by a check constraint; §11 asserts the database refuses a second (§2).

§2 discharges organization isolation with "exactly one organization exists, and creating
a second is refused". §9 makes the page "edit only, no create and no delete". Nothing
says where the one row comes from — seeded by `--migrate` like the Administrator role, or
created by the page's first save — or what refuses a second.

Both matter. If the first save creates it, two administrators saving at once both find no
row and both insert: the check-then-act shape `IMPLEMENTATION-NOTES.md` documents for
`/account/setup`, with its warning that a unique index on content does not serialize "at
most one row". And the planned test "a second organization is refused" has no code path to
call if the UI cannot create one — it either exercises a method nothing else uses, or
nothing. Separately, §10's Playwright journey finalizes an invoice, which needs a complete
seller profile, but does not set one up; `TEST-STRATEGY.md` lists organization setup as a
critical journey in its own right.

**The spec should state:** how the row comes to exist (seeding it in `--migrate` with a
fixed identity keeps creation out of every request path); the database guarantee against
a second (a fixed key under a check constraint, or a unique index on a constant
expression); what the refusal test calls; and that the Playwright journey begins by
completing the organization.

### C3 — "No long-term deserialization obligation" does not survive the spec's own detail page

- [x] Dispositioned — **Fixed, and it changed the design.** Owner's ruling: artifacts are produced after commit through Wolverine, and only a rendered, validated and archived invoice locks. §4 now has three states — `Draft`, `Numbered` (data editable, number and issue date frozen), `Locked` — a strict snapshot reader while unlocked, the detail page reading `Invoice` columns, and Stage 1 databases declared disposable.

§4 deletes the format version, golden fixtures and evolution rules on the premise that
the snapshot is read only at production time. Two readers contradict it:

- §9's invoice detail page shows "the finalized number, totals and tax category" without
  saying from where. From the snapshot, it reads every snapshot ever written, for the life
  of the installation — the obligation §4 deletes. And it fails silently: `System.Text.Json`
  leaves a member missing from older JSON at its default, so after any rename an old
  invoice shows 0.00 and no tax category rather than an error.
- §4 says every artifact is produced "in one transaction". `DOMAIN-MODEL` §14 lists
  `DocumentGenerationRequested` and `ElectronicInvoiceValidationRequested` — generation
  after the commit, through Wolverine's durable queue. A queued message survives a restart
  and so an upgrade, and the walking skeleton's "retry does not create … duplicate send"
  presumes such retries. The version that handles it reads a snapshot an earlier one wrote.

Invoices finalized while only Stage 1 exists are the first case of that window: no stage
will ever produce their artifacts.

**The spec should state:** that the `Invoice` row carries number, document date, totals
and tax category as columns, and nothing but artifact production and hash verification
reads the snapshot bytes; which reading of "in one transaction" binds Stages 2–3 — inside
the finalization transaction (accepting that the E-Invoice-EU adapter's availability then
gates finalization), or after it, in which case the snapshot stays readable until its
artifacts are archived and deserialization fails loudly (`JsonUnmappedMemberHandling.Disallow`,
required members) instead of defaulting; and whether databases holding Stage-1-only
invoices are disposable.

### C4 — Irreversibility is declared an invariant with no enforcement point and no test

- [x] Dispositioned — **Fixed.** §4: the aggregate refuses number and issue-date changes in `Numbered` and every change in `Locked`; no database trigger (owner's call, taken by default); §11 covers it in aggregate unit tests.

§4: "No edit after finalization, ever … This is a Stage 1 invariant", and such rules
"belong in code rather than in someone's head". §6 makes the document date "immutable
after". Nothing says which code. The snapshot row and the invoice's state, number, date and
(after C3) totals are ordinary columns any later slice on `InvoicesDbContext` can update,
and §10 lists no test that an edit to a finalized invoice is refused — though
`TEST-STRATEGY.md` puts state transitions in the unit layer. §10's mutation discipline has
nothing to redden for an invariant with no test.

**The spec should state:** where the refusal lives (the aggregate's methods refuse when
`Finalized`; whether the snapshot table also gets a database guard against `UPDATE` and
`DELETE` is the owner's call — it catches a bug, not an operator); and tests that each
edit — line, quantity, customer, project, document date — of a finalized invoice is
refused.

### C5 — Row provenance for the new entities is unwired, and its user source does not work in a circuit

- [x] Dispositioned — **Fixed.** §3 requires `IAuditable` and `AuditSaveChangesInterceptor` on every new context with a host-composition test and a checklist item; §8 resolves `ICurrentUserAccessor` from the circuit's `AuthenticationState`; §11 checks a row written through an interactive page.

`IAuditable` is "implemented by every entity Fakturenn defines", filled by
`AuditSaveChangesInterceptor`. The spec mentions neither, and two gaps follow, both silent:

- The interceptor is added only to `IdentityDbContext` (`IdentityConfiguration.cs`);
  `InvoicesDbContext` is registered without it, and `CLAUDE.md`'s module checklist — which
  §3 adopts — does not mention it. Identity's entities initialize `CreatedBy = string.Empty`,
  so a new entity copying them saves empty provenance and a `0001-01-01` timestamp, and
  no constraint objects.
- The user comes from `HttpContextCurrentUserAccessor`, which reads
  `IHttpContextAccessor.HttpContext`. The E02a spec's reason for static account pages is
  that "a Blazor Server circuit has no live `HttpContext`". On an interactive page the
  accessor gives at best the principal from when the circuit connected and at worst null,
  which `AuditStamp` records as `system`. These are the rows provenance exists for: who
  changed the IBAN, who finalized.

**The spec should state:** that the new entities implement `IAuditable`; that every new
context registration adds the interceptor, guarded by a host-composition test in
`Fakturenn.Web.UnitTests` per context (and the checklist gains the item); and that inside
a circuit `ICurrentUserAccessor` resolves from the circuit's `AuthenticationState`, with a
test that a row written through an interactive page carries the signed-in user's name.

### C6 — "Complete" is undefined, and the `S`-only rule does not check what `S` itself requires

- [x] Dispositioned — **Fixed.** §5 lists what "complete" means for seller, customer, project and line, with the seller VAT ID mandatory; §7 adds the `S`-at-0% and currency refusals; service period and due date enter the draft and the snapshot now.

§5 step 2 asserts "seller profile complete, customer billing snapshot complete, line
mapping valid" — `DOMAIN-MODEL` §8's words — with no field list. The implementer invents
one, and the choice is irreversible per invoice: whatever passes is numbered and frozen.

§7's principle — refuse what would be legally wrong — stops at the category. EN 16931
attaches two rules to `S` itself:

- **BR-S-05:** an `S` line's rate must be greater than zero. A catalog item at 0% — the
  obvious entry for a German Kleinunternehmer (§19 UStG), common in this product's
  audience — finalizes as `S` at 0%, which Stage 3's validation must reject, with no
  correction possible before M5.
- **BR-S-02:** an `S` invoice needs the seller's VAT identifier or tax registration
  identifier. Stage 1's `Organization` has a VAT ID and no tax number, so unless
  "complete" requires the VAT ID such an invoice passes.

Also unaddressed: a catalog price in a currency other than the organization's —
`SharedKernel.Money`'s `+` throws `InvalidOperationException`, an unhandled exception in a
circuit rather than a named refusal; and the fixed example's service period and buyer
reference, plus the due date or payment terms BR-CO-25 requires for a positive amount
due, none of which the draft or snapshot carries.

**The spec should state:** the field list behind each "complete", with the organization's
VAT ID mandatory while it is the only seller tax identifier; a named refusal for `S` at
0% beside the cross-border one; currency agreement as a named refusal; and whether service
period and payment terms enter the draft and snapshot now or Stage-1-only data is
disposable (C3).

### C7 — `CustomerCatalogItemNumber` is put on the catalog item, making a per-customer reference global

- [x] Dispositioned — **Fixed.** Owner's ruling: `CustomerCatalogItemReference` owned by Customers as `MODULE-OWNERSHIP` models it, resolved at finalization for the invoice's customer (§3). The spec's original placement on `CatalogItem` was the author's error.

§3 lists `CustomerCatalogItemNumber` as a field of `CatalogItem`. `MODULE-OWNERSHIP.md`
assigns `CustomerCatalogItemReference` to **Customers**, and `DOMAIN-MODEL` §5 and
`SPEC-v0.1.md` §6 model it as `(CustomerId, CatalogItemId, CustomerCatalogItemNumber,
CustomerDescription)` — one per customer and item. On the catalog item it is one value for
everyone: invoice `DEV-BACKEND` to a second customer and that invoice states Example Client
GmbH's article number `SI-9001` as the buyer's own identifier — frozen in the snapshot,
and from Stage 3 sent in the XML (BT-156) to the wrong buyer. The spec does not
acknowledge the disagreement, which `CLAUDE.md` says is itself the finding.

**The spec should state:** either the reference as `MODULE-OWNERSHIP.md` models it, owned
by Customers and resolved at finalization for the invoice's customer, or that Stage 1
carries no customer-specific item number yet.

### C8 — Projects has a module and a draft field, but no page, no creation path and no customer link

- [x] Dispositioned — **Fixed.** Owner's ruling: projects are created on the customer form and belong to that customer; finalization refuses a mismatch (§3, §5, §10); the human test creates `PRJ-2026-083`.

§3 creates `Fakturenn.Modules.Projects`, §9's draft page selects a project, and §5
resolves it at finalization. But §9's five pages include no project page, §11 never
creates one, and §5's list of what a draft holds — "customer id, catalog item id,
quantity, document date" — omits it. Nothing says a project belongs to one customer, so
nothing stops an invoice to customer A carrying customer B's `CustomerProjectReference`:
another customer's project number printed on, and from Stage 3 embedded in, A's invoice.
The walking skeleton requires `PRJ-2026-083` in the documents, so M1 cannot defer this.

**The spec should state:** how a project is created in Stage 1 (a sixth page, or part of
the customer form); that it belongs to one customer and finalization refuses a mismatch;
that the draft holds the project id; and the human-test step creating `PRJ-2026-083`.

### C9 — MudBlazor's providers sit in the static layout, where interactive pages cannot reach them

- [x] Dispositioned — **Fixed.** §9 moves the MudBlazor providers out of the static layout into one interactive component every business page renders; the Playwright journey opens a select and the date picker.

`MainLayout.razor` renders `<MudPopoverProvider />` and `<MudSnackbarProvider />`, and
under per-page interactivity the layout stays static. MudBlazor 9.11.0's own message,
decompiled from `PopoverService`: "Missing <MudPopoverProvider /> in the active render
scope, so popovers cannot be displayed. Add <MudPopoverProvider /> within the same
interactive render mode as the components that use it: … on each page for per-page
interactivity." It logs once and the popover does not open. `MudSelect`,
`MudAutocomplete` and `MudPicker` all render through `MudPopover` (decompiled) — every
selector the draft page has, the date included.

The obvious fix fails as well: `MudPopoverProvider.OnAfterRenderAsync` throws "Duplicate
MudPopoverProvider detected" when `countProviders` — a `document.querySelectorAll` over the
provider container class in `MudBlazor.min.js` — finds two, and the static layout's
container is still in the DOM. A throw from a component ends the circuit.

§8 says its constraints are "met rather than rediscovered"; this third one will be
rediscovered. The Playwright journey would catch it, which is why it is not a security
finding.

**The spec should state:** where the providers live — out of the static layout, inside an
interactive component every business page renders — and that the Playwright journey opens
a select and the date picker.

### C10 — Circuits make session affinity and WebSocket forwarding deployment requirements, and nothing says so

- [x] Dispositioned — **Fixed.** §12: `DEPLOYMENT-BASELINE.md` gains session affinity above one replica, WebSocket forwarding, and the loss of open circuits on a rolling update.

`DEPLOYMENT-BASELINE.md` commits to "stateless web replicas". Stage 1 creates the first
circuit, and a circuit is in-memory state on one replica: `blazor.web.js` 10.0.12
connects with `withUrl("_blazor")` and default options, so the negotiate request and the
WebSocket must reach the same replica, and the circuit can never move. With two replicas
and no affinity, interactive pages fail to connect or lose their state to the reconnect
modal and a reload. A proxy that does not forward `Upgrade` breaks them on one replica.
The E02a spec recorded affinity (§9, "What sticky sessions do and do not solve"), but it
never reached the deployment document because nothing needed it until now. The Definition
of Done item at stake is "Kubernetes compatibility is not broken".

**The spec should state:** the `DEPLOYMENT-BASELINE.md` additions — ingress session
affinity above one replica, WebSocket forwarding at the proxy — and that a rolling update
ends open circuits, so anything typed and not yet saved is lost.

### C11 — Two Definition of Done items are unaddressed: migration from a previous state, and backup implications

- [x] Dispositioned — **Fixed.** §11 adds the migration from `main`'s schema; §12 adds the counter-rewind operator rule and states that the hash proves corruption, not tampering.

`PLAN-v0.1.md` requires that "migrations work from clean and previous states" and that
"security and backup implications are documented". The spec tests a clean database only
and has no security or backup section.

*Previous state.* Stage 1 gives `InvoicesDbContext` its first tables on top of an applied
`InitialCreate` and adds four schemas a database at today's `main` lacks.
`MessagingStartupTests` already builds an upgraded-database fixture; nothing says Stage 1's
migrations get the same.

*Backup.* Stage 1 introduces the first state whose rollback is a legal problem. Restoring
a dump older than the last finalized invoice rewinds the counter, and the next
finalizations re-issue numbers that already exist — once Stage 4 sends, on documents
customers hold. `DEPLOYMENT-BASELINE.md` warns that a restore replays queued messages; the
counter needs the same warning. And the snapshot hash stored beside the bytes detects
corruption, not tampering — whoever can rewrite the bytes rewrites the hash in the same
row — which should be written down before a later stage cites the hash as integrity
evidence.

**The spec should state:** a migration test from `main`'s schema; and a backup and
security paragraph covering the counter-rewind hazard with its operator rule (reconcile the
counter against the last number issued before issuing again after a restore) and what the
hash does and does not prove.

---

## Minor findings

### M1 — "Today" has no time zone and no evaluation point

- [x] Dispositioned — **Fixed.** §6: today in the organization's time zone (default `Europe/Berlin`), taken from `IClock` once at draft creation (owner's call, taken by default).

§6 defaults the document date to "today". The only clock is `IClock.UtcNow`, the container
runs in UTC unless told otherwise, and `Organization` has no time zone. Between midnight
and 02:00 in German summer time UTC is still the previous day: an invoice written at 00:30
on 1 September defaults to 31 August, and under `LastDayOfPreviousMonth` to **31 July** — a
month early, numbered into July. Nor is it said whether the default is frozen at draft
creation: a draft finalized three days later is then dated three days back.

**The spec should state:** the time zone that defines "today", and whether the default is
frozen at draft creation or follows the calendar until the user sets a date.

### M2 — Number format, starting value and scheme edits are left to the implementer

- [x] Dispositioned — **Fixed.** §6 states the format per scope, "next number" semantics, and that the scheme locks after the first allocation (owner's call, taken by default).

Only `PerDay`'s format is given; §10 tests formatting "for each reset scope" against
formats the spec never states (is `PerYear` `R26…` or `R2026…`?). "`Continuous` with the
start set to that tool's last number" duplicates the previous tool's last invoice if the
starting value is, as the word suggests, the first number issued — a duplicate no index can
see. And nothing says whether the scheme may change after the first invoice, or what an
edited starting value does to an existing counter.

**The spec should state:** the format per reset scope; whether the configured value is the
last number used or the first to issue; and whether the scheme locks after first issuance
(see S1 for the collision a scope change can cause).

### M3 — Master-data field rules are unstated, and the fixed example fails the obvious ones

- [x] Dispositioned — **Fixed.** §10 states the field rules — ISO 3166-1 alpha-2, IBAN mod-97, VAT ID format only, unique numbers — and replaces the walking skeleton's IBAN with a checksum-valid example.

Validation of IBAN, VAT ID and country, and uniqueness of customer and catalog item
numbers, are not mentioned. Either way costs something: without validation a mistyped IBAN
is frozen into every invoice until noticed; with it, the fixed example cannot be entered —
`DE00 0000 0000 0000 0000 00` fails ISO 13616 (mod 97 gives 62; check digits `00` and `01`
are never valid), and `DE123456789` fails the German VAT ID check digit (computed: 8
expected). §7's domestic test compares countries, which must be codes or `DE` and
`Germany` differ. A duplicated customer number makes the buyer reference `C-4711` ambiguous.

**The spec should state:** which fields are validated; country as ISO 3166-1 alpha-2;
unique customer and catalog item numbers; and, if validating, corrected identifiers for
`WALKING-SKELETON.md`.

### M4 — `Money` already exists, and the fixed example never rounds

- [x] Dispositioned — **Fixed.** §5 reuses the shared kernel's `Money` and `Percentage`; §11 adds the 12.5 h × 104.60 midpoint.

§5 lists `Money` and `VatRate` as value objects to create. `Fakturenn.SharedKernel` already
has `Money` (ISO 4217 checked, `Round()` away from zero, documented as commercial rounding)
and `Percentage`, whose `Of` rounds. A second `Money` would likely use `Math.Round`'s
default, to-even: 12.5 h at 104.60 is 1307.50 net, 19% of it 248.425 — 248.43
commercially, 248.42 to-even. The fixed example (exactly 152.00) rounds nothing, though
`TEST-STRATEGY.md` lists rounding as a unit-test concern.

**The spec should state:** reuse of the shared kernel's `Money` and `Percentage` (or why
`VatRate` differs), and one unit test at a rounding midpoint.

### M5 — Documents the spec supersedes are left unamended

- [x] Dispositioned — **Fixed.** §13 lists every amendment the change carries, including ADR-003, `DOMAIN-MODEL`, `CLAUDE.md`, the three Identity comments, and the ADR-005/006 promotion assigned to the plan's last task.

- `DOMAIN-MODEL` still has `OrganizationId` on `Customer`, `Project`, `CatalogItem`,
  `Invoice` and more, and "numbering is scoped by organization"; §15 still lists the
  sequence question this spec answers; `CatalogItem` has `TaxCategoryId` where the spec has
  a rate; `Invoice` has `IssueDate` where the spec says "document date".
- ADR-003 says "Use JSONB selectively for snapshots"; the spec stores bytes. And
  `CLAUDE.md`'s "No document binary data in PostgreSQL" reads, on its face, against a
  `bytea` snapshot column — say the snapshot is data, not a document binary, before a
  reviewer flags it.
- The E02a spec gave the `Organization` aggregate and organization-scoped roles to E02b,
  and three comments in `Fakturenn.Modules.Identity` (`Role.cs`, `UserRole.cs`,
  `IdentityDbContext.cs`) still say E02b adds an `OrganizationId`. Stage 1 takes the
  aggregate and makes the column moot; what remains of E02b is unsaid.
- ADR-005 and ADR-006 become "decidable"; nothing assigns their promotion.

**The spec should state:** the amendments it carries, so they land with the code rather
than when a later reader trips on the disagreement.

### M6 — "Canonical JSON" and the "hash chain" are undefined

- [x] Dispositioned — **Fixed.** §4 drops "canonical", names the serializer, and has each later artifact record the snapshot hash it came from.

Bytes serialized once and hashed once need no canonical form — verification hashes the
stored bytes — and "canonical" invites an RFC 8785 library with no consumer.
`WALKING-SKELETON.md` requires "All artifacts have SHA-256 hashes", not a chain, and the
spec does not say what links to the snapshot hash or who verifies it.

**The spec should state:** the serializer and its fixed options (or drop "canonical"),
and which later artifact records the snapshot hash it was produced from.

### M7 — The backlog defect Stage 1 claims stays where it was observed

- [x] Dispositioned — **Fixed.** §9 keeps the `BACKLOG.md` MudBlazor entry open for the static pages; §13 narrows it.

§8 takes up `BACKLOG.md`'s MudBlazor entry: "Stage 1 is that epic". The entry's observed
symptom is a `MudTextField` label on the sign-in form and the authenticator field —
account pages §8 keeps static. Ten `MudTextField`s under `Components/Account/` and five on
`/admin/users` keep it.

**The spec should state:** that the entry stays open for the static pages, or how they are
fixed there.

### M8 — Prerendering is left at its default without a decision

- [x] Dispositioned — **Fixed.** §9: prerendering off for the business pages, with the reason.

`@rendermode InteractiveServer` prerenders unless told otherwise: the page renders over
HTTP, then again in the circuit, running `OnInitializedAsync` twice. A draft page that
creates its draft on initialization creates two. And controls are visible before the
circuit attaches, so a Playwright click can land on a button not yet live — the timing
flake class `IMPLEMENTATION-NOTES.md`'s browser-suite notes already record.

**The spec should state:** prerendering on or off for the business pages, and if on, that
initialization never writes.

### M9 — Three test-list details

- [x] Dispositioned — **Fixed.** §11 moves the gated-circuit test to `Fakturenn.IntegrationTests`, adds the backdating test with a clock that differs from the issue date, and says "absent" for the annotation.

- "A gated user is redirected from an interactive business page and obtains no circuit"
  is placed in `Fakturenn.Web.UnitTests`, which has no database and no sign-in; it belongs
  beside `EnrolmentGateTests` in `Fakturenn.IntegrationTests`.
- §10's mutation rule names "the backdating test", which §10's list does not contain; it
  needs a clock that differs from the document date or the `now()` mutation stays green.
- The new contexts are asserted "through its `WolverineEnabled` model annotation" without
  saying present or absent. Stage 1 publishes nothing, so by `CLAUDE.md`'s item 9 they are
  unenrolled and belong in `The_unenrolled_contexts_carry_no_wolverine_model_annotation`.

**The spec should state:** the corrected project, the backdating test, and "absent".

### M10 — The human test cannot be followed from a clean checkout

- [x] Dispositioned — **Fixed.** §14 starts from building the image, `down --volumes`, `/account/setup` with TOTP, and creates the project.

`compose.yaml` runs a local `fakturenn:dev` image that must be built first (`CLAUDE.md`'s
`dotnet publish … /t:PublishContainer` line); "clean database" needs
`docker compose down --volumes`; "Sign in" on a clean database is first-run `/setup` plus
TOTP enrolment; and no step creates project `PRJ-2026-083`, which the draft asks for (C8).

**The spec should state:** those steps.

### M11 — The customer carries Stage 2–4 fields the spec's own argument would defer

- [x] Dispositioned — **Fixed.** Owner's ruling: those fields arrive with the stage that reads each (§3).

§3 defers the organization's mail and signing references to Stage 4, and §12 defers custom
fields because adding them later "is cheap". The customer still gains delivery email,
e-invoice profile, PDF template name and signing policy now — read by nothing in Stage 1,
each with one possible value: the "configurability that nothing configures" `CLAUDE.md`
rules out.

**The spec should state:** that they arrive with the stage that reads them, or why the
customer is the exception.

### M12 — The irreversible step has no confirmation, and what is confirmed may not be what is finalized

- [x] Dispositioned — **Fixed.** §5: a confirmation showing totals from the same code path; finalization refuses if the gross differs from what was confirmed (owner's call, taken by default).

Finalize consumes a number for good, and `InvoiceCorrection` does not exist until M5, so a
misclick before then has no remedy. And the draft shows totals from when it rendered while
finalization resolves price and rate afresh; §5 accepts that for addresses, but a price
changed in another tab finalizes an amount the user never saw.

**The spec should state:** a confirmation showing the totals finalization will use, or
that §5's accepted cost covers prices and rates too.

---

### M13 — VAT is computed per line or per category; the spec does not say which

*Added by the spec's author after the review, from a list written and hashed before the
review was read. The reviewer's M4 covers the rounding mode; this is the rounding
granularity.*

- [x] Dispositioned — **Fixed.** §5: VAT per category on summed line nets, rounded once; §11 adds the two-line test.

§5 says totals are computed "from line data" but not at what level VAT is rounded. EN 16931
computes VAT per category on the sum of the line net amounts in that category, rounded once;
summing separately rounded per-line VAT can differ by a cent per line. With one line the two
agree, so neither the fixed example nor any Stage 1 test distinguishes them — and the
aggregate already holds a collection, so E09's second line inherits whichever the
implementation happened to pick, frozen into every snapshot written before then.

**The spec should state:** VAT per category on the summed line nets, rounded once, with a
two-line unit test where per-line rounding would differ.

### M14 — Decimal input under a circuit depends on the culture the circuit captured

*Added by the spec's author after the review, from the same hashed list. Not verified by
executing it.*

- [x] Dispositioned — **Fixed.** §9: numeric input follows the circuit's culture; §11's journey enters a decimal in German.

Quantity and unit price become interactive MudBlazor numeric fields. Parsing follows
`CurrentCulture`, so `100,50` means one hundred and a half in `de-DE` and is rejected or
misread in `en`. On a static page request localization sets the culture per request; a
circuit, as commonly documented, keeps the culture it started with, so switching language
mid-session does not change parsing until the circuit is re-established.

**The spec should state:** which culture governs numeric input on the interactive pages, and
one Playwright check entering a decimal in German.

## Strengths — keep these

Recorded so the revision pass does not accidentally weaken them:

- The pipeline-stage cut, with its reason: a subsystem cut defers the end-to-end proof the
  walking skeleton exists to give (§1).
- The no-re-render ruling, naming the XML as the part most tempting to regenerate and
  routing every correction through `InvoiceCorrection` (§4).
- Bytes, not `jsonb`, for a stated reason that checks out — executed against PostgreSQL 17.
- A counter row rather than a sequence, for a stated reason that checks out — `nextval`
  survives `ROLLBACK`, executed.
- Scheme configuration in Organizations and the counter in Invoices, with the transaction
  boundary as the reason (§6).
- A closed scheme rather than a template language, rejected because an invalid template
  surfaces at finalization, the worst moment (§6).
- The counter keyed on the document date, with the test that would pass on the wrong
  implementation named (§6, §10).
- `S` only with a named refusal, and the tax category stored from the first migration (§7).
- VIES never a finalization precondition (§7, `BACKLOG.md`).
- Resolution at finalization with its accepted cost written down, and event-maintained
  read models rejected for a reason rather than by default (§5).
- Per-page interactivity with account pages static and `/_blazor` left blocked — every
  factual claim §8 makes about the code is accurate.
- "Each guard is proven by mutation", including the instruction to fix or remove a test
  that stays green (§10).
- One organization per instance, with the tenant key in the connection rather than the
  row (§2).
