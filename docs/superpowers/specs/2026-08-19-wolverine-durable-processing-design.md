# Wolverine durable processing — design

**Epic:** M0's final piece. Closes the milestone.
**ADR:** ADR-007, currently *Proposed*. This design is what makes it *Accepted*.

## 1. What this delivers, and what it does not

M0 asks for the foundation, not a user of it. No module has a side effect to
queue yet — the first will be E12's e-invoice generation, followed by E14's
mail.

So this epic delivers **a working transactional outbox with nothing publishing
through it**, and proves the seam with tests rather than with a demonstration
message shipped in production code.

The alternative — defer everything to E15 — was rejected. The transactional
outbox is the entire reason ADR-007 chose Wolverine, and it is the part that is
expensive to retrofit. If E12 discovers that finalising an invoice and queueing
its e-invoice do not share a transaction, that is a schema and lifetime problem
across every module. Proving it once, now, is what makes the foundation real
rather than nominal.

## 2. Execution model: in-process

Wolverine runs inside `Fakturenn.Web`. One container, one deployment unit, one
`--migrate`.

**Why not a separate worker.** This is self-hosted software whose deployment
target is one operator running Compose or a small Kubernetes namespace. A second
always-on container is real operational weight for them. The work E12–E14 will
queue — render a PDF, sign a MIME part, talk to SMTP — is seconds, not minutes.
Durability comes from the outbox, not from process separation: a crash mid-send
loses nothing either way, because the envelope is committed in PostgreSQL.

**This is reversible.** The same handlers run in a worker later by changing where
the host starts them. `DEPLOYMENT-BASELINE.md` already commits to stateless
replicas, so a busy instance does not block a message — another picks it up.

**The one thing in-process does not solve** is scheduled work. E10's reminders
and E16's backups are timer-driven, and N replicas each firing the same timer is
a real problem. It is a *scheduling* problem — a leader election, or a Kubernetes
CronJob calling an entrypoint — and it does not require a permanent second
container in v0.1. Deferred deliberately, not overlooked.

## 3. Assembly and schema

**`Fakturenn.Infrastructure.Messaging`**, mirroring
`Fakturenn.Infrastructure.DataProtection`. It owns Wolverine registration and
storage provisioning. Only `Fakturenn.Web` references it; architecture rule 4
keeps it out of every module automatically, by name pattern.

It takes the full new-assembly checklist from `CLAUDE.md`: `Fakturenn.slnx`, a
`typeof(...).Assembly` line in `FakturennArchitecture.Loaded`, a
`ProjectReference` from the architecture tests, and an entry in the anti-vacuity
list. The last has no test behind it, so the loader line is verified
load-bearing by removing it and confirming **two** tests fail — the
loader-omission cross-check and the anti-vacuity guard — as Tasks 5 and 16 did.

**Schema `messaging`**, fourth alongside `identity`, `invoices` and
`dataprotection`.

## 4. Who writes the DDL, and when

**Wolverine owns its table definitions. We own the timing.**

Wolverine's envelope, dead-letter and node tables are its internal schema and
change between versions. Hand-writing EF migrations mirroring a library's
private tables means re-deriving them on every upgrade, and getting that wrong
produces a runtime failure in message storage rather than a build error — the
same invisible-until-it-isn't class as the model-cache defect E02a Task 14
found.

**Auto-provisioning at startup is switched off explicitly**, with a comment
naming the invariant it would otherwise break: *migrations never run
automatically at startup; use the explicit `--migrate` entrypoint*.

**Two knobs, not one.** An earlier draft of this section said that omitting
`UseResourceSetupOnStartup()` was sufficient. It is not, and the error was found
by starting the host rather than by reading documentation:
`WolverineOptions.AutoBuildMessageStorageOnStartup` is a *separate* setting
defaulting to `AutoCreate.CreateOrUpdate`, and with only the first omission the
host created the entire `messaging` schema on boot — confirmed by Wolverine's own
log line, `Applied database migration for Wolverine Envelope Storage`. Both are
required: omit `UseResourceSetupOnStartup()` **and** set
`AutoBuildMessageStorageOnStartup = AutoCreate.None`.

**Wolverine 6.30.0 no longer bundles Roslyn**, so the default
`TypeLoadMode.Dynamic` throws at startup. `TypeLoadMode.Auto` uses reflection
dispatch, needs no extra package and no `codegen write` step. Revisit only if
handler dispatch shows up in a profile.

**`--migrate` becomes a sequence of separately-invocable steps**, not one call:

1. EF migrations, via the existing `DatabaseMigrator.RunAsync`
2. Messaging storage provisioning
3. Role seeding, then permission-catalogue validation (unchanged)

Order is stated in code. Wolverine does not need the business schemas, but a
partial `--migrate` that created messaging tables against a database with no
business schema is a confusing halfway state.

**The steps are internal composition, not a public menu.** A future migration
service is a self-contained service with one job and one operation: *migrate*.
Its consumer calls that and gets success or failure — it does not choose which
steps run, or in what order, or whether to skip one. Keeping the steps separable
here is for readability and testing; the *contract* is a single operation, and
that is what the eventual service exposes.

