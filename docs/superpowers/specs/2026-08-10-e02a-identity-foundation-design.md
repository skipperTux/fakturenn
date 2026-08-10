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
- Recovery codes, hashed, shown once
- Administrator-managed users: create, reset password, clear TOTP, lock and unlock
- Permission-based authorization with database-stored roles
- Operator CLI entrypoints for the locked-out cases
- Data Protection key ring persisted to PostgreSQL
- TOTP shared secrets encrypted at rest
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
| Data Protection key ring | Persisted to PostgreSQL via EF Core | The filesystem ring regenerates on restart, which breaks cookies and antiforgery across restarts and makes the stateless replicas `DEPLOYMENT-BASELINE.md` requires impossible. |
| Key ring ownership | New `Fakturenn.Infrastructure.DataProtection` project | `MODULE-OWNERSHIP.md` does not assign key material to the Identity module, and key rings are not domain data. No exceptions to the module map. |
| TOTP secret at rest | EF Core value converter over `IDataProtector` | Identity keeps ownership of the storage and the flow; the column becomes ciphertext. See §8 for the limits of this. |

## 4. Module and project layout

Three new projects:

```text
src/Fakturenn.Modules.Identity/
  Domain/
    ApplicationUser.cs           IdentityUser<Guid> plus DisplayName, CreatedAt, MustEnrolTotp
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
src/Fakturenn.Web/
  Components/Account/            static-SSR auth pages
  Operations/                    CLI entrypoints
  FakturennWebApplication.cs     Identity, authorization, Data Protection registration
  Program.cs                     three context factories instead of one

tests/Fakturenn.Modules.Identity.UnitTests/   new, the first per-module test project
tests/Fakturenn.IntegrationTests/             migration and round-trip tests
tests/Fakturenn.UiTests/                      the SPIKE-009 journey
tests/Fakturenn.ArchitectureTests/            three more loader lines
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
AspNetUsers          stock Identity, extended with DisplayName, CreatedAt, MustEnrolTotp
AspNetUserTokens     stock; holds the authenticator key, Value encrypted at rest
AspNetUserClaims     stock
AspNetUserLogins     stock, unused until OIDC
Role                 Id, Name, Description, IsSystemRole
RolePermission       RoleId, Permission
UserRole             UserId, RoleId
```

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

E02a defines only the permissions it enforces:

```csharp
public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
}
```

Seeded role: `Administrator`, `IsSystemRole = true`, holding all four.

A startup validation fails fast when `RolePermission` holds a permission string the code does not define. Without it a stale or misspelt row silently grants nothing, which looks identical to a working configuration until someone is denied access they believe they have.

`IsSystemRole` marks roles the application depends on. The last user holding `users.manage` cannot be stripped of it, and a system role cannot be deleted — otherwise an instance can be locked out of its own administration through the UI, with only the CLI to recover.

### YAGNI justification

Building a permission layer for four permissions would normally be over-engineering. It is not here: E02b arrives immediately as the second consumer with organization-scoped roles, and `PLAN-v0.1.md`'s Definition of Done requires *every* epic to test authorization. Establishing the seam once, before a dozen slices exist, is cheaper than retrofitting it across them.

## 7. Flows

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
                            success -> cookie issued, redirect to returnUrl
                            locked   -> /account/lockout

administration, requires users.manage
  /admin/users              list, create, reset password, clear TOTP, lock, unlock
