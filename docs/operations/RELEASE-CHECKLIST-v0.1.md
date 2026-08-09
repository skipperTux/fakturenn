# Release checklist — v0.1 human-test release

`docs/testing/TEST-STRATEGY.md` states:

> v0.1 human-test release requires all automated suites green and successful
> independent verification of at least one Factur-X, one XRechnung, one S/MIME
> message, and one OpenPGP message.

The workflows under `.github/` are syntactically valid and built from commands
verified locally, but this repository has no remote yet, so none of them —
`ci.yml`, `codeql.yml` or `release.yml` — has ever executed. The first push is
their first real run. Treat every box in "Automated gates" as unverified until
then, and watch the Actions run itself rather than assuming green.

The four independent verifications below cannot be automated away —
"independent" means verified by a tool that is not part of this codebase.

This checklist fails closed **because the `publish` job in `release.yml` waits
on the `release` GitHub Environment**, which blocks the job until a configured
reviewer approves the run — *provided that environment's required reviewers
have been configured*. That is a repository setting, not something the
workflow file can create by itself, so it must be armed once before the first
release:

> **One-time prerequisite:** in the repository's GitHub settings, go to
> Settings → Environments → create (or open) an environment named `release` →
> add at least one required reviewer. Until this is done, the `environment:`
> reference in `release.yml`'s `publish` job has no gating effect and the job
> runs straight through — an unticked box below will not actually block
> anything.

## Automated gates

- [ ] The one-time `release` environment reviewer setup above is confirmed in
      place (Settings → Environments → `release` → required reviewers)
- [ ] `release.yml`'s own `format`, `build-test`, `integration` and `ui` jobs
      are green on the tag (these four re-verify the tagged commit
      independently of `main`; `compose-smoke` is deliberately not repeated
      here — the `publish` job's real multi-arch registry push is a stronger
      check)
- [ ] `ci.yml` — including `compose-smoke` — was green for the commit the tag
      points at. `ci.yml` triggers only on push/PR to `main`, never on tags,
      so check the Actions run for that commit's push/PR to `main`, not a run
      "on the tag" (there isn't one)
- [ ] `codeql.yml` reports no new alerts for the commit the tag points at, for
      the same reason: it triggers only on push/PR to `main`
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

- [ ] From a clean checkout, publishing the image locally and then running
      `docker compose up --detach` (see `CLAUDE.md`'s Commands section for the
      exact `dotnet publish .../t:PublishContainer` invocation — `compose.yaml`
      references `fakturenn:dev` with no `build:` stanza and no such tag exists
      in any registry, so `docker compose up` alone cannot pull it) reaches
      `/health` healthy
- [ ] The migration Job (`docker compose --profile migrate run --rm migrate`)
      applies from an empty database
- [ ] A backup taken per `DEPLOYMENT-BASELINE.md` restores with matching
      artifact hashes

## Status for the current release

**Not satisfiable yet.** Factur-X and XRechnung generation arrive with epic
E12; S/MIME and OpenPGP signing with epic E14. Until both land, this checklist
cannot be completed truthfully, and v0.1 cannot be declared a human-test
release. This is the intended behaviour, not an oversight.
