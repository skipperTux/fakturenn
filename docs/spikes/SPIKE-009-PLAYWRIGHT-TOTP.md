# SPIKE-009 — Playwright TOTP authentication

**Status: closed.** Answered by E02a Task 15. The prototype is not a throwaway — it
is `tests/Fakturenn.UiTests`, which runs in CI.

## Questions

- Deterministic TOTP secrets?
- Parallel isolation?
- Reusable authenticated state?

## Exit

Automated password + TOTP login against packaged application.

**Met.** `IdentityJourneyTests.Setup_then_password_and_totp_sign_in_reaches_the_application`
drives first-run setup, the password form, forced authenticator enrolment and the
recovery-code acknowledgement through Chromium against the real application, on a real
socket, against a real PostgreSQL container. Nothing bypasses the second factor.

## Answers

### Deterministic TOTP secrets — do not fix the secret, read it

The obvious design is a fixed base32 secret seeded into the database. It was not used.
Seeding a secret means the test never visits the enrolment page, and the enrolment page
is the thing under test: it is what shows the key, and a key it displayed but did not
store would go unnoticed.

What the suite does instead: it runs the real enrolment, reads the manual-entry key out
of the page's own `totp-key` element, strips the display grouping, and computes a live
RFC 6238 code from it with `Otp.NET`. Determinism comes from the code being *derived*
rather than guessed — `AuthenticatedWebAppFixture.CodeFor(key)` — not from the secret
being constant. A secret that is different on every run is fine; a code that is computed
wrongly is not, and that is what the assertions catch.

`WrongCodeFor(key)` is the counterpart: the live code plus 500 000, modulo one million.
It is syntactically valid, and it is derived from the real code so it cannot accidentally
*be* the real code, including across a thirty-second window roll. ASP.NET Core Identity
accepts a window of ±2 time steps, so a code computed a few seconds before it is
submitted still verifies; the suite does not need to synchronise with the window.

### Parallel isolation — one container per fixture, and no parallel fixtures

Isolation between fixtures: each `AuthenticatedWebAppFixture` starts its own
`postgres:17-alpine` Testcontainer and its own host on port 0, so two fixtures share no
database, no key ring and no port. `ContentSecurityPolicyTests` has a fixture of its own
precisely because it needs a `/setup` page that no user has closed yet.

Isolation *within* a fixture: one browser context per test, and accounts that must not
disturb each other get their own user. `Locking_a_user_stops_their_existing_session`
locks a purpose-made victim rather than the administrator, whose session is cached and
replayed by every other test in the collection.

**But the test assembly does not run collections in parallel, and that is a measured
decision rather than caution.** With parallelism on, two fixtures initialising at once
intermittently killed the whole shared collection with

```text
System.InvalidOperationException: The model must be finalized and its runtime
dependencies must be initialized before 'GetRelationalModel' can be used.
  at Microsoft.EntityFrameworkCore.RelationalModelExtensions.GetRelationalModel(IModel)
  at Microsoft.EntityFrameworkCore.Migrations.Internal.Migrator.HasPendingModelChanges()
  at Fakturenn.UiTests.AuthenticatedWebAppFixture.MigrateAsync()
```