Two consequences follow, and both belong in the code rather than in someone's
head.

**Taking a backup is not this operation's job.** It is a prerequisite the
operator satisfies before invoking it. Nothing in `--migrate` will attempt one.

**There is no full rollback, so failure must be precise.** PostgreSQL makes DDL
transactional per migration, but this operation spans several `DbContext`
migrations plus role seeding plus catalogue validation, and no transaction wraps
all of them. A failure partway through therefore leaves a partially-migrated
database. The operation's obligation is to say exactly which step failed and
why, exit non-zero, and leave the operator to restore the backup — never to
half-repair, and never to imply a rollback it cannot perform. The existing
catalogue-validation failure already behaves this way: it names the offending
permission and refuses to complete.

**Nothing provisions at startup**, and the two un-provisioned cases now behave
differently. This is a deliberate ruling, not an accident of the library.

*No connection string configured* — the host starts. `AddFakturennMessaging`
returns before configuring persistence and Wolverine falls back to in-memory
queues, so `/alive` answers 200 and `/health` answers 503 exactly as before. The
database-free UI fixture depends on this.

Because that fallback is silent, **the host must log at Critical when durable
persistence is not configured**, naming the consequence: messages are held in
memory and will not survive a restart. A silently non-durable instance is the
failure class this design exists to prevent, and a fallback nobody is told about
reintroduces it.

*A connection string configured, but the schema not provisioned* — **the host
crashes during `StartAsync`, and that is intended.** With
`AutoBuildMessageStorageOnStartup = AutoCreate.None`, Wolverine logs *Skipping
automatic message storage migration on startup* and then throws from its own
explicit check, `MessageDatabase.AssertStorageExistsAsync`: *The Wolverine message
storage for database 'default' is missing or out of date (schema difference:
Create).*

An earlier draft attributed the crash to the durability agent's
`messaging.wolverine_nodes` query. That was measured against
`ResourceMigrationFailureMode.ContinueOnFailures` and `DurabilityMode.Solo` —
both were tried, both still crashed, from that second touchpoint — but it is
**not** where the shipped configuration dies, and neither `wolverine_nodes` nor
`messaging` appears anywhere in the resulting exception chain. Anything matching
on the failure text must match on Wolverine's own noun, *message storage*. The
ruling is unchanged; only the mechanism was misdescribed.

Rather than fight the library into a "starts fine, storage silently absent" shape
it does not support, this design accepts the crash.

The reasoning, weighed against this project's stated preference for self-healing
over crash-looping: an instance pointed at a real database that has never been
migrated is a **deployment error**, not a transient fault. `DEPLOYMENT-BASELINE.md`
already mandates a migration step before traffic. Retrying cannot fix a missing
schema, so a crash loop here is a loud, correct signal rather than a failure to
recover — unlike a database that is merely slow to accept connections, which
`DatabaseMigrator`'s retry budget does and should absorb.

## 5. The outbox seam

**Enrolment lives in the host.** `Fakturenn.Web` registers Wolverine's EF Core
integration and enrols each module context. `Fakturenn.Modules.Invoices` gains
no reference to `Fakturenn.Infrastructure.Messaging`, and its `DbContext` is
untouched — the same arrangement as the audit interceptor: the module owns the
context, the host wires the behaviour onto it.

Enrolled today: `InvoicesDbContext`, the only business context that exists.

Note that it is **schema-only** — no `DbSet`, no entities, one migration that
creates the `invoices` schema. Enrolling an empty context is not a problem: the
outbox binds to the context's connection and transaction, not to its entities.
But it does shape how this epic can be tested — see section 7.

**Not enrolled, deliberately:**

- `IdentityDbContext` — Identity has no side effect to queue. Its authentication
  events are Serilog, synchronously, and that is correct. Enrolling it "for
  symmetry" is the speculative wiring `CLAUDE.md`'s YAGNI rule rejects.
- `DataProtectionDbContext` — not a business context.

**How a slice uses it.** A feature slice takes its module's `DbContext` and the
outbox for that context, writes its rows and publishes in the same unit of work,
and commits once. The envelope lands in `messaging.*` inside that transaction.
If the transaction aborts, the envelope goes with the rows.

**Message types belong in the publishing module's `.Contracts` assembly** — the
existing cross-module surface — never in `Fakturenn.Infrastructure.Messaging`,
which is infrastructure and knows nothing about invoices.

**Retries, dead-lettering and serialisation are Wolverine's defaults.** No custom
policy in M0. E12 and E14 will have real opinions once real handlers exist. The
only thing configured is durable local queues, because that is the choice that
makes the storage real rather than in-memory.

## 6. Slices reference Wolverine directly, and there is no containment rule

A slice publishing through the outbox means `Fakturenn.Modules.<Name>`
references the Wolverine package. That is deliberate.

The alternative — a module-owned publishing interface implemented by
infrastructure — is the Clean Architecture instinct, and `CLAUDE.md`'s SOLID
section leans that way. But its KISS rule cuts harder: *no abstraction without a
second caller; no interface with one implementation and no test double.* And
ADR-002 rejects mandatory layers per feature.

