# Design — E02a Identity foundation

**Date:** 2026-08-10
**Status:** Approved
**Milestone:** M0 Foundation
**Epic:** E02 Identity and organizations, first slice
**Supporting docs:** `docs/architecture/adr/ADR-008.md`, `docs/security/SECURITY-BASELINE.md`, `docs/architecture/MODULE-OWNERSHIP.md`, `docs/spikes/SPIKE-009-PLAYWRIGHT-TOTP.md`, `docs/operations/DEPLOYMENT-BASELINE.md`

## 1. Why E02 is split

`PLAN-v0.1.md` lists E02 as "Identity and organizations". `MODULE-OWNERSHIP.md` assigns those to two different modules, and organization isolation is a third, cross-cutting concern that every later epic must respect. One spec covering all three would bury the only genuinely unresolved risk — `SPIKE-009`, automated TOTP login — in the middle of a large plan.

E02 is therefore two slices:

- **E02a (this spec)** — authentication: users, password, mandatory TOTP, recovery codes, lockout, rate limiting, admin user management, operator recovery entrypoints, and permission-based authorization at system scope.
- **E02b** — the `Organization` aggregate, `Membership`, organization-scoped roles, and the isolation mechanism.

E15 (Wolverine durable messaging) completes M0 and is unrelated to either.

Identity comes first because `SPIKE-009` is open and its exit criterion — "automated password + TOTP login against packaged application" — is the one thing in M0 that might not work the way we assume. Discovering that before the organization model is built on top of it is worth more than the reverse ordering.

## 2. Scope

### In

- First-run setup that creates the first administrator, then disappears permanently
- Password sign-in with lockout and rate limiting
- Mandatory TOTP enrolment and challenge
- Recovery codes, shown once, consumed on use, encrypted at rest (see §9)
- Row-level audit provenance on every entity Fakturenn defines (see §7)
- Forced password change when an administrator or operator set the password
- Sign-out
- Administrator-managed users: create, reset password, clear TOTP, lock and unlock
- Permission-based authorization with database-stored roles
- Session revocation on credential and lock-state changes
- Operator CLI entrypoints for the locked-out cases
- Data Protection key ring persisted to PostgreSQL
- TOTP shared secrets and recovery codes encrypted at rest
- Forwarded-header trust, HSTS and a Content Security Policy
- Structured authentication event logging
- English and German resources for every page this epic adds
- The automated password + TOTP journey that closes `SPIKE-009`

### Out, with reasons

| Deferred | To | Why |
| --- | --- | --- |
| Self-service password reset | E14 | Requires SMTP. A reset form that cannot deliver its token is a dead form, not a feature. |
| Email-based user invitations | E14 | Same dependency. E02a administrators create users with a password directly. |
| Generic OIDC | Later | ADR-008 calls it *optional*. No requirement and no second caller today. |
| `Organization`, `Membership`, isolation | E02b | Next slice. |
| Organization-scoped roles | E02b | Roles gain an `OrganizationId` when organizations exist. |
| Permission management UI | Later | Roles are seeded and editable by SQL until something needs otherwise. |
| `roles.read` / `roles.manage` permissions | E02b | A permission constant with no enforcement site is speculative surface. They arrive with the UI that enforces them. |
| Recovery-code regeneration page | Later | `--reset-mfa` and the administrator's clear-TOTP action both force re-enrolment, which issues a fresh set. A self-service page belongs to whichever epic adds account self-service. |
| 2FA "remember this machine" | Rejected, not deferred | A persistent second-factor bypass cookie is not a trade worth making on a system holding invoicing records and signing identities, for a sign-in that happens a few times a day. |
| Shared rate-limit state across replicas | Rejected | Solving it needs distributed state this project does not otherwise require. The per-replica multiplication is accepted in §8; lockout is the durable control. |

## 3. Decisions

