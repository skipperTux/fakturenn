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

## Password entropy: warn, never meter

**Lands in:** the `IPasswordValidator<ApplicationUser>` seam established in E02a — one class, no schema change.

**The idea.** Calculate entropy and warn when it is confidently low. Do **not** display a strength meter, a score, or a coloured bar.

The asymmetry is the whole point. A meter always shows something, so it always reassures — including when the measurement cannot support that reassurance. It is trivially satisfied by a password a dictionary attack finds immediately. A warning that fires only on confident true positives cannot mislead, because silence is not a claim.

Off by default, threshold in configuration.

**Full reasoning, sources, and the licence note about implementing from a published description rather than from GPL source:** `docs/superpowers/specs/2026-08-10-e02a-identity-foundation-design.md`, section 8.
