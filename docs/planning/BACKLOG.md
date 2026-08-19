# Backlog

Ideas raised during design that are **not** scope for the epic they surfaced in, recorded so the reasoning is not lost. Nothing here is committed work.

Each entry states what it is, why it was deferred, and where it would land. An entry that cannot say where it lands is not ready to be here.

## Outbound IPv6: Happy Eyeballs for `HttpClient`

**Lands in:** the first epic making outbound HTTP calls — E12 (E-Invoice-EU adapter), then E13 (Kimai import) and optional OIDC.

**The gap.** .NET supports IPv6 at the socket level and Kestrel listens on it, so inbound is fine. Outbound is not: `SocketsHttpHandler` does not implement RFC 8305. When a host resolves to both AAAA and A records, .NET attempts addresses sequentially with the full connect timeout each, so a broken or blackholed IPv6 path stalls the request instead of racing IPv4 and taking whichever answers first. On a network where IPv6 resolves but does not work, every outbound call pays that stall.

**Verify before building.** The claim above should be re-checked against the .NET version in use at the time — this is a long-standing gap rather than a permanent one, and it may be closed upstream.

**Approach.** `SocketsHttpHandler.ConnectCallback` is the extension point: resolve both families, start connection attempts staggered by a short delay, take the first to succeed, cancel the rest. The reference write-up is [IPv6 is hard: Happy Eyeballs and .NET's HttpClient](https://slugcat.systems/post/24-06-16-ipv6-is-hard-happy-eyeballs-dotnet-httpclient/), which describes the approach in prose — implement from the description.

**Related but separate:** MailKit (E14) does its own connection establishment and does not go through `HttpClient`, so it needs its own assessment rather than inheriting this fix.

**Project context.** `CLAUDE.md` states a preference for IPv6 over IPv4 and for handling both families in anything address-related. This is the outbound half of honouring that.

## TOTP enrolment: a QR code beside the manual key

**Lands in:** E02b, the second identity slice. It is the next epic that opens the identity pages, and the enrolment page is the only file this touches — carrying it further means a second reader meeting the same gap and re-deriving the same reasoning. It is explicitly *not* E14: the other E02a deferrals point at E14 because they need SMTP, and a QR code needs nothing but the key the page already renders.

**The gap.** `/account/enrol-totp` shows the base32 shared secret in four-character groups and nothing else. A user must type 32 characters into their authenticator app. Every app in common use accepts that, so this is a convenience gap on a screen each user sees once, not a compatibility gap — which is why E02a shipped without it rather than treating it as unfinished.

**What it needs.** An `otpauth://totp/` URI built from the issuer, the account name and the key, rendered as a QR code. The URI is trivial; the renderer is the decision.

**Do the URI first, on its own.** Manual testing of the shipped E02a build found that KeePassXC imports the account's user name and password but **does not recognise the bare base32 key** — it wants an `otpauth://` URI, as do Aegis, 1Password and the rest. So rendering the URI as selectable text is not a stepping stone to the QR code, it is most of the value: it fixes password-manager import with **no new dependency at all**, and a QR code is only a picture of that same string. Split this entry if it helps — the URI is a small, safe change; the QR renderer is the part that needs a dependency decision. A QR library is a new dependency on a page that displays a live second-factor secret, so it has to be one worth trusting and it must render locally — the "no external asset CDN, ever" ruling in `IMPLEMENTATION-NOTES.md` rules out any image service, and pointing a third party at an `otpauth://` URI would hand them the secret regardless.

**Constraints it must not break.** The page's response is already `no-cache, no-store` because it carries the secret, and the QR image must be inline (a `data:` URI) rather than a second request, or the secret gets its own cacheable URL. The Content-Security-Policy test is the check that an inline image source is actually permitted.

**Why not in E02a.** `CLAUDE.md`'s YAGNI rule, applied honestly: manual entry works for every user today, and the alternative was a new dependency in the sign-in path for one screen. See section 8 of `docs/superpowers/specs/2026-08-10-e02a-identity-foundation-design.md`.

## Password entropy: warn, never meter

**Lands in:** the `IPasswordValidator<ApplicationUser>` seam established in E02a — one class, no schema change.

**The idea.** Calculate entropy and warn when it is confidently low. Do **not** display a strength meter, a score, or a coloured bar.

The asymmetry is the whole point. A meter always shows something, so it always reassures — including when the measurement cannot support that reassurance. It is trivially satisfied by a password a dictionary attack finds immediately. A warning that fires only on confident true positives cannot mislead, because silence is not a claim.

Off by default, threshold in configuration.

**Full reasoning, sources, and the licence note about implementing from a published description rather than from GPL source:** `docs/superpowers/specs/2026-08-10-e02a-identity-foundation-design.md`, section 8.

## MudBlazor components render without their JavaScript

**Lands in:** the epic that first needs an interactive component. Not urgent, but it is a standing constraint every UI epic will meet, so it should be met knowingly.

**What was observed.** In the shipped E02a build, the floating label of a `MudTextField` sits on top of the value instead of shrinking above it — reproduced with a browser autofill on the sign-in form and with a typed authenticator code. It looks like a styling bug. It is not.

**The cause.** `AddInteractiveServerRenderMode()` is registered and `MapRazorComponents<App>()` calls it, but **no component declares `@rendermode`** — not `<Routes />`, not any page. Every component therefore renders as static server-side HTML with none of MudBlazor's JavaScript, and the floating label depends on that script to notice the field has a value. Anything else in MudBlazor that needs interactivity — dialogs, menus, snackbars, client-side validation — will fail the same way, silently, looking like CSS.

**Why turning it on is not one line.** E02a's enrolment gate deliberately leaves `/_blazor` **blocked** (`EnrolmentGate`), because allowlisting the circuit endpoint would let a gated user open a SignalR connection and render components server-side, bypassing the gate for any interactive page. Failing closed was the right default while nothing was interactive. The epic that introduces interactivity has to decide how a gated user's circuit is handled, and `IMPLEMENTATION-NOTES.md` records the reasoning so that decision is met rather than rediscovered.

**Sign-in must stay static.** Whatever else becomes interactive, the credential forms cannot come from a circuit — see `IMPLEMENTATION-NOTES.md` under "Testing the entrypoint" and the static-SSR notes.

## Human-readable invoice PDFs: template plus letterhead overlay

**Lands in:** the document-rendering epic. Architecture rule 3 already names the library — only `Fakturenn.Infrastructure.Documents*` may reference PDFsharp or MigraDoc.

**The binding constraint is PDF/A-3, not design.** ZUGFeRD requires PDF/A-3b with the XML embedded as an attachment and specific XMP metadata. That rules out the option most people reach for first — HTML and CSS through a headless browser — because Chromium cannot emit PDF/A-3, quite apart from importing a browser into the runtime image and brushing against SPEC §3's "arbitrary executable templates" non-goal. Font embedding is also mandatory under PDF/A, which constrains the font choice before any layout work starts.

**Ship both of these, neither of which is a designer.**

*Parameterised template (the default).* Logo upload, colours, fonts, a few layout toggles, rendered by MigraDoc. Simple, testable, and every invoice looks like Fakturenn's template wearing the customer's logo — which is fine for most users and a poor fit for anyone with a designed identity.

*Letterhead overlay (the escape hatch).* The user uploads their existing letterhead PDF and PDFsharp imports the page as a form XObject, drawing invoice content on top. Small businesses already have letterhead from a designer, and this keeps their brand byte-for-byte.

**The overlay does not have to accept everything.** Publish size constraints and make fitting the user's responsibility, backed by a preview. **DIN 5008** is the anchor worth using: it fixes the address-window zone, the fold marks and the margins for German business letters, so "keep these zones clear" becomes a citable rule rather than a Fakturenn invention — and it is what makes the result work in a windowed envelope. https://de.wikipedia.org/wiki/DIN_5008

**Worth an ADR** when the epic opens: the PDF/A-3 conformance route, the font decision, and where the overlay's coordinate origin lives.

## Invoices per project, from Kimai

**Lands in:** the Kimai import epic.

**The requirement.** Some customers want one invoice per project rather than one per customer, so a single customer can receive several invoices for the same period — potentially several on the same day.

**The fields already exist.** `ProjectNumber` and `CustomerProjectNumber` are in `docs/domain/DOMAIN-MODEL-v0.1.md`, as is `KimaiEntriesImported`. What does not exist yet is the grouping: invoice generation has to group by customer **x project x period**, not customer x period.

**Invoice numbering is not the risk it first appears.** The scheme in use is `RYYMMDDC` with a **daily** counter — `R2608131`, `R2608132`, `R2608133` — so ten invoices in one day are unique by construction no matter how many customers are involved. The prefix is fixed width, so the counter cannot collide with the date.

**The real risk is allocating that counter concurrently.** Two invoices generated at the same instant both read the same "highest counter today" and both compute the same next value; a unique index then turns the loser into a *failure* rather than a correct second number. This is the same check-then-act shape as first-administrator creation, and E02a already established the pattern: a PostgreSQL advisory lock held for the transaction — see `SetupLock` and the notes in `IMPLEMENTATION-NOTES.md` under Persistence, including the requirement that an explicit transaction under `EnableRetryOnFailure` goes through `CreateExecutionStrategy().ExecuteAsync(...)` and builds its entities inside the delegate, because that delegate can re-run.

**Also needs:** a uniqueness constraint on the invoice number in the database, so the guarantee does not rest on the allocator alone.

## xunit v3 4.0.0: the parallelism attribute has no drop-in replacement

**Lands in:** its own change, whenever it is convenient. Nothing depends on it.

**Why it is not just a version bump.** 4.0.0 marks
`CollectionBehavior.DisableTestParallelization` obsolete, and this repository
treats warnings as errors, so the bump is a build failure rather than a warning.
`tests/Fakturenn.UiTests/AssemblyInfo.cs` is the only place that uses it — and it
is load-bearing: it keeps the browser suite serial so three EF Core hosts do not
build models concurrently in one process. The comment above that line explains
what happens when they do.

**The blocker.** The release notes give the replacement as
`[assembly: Parallelization(Mode = ParallelMode.Off)]`, but neither
`ParallelizationAttribute` nor `ParallelMode` resolves from the packages
`xunit.v3.mtp-v2` 4.0.0 restores — `xunit.v3.core` stays at 3.0.1 in the graph
while `xunit.v3.core.mtp-v2` moves to 4.0.0. So this is a question about how the
metapackage composes its dependencies, not a rename to look up. Answer that
before editing the attribute.

**Do not** work around it by suppressing the obsolete diagnostic, or by widening
a timeout, or by deleting the attribute and hoping. Serial execution is the
property under discussion, and a green run proves nothing about it — the failure
it prevents was 2 reds in 13 runs.

**Also worth checking while there:** 4.0.0 relaxes
`TestContext.Current.CancellationToken` so it no longer throws once the context
is disposed. This suite uses that token everywhere. A relaxation cannot break a
correct caller, but it can hide an incorrect one, so it is worth a look rather
than an assumption.

**Dependabot PR #13 was closed rather than left open**, because a stale red PR
on the board teaches everyone to ignore reds. Dependabot will re-raise it.