| Decision | Choice | Reason |
| --- | --- | --- |
| Account provisioning | First-run setup page, then administrator-created only | SPEC §3 lists public multi-tenant SaaS as a non-goal. No registration endpoint exists in the codebase — not hidden, not disabled by configuration, simply absent. |
| Auth page rendering | Static SSR Blazor components, MudBlazor-styled | A Blazor Server circuit has no live `HttpContext`, so `SignInManager` cannot issue a cookie from one. Static SSR with real form posts is the supported .NET 8+ path. |
| TOTP | Mandatory for every user | `SECURITY-BASELINE.md` lists TOTP as a baseline control. An optional control on a single-operator instance is one nobody enables. |
| Lockout recovery | Recovery codes plus a `--reset-mfa` CLI entrypoint | For self-hosted software, a solo operator losing phone and codes is a data-loss event, not an inconvenience. |
| Password reset | Administrator-initiated and CLI in E02a | See §2. |
| Authorization model | Permissions as code constants, roles as database rows | Code authorizes on permissions only. An operator can create a role without a deploy; nobody can invent a permission the code does not enforce. |
| Role storage | Custom `Role` / `RolePermission` / `UserRole` tables | Stock `AspNetRoles` and `AspNetUserRoles` have nowhere to put E02b's `OrganizationId`, and running two role systems side by side later is worse than not adopting one now. |
| User key type | `Guid` via `GuidV7IdGenerator` | Consistent with `InvoiceId`; time-ordered keys preserve B-tree locality. |
| Data Protection key ring | Persisted to PostgreSQL via EF Core | Sticky sessions give a Blazor circuit affinity but do not share keys, so a cookie encrypted by one replica cannot be read by another. `DEPLOYMENT-BASELINE.md` commits to stateless replicas and Kubernetes. Justified independently of any encryption decision. |
| Key ring ownership | New `Fakturenn.Infrastructure.DataProtection` project | `MODULE-OWNERSHIP.md` does not assign key material to the Identity module, and key rings are not domain data. No exceptions to the module map. |
| Audit provenance | `IAuditable` filled by an EF Core interceptor | Added after this spec was first approved, at the project owner's request. Placed before the entities so the columns land in the first migration rather than an `ALTER` later. |
| Password policy | ASP.NET Core Identity's own `PasswordOptions`, bound from configuration. Defaults: 12 characters, upper plus lower plus digit, no punctuation requirement | No third-party scorer. Three were evaluated and none earned a dependency in the sign-in path — see §8. The password is one factor of two, and mandatory TOTP carries the weight a scorer was being asked to carry. |
| Permission delivery | Derived at sign-in, carried as cookie claims | Avoids a database query per authorized request. The staleness this introduces is bounded by the security-stamp interval below — the two decisions only make sense together. |
| Session revocation | Explicit stamp rotation, one-minute validation interval | Identity rotates the stamp on password and two-factor changes but **not** on lockout. The default thirty-minute interval would leave a locked user working for half an hour. |
| Rate-limit partition | Username plus client IP | IP alone is useless behind a shared address and a self-DoS behind a proxy. Requires forwarded-header trust, which is why §9 configures it. |
| Setup concurrency | Unique index plus caught violation | A count-then-insert guard is a check-then-act race between two posts, or between a replica and a migration Job. |
| Email confirmation | Off | No SMTP until E14. Leaving Identity's confirmed-account requirement on would lock out every user with no way to confirm. |
| Forwarded-header trust | Delimiter-separated strings, not configuration arrays | .NET binds arrays by index, so an environment variable can overwrite elements but cannot *replace* a list. One string in one variable can be. |
| Localization | In scope for this epic | `PLAN-v0.1.md`'s Definition of Done requires complete English and German resources per epic. Deferring means shipping knowingly not-done. |
| Authentication logging | Structured Serilog events now | An operator needs to answer "is someone attacking this instance" from day one. Distinct from the Audit module and from §7's row provenance. |
| TOTP secret at rest | EF Core value converter over `IDataProtector` | Identity keeps ownership of the storage and the flow; the column becomes ciphertext. See §9 for the limits of this. |

## 4. Module and project layout

Four new projects:

```text
src/Fakturenn.Infrastructure.Persistence/
  AuditSaveChangesInterceptor.cs fills IAuditable on save (§7)

src/Fakturenn.Modules.Identity/
  Domain/
    ApplicationUser.cs           IdentityUser<Guid> plus DisplayName, MustEnrolTotp, IAuditable
    Role.cs                      Id, Name, Description, IsSystemRole
    RolePermission.cs            RoleId, Permission
    UserRole.cs                  UserId, RoleId
  Authorization/
    Permissions.cs               the closed set of permission constants
    PermissionPolicyProvider.cs  maps a permission name to an authorization policy
    PermissionAuthorizationHandler.cs
  Persistence/
    IdentityDbContext.cs         schema "identity"
    Migrations/                  module-owned, with the generated-code .editorconfig
  IdentityModule.cs              assembly marker

src/Fakturenn.Modules.Identity.Contracts/
  UserId.cs                      readonly record struct, the cross-module surface

src/Fakturenn.Infrastructure.DataProtection/
  DataProtectionDbContext.cs     implements IDataProtectionKeyContext, schema "dataprotection"
  Migrations/
```

Changes to existing projects:

```text
src/Fakturenn.SharedKernel/
  IAuditable.cs                  row-level provenance contract (§7)
  ICurrentUserAccessor.cs        who is acting, or null outside a request
  AuditStamp.cs                  the interceptor's decisions, as a pure function

src/Fakturenn.Web/
  Components/Account/            static-SSR auth pages
  Operations/                    CLI entrypoints
  HttpContextCurrentUserAccessor.cs
  FakturennWebApplication.cs     Identity, authorization, Data Protection registration
  Program.cs                     three context factories instead of one

tests/Fakturenn.Modules.Identity.UnitTests/   new, the first per-module test project
tests/Fakturenn.UnitTests/                    AuditStamp, alongside the existing fakes
tests/Fakturenn.IntegrationTests/             migrations, audit stamping, token encryption
tests/Fakturenn.UiTests/                      the SPIKE-009 journey
tests/Fakturenn.ArchitectureTests/            four more loader lines
```

### Harness obligations

Adding a project under `src/` requires two edits, and a test enforces the second:

1. add it to `Fakturenn.slnx` under `/src/`;
2. add one `typeof(<public type>).Assembly` line to `FakturennArchitecture.Loaded`.

Omitting step 2 fails `The_loader_omits_no_assembly_declared_under_src_in_the_solution`.

`DatabaseMigrator.RunAsync` already accepts `IReadOnlyList<Func<DbContext>>`, and the comment at `Program.cs` anticipates exactly this case, so registering the two new contexts is a list addition rather than a signature change.

### Architecture rules stop being vacuous

`Fakturenn.Modules.Identity` is the **second** module. Until now, rules 5 (no cross-module implementation references) and 6 (no cycles between modules) could not be violated because only one module existed; a reviewer proved them sound against synthetic assemblies. From this slice on they constrain real code:

- `Fakturenn.Modules.Identity` must not reference `Fakturenn.Modules.Invoices`, only its `.Contracts`.
- Neither module may reference `Fakturenn.Infrastructure.DataProtection`. The Identity module depends on the framework's `IDataProtectionProvider` abstraction only; the concrete key store is wired in `Fakturenn.Web`.

## 5. Data model

Schema `identity`:

```text
AspNetUsers          stock Identity, extended with DisplayName, MustEnrolTotp,
                     MustChangePassword, and the four IAuditable columns (§7)
AspNetUserTokens     stock; holds the authenticator key and the recovery codes,
                     Value encrypted at rest (§9)
AspNetUserClaims     stock, unused — permissions are derived, never stored per user (§6)
AspNetUserLogins     stock, unused until OIDC
Role                 Id, Name, Description, IsSystemRole, + IAuditable
RolePermission       RoleId, Permission, + IAuditable
UserRole             UserId, RoleId, + IAuditable
```

Every table Fakturenn defines carries `CreatedAt`, `CreatedBy`, `ModifiedAt` and `ModifiedBy` per §7. `AspNetUsers` does too, because we extend that entity; the stock tables we do not define are left alone.

`MustChangePassword` exists because an administrator who creates a user, or an operator who runs `--reset-password`, knows that password. The state forces a change at next sign-in so the credential stops being shared the moment it is first used.

`AspNetRoles` and `AspNetUserRoles` are deliberately not used.

`UserRole` gains an `OrganizationId` in E02b. It is a separate table now specifically so that migration is an added column rather than a swap of role systems.

Schema `dataprotection`:

```text
DataProtectionKeys   Id, FriendlyName, Xml
```

## 6. Authorization model

```text
User ──< UserRole >── Role ──< RolePermission >── Permission
                                                      │
                            code authorizes on this ──┘
```

- **Permissions are code constants.** A typo is a build error, and every permission is greppable to its enforcement sites.
- **Roles are data.** Seeded with defaults, editable without a deploy.
- **Code never authorizes on a role name.** `[Authorize(Policy = Permissions.UsersManage)]`, never `[Authorize(Roles = "Administrator")]`.

E02a defines only the permissions it enforces, and each one names its enforcement site:

```csharp
public static class Permissions
{
    public const string UsersRead = "users.read";      // GET /admin/users
    public const string UsersManage = "users.manage";  // every mutating admin endpoint
}
```

`roles.read` and `roles.manage` were in an earlier draft and are **removed**: roles are seeded and edited by SQL in E02a, there is no roles UI in scope, and a permission constant with no enforcement site is exactly the speculative surface this project's YAGNI rule exists to prevent. E02b adds them when it adds the UI that enforces them.

### How a principal acquires its permissions

Permissions are **derived at sign-in and re-derived at every security-stamp validation**, then carried as claims of type `fakturenn.permission` in the authentication cookie. A custom `IUserClaimsPrincipalFactory<ApplicationUser>` performs the derivation by joining `UserRole` to `RolePermission`.

This is a decision with a consequence, and the consequence is why §8's security-stamp handling exists: **a claim in a cookie is a cached authorization decision.** Removing a role from a user does not take effect until that cookie is re-validated. The two mechanisms are therefore specified together — the stamp interval bounds how stale a permission set can be.

The alternative, a database lookup per request, was rejected: it puts a query on every authorized request to remove a staleness window that the stamp interval already bounds to minutes.

### Seeding, and what happens when a later epic adds a permission

The `Administrator` system role is seeded **by the `--migrate` entrypoint**, not at application startup. Startup seeding on multiple replicas races on the unique role-name index, and `--migrate` already runs exactly once by design.

Seeding is a **re-sync, not a create-if-absent**: it grants the system role every permission in `Permissions.All` that it does not already hold. Without that rule, an installation upgraded to a version defining a fifth permission would have an `Administrator` role silently missing it — the startup validation catches permission strings the code does not define, but nothing would catch grants the code defines and the database lacks.

System roles are re-synced. Operator-created roles are never touched.

Seeded role: `Administrator`, `IsSystemRole = true`, holding all four.

A startup validation fails fast when `RolePermission` holds a permission string the code does not define. Without it a stale or misspelt row silently grants nothing, which looks identical to a working configuration until someone is denied access they believe they have.

`IsSystemRole` marks roles the application depends on. The last user holding `users.manage` cannot be stripped of it, and a system role cannot be deleted — otherwise an instance can be locked out of its own administration through the UI, with only the CLI to recover.

### YAGNI justification

Building a permission layer for four permissions would normally be over-engineering. It is not here: E02b arrives immediately as the second consumer with organization-scoped roles, and `PLAN-v0.1.md`'s Definition of Done requires *every* epic to test authorization. Establishing the seam once, before a dozen slices exist, is cheaper than retrofitting it across them.

## 7. Row-level audit provenance

Every entity Fakturenn defines records who created it and who last changed it. The values are filled by an EF Core `SaveChanges` interceptor, so entity code never sets them by hand and cannot forget to.

```csharp
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    string CreatedBy { get; set; }
    DateTimeOffset ModifiedAt { get; set; }
    string ModifiedBy { get; set; }
}
```

### This is not the Audit module

