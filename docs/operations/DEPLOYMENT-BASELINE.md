# Deployment Baseline

## Reference deployment: Docker Compose

Required services:

```text
fakturenn-app
postgres
e-invoice-eu
```

Optional:

```text
fakturenn-worker
s3-compatible storage
validator
OIDC provider
mail test services
```

Defaults:

- local Identity
- PostgreSQL
- filesystem document storage
- app-hosted workers

## Kubernetes compatibility

Requirements:

- stateless web replicas
- external document storage for multiple replicas
- readiness/liveness/startup probes
- graceful shutdown
- explicit migration Job
- secret files or secret references
- durable background work safe across replicas
- no Docker socket dependency

Initial distribution:

- Kustomize base and overlays
- Helm may follow after configuration stabilizes

Targets:

- generic Kubernetes
- Talos Linux
- K3s
- RKE2

## Reverse proxy and forwarded headers

Set `Network:KnownProxies` or `Network:KnownNetworks` to the proxy in front.
With neither set, forwarded headers are ignored entirely and the application
trusts only the peer address it observes itself. That is the safe default, not
a failure — configure trust before expecting any forwarded header to have an
effect.

`X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host` are the primary
path. Every reverse proxy emits them and ASP.NET Core consumes them natively;
any proxy will do. Fakturenn additionally understands the RFC 7239 `Forwarded`
header, which ASP.NET Core itself does not.

Whichever header the proxy sets, it must strip the inbound copy of it.
Fakturenn ignores `Forwarded` whenever `X-Forwarded-For` is present rather than
merging two chains of different provenance.

### If the proxy is configured to emit `Forwarded`

**The `for=` element must carry a real address.** RFC 7239 section 6.3 defines
an obfuscated identifier such as `for=_YQuN68tm6`, and section 8.3 recommends
it as a default configuration; an obfuscated identifier carries no address at
all. Fakturenn then falls back to the peer address, and per-client account rate
limiting degrades to per-proxy — every client behind that proxy shares one
budget.

Implementations differ on this, so check the one in use rather than assuming:

- HAProxy 2.8 and later emits `Forwarded` first-class via `option forwarded`.
  The bare form expands to `proto for` and puts a real address in `for=`, so
  the default is already correct. It is set in `defaults`, `listen` or
  `backend`, and ignored in a `frontend`.
- YARP's `Forwarded` request transform defaults its `ForFormat` to `Random`,
  which is an obfuscated identifier. Use `Ip`, `IpAndPort` or `IpAndRandomPort`
  instead.

Note the independence trap: emitting `Forwarded` does not imply emitting
`X-Forwarded-*` beside it, so there may be no fallback. HAProxy's
`option forwarded` is independent of `option forwardfor` — enabling only the
former sends `Forwarded` and no `X-Forwarded-For` at all. YARP goes further and
switches its `X-Forwarded` transforms **off** when the `Forwarded` transform is
enabled. In both cases the `for=` element is everything the application
receives.

## Migrations

Migrations never run automatically at startup — more than one replica starting
at once would race. Apply them with the explicit `--migrate` entrypoint,
before traffic reaches the application:

```bash
dotnet run --project src/Fakturenn.Web -- --migrate
# or, against the reference Compose deployment:
docker compose --profile migrate run --rm migrate
```

`--migrate` retries a connection failure (the database not yet accepting
connections — a Kubernetes migration Job has no ordering guarantee against
first-boot `initdb`, WAL replay, or a restored volume) until a wall-clock
budget is exhausted; a genuine migration error (PostgreSQL rejecting the
migration itself) fails immediately, without retrying. It exits `0` on
success, non-zero otherwise. Bound by the `Database` configuration section:

- `Database:StartupTimeoutSeconds` (default `120`) — total wall-clock budget,
  measured with a monotonic clock, for `--migrate` to reach the database and
  apply migrations. This is the *only* knob that bounds `--migrate`'s wait.
- `Database:RetryDelaySeconds` (default `5`) — sleep between `--migrate`
  connection retry attempts, and the cap on the runtime execution strategy's
  own backoff.
- `Database:MaxRetries` (default `5`) — scoped only to the runtime EF Core
  execution strategy (`EnableRetryOnFailure`), i.e. how many times a database
  operation is retried once the application is already serving traffic. Does
  **not** apply to `--migrate`.

