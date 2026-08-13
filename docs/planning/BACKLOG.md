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

**What it needs.** An `otpauth://totp/` URI built from the issuer, the account name and the key, rendered as a QR code. The URI is trivial; the renderer is the decision. A QR library is a new dependency on a page that displays a live second-factor secret, so it has to be one worth trusting and it must render locally — the "no external asset CDN, ever" ruling in `IMPLEMENTATION-NOTES.md` rules out any image service, and pointing a third party at an `otpauth://` URI would hand them the secret regardless.

**Constraints it must not break.** The page's response is already `no-cache, no-store` because it carries the secret, and the QR image must be inline (a `data:` URI) rather than a second request, or the secret gets its own cacheable URL. The Content-Security-Policy test is the check that an inline image source is actually permitted.

**Why not in E02a.** `CLAUDE.md`'s YAGNI rule, applied honestly: manual entry works for every user today, and the alternative was a new dependency in the sign-in path for one screen. See section 8 of `docs/superpowers/specs/2026-08-10-e02a-identity-foundation-design.md`.

## Password entropy: warn, never meter

**Lands in:** the `IPasswordValidator<ApplicationUser>` seam established in E02a — one class, no schema change.

**The idea.** Calculate entropy and warn when it is confidently low. Do **not** display a strength meter, a score, or a coloured bar.

The asymmetry is the whole point. A meter always shows something, so it always reassures — including when the measurement cannot support that reassurance. It is trivially satisfied by a password a dictionary attack finds immediately. A warning that fires only on confident true positives cannot mislead, because silence is not a claim.

Off by default, threshold in configuration.

**Full reasoning, sources, and the licence note about implementing from a published description rather than from GPL source:** `docs/superpowers/specs/2026-08-10-e02a-identity-foundation-design.md`, section 8.
