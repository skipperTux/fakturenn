# Release checklist — v0.1 human-test release

`docs/testing/TEST-STRATEGY.md` states:

> v0.1 human-test release requires all automated suites green and successful
> independent verification of at least one Factur-X, one XRechnung, one S/MIME
> message, and one OpenPGP message.

Automated suites are gated by CI. The four verifications below cannot be
automated away — "independent" means verified by a tool that is not part of
this codebase. This checklist fails closed: an unticked box blocks the release.

## Automated gates

- [ ] `format`, `build-test`, `integration`, `ui` and `compose-smoke` are green
      on the tagged commit
- [ ] CodeQL reports no new alerts
- [ ] The published image runs as a non-root user

## Independent verification

- [ ] **Factur-X** — one generated invoice validated by an external EN 16931
      validator not maintained in this repository. Record tool, version and
      report.
- [ ] **XRechnung** — one generated invoice validated by the official KoSIT
      validator. Record the validator version, the scenario configuration
      version, and the report.
- [ ] **S/MIME** — one signed message whose signature verifies in a mail client
      that has never seen this codebase. Record client and version.
- [ ] **OpenPGP** — one signed message whose signature verifies with GnuPG.
      Record the GnuPG version and the key fingerprint used.

## Operational verification

- [ ] `docker compose up` from a clean checkout reaches `/health` healthy
- [ ] The migration Job applies from an empty database
- [ ] A backup taken per `DEPLOYMENT-BASELINE.md` restores with matching
      artifact hashes

## Status for the current release

**Not satisfiable yet.** Factur-X and XRechnung generation arrive with epic
E12; S/MIME and OpenPGP signing with epic E14. Until both land, this checklist
cannot be completed truthfully, and v0.1 cannot be declared a human-test
release. This is the intended behaviour, not an oversight.
