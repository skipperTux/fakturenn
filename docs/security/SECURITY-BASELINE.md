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
