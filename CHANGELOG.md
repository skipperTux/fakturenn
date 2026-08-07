# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Repository contract documents: `CLAUDE.md`, `README.md`, `LICENSE`, `CHANGELOG.md`.
- .NET 10 solution scaffold with central package management and warnings as errors.
- Shared kernel value objects: `Money`, `Percentage`, `IClock`, `IIdGenerator`.
- Filesystem blob writer with SHA-256 hashing.
- Invoices module seam with a module-owned `DbContext` and an explicit
  `--migrate` entrypoint.
- Blazor Interactive Server host with MudBlazor, English and German
  localization, and `/health` and `/alive` endpoints.
- Test harness: unit, architecture, integration (Testcontainers), compliance
  (golden-file comparer) and Playwright UI suites.
- Container image published with the .NET SDK container tooling and a Compose
  reference deployment.
- GitHub Actions CI, CodeQL, Dependabot, and a tag-triggered release pipeline
  publishing to GHCR with an SBOM and SHA-256 checksums.
- `docs/operations/RELEASE-CHECKLIST-v0.1.md`, the fail-closed human
  verification gate for the v0.1 release.