**The precedent is already in the repository: modules reference
`Microsoft.EntityFrameworkCore` directly.** Nobody wrapped EF behind a
module-owned repository interface. Wolverine's outbox is the same category of
tool — a persistence-adjacent capability the slice uses, not a foreign system it
needs insulating from.

**No architecture rule contains Wolverine**, unlike rules 2 and 3 for MimeKit and
PDFsharp. Those are transport and rendering concerns with one legitimate owning
adapter. Messaging is cross-cutting, like EF.

This is written down because a future reviewer will see a module referencing a
third-party messaging library, compare it to rules 2 and 3, and "fix" it by
introducing the abstraction this section rejects.

### What replacing Wolverine would actually cost

The question worth asking about any abstraction is its price against the
probability of needing it. Estimated here so the decision can be revisited with
numbers rather than taste.

**Portable as-is.** Handler bodies are plain methods taking a message — they move
to MassTransit, NServiceBus or Rebus close to verbatim. Message types are POCOs
in `.Contracts`. Neither carries Wolverine in its shape.

**Contained already.** Transport choice, persistence, retry policy and
provisioning — the genuinely heavy part — live entirely in
`Fakturenn.Infrastructure.Messaging` and the host's registration. A swap
rewrites that assembly, which is what it is for.

**What leaks into slices** is the publish call itself: one line per call site.

**Blast radius today is zero** — nothing publishes. At v0.1 completion, across
E10, E12 and E14, realistically five to fifteen call sites.

**Probability is low.** Wolverine with a PostgreSQL outbox is a deliberate fit
for a self-hosted single-database application. The plausible trigger is
abandonment or a licence change, not dissatisfaction.

So the abstraction would cost an interface with one implementation and no test
double — which `CLAUDE.md` bans outright — to save roughly a dozen one-line
edits at low probability. Rejected.

**The cheap insurance is a convention, not an interface:** keep publish calls
thin and inside slice code, and keep Wolverine types out of domain types. That
costs nothing and preserves most of the flexibility an abstraction would buy.

## 7. Testing: the seam we configured, not the library behind it

Four assertions, each about our configuration.

1. **A rolled-back transaction publishes nothing.** Open a transaction on the
   enrolled context, publish, abort. Assert **the handler was never invoked** and
   no envelope survives in `messaging.*`. This distinguishes an engaged outbox
   from direct publishing, and the failure it catches — a failed invoice still
   firing its e-invoice — is invisible in the happy path.

   The discriminator is the *handler invocation*, not the absent envelope: a
   direct, non-transactional publish leaves no envelope row either, because it
   never writes one. Only "the message was delivered despite the rollback"
   separates the two.

   **No business row is written, because there is none to write.**
   `InvoicesDbContext` is schema-only today — it declares no `DbSet` and its
   `InitialCreate` migration creates the schema and nothing else. That is fine:
   the property under test is whether the *envelope* participates in the
   caller's transaction, and a business row would add realism without adding
   proof. When E09 gives the module real entities, this test is worth revisiting
   so it writes one — at which point it exercises the exact shape a slice will
   use.
2. **A committed transaction delivers.** Proves enrolment exists at all.
3. **The envelope is persisted on commit.** A row-count assertion against
   `messaging.*` before any handler runs. This is the sliver of durability that
   is ours: it proves durable storage was selected rather than an in-memory
   transport, which would pass 1 and 2 and lose everything on restart.
4. **Handler discovery is configured for the module assemblies.** A
   host-composition guard in `tests/Fakturenn.Web.UnitTests`, the same site and
   reason as `The_claims_principal_factory_is_the_permission_factory`: wiring a
   unit test over a class cannot see.

Plus **the enrolment checklist guard**: every module context registered for
migration must also be enrolled with the outbox. Adding a module and forgetting
enrolment fails a test rather than shipping a silently non-transactional
publisher. Enrolment is per-context, and an unenrolled context still *publishes*
— non-transactionally, with no error and no warning.

**Each is proven by mutation.** Break the enrolment and 1 must redden; point
storage at in-memory and 3 must redden; drop an assembly from discovery and 4
must redden. A mutation that leaves everything green means the test is
decorative and it is removed or fixed, not kept.

**The message type and handler live in the integration test project.** Nothing
demonstrative ships in production code.

### What is deliberately not tested

Wolverine's restart recovery, retry behaviour, dead-lettering and envelope
serialisation. Those are the library's promises, and testing them is the same as
testing that Npgsql returns the rows it fetched.

The rule, stated so nobody later "improves" coverage by re-proving it: **test the
seam you configured, not the library behind it.** A kill-and-restart test would
exercise Wolverine's durable queue, not our wiring. Assertion 3 covers the part
that is ours — that durable storage is what we selected.

## 8. Known risk

The integration suite already peaks at eleven concurrent PostgreSQL containers,
and Wolverine adds a node registry and background sender loops to every host
those tests build. If that interacts badly — stalled shutdowns, port pressure,
envelopes leaking between tests — it is reported as a finding, not absorbed by
widening a timeout.
