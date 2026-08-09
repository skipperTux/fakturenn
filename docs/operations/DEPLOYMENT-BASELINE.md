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
