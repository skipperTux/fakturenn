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

## Cryptography

- certificate expiry monitoring
- key rotation path
- fail closed when required signing is unavailable
- independent signature verification tests