`MODULE-OWNERSHIP.md` assigns an **Audit** module owning `AuditEvent` and correlation metadata. That is an event log: a record of things that happened. This is a property of each row: who put it there. Same word, different thing. A later epic building `AuditEvent` does not supersede this and should not absorb it.

### Why it belongs in E02a rather than later

The columns must exist in each table's **first** migration. Adding them afterwards means an `ALTER` against tables that already shipped, and every entity written between now and then has to be revisited. Since E02a creates the first entities beyond the walking-skeleton seam, this is the last moment it is free.

### Placement

| Type | Project | Why |
| --- | --- | --- |
| `IAuditable`, `ICurrentUserAccessor`, `AuditStamp` | `Fakturenn.SharedKernel` | Pure contracts and a pure function. No EF Core, no ASP.NET Core. |
| `AuditSaveChangesInterceptor` | `Fakturenn.Infrastructure.Persistence` | Needs EF Core. The shared kernel is referenced by the `.Contracts` assemblies that form the cross-module surface, so a persistence dependency there would land on every module's public surface. |
| `HttpContextCurrentUserAccessor` | `Fakturenn.Web` | Only the host knows about requests. |

Modules implement `IAuditable` and never reference the interceptor, so the existing architecture rule that no module may depend on infrastructure continues to hold.

### Who is "the current user"

`ICurrentUserAccessor` returns the acting user's name, or `null` when there is no request. The host implementation resolves `preferred_username`, then `ClaimTypes.Name`, then `ClaimTypes.NameIdentifier`.

`preferred_username` is first so that adding generic OIDC later, per ADR-008, changes nothing but that one class. Local Identity does not issue it, so today the name claim answers.

When there is no user — migrations, seeding, background work, the operator entrypoints — the interceptor records `system`. That is truthful rather than a placeholder: nobody was authenticated at that moment. The first administrator is therefore created by `system`, which is correct.

### Two rules the interceptor enforces

**Existing provenance is preserved on insert.** A seeder or an import knows the real creator; overwriting it would replace a fact with the identity of whoever ran the import. Only absent or blank values are filled.

**Creation provenance is immutable.** On update the interceptor marks `CreatedAt` and `CreatedBy` as unmodified, so nothing in the object graph can rewrite them — deliberately or by accident. An integration test tampers with `CreatedBy` and asserts it survives; a plain round-trip would never notice.

### Testing

The decisions live in `AuditStamp`, a pure function, so they are unit-tested without a database or a request pipeline. The interceptor is thin glue over it, covered by integration tests against real PostgreSQL: that an insert is stamped, that no signed-in user yields `system`, and that an update moves `ModifiedBy` while `CreatedBy` stands.

`IClock` supplies the time rather than `DateTimeOffset.UtcNow`, so tests assert an exact timestamp instead of a tolerance window.

## 8. Flows

```text
no users in database
  GET  /                    302 -> /setup
  GET  /setup               form: email, display name, password
  POST /setup               create admin, assign Administrator, redirect to TOTP enrolment
  GET  /setup               404 once any user exists

TOTP enrolment (forced while MustEnrolTotp)
  GET  /account/enrol-totp  QR code plus manual entry key
  POST /account/enrol-totp  verify a code, then show recovery codes ONCE
                            leaving without acknowledging keeps MustEnrolTotp set

sign-in
  GET  /account/login
  POST /account/login       password; Identity counts lockout
                            success -> /account/login-2fa
  POST /account/login-2fa   TOTP code, or a recovery code, consumed on use
                            success -> MustChangePassword ? /account/change-password
                                                          : returnUrl
                            locked   -> /account/lockout

forced password change (while MustChangePassword)
  GET  /account/change-password
  POST /account/change-password   current + new; clears the flag, rotates the stamp

sign-out
  POST /account/logout      always available; rendered in the layout when signed in

administration
  GET  /admin/users         requires users.read
  POST /account/admin/*     requires users.manage
                            create, reset password, clear TOTP, lock, unlock
```

Every `/setup` and `/account/*` page is static SSR posting a real form. The rest of the application stays Interactive Server.

### Guards and state

`/setup` is guarded by a user-count query, not a configuration flag. A flag can be left on; a populated table cannot.

**The setup race.** The count query and the insert are not atomic: two concurrent posts, or a replica racing a `--create-admin` Job, can both pass the check. The guard is therefore a **unique index on the normalized user name plus a caught constraint violation** — the loser of the race receives the same "already configured" response as a late visitor. A count query alone is a check-then-act bug.

**A fresh instance is owned by whoever reaches `/setup` first.** This is accepted, as most self-hosted software accepts it. The mitigation is documented rather than coded: run `--create-admin` before exposing the instance, or do not expose it until setup is complete.

**Enrolment idempotency.** A user who verifies TOTP but leaves before acknowledging the recovery codes keeps `MustEnrolTotp` set. Returning to the enrolment page **reuses the existing authenticator key** rather than resetting it, so the entry already added to their authenticator app keeps working. The key is reset only by `--reset-mfa` or the administrator's clear-TOTP action.

**Recovery codes.** Ten are issued at enrolment. They are shown once. There is no regeneration UI in E02a — when they run out or are lost, `--reset-mfa` or an administrator's clear-TOTP forces re-enrolment, which issues a fresh set. A regeneration page belongs to whichever epic adds account self-service.

**"Remember this machine" is not offered.** Identity supports skipping the second factor on a trusted browser; E02a does not enable it. On a system holding invoicing records and signing identities, a persistent second-factor bypass cookie is not a trade we need to make for a login that happens a few times a day.