— one thread reading an EF Core model another thread was still finalising, out of EF's
process-wide internal service provider. It reproduced twice in eleven runs and left nine
runs green in between: often enough to fail a pipeline, rare enough to be written off as
"flaky Playwright". `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
in `tests/Fakturenn.UiTests/AssemblyInfo.cs` carries the reasoning.

This is a harness constraint, not a product defect. A deployed instance is one host in
one process; the one case where two Data Protection providers legitimately meet inside
one process is already covered by Task 14's `UserTokenProtectorModelCacheKeyFactory`.
The cost is about eleven seconds — the suite is dominated by the sixty-second
security-stamp wait, not by its parallelism.

### The other race, and it was not the fixtures

Serialising the collections removed one intermittent failure and left a second, which is
worth recording because it looks identical from the summary line and has nothing to do
with EF Core. The first-run journey hung the full timeout on
`WaitForURLAsync("**/account/login")` after posting `/setup`, while the server log showed
the navigation had already succeeded:

```text
HTTP POST /account/setup responded 302
HTTP GET  /account/login responded 200
```

`ClickAsync` returns when the click is dispatched, not when the navigation it causes
settles. When the navigation wins the race, Playwright finds the URL already matching and
falls through to `WaitForLoadStateAsync(Load)` — for a document whose `load` event has
already fired. It then waits for an event that will never come again. Playwright's own log
said only `"NetworkIdle" event fired`.

Every such wait is now `AuthenticatedWebAppFixture.ArriveAtAsync(page, path, testId)`: wait
for an element the destination page renders, then assert `new Uri(page.Url).AbsolutePath`.
A locator wait re-evaluates rather than listening for a one-shot event, and the path
assertion means the element cannot stand in for the wrong page. It asserts more than the
call it replaced, not less.

The failure also cascaded badly — one real failure produced four red tests, three of them
complaining about a missing `setup-email` field on a page that is *supposed* to be gone by
then. `EnsureAdministratorAsync` now caches the first-run failure and re-throws it as the
inner exception of every later caller's, so the diagnosis survives the cascade.

### Reusable authenticated state — Playwright `storageState`, from a genuine sign-in

`AuthenticatedWebAppFixture.SignInAsAdministratorAsync(IBrowser)` performs the first-run
journey once, serialises the resulting cookie jar with `IBrowserContext.StorageStateAsync`,
and replays it into a fresh context for every later caller. Reuse is an optimisation; the
state itself is the output of a real setup, a real password post and a real code the
application accepted.

Fabricating the cookie was rejected. It is the cheaper option and it removes the only
evidence that authentication works — every test built on it would then pass against an
application whose sign-in is broken.

## Proof that the journey is load-bearing

Three mutations, each run against the whole suite:

| Mutation | Result |
| --- | --- |
| `script-src 'self'` narrowed to `script-src 'none'` | `ContentSecurityPolicyTests` red. The console channel reported the blocked script by name and the anti-vacuity guard reported `/_framework/blazor.web.js` never fetched. |
| `[Authorize(Policy = Permissions.UsersRead)]` on `/admin/users` weakened to a bare `[Authorize]` | `A_signed_in_user_without_the_permission_is_turned_away` red, and only that test — it reached `/admin/users` instead of `/account/denied`. The administrator's own journey stayed green, which is why one journey alone would not have noticed. |
| Authenticator token provider replaced with one that validates any code | `Setup_then_password_and_totp_sign_in_reaches_the_application` **still green**, because it submits a correct code. `A_wrong_authenticator_code_does_not_sign_the_user_in` red. A journey that passes against a broken verifier is not testing authentication; the wrong-code test is the half that is. |

## Discarded alternatives

- **A fixed, seeded authenticator secret.** Skips the enrolment page, which is the
  component that has to display and store the key correctly.
- **A fabricated authentication cookie.** Removes the only proof that sign-in works.
- **`WebApplicationFactory` / an in-memory test server.** No real socket, so no
  WebSocket circuit and no browser. The integration suite already covers what an
  in-memory host can cover; this suite exists for what it cannot.
- **A shared PostgreSQL instance across fixtures.** Cheaper to start, but the
  Content-Security-Policy walk needs a database in which no user exists yet, and the
  shared collection needs one in which the administrator does.
- **Asserting only that a `Content-Security-Policy` header is present.** The header is
  present whether the policy is right or blocks every script on the page. The suite asks
  the browser instead — the `securitypolicyviolation` event, the console error and the
  failed request — and separately asserts that the assets the policy governs were really
  fetched, so "no violations" cannot mean "nothing was loaded".

## Recommendation

Keep the shape for every later UI journey: a real host on a real socket, a real database
container, a real sign-in, `storageState` for reuse, one browser context per test,
`ArriveAtAsync` rather than `WaitForURLAsync` after any click, and no parallel collections
in this assembly. No ADR change — SPIKE-009 confirmed the testing
approach `SPEC-v0.1.md` §10 already sets out rather than changing it.
