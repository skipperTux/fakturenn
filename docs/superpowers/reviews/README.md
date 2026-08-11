# Spec reviews

Every epic design spec receives a review **before** it is turned into an
implementation plan. The review is a gate in the spec-to-plan step, not an
afterthought: a finding fixed in the spec costs one edit; the same finding
discovered mid-plan costs rework across tasks.

## Process

1. A spec under `docs/superpowers/specs/` reaches **Status: Approved**.
2. A read-only review runs against the spec and its supporting documents,
   judging three questions: is it **complete**, is it **sound**, is it
   **secure**?
3. The review lands here as `YYYY-MM-DD-<spec-slug>-review.md`, with every
   finding carrying an identifier, a severity, and a disposition checkbox.
4. A revision pass addresses the findings in the spec. Each finding is either
   fixed or explicitly rejected with a reason — silence is not a disposition.
5. Only when every finding is dispositioned does the spec proceed to
   `docs/superpowers/plans/`.

## Severities

- **Security** — a gap that ships exploitable or operationally broken if the
  implementation follows the spec literally.
- **Completeness** — a decision, flow, or state the spec needs and does not
  contain.
- **Minor** — inconsistencies, wrong cross-references, one-sentence
  ambiguities.

## Reviews

- [2026-08-11 — E02a Identity foundation](2026-08-11-e02a-identity-foundation-spec-review.md)
