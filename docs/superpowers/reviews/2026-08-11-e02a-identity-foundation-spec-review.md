# Spec review — E02a Identity foundation

**Date:** 2026-08-11
**Spec under review:** `docs/superpowers/specs/2026-08-10-e02a-identity-foundation-design.md`
**Spec status at review time:** Approved
**Review type:** Read-only, pre-plan gate — complete / sound / secure
**Documents consulted:** the spec; `docs/superpowers/plans/2026-08-10-e02a-identity-foundation.md`;
`SECURITY-BASELINE.md`; `DEPLOYMENT-BASELINE.md`; `MODULE-OWNERSHIP.md`; `PLAN-v0.1.md`;
`SPEC-v0.1.md` §9/§10; ADR-008; SPIKE-009. Code claims verified against the repository
(`GuidV7IdGenerator`, `IClock`, `DatabaseMigrator.RunAsync(IReadOnlyList<Func<DbContext>>)` —
all exist as the spec states).

## Verdict

- **Sound: yes.** No decision this review would reverse. The core choices —
  permission constants with roles as data, custom role tables for E02b's
  `OrganizationId`, `IdentityUserContext` as the base class, static SSR
  authentication pages, the Data Protection key ring in PostgreSQL — are all
  correctly reasoned. §9 is the strongest section: the empirically verified
  plaintext recovery-code finding, the partial-versus-full-compromise table,
  and the availability cost of encryption stated plainly are exactly what a
  security section should look like.
- **Secure: direction right, holes real.** S1–S7 below are gaps that ship
  exploitable or operationally broken if the implementation follows the spec
  literally. S1, S3, and S4 are the most consequential.
- **Complete: no.** Missing flows (logout, forced password change), seeding
  and permission-upgrade semantics, rate-limiting mechanics, and the
  localization stance.

**Recommendation:** one spec revision pass addressing S1–S7 plus the
seeding/upgrade paragraph (C1) before implementation starts. The remaining
findings are one-line fixes.

---

## Revision outcome — 2026-08-11

All 19 findings dispositioned: 18 fixed, 1 fixed partly by deletion (C2). None
rejected. The spec is revised; the implementation plan is revised to match.

**S2 was the finding that justified the gate.** It reads as a documentation gap
— "the spec is silent while the plan has already decided" — but the plan's
decision was incomplete in a way that would have shipped: `PermissionClaims` was
read by the authorization handler and written by nothing. Every authorized page
would have returned 403, including the administrator's own. It would not have
been caught by the plan's tests, because they construct a `ClaimsPrincipal` with
the claims already present.

That is the same shape as this project's earlier dead-architecture-rules
incident: a mechanism that was never wired, and a test suite that asserted the
mechanism's *inputs* rather than its *effect*. The spec now specifies the claims
factory, and §11 adds an end-to-end test that a permitted user actually reaches
an authorized page.

**Two findings were the reviewer catching the author's own errors.** M1 and M2
were introduced by the section renumber that inserted §7 — a self-review had
already run over that change and missed both.

**Scope discipline held.** C2 was resolved by *removing* two permission
constants rather than inventing enforcement sites for them, and three items were
rejected outright rather than deferred: 2FA "remember this machine", shared
rate-limit state across replicas, and a recovery-code regeneration page. The
project owner's framing — find the balance between must-have, nice-to-have and
YAGNI for an open-source project rather than importing commercial-SaaS instincts
wholesale — is what those three rejections implement.

---

## Security findings

### S1 — Session revocation on administrative actions is unspecified

- [x] Dispositioned — **Fixed.** §8 now requires explicit security-stamp rotation on every admin and CLI action that changes credentials or lock state, with `ValidationInterval` set to one minute rather than the default thirty. §11 adds a test that a locked user's existing session stops working.