### Password policy

The policy is **configuration, not code**. ASP.NET Core Identity's own `PasswordOptions` are bound from `appsettings.json`, so a deployment can tighten or loosen them without a rebuild:

```json
"Identity": {
  "Password": {
    "RequiredLength": 12,
    "RequireUppercase": true,
    "RequireLowercase": true,
    "RequireDigit": true,
    "RequireNonAlphanumeric": false,
    "RequiredUniqueChars": 4
  }
}
```

Defaults are 12 characters with upper, lower and a digit; punctuation is not required, because requiring it mostly produces a `!` on the end. `RequireNonAlphanumeric` is the one Identity default that is deliberately flipped off.

### Why no strength scorer

Three libraries were evaluated and measured, and none earned a place in the sign-in path:

| | Outcome |
| --- | --- |
| `zxcvbn-core` | Ranks correctly, and is the only one that does. Last stable release **February 2021**; the 2022 betas are the end of the line. |
| Every other zxcvbn .NET port | `Devolutions.Zxcvbn` 2020, `Hexasoft.Zxcvbn` 2017, `zxcvbn-netstandard` 2018, `zxcvbn.net` 2014. There is no maintained port. |
| `Easy.Password.Validator` | Actively maintained, June 2026, with bad lists and l33t decoding. But its score is length-dominated: `Sommer2026!` scores 96 while a random ten-character password scores 98 and a reasonable two-word password scores 88. No threshold separates good from bad. Its entropy mode did not produce a usable value in testing. |

The choice was therefore between an unmaintained dependency doing regex matching on untrusted input, and a maintained one whose ranking inverts exactly where it matters. Neither is worth it **because the password is one factor of two**. TOTP is mandatory, lockout is durable and rate limiting is in front of the endpoint. A scorer was being asked to carry weight those controls already carry.

### What this policy does not do, stated plainly

Structural rules are known to be insufficient. Each of these satisfies the default policy and is still weak:

| Password | Satisfies |
| --- | --- |
| `Passwort1234` | 12 characters, upper, lower, digit |
| `Sommer2026Ab` | same |
| `Aaaaaaaaaaa1` | same, and even `RequiredUniqueChars: 4` only raises the bar to `Aaaabbbb1234` |

This is recorded rather than papered over. The mitigations are the ones already in this design — mandatory second factor, lockout after five failures, rate limiting, and session revocation on credential change — plus the operator's ability to raise the requirements in configuration for a deployment that needs it.

If a maintained, well-calibrated .NET strength estimator appears, this decision is worth revisiting: the seam is an `IPasswordValidator<ApplicationUser>` registration, which is one class and no schema change.

### Backlog: an entropy warning, not a strength meter

Recorded here because the seam above is where it would land, not as scope for this epic.

Entropy is arithmetic over a character-class size and a length; the algorithm is public and small. `zxcvbn` itself is roughly a decade old, so any port starts from old code — implementing the calculation directly is not the hard part.

The hard part is what a password meter *does to the user*. A meter that says "strong" grants confidence the measurement cannot support: it is trivially satisfied by a password a dictionary attack finds immediately, and its false positives actively mislead. The proposal is therefore deliberately asymmetric:

- **Never display a strength meter or a score.** No bar, no colour, no "strong".
- **Warn only when the estimate is confidently low**, on the true positives where a warning is honest.
- **Configurable on or off**, with the threshold in `appsettings.json`, defaulting to off.

A warning that fires only when it is confident tells the user something true. A meter that always shows something tells them something reassuring, which is worse than silence. If this is built, the KeePassXC health-check approach is the reference for the estimator's shape — implemented from its published description rather than its source, which keeps it clear of GPL entanglement, since algorithms are not copyrightable and only expression is.

Sources for whoever picks this up:

