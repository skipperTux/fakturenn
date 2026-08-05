# SPIKE-006 — Wolverine topology

## Questions

- App-hosted vs separate worker?
- PostgreSQL schema and outbox?
- Retry, poison handling, versioning, replica behavior?

## Exit

HTTP transaction → durable message → forced failure → successful retry.