The spec's admin actions (§2: reset password, clear TOTP, lock) and the CLI
entrypoints (§10) say nothing about what happens to the target user's
**existing** authentication cookie. Without an explicit security-stamp
rotation on these actions and a configured
`SecurityStampValidatorOptions.ValidationInterval`, a locked or
password-reset user keeps a working session for up to the default 30 minutes
— or indefinitely if stamp validation is never wired. "Lock" that does not
terminate the session is not lock.

**The spec should state:** every admin and CLI action that changes
credentials or lock state rotates the security stamp; the chosen validation
interval; and a test that a locked user's existing session stops working.

### S2 — How the authorization handler learns permissions is undefined

- [x] Dispositioned — **Fixed, and it was worse than a spec gap.** The plan already read `fakturenn.permission` claims that *nothing populated* — every `[Authorize(Policy = ...)]` would have denied, including the administrator's own page, and the hand-constructed unit tests would still have passed. §6 now specifies an `IUserClaimsPrincipalFactory` deriving permissions at sign-in and at each stamp validation, and §11 adds the end-to-end test that would have caught it.

§6 defines the model but never says whether permissions are stamped as
claims into the cookie at sign-in (the implementation plan has already
chosen exactly this, via `PermissionClaims`) or looked up per request.
Claims-in-cookie means stripping a role from a user has no effect until
their cookie is re-validated — which loops back to S1: role changes must
also rotate the security stamp, and the permission claims must be re-derived
at stamp validation. This is a real design decision with a security
consequence, and the spec is silent on it while the plan has already
decided.

### S3 — The last administrator can be locked with no recovery path in the spec

- [x] Dispositioned — **Fixed.** §10 adds `--unlock-user`, and `--reset-password` now explicitly clears lockout. The guard protected the wrong thing without them.

The `IsSystemRole` guard (§6) prevents stripping `users.manage` from its
last holder and prevents role deletion — but not **locking** the last
administrator via `/admin/users`. The operator entrypoints (§10) are
`--create-admin` (refuses once users exist), `--reset-password`,
`--reset-mfa`, `--list-users` — there is **no `--unlock-user`**, and whether
`--reset-password` clears lockout state is unstated.

**Resolve one of:** add an unlock entrypoint; define `--reset-password` as
also unlocking; or extend the last-administrator guard to the lock action.

### S4 — No password policy anywhere

- [x] Dispositioned — **Fixed.** §3 fixes minimum length 12, no composition rules, per current NIST guidance.

ASP.NET Core Identity's defaults (6 characters) ship unless overridden, and
neither the spec nor `SECURITY-BASELINE.md` states requirements. The spec
should fix a policy — for example minimum length 12+, no composition rules,
per current NIST guidance. This is exactly the kind of decision §3's table
exists for.

### S5 — Rate limiting is named but not designed

- [x] Dispositioned — **Fixed.** §8 partitions on username plus client IP; §9 configures forwarded-header trust, without which the partition is meaningless behind a proxy. The per-replica multiplication is now an accepted, written trade-off with lockout named as the durable control.

§8 says "built-in limiter" on login and 2FA, but not the partition key
(per-IP? per-username? both?). Per-IP behind a reverse proxy requires
forwarded-header configuration — which `SECURITY-BASELINE.md` explicitly
lists ("proxy-header configuration") and the spec never mentions — otherwise
every client shares the proxy's IP and the limiter is either a self-DoS or
useless. Additionally, the built-in limiter is in-memory per replica, and
`DEPLOYMENT-BASELINE.md` commits to multiple stateless replicas, so limits
are effectively multiplied by replica count. That is probably acceptable —
but it should be an accepted, written trade-off in the style of §9, not an
omission.

### S6 — `/setup` and `--create-admin` share a TOCTOU race and an unowned-instance window

- [x] Dispositioned — **Fixed.** §8 replaces the count-then-insert guard with a unique index plus a caught constraint violation, and states plainly that a fresh instance is owned by whoever reaches `/setup` first, with `--create-admin` before exposure as the documented mitigation.