- [What is password entropy?](https://proton.me/blog/what-is-password-entropy) — the concept and why bits, not adjectives.
- [How to calculate entropy](https://generatepasswords.org/how-to-calculate-entropy/) — the arithmetic, which is the small part.
- [Why password strength meters are not so great after all](https://generatepasswords.org/why-password-strength-meters-are-not-so-great-after-all/) — the argument this backlog item is built on, and the reason it specifies a warning rather than a meter.
- [How KeePassXC's password health check works](https://keepassxc.org/blog/2020-08-15-keepassxc-password-healthcheck/) — the reference implementation's approach, in prose.

### Lockout, sessions, and rate limiting

Lockout: five failures, fifteen-minute window.

**Locking a user must end their session.** Identity rotates the security stamp automatically on password reset and on two-factor changes, but **not** on lockout. Every administrative and CLI action that changes credentials or lock state therefore rotates the security stamp explicitly, and `SecurityStampValidatorOptions.ValidationInterval` is set to **one minute** rather than the default thirty. A lock that leaves a working session for half an hour is not a lock. The one-minute interval is also what bounds how stale a cookie's permission claims can be (§6).

**Rate limiting** partitions on **username plus client IP**, not IP alone: IP alone is either useless behind a shared address or a self-DoS behind a proxy. Ten attempts per minute per partition on `/account/login`, `/account/login-2fa` and `/account/login-recovery`.

This requires correct client addresses, so forwarded-header trust is configured — see §9. It is not optional: without it every request behind a reverse proxy carries the proxy's address and the limiter partitions everyone into one bucket.

**Accepted trade-off:** the built-in limiter is in-memory per replica, so with *N* replicas the effective limit is *N* × the configured value. Solving it properly means shared state, which is a distributed-systems dependency this project does not otherwise need. Lockout — which *is* durable, being a database column — remains the real control; the limiter exists to blunt the enumeration oracle that lockout alone creates.

Sign-in failures never distinguish an unknown user from a wrong password.

## 9. Data Protection, web hardening and observability

The key ring is persisted to PostgreSQL with `PersistKeysToDbContext<DataProtectionDbContext>()` and a fixed application name, so every replica shares one ring and it is covered by the existing database backup rather than needing a second backup story.

### What sticky sessions do and do not solve

Blazor Server needs session affinity, because a SignalR circuit must stay on one server. Affinity is necessary but does not share keys, and the two are often conflated:

- **Circuit affinity** — solved by sticky sessions.
- **Key sharing** — not solved. The authentication cookie is a persistent browser cookie, independent of the circuit. A cookie encrypted by one replica cannot be decrypted by another, and the same holds for an antiforgery token on a form rendered by one replica and posted to another. Affinity is also not permanent: a replica restart or a scale event moves the client, and a per-replica ring then forces a new sign-in.

The circuit itself is lost on restart regardless of any of this. That is expected and is not what the shared ring addresses.

Honest magnitude of leaving the ring on the filesystem:

| Deployment | Effect |
| --- | --- |
| Single replica, occasional restarts | forced sign-in after each restart; an annoyance, not a fault |
| Rolling update | forced sign-in on every deployment |
| Multiple replicas | cookies and antiforgery break whenever affinity moves |

Only the third row is a genuine defect, so calling this a bug today would overstate it. It is in scope because `DEPLOYMENT-BASELINE.md` commits to stateless replicas and Kubernetes compatibility, which makes the third row a requirement rather than a hypothetical.

### What the value converter actually covers

The converter is applied to `IdentityUserToken.Value`, which stores **both** second-factor credentials:

| Token name | Contents | Stock Identity storage |
| --- | --- | --- |
| `AuthenticatorKey` | the base32 TOTP shared secret | plaintext |
| `RecoveryCodes` | the recovery codes, semicolon-joined | **plaintext, not hashed** |

The second row corrects an earlier draft of this spec, which claimed recovery codes were hashed. `UserStoreBase.ReplaceCodesAsync` joins them into a single plaintext token value; hashing them would require a custom store.

This was **verified empirically**, not assumed. A throwaway probe referencing `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, creating a user, resetting the authenticator key, generating three recovery codes, and then reading the rows back directly through the `DbContext` rather than through `UserManager`:

```text
generated codes: XBK77-435VP,TG5RD-6TJW9,QWVJ8-F983Q

ROW  LoginProvider=[AspNetUserStore]  Name=AuthenticatorKey  Value=2W2NZBPUT2YX3LP3SUMMXICIO2INDYYU
ROW  LoginProvider=[AspNetUserStore]  Name=RecoveryCodes     Value=XBK77-435VP;TG5RD-6TJW9;QWVJ8-F983Q
```

The codes are stored exactly as issued. The join happens in `UserStoreBase`, so this is provider-independent; the probe used SQLite for convenience and PostgreSQL behaves identically.

The consequence is the argument for the converter: **a read of this one table yields a working second factor for every user** — both the TOTP secret and the recovery codes that bypass it. That is a stronger case than "encrypt the TOTP secret", and it is the reason this design departs from stock Identity here rather than following it.

### The limit of that protection, stated plainly

The key ring and the ciphertext live in the same database. Encryption therefore defends against **partial** exposure and **not** against full database compromise. Anyone holding the whole database holds both halves.

Measured against what ASP.NET Core Identity does by default — the base32 secret in plaintext in `AspNetUserTokens.Value` — this is never worse on confidentiality:

| Exposure | Stock Identity | Ring in the same database |
| --- | --- | --- |
| One table leaks: a dump of `AspNetUserTokens`, an injection reaching one table, a query log | exposed | protected |
| Read-only replica or partial backup | exposed | protected |
| Full database compromise | exposed | exposed — equal |

### Where it *is* worse: availability, not confidentiality

Plaintext secrets survive the loss of a key ring. Encrypted ones do not: lose the ring and every user must re-enrol TOTP. That is the cost this design accepts, and it is the reason the ring goes in the database rather than somewhere safer.

Moving the ring outside the database — a mounted certificate via `ProtectKeysWithCertificate` — genuinely separates the trust boundaries, so a stolen database alone yields nothing. But it also means a database restore without the matching certificate destroys every TOTP secret. The more secure option carries the worse failure mode, and for self-hosted software operated by one person, silent unrecoverable loss is the likelier disaster.

Keeping the ring in the database makes ciphertext and key atomic under backup and restore: neither can be restored without the other.

### Why this choice forecloses nothing

The value converter depends on `IDataProtector`. How the ring itself is protected is a separate seam, `IXmlEncryptor`. Adding certificate protection later — or an external provider such as OpenBao's transit engine — changes configuration only: not the converter, not the schema, not a migration. Choosing Data Protection now keeps the stronger options open at no cost, which is the argument for it over a hand-rolled AES-GCM converter reading a key from a mounted file.

`SECURITY-BASELINE.md` requires private keys to be referenced from mounted secret files rather than stored as ordinary database columns. A Data Protection key ring is key material, so the baseline points toward separation for deployments that can carry the operational burden.

E02a therefore supports optional `ProtectKeysWithCertificate`, reading a certificate from a configured path — a mounted secret file, a Docker secret, or a Kubernetes secret. It is **off by default**, because requiring certificate management to run `docker compose up` on a laptop is disproportionate. When it is off, the application logs once at startup that the ring is unprotected at rest, that full database compromise therefore yields TOTP secrets, and when it is on, that restoring a database without the certificate will invalidate every enrolled authenticator.

`docs/operations/DEPLOYMENT-BASELINE.md` gains a section covering the backup implication, the certificate option, and the restore hazard that comes with it.

### Forwarded headers

E02a introduces the application's first cookie and its first rate limiter, both of which depend on knowing the real client address. `SECURITY-BASELINE.md` lists proxy-header configuration; this is where it lands.

Trust is **configuration, not code**, and it is expressed as **delimiter-separated strings** rather than configuration arrays:

```text
Network:KnownProxies    "203.0.113.7, 203.0.113.8"
Network:KnownNetworks   "10.0.0.0/8; 172.16.0.0/12"
Network:ForwardLimit    1
```

A string is used deliberately. .NET binds configuration arrays by index, so an environment variable can only *add to or overwrite individual elements* of a list defined in `appsettings.json` — it cannot replace the list. An operator who wants exactly two trusted proxies, and nothing inherited, cannot express that with an array. One string in one variable can be replaced wholesale.

Three states, and they are not the same:

| Configuration | Behaviour |
| --- | --- |
| Not set | `X-Forwarded-*` ignored entirely. A decision, not an error — no reverse proxy in front. Logged as a warning at startup |
| Set and parseable | Trust exactly those entries. The middleware's loopback defaults are **cleared** first, so the trust list is what the operator asked for and nothing more |
| Set but nothing parses | **Startup fails.** Otherwise the middleware silently falls back to loopback-only and drops every forwarded header at request time — a typo would become an unexplained redirect-to-`http` months later |

The resolved trust list is logged at startup as **values, not counts**. A count of one looks identical whether the operator chose that entry or inherited it.

### Transport and content hardening

- **HSTS** in production only, never in development — a `Strict-Transport-Security` header issued from a local HTTP run poisons the browser for `localhost` across other projects.
- **Content Security Policy.** Blazor Server needs specific allowances, and an over-strict policy breaks the application in ways that look like unrelated bugs rather than like a policy error. The policy therefore ships **with a Playwright test that fails if it blocks the application's own scripts or styles**. An untested CSP is worse than none, because it creates confident-looking evidence of protection while breaking the page.
- Authentication cookies are `HttpOnly`, `SameSite=Lax`, and `Secure` when the request is HTTPS. `Always` is not used: the reference Compose deployment serves plain HTTP on localhost, and forcing `Always` would drop the cookie and make sign-in fail with no visible error.

### Authentication event logging

Serilog is already configured. E02a emits structured events for sign-in success and failure, lockout, two-factor outcome, enrolment, and every administrative and CLI action. An operator should be able to answer "is someone attacking this instance" on day one.

This is **not** the Audit module and does not pre-empt it: that owns `AuditEvent` as domain data with correlation metadata. This is operational telemetry, and it is also distinct from §7's row provenance, which records who changed a row and says nothing about a failed attempt that changed nothing.

Log events never contain a password, a TOTP code, a recovery code, or an authenticator key.

**One forward-looking allowance.** Log shipping is an operations concern: the container writes to standard output and a collector ships it. But some log stores take a line's headline text from a field named `_msg` — [VictoriaLogs](https://docs.victoriametrics.com/victorialogs/) is the one this project has been asked to stay compatible with — and a JSON formatter that writes the rendered message under any other name leaves every row in the UI reading as a missing-field placeholder while the real text sits one click away. That cannot be fixed outside the application.

E02a therefore ships a JSON console formatter that emits `_msg`, as a stable public type, **not selected by default**: the human-readable console formatter stays the default, and an operator selects the JSON one through existing Serilog configuration.

The cost is roughly thirty lines and one type name that must stay stable, because configuration will name it. The alternative is a code change and a release the day operations asks. This project ships **no** log-store configuration, no URL and no credential — only the ability to be pointed at one, which keeps the coupling at the level of a field name rather than a dependency.

## 10. Operator entrypoints

Alongside `--migrate`:

```text
--create-admin <email>      only when no users exist; password from stdin
                            sets MustChangePassword and MustEnrolTotp
--reset-password <email>    sets a new password from stdin, sets MustChangePassword,
                            clears lockout, rotates the security stamp
--reset-mfa <email>         clears TOTP, forces re-enrolment, rotates the stamp
--unlock-user <email>       clears lockout and the failed-attempt count
--list-users                diagnostic: email, display name, lockout state, TOTP state
```

`--unlock-user` exists because the `IsSystemRole` guard prevents *stripping* the last administrator's permissions but does not prevent **locking** them. Without it, an administrator who locks themselves out — or is locked by another administrator — has no route back, and the guard would have protected the wrong thing. `--reset-password` clears lockout for the same reason: an operator resetting a password almost always wants the account usable afterwards, and a reset that leaves the account locked is a surprise.

Every entrypoint that changes credentials or lock state rotates the security stamp, so any existing session for that user stops working. This is the CLI half of §8's rule; an administrator locking a user through the UI and an operator locking them through the CLI must not behave differently.

`--create-admin` and the `/setup` page are alternative routes to the same state, not a sequence. Both are guarded by the same "no users exist" query, so whichever runs first closes the other. The page suits an operator with a browser; the entrypoint suits a Kubernetes Job or an unattended install. The user created by `--create-admin` still has `MustEnrolTotp` set and completes enrolment at first sign-in, so the CLI path never produces an account without a second factor.

These require host and database access rather than a password. That is deliberate: they are the recovery path for an operator locked out of their own records, and the same property that makes them useful makes them unavailable to anyone who only reaches the web interface.

A password must never be passed as a command-line argument — it would land in shell history and process listings. `--create-admin` and `--reset-password` read from a file path or standard input.

## 11. Testing

Per `SPEC-v0.1.md` §10, in order of preference: real objects, then fakes, then NSubstitute only where interaction is the behaviour under test.

| Tier | Coverage |
| --- | --- |
| Unit (`Fakturenn.Modules.Identity.UnitTests`) | permission-to-policy mapping; the startup validation rejecting an undefined permission; `IsSystemRole` protection; the last-administrator guard; the enrolment-gate path policy; forwarded-header trust parsing, including that a configured-but-unparseable list throws |
| Integration | both new migrations apply to a clean database and are idempotent; a TOTP secret round-trips through the value converter and is **not** readable as plaintext in the column; the Data Protection ring survives a simulated restart; **the claims factory derives a user's permissions from their roles**; seeding re-syncs a system role that is missing a permission the code defines; audit stamping, including that `CreatedBy` survives an update that tries to change it |
| UI (Playwright) | the full password + TOTP journey; `/setup` returning 404 after the first user; lockout after five failures; recovery-code sign-in consuming the code; **an authorized page reaching a permitted user rather than 403**; forced password change on first sign-in; that the Content Security Policy does not block the application's own scripts or styles |

### The two tests that exist because of the spec review

**A permitted user reaches an authorized page.** The permission handler reads claims that a claims factory must write. Unit tests that hand-construct a principal with the claims already present pass whether or not anything populates them in production — which is exactly how a review found that nothing did. Only an end-to-end assertion catches it.

**A locked user's existing session stops working.** Sign in, lock the account from another session, and confirm the first session cannot reach an authorized page within the stamp validation interval. Without it, "lock" is a database column with no effect on anyone already inside.

### Closing SPIKE-009

The spike asks three questions; the answers are:

- **Deterministic TOTP secrets** — tests seed a user with a known base32 authenticator key and compute codes with `Otp.NET` (MIT). This exercises real RFC 6238 and real `VerifyTwoFactorTokenAsync`.
- **Parallel isolation** — one PostgreSQL container per test class, as the integration tier already does.
- **Reusable authenticated state** — Playwright `storageState` captured after one genuine sign-in and reused by tests that are not testing sign-in.

**No test may bypass two-factor authentication.** A test-only bypass would make every later UI test pass against an application whose authentication does not work — the same failure mode that let three architecture rules sit dead through an entire task earlier in this project.

The integration test asserting that the stored token is not plaintext is the one that matters most in this list: it is the only check that would notice the value converter being silently dropped in a future refactor.

## 12. Risks

- **Static SSR and Interactive Server in one application.** Mixing render modes has sharp edges around antiforgery and redirects. The Playwright journey is the check that the combination actually works end to end, rather than compiling.
- **Data Protection and the container.** If the key ring is misconfigured, symptoms appear as random sign-out rather than as an error. The integration test simulating a restart is what turns that into a visible failure.
- **`Otp.NET` must match Identity's algorithm.** Identity's authenticator provider implements standard RFC 6238 over the shared key, so a standard library agrees — but this is an assumption to verify in the first task that uses it, not at the end.
- **Three new migration contexts at once.** `--migrate` now applies three sets. The existing wall-clock startup budget covers all of them collectively, not each individually; the integration tests must confirm the budget is still adequate.
- **`MustEnrolTotp` as a partial-authentication state.** A user who authenticated by password but has not finished enrolling must be able to reach only the enrolment page. Getting that wrong either locks users out or opens a hole; it is worth an explicit test rather than trust.
- **Losing the Data Protection key ring invalidates every enrolled authenticator.** This is the availability cost of encrypting TOTP secrets, accepted deliberately in §9. Keeping the ring in the database makes it atomic under backup and restore, which is the mitigation; `--reset-mfa` is the recovery path if it happens anyway. An operator who enables certificate protection takes on a second, independent way to lose the same data.

### Accepted risks

Stated rather than mitigated, so that a later reader knows they were considered:

- **TOTP codes are replayable within their time window.** Identity keeps no cache of used codes, so a code captured in transit can be reused for up to the validity window. Mitigating it means a per-user store of consumed codes and the cache-invalidation problems that come with it. Standard practice is to accept this and rely on TLS; E02a does the same.
- **Rate limits multiply by replica count.** The in-memory limiter is per replica. Accepted in §8, with lockout as the durable control.
- **CLI actions leave a thinner trail than their UI equivalents.** An operator running `--reset-mfa` is recorded as `system` by §7's provenance, because no user is authenticated. The authentication event log (§9) records that the action happened, but not who ran it — that is inherent to a recovery path whose whole point is working when nobody can sign in. Host and database access is the control.
- **A fresh instance is claimed by whoever reaches `/setup` first.** Accepted in §8, with `--create-admin` before exposure as the documented mitigation.