These two mechanisms — the `--migrate` entrypoint's wall-clock retry loop and
the runtime execution strategy's retry count — are deliberately not unified
into a single setting: they have different lifetimes and a count-based budget
makes the real wait depend on how the database is unavailable.

## Authentication event log

Every authentication decision is written to the standard logging pipeline under
the category **`Fakturenn.Auth`**, as a message of the form
`AuthEvent {Event} {Email}` (or `{Actor} {Target}` for an administrative
action). The `Event` property carries a stable name, so alerting rules select
on it rather than on message wording.

| Event | Level | Written when |
| --- | --- | --- |
| `SignInSucceeded` | Information | A password sign-in completed for an account with no second factor yet |
| `SignInFailed` | Warning | A password sign-in was refused |
| `AccountLockedOut` | Warning | A sign-in attempt met a locked account |
| `TwoFactorSucceeded` | Information | The authenticator code was accepted |
| `TwoFactorFailed` | Warning | The authenticator code was refused |
| `RecoveryCodeUsed` | Warning | A recovery code was redeemed, and thereby spent |
| `RecoveryCodeFailed` | Warning | A recovery code was refused |
| `TotpEnrolled` | Information | Enrolment completed |
| `PasswordChanged` | Information | A user replaced their own password |
| `SignedOut` | Information | A session was ended by its owner |
| `FirstAdministratorCreated` | Information | `/setup` minted the first administrator |
| `AdminCreatedUser` | Information | An administrator created an account |
| `AdminResetPassword` | Information | An administrator reset somebody's password |
| `AdminClearedMfa` | Information | An administrator cleared somebody's second factor |
| `AdminLockedUser` | Information | An administrator locked an account |
| `AdminUnlockedUser` | Information | An administrator unlocked an account |
| `OperatorCreatedAdmin` | Information | `--create-admin` |
| `OperatorResetPassword` | Information | `--reset-password` |
| `OperatorResetMfa` | Information | `--reset-mfa` |
| `OperatorUnlockedUser` | Information | `--unlock-user` |

`SignInFailed` deliberately carries **no reason**. The sign-in endpoint answers
identically for an unknown address and a wrong password, and a log that
distinguished them would hand the enumeration oracle to anyone who can read it.

No event carries a password, TOTP code, recovery code, authenticator key,
password-reset token, security stamp or Data Protection payload — asserted by
`AuthEventLoggingTests.No_secret_reaches_a_sink`, which drives real sign-in,
enrolment, recovery, password-change and administrative-reset flows against an
in-memory sink attached to the running host.

### Selecting the `_msg` JSON formatter

Some log stores take a line's headline text from a field named exactly `_msg`
and render a placeholder when it is absent. `MessageFieldJsonFormatter` writes
one JSON object per event with the rendered message under that name. It is
**not** selected by default — the human-readable console formatter stays the
default — and an operator adopts it by configuration alone:

```json
"Serilog": { "WriteTo": [ { "Name": "Console", "Args": {
  "formatter": "Fakturenn.Infrastructure.Logging.MessageFieldJsonFormatter, Fakturenn.Infrastructure.Logging" } } ] }
```

The type and assembly names in that string are part of the contract. They are
resolved at runtime, so a typo fails only in the deployment that adopted the
formatter, and only when that sink is used.

## The Data Protection key ring

The application encrypts data at rest with ASP.NET Core Data Protection. The key
ring is **stored in the same PostgreSQL database as the data it protects**, in
table `dataprotection."DataProtectionKeys"`, and the application name is pinned
to `Fakturenn` so that every replica derives the same keys. Without that pin each
instance would invent its own ring and a cookie written by one replica would be
unreadable by another — sticky sessions give circuit affinity, not key sharing.

What the ring protects:

- **`AspNetUserTokens.Value`** — the TOTP authenticator secret and the recovery
  codes of every enrolled user. Identity stores both in plaintext by default; this
  application encrypts the column instead.
- The authentication cookie, the two-factor cookie, and the short-lived cookie
  that carries freshly generated recovery codes to their display page.
- Antiforgery tokens.

### Backup and restore: the key and the ciphertext are one artifact

Because the ring lives in the database, an ordinary logical or physical backup of
that database captures the keys and the ciphertext **together, at the same point
in time**. That is deliberate and it is the property to preserve: it means there is
no separate key file to forget, and no window in which a restore produces
ciphertext whose key is a different vintage.

The consequence is the part an operator must not learn by discovering it:

