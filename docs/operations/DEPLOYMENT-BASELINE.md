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

## Backup

Back up consistently:

- PostgreSQL
- filesystem or S3 documents
- secrets and certificates
- configuration

Restore must verify artifact hashes and database/storage consistency.
