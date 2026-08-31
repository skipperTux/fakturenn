# Security Baseline

## Identity

- ASP.NET Core Identity
- TOTP
- recovery codes
- lockout
- rate limiting
- secure password reset
- optional generic OIDC

## Secrets

Private keys and credentials are referenced from:

- mounted secret files
- Docker secrets
- Kubernetes secrets
- OpenBao or compatible secret provider
- OS certificate store where supported

Never store private keys as ordinary database columns.

## Web

- antiforgery
- secure cookies
- HSTS guidance
- proxy-header configuration
- CSP where compatible
- server-side authorization
- organization isolation checks

## Integrations

- TLS validation enabled by default
- explicit development-only insecure overrides
- SSRF protection for configured endpoints
- input limits for files and MIME attachments
- safe filename handling

## Runtime image

The published image is chiseled: no shell, no package manager, non-root by
default. Two things about it are worth stating rather than assuming.

- **It now contains a C# *and* a Visual Basic compiler.** Wolverine 6.30.0 no
  longer bundles Roslyn, so the host registers `WolverineFx.RuntimeCompilation`
  and the image carries the `Microsoft.CodeAnalysis` assemblies — about **34 MB**
  the pre-messaging image did not have. Read that figure carefully: the ten
  assemblies in `/app` account for 21 MB, and 117 localized satellite resource
  DLLs across 13 culture directories add a further 12.6 MB. An earlier draft
  said 21 MB, which is the right number for the wrong scope — it named under
  half of the 46 MB the image actually grew. `Microsoft.CodeAnalysis.VisualBasic`
  and its workspaces assembly (5.7 MB together) ship too, though nothing here
  compiles Visual Basic. What is compiled is code Wolverine generates from the
  application's own handler signatures at first dispatch, never anything that
  arrives over the network, and no entrypoint exposes compilation to a caller.
  It is still a larger runtime surface than a shell-less image implies, and a
  hardening review should know it is there.
- **The application process cannot write to its own content root**, and nothing
  should be designed to. `/app` is root-owned and the process runs as UID 1654.
  Wolverine's generated-source writing is switched off for exactly this reason;
  see `docs/architecture/IMPLEMENTATION-NOTES.md`.

## Logging

Never log:

- passwords
- SMTP/IMAP credentials
- private keys
- access tokens
- complete invoice contents by default
- complete email bodies by default

Authentication adds to that list, and adds one thing that *is* logged. Never
log a TOTP code, a recovery code, an authenticator key, a password-reset token,
a security stamp, a Data Protection payload, a cookie value, or a session
identifier from which a session could be reconstructed. **Do** log the e-mail
address an event concerns: an operator cannot act on an incident without
knowing which account it names.

A sign-in failure must not record *why* it failed at a granularity that
separates "no such account" from "wrong password". The endpoint answers
identically for both; a log that did not would hand the enumeration oracle to
whoever reads it.

The event catalogue, its levels and the `_msg` JSON formatter are documented in
`docs/operations/DEPLOYMENT-BASELINE.md`.

## Cryptography

- certificate expiry monitoring
- key rotation path
- fail closed when required signing is unavailable
- independent signature verification tests