Both are guarded by a "no users exist" query; nothing serializes
check-and-insert. Two concurrent `/setup` posts — or a replica racing a
Kubernetes Job — can both pass the count query. The spec needs a stated
mechanism: a unique constraint plus catch, a serializable transaction, or an
advisory lock.

Separately: a freshly deployed, internet-reachable instance is owned by
whoever reaches `/setup` first. Most self-hosted software accepts this risk;
the spec should say it accepts it, with `--create-admin` before exposure as
the mitigation.

### S7 — Admin-created users: the administrator knows the password, and "must change password" has no state or flow

- [x] Dispositioned — **Fixed.** §5 adds `MustChangePassword`; §8 adds the forced-change flow; §10 sets the flag on both `--create-admin` and `--reset-password`, and on administrator-created users.

§2 says "administrators create users with a password directly", and only
`--reset-password` mentions forcing a change at next sign-in. The
admin-creates-user path should force the same change. Beyond that, the spec
has **no data-model field and no flow** for "must change password" at all:
§5 shows only `MustEnrolTotp`, and §8 has no change-password page. That
state and flow are missing from the spec entirely, on both the CLI and the
admin paths.

---

## Completeness findings

### C1 — Seeding timing and the permission upgrade path

- [x] Dispositioned — **Fixed.** §6 states that seeding runs from `--migrate` rather than at startup, avoiding a multi-replica race on the unique role-name index, and that it is a **re-sync** rather than create-if-absent, so a permission added by a later epic is granted to existing installations.

§6 says the `Administrator` role is "seeded" — never when. Startup?
`--migrate`? Multi-replica startup seeding races on the unique name index.
The bigger gap: the startup validation catches **unknown** permission
strings but not **missing** grants. When a later epic adds a fifth
permission constant, existing installations' `Administrator` role rows lack
it, silently. The spec needs a stated rule: system-role grants are re-synced
to `Permissions.All` during seeding/migration.

### C2 — Permission set inconsistency

- [x] Dispositioned — **Fixed, partly by deletion.** `roles.read` and `roles.manage` are removed — a permission constant with no enforcement site is exactly the speculative surface this project's YAGNI rule forbids, and E02b will add them with the UI that enforces them. `users.read` is kept and given a real site: the user list. Mutations require `users.manage`.

