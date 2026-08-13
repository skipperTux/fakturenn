# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Sign-in with a mandatory second factor.** An account is reached with a
  password and a TOTP code from an authenticator app. There is no way to
  configure the second factor away, and no account can use the application
  before enrolling one.
- **First-run setup.** A new installation serves a one-time `/setup` page that
  creates the first administrator. It closes permanently once any user exists,
  and exactly one administrator is created even if several people submit the
  form at the same moment.
- **Ten single-use recovery codes**, shown once at enrolment, for the case
  where the authenticator device is lost. A redeemed code is spent.
- **User administration** at `/admin/users` for accounts holding the
  `users.manage` permission: create an account, reset a password, clear
  somebody's second factor, and lock or unlock an account. Locking a user ends
  the session they are currently holding rather than waiting for it to expire.
- **Accounts created or reset by an administrator must choose their own
  password** at next sign-in, so an administrator-chosen password is never a
  standing credential.
- **Automatic lockout** after repeated failed sign-in attempts, and per-account
  rate limiting on every account endpoint.
- **Operator recovery from the command line** — `--create-admin`,
  `--reset-password`, `--reset-mfa`, `--unlock-user` and `--list-users` — for
  the case where nobody can sign in any more. Passwords are read from standard
  input, never from the command line. See `docs/operations/DEPLOYMENT-BASELINE.md`.
- **An authentication event log** under the `Fakturenn.Auth` category: twenty
  named events covering every sign-in, second-factor, recovery-code and
  administrative outcome, suitable for alerting. No event carries a password,
  code, key or token.
- **Full German and English user interface.** Every page this release adds, and
  the validation messages behind it, exist in both languages; the page follows
  the browser's `Accept-Language`.

### Security

- **Authenticator secrets and recovery codes are encrypted at rest.**
  ASP.NET Core Identity stores both in plaintext by default; this release
  encrypts the column with a key from a Data Protection key ring held in the
  same database, so a database backup captures ciphertext and key together.
  **Restoring one without the other permanently destroys every enrolled
  authenticator and recovery code** — see the key ring section of
  `docs/operations/DEPLOYMENT-BASELINE.md` before planning a restore.
- Sign-in answers identically for an unknown address and a wrong password, and
  the log does not distinguish them either, so neither can be used to discover
  which addresses have accounts.
- A Content-Security-Policy is served on every response and is verified in a
  real browser rather than by checking that the header exists.
- `--migrate` refuses to complete if the database grants a permission this
  version does not define, so a stale grant blocks the deployment instead of
  silently granting nothing.

## [0.1.0-alpha.1]

### Added

- Repository contract documents: `CLAUDE.md`, `README.md`, `LICENSE`, `CHANGELOG.md`.
- .NET 10 solution scaffold with central package management and warnings as errors.
- Shared kernel value objects: `Money`, `Percentage`, `IClock`, `IIdGenerator`.
- Filesystem blob writer with SHA-256 hashing.
- Invoices module seam with a module-owned `DbContext` and an explicit
  `--migrate` entrypoint.
- Blazor Interactive Server host with MudBlazor, English and German
  localization, and `/health` and `/alive` endpoints.
- Test harness: unit, architecture, integration (Testcontainers), compliance
  (golden-file comparer) and Playwright UI suites.
- Container image published with the .NET SDK container tooling and a Compose
  reference deployment.
- GitHub Actions CI, CodeQL, Dependabot, and a tag-triggered release pipeline
  publishing to GHCR with an SBOM and SHA-256 checksums.
- `docs/operations/RELEASE-CHECKLIST-v0.1.md`, the fail-closed human
  verification gate for the v0.1 release.