```

Every `/setup` and `/account/*` page is static SSR posting a real form. The rest of the application stays Interactive Server.

`/setup` is guarded by a user-count query, not a configuration flag. A flag can be left on; a populated table cannot.

Lockout: five failures, fifteen-minute window. Rate limiting on the login and 2FA endpoints via ASP.NET Core's built-in limiter — `SECURITY-BASELINE.md` requires it, and without it lockout degrades into a user-enumeration oracle.

Sign-in failures never distinguish an unknown user from a wrong password.

## 8. Data Protection and secrets at rest

The key ring is persisted to PostgreSQL with `PersistKeysToDbContext<DataProtectionDbContext>()` and a fixed application name, so every replica shares one ring and it is covered by the existing database backup rather than needing a second backup story.

This fixes a pre-existing defect. The ring currently lives in the container filesystem and regenerates on restart, so authentication cookies and antiforgery tokens already break across restarts, and the stateless replicas `DEPLOYMENT-BASELINE.md` requires cannot work at all.

TOTP shared secrets are encrypted through an EF Core value converter over `IDataProtector`, applied to `IdentityUserToken.Value`.

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

## 9. Operator entrypoints

Alongside `--migrate`:

```text
--create-admin <email>      only when no users exist; password from a secret file or stdin
--reset-password <email>    sets a new password and forces a change at next sign-in
--reset-mfa <email>         clears TOTP and forces re-enrolment
--list-users                diagnostic: email, display name, lockout state, TOTP state
```

`--create-admin` and the `/setup` page are alternative routes to the same state, not a sequence. Both are guarded by the same "no users exist" query, so whichever runs first closes the other. The page suits an operator with a browser; the entrypoint suits a Kubernetes Job or an unattended install. The user created by `--create-admin` still has `MustEnrolTotp` set and completes enrolment at first sign-in, so the CLI path never produces an account without a second factor.

These require host and database access rather than a password. That is deliberate: they are the recovery path for an operator locked out of their own records, and the same property that makes them useful makes them unavailable to anyone who only reaches the web interface.

A password must never be passed as a command-line argument — it would land in shell history and process listings. `--create-admin` and `--reset-password` read from a file path or standard input.

## 10. Testing

Per `SPEC-v0.1.md` §10, in order of preference: real objects, then fakes, then NSubstitute only where interaction is the behaviour under test.

| Tier | Coverage |
| --- | --- |
| Unit (`Fakturenn.Modules.Identity.UnitTests`) | permission-to-policy mapping; the startup validation rejecting an undefined permission; `IsSystemRole` protection; the last-administrator guard |
| Integration | both new migrations apply to a clean database and are idempotent; a TOTP secret round-trips through the value converter and is **not** readable as plaintext in the column; the Data Protection ring survives a simulated restart |
| UI (Playwright) | the full password + TOTP journey; `/setup` returning 404 after the first user; lockout after five failures; recovery-code sign-in consuming the code |

### Closing SPIKE-009

The spike asks three questions; the answers are:

- **Deterministic TOTP secrets** — tests seed a user with a known base32 authenticator key and compute codes with `Otp.NET` (MIT). This exercises real RFC 6238 and real `VerifyTwoFactorTokenAsync`.
- **Parallel isolation** — one PostgreSQL container per test class, as the integration tier already does.
- **Reusable authenticated state** — Playwright `storageState` captured after one genuine sign-in and reused by tests that are not testing sign-in.

**No test may bypass two-factor authentication.** A test-only bypass would make every later UI test pass against an application whose authentication does not work — the same failure mode that let three architecture rules sit dead through an entire task earlier in this project.

The integration test asserting that the stored token is not plaintext is the one that matters most in this list: it is the only check that would notice the value converter being silently dropped in a future refactor.

## 11. Risks

- **Static SSR and Interactive Server in one application.** Mixing render modes has sharp edges around antiforgery and redirects. The Playwright journey is the check that the combination actually works end to end, rather than compiling.
- **Data Protection and the container.** If the key ring is misconfigured, symptoms appear as random sign-out rather than as an error. The integration test simulating a restart is what turns that into a visible failure.
- **`Otp.NET` must match Identity's algorithm.** Identity's authenticator provider implements standard RFC 6238 over the shared key, so a standard library agrees — but this is an assumption to verify in the first task that uses it, not at the end.
- **Three new migration contexts at once.** `--migrate` now applies three sets. The existing wall-clock startup budget covers all of them collectively, not each individually; the integration tests must confirm the budget is still adequate.
- **`MustEnrolTotp` as a partial-authentication state.** A user who authenticated by password but has not finished enrolling must be able to reach only the enrolment page. Getting that wrong either locks users out or opens a hole; it is worth an explicit test rather than trust.
- **Losing the Data Protection key ring invalidates every enrolled authenticator.** This is the availability cost of encrypting TOTP secrets, accepted deliberately in §8. Keeping the ring in the database makes it atomic under backup and restore, which is the mitigation; `--reset-mfa` is the recovery path if it happens anyway. An operator who enables certificate protection takes on a second, independent way to lose the same data.