§6 claims "E02a defines only the permissions it enforces", but `RolesRead`
and `RolesManage` have no enforcement site — roles are managed by SQL and
there is no roles UI in scope. And §8 gates all of `/admin/users` on
`users.manage`, so where is `users.read` enforced? Either drop the
unenforced constants (the spec's own YAGNI rule) or name their enforcement
sites.

### C3 — Missing flows in §8

- [x] Dispositioned — **Fixed.** §8 adds sign-out and the forced password change, fixes the recovery-code count at ten, defers a regeneration page with a reason, and rejects 2FA "remember this machine" outright rather than deferring it.

- Logout.
- Forced password change (see S7).
- Recovery-code regeneration and exhaustion — the production code count is
  also undecided (the probe used 3; that was a test convenience, not a
  decision).
- 2FA "remember this machine": allow or not. A security-posture choice that
  should be explicit; recommendation is off.

### C4 — Localization stance absent

- [x] Dispositioned — **Fixed.** Localization is in scope for E02a. Deferring it would mean shipping an epic that knowingly fails a Definition of Done item.

Definition of Ready requires "localization impact is known"; Definition of
Done requires complete English and German resources. The spec never mentions
resources for the setup/login/enrolment pages. Either they are in scope (say
so) or explicitly deferred to E03 with a reason.

### C5 — Cookie and web hardening unplaced

- [x] Dispositioned — **Fixed.** §9 adds forwarded headers, HSTS and a Content Security Policy. The CSP ships with a Playwright test that fails if it blocks the application's own assets — an untested CSP produces confident-looking evidence of protection while breaking the page.

`SECURITY-BASELINE.md` lists secure cookies, HSTS, proxy headers, and CSP.
E02a introduces the application's first cookie. If those controls land in a
different epic, the spec should say which; silence reads as dropped.

### C6 — Email confirmation must be off

- [x] Dispositioned — **Fixed.** §3 records that Identity's confirmed-account requirement is off, with the reason: no SMTP until E14, and the wrong default locks out every user.

No SMTP until E14, so `RequireConfirmedAccount` / confirmed-email sign-in
checks must be disabled. Unstated — and the wrong default locks out every
user.

### C7 — Authentication event logging

- [x] Dispositioned — **Fixed.** §9 adds structured Serilog authentication events, distinguished from both the Audit module and §7's row provenance. It also records the one forward-looking allowance the project owner asked for: a `_msg`-emitting JSON formatter shipped but not selected, so a log store can be adopted by configuration rather than by a code change.

Failed sign-ins, lockouts, admin actions: not row provenance (§7 does not
cover them) and the Audit module is a later epic. The spec should explicitly
defer or include; sign-in observability is a common operator need from day
one.

---

## Minor findings

### M1 — Wrong cross-references in §12

- [x] Dispositioned — **Fixed.** The stale `§8` in the risks list now reads `§9`. Both stale references were introduced by the section renumber that inserted §7; the review caught what the author's own check missed.

The last §12 bullet cites "§8" twice for the encryption decision — it is §9
("accepted deliberately in §8"; "the mitigation; … §8").

### M2 — §5 data model omits three audit columns

- [x] Dispositioned — **Fixed.** §5 now lists `MustChangePassword` and the four `IAuditable` columns, and notes that `AspNetUserClaims` is unused because permissions are derived rather than stored per user.

§5 says `AspNetUsers` is extended with `DisplayName, CreatedAt,
MustEnrolTotp` — §7's `IAuditable` adds four columns, so
`CreatedBy`/`ModifiedAt`/`ModifiedBy` are missing from the listing.

### M3 — Enrolment idempotency ambiguity

- [x] Dispositioned — **Fixed.** §8 states that returning to enrolment reuses the existing authenticator key rather than resetting it, so an entry already added to an authenticator app keeps working. The key is reset only by `--reset-mfa` or an administrator's clear-TOTP.

A user verifies TOTP, leaves without acknowledging the recovery codes, and
`MustEnrolTotp` stays set (§8). Does the next visit regenerate the secret?
If so, the already-added authenticator entry is dead — safe but confusing.
The state machine deserves one sentence.

### M4 — TOTP replay window

- [x] Dispositioned — **Fixed as an accepted risk.** §12 records that Identity keeps no used-code cache, so a captured code is replayable within its window, and that standard practice is to accept this and rely on TLS.

Identity's TOTP verification has no used-code replay cache; a captured code
is reusable within the time window. A standard accepted risk — worth a line
in §12.

### M5 — CLI provenance is thinner than UI provenance

- [x] Dispositioned — **Fixed as an accepted risk.** §12 records that CLI recovery actions are stamped `system` because no user is authenticated, that the event log records the action but not the operator, and that host and database access is the control.

CLI actions stamp `CreatedBy = "system"` — truthful per §7, but the operator
identity is lost for recovery actions. Acceptable; note that `--reset-mfa`
run with host access leaves a thinner trail than its UI equivalent.

---

## Strengths — keep these

Recorded so the revision pass does not accidentally weaken them:

- The E02/E02a/E02b split rationale (§1), driven by SPIKE-009 being the one
  genuinely unresolved risk.
- The empirical verification of plaintext recovery-code storage (§9) —
  probe output included, provider-independence argued via `UserStoreBase`.
- The honest threat model: encryption defends partial exposure, not full
  database compromise, stated in a table rather than implied.
- The availability-versus-confidentiality trade-off on the key ring,
  including why the *more* secure option (external certificate) has the
  *worse* failure mode for a solo operator.
- "No test may bypass two-factor authentication" (§11) — the lesson from the
  vacuous-architecture-rules incident, applied forward.
- Permission-constants/roles-as-data with the startup validation against
  undefined permission strings (§6).
