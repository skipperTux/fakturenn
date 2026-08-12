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
trusts only the peer address it observes itself.

Fakturenn reads either `X-Forwarded-*` or the RFC 7239 `Forwarded` header. **A
proxy that emits `Forwarded` must be configured to put a real address in
`for=`.** RFC 7239 section 6.2 makes an obfuscated identifier such as
`for=_YQuN68tm6` the default form, and an obfuscated identifier carries no
address at all: Fakturenn cannot see the client, and per-client account rate
limiting degrades to per-proxy — every client behind that proxy shares one
budget.

For YARP, this is the `Forwarded` request transform's `ForFormat`. It defaults
to `Random`. Use `Ip`, `IpAndPort` or `IpAndRandomPort` instead. Enabling that
transform at all also switches YARP's `X-Forwarded` transforms **off**, so
there is no fallback header — the obfuscated `for=` is everything the
application receives.

Whichever header the proxy sets, it must strip the inbound copy of it.
Fakturenn ignores `Forwarded` whenever `X-Forwarded-For` is present rather than
merging two chains of different provenance.

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

## Backup

Back up consistently:

- PostgreSQL
- filesystem or S3 documents
- secrets and certificates
- configuration

Restore must verify artifact hashes and database/storage consistency.