> **Restoring the database without its own key ring — or restoring the key ring
> without its own database — destroys every enrolled authenticator and every
> recovery code in the installation.** Not "requires re-authentication": the stored
> secrets become permanently undecryptable. Every user is locked out of their
> second factor at once, and the only way back in is an operator with shell access
> running `--reset-mfa` for each of them, followed by every user re-enrolling.

In practice this means:

- **Never restore `dataprotection."DataProtectionKeys"` and the `identity` schema
  from different backups.** Restore the whole database as one unit, from one dump.
- **Never truncate or "clean up" the key table.** Expired keys are still needed to
  read values written while they were current; Data Protection retires a key for
  *writing* long before the data it wrote stops existing.
- **A partial restore is worse than a failed one**, because it succeeds. The
  application starts, `/health` reports ready, and the failure appears one user at
  a time as a `CryptographicException` on sign-in.
- After any restore, verify with a real sign-in that completes the second factor,
  not merely with `/health`.

### The keys are not encrypted at rest

`ProtectKeysWithCertificate` is **not** configured. The key ring is stored as the
database sees it, so anything that can read the database — including a backup file
lying around unencrypted — holds the keys to the authenticator secrets. Protect the
database and its backups accordingly: encrypt backup artifacts at rest and restrict
who can read them.

Adding certificate protection later is possible, but note it introduces a *second*
restore hazard of the same shape: from that point the database alone is no longer
sufficient, and a restore also needs the certificate and its private key, backed up
separately and kept for as long as any key it wrapped. Losing that certificate has
exactly the effect described above. It is not currently configured, so today the
database is the single artifact — do not change that without updating this section.

## Operator recovery entrypoints

Five flags on the application binary exist for the case where the application's own
controls have locked the operator out. They deliberately bypass authentication, the
rate limiter, the enrolment gate and every permission policy. What keeps that safe
is that they are reachable **only from a shell on the host** — none is mapped as an
HTTP endpoint, and none ever should be.

```bash
dotnet run --project src/Fakturenn.Web -- --list-users

# In the reference Compose deployment, as a one-shot container against the same
# database. `run` overrides the service's command and attaches a terminal, which the
# password prompt needs; it does not publish the app's ports or start a second host.
docker compose run --rm fakturenn-app --list-users
```

In Kubernetes the equivalent is a `Job` running the same image with the flag as its
`args`, sharing the application's connection-string secret — the same shape as the
migration Job, and for the same reason: it must not run as a side effect of a Pod
serving traffic.

| Flag | Effect |
| --- | --- |
| `--create-admin <email>` | Creates an administrator. Takes the same advisory lock as `/account/setup`, so concurrent invocations cannot each mint one. The new account must change its password and enrol an authenticator at first sign-in. |
| `--reset-password <email>` | Sets a new password, clears any lockout, zeroes the failure count, and requires a change at next sign-in. |
| `--reset-mfa <email>` | Clears the authenticator key and the recovery codes and requires re-enrolment. This is the recovery path after a lost device — and the only remedy after a key-ring loss. |
| `--unlock-user <email>` | Clears the lockout **and rotates the security stamp**, which ends any session the account still holds. An account locked by failed sign-in attempts never had a rotation, so a session opened before those attempts would otherwise still be live — and whoever was guessing the password is exactly who might be holding it. |
| `--list-users` | Prints e-mail, display name, locked, locked-until, two-factor and must-enrol. Nothing else: no authenticator key, no recovery code, no password hash. A diagnostic reached for when an instance is already in trouble ends up in scrollback and support tickets. |

**Passwords are read from standard input, never from the command line.** `argv` is
visible in `ps` to every user on the host and lands in shell history. Passing a
password positionally or via `--password` exits `2` and changes nothing; the prompt
is written to standard error so the flags remain pipeable.

Every one of these writes an authentication event (`OperatorCreatedAdmin`,
`OperatorResetPassword`, `OperatorResetMfa`, `OperatorUnlockedUser`) under the
`Fakturenn.Auth` category, so operator action appears in the same timeline as
everything else.

## Backup

Back up consistently:

- PostgreSQL — **including** `dataprotection."DataProtectionKeys"`; see the key
  ring section above for why this must be one dump, restored as one unit
- filesystem or S3 documents
- secrets and certificates
- configuration

Restore must verify artifact hashes and database/storage consistency, and must be
verified by a real sign-in through the second factor rather than by `/health`
alone.
