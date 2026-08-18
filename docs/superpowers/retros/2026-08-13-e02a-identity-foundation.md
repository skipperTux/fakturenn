# Retrospective — E02a, the identity foundation

Eighteen planned tasks, three review rounds, 280 tests, merged green on the
first CI run. This records how the work went, not what it built — the code is
in the repository and the verified facts are in
`docs/architecture/IMPLEMENTATION-NOTES.md`. Kept because the same failure
shapes will recur in the next epic.

## The dominant defect shape

**A decision correct in the task that made it, silently inherited by later
tasks that never re-derived it.** Every serious defect in this epic had that
shape, including all three the final review classed as blocking:

- Antiforgery was measured and consciously accepted for **one unauthenticated
  endpoint** on an unconfigured instance. Ten authenticated endpoints then
  copied the handler shape, and the acceptance argument was never re-checked
  against them.
- `/account/enrol-totp` re-reading the existing key was right while the
  enrolment gate was the only route to that page. Nothing revoked that once the
  flag could be clear.
- `ci.yml` listed the test suites that existed when it was written. Two more
  were added and never appeared, so 97 assertions compiled without ever running.
- `MustChangePassword` was set by one task and enforced by none until another —
  a flag handed across a gap.
- `RefreshSignInAsync` was needed because two separate, individually-correct
  decisions — a one-minute stamp validation interval and a stamp-rotating
  enrolment — combined into a silent sign-out.

None of these is a mistake in the task that made it. They are invisible from
inside any single task, and per-task review cannot see them by construction.

**The rule that would have caught all five:** when a later task copies an
earlier task's shape, re-derive the earlier task's acceptance argument against
the new context. "It was fine there" is not evidence it is fine here.

## Green is not evidence

Repeatedly, the suite was green and the guarantee was absent.

- **A test can pass against both a correct and an incorrect implementation.**
  The plan's own seeding test passed against a naive create-if-absent seeder.
  Only a test that deletes a granted permission and re-runs distinguishes
  re-sync from create-if-absent.
- **A test can assert an outcome that something else is producing.** Removing
  the setup advisory lock reddened nothing, because an incidental unique index
  on the role name was serialising the racers. When a mutation produces no red,
  the question is *what else is providing this guarantee*, not *the guard is
  redundant*.
- **Removing a whole subsystem left 245 tests green.** Deleting
  `PersistKeysToDbContext` — the shared Data Protection key ring, which the spec
  named as the mitigation for a stated risk — broke nothing any test noticed.
- **A test written to catch a false green contained one.** The key-ring test
  passes without the ring, because `AddDataProtection` falls back to a shared
  home directory; only its row-count assertion makes it real.

Mutation testing is what surfaced each of these, and it only works if the
mutation is applied to the **shipped** code and the failure is read carefully.
See `IMPLEMENTATION-NOTES.md`, "Mutation testing: how to revert, and how to
read a green".

## A plan's own tests are not verified tests

The plan for this epic prescribed test code. That code contained defects:

- A test that could not distinguish delegation from a hardcoded `null`, because
  the fixture it used made both produce the same result.
- A CSP assertion that checked only that the header existed — passing whether or
  not the policy broke every script on the page.
- The most security-sensitive endpoint in the epic — an unauthenticated route
  that mints an administrator — shipped in the plan with **no automated test at
  all**.

Treat prescribed tests as claims to verify, not as work already done.

## What a person catches that a suite cannot

Two real defects were found in about ninety seconds of using the application,
after 280 tests, eighteen task reviews and a full adversarial review had passed:

- Enrolment failed if the user took longer than a minute to type the code.
  **Every test posts its form within milliseconds of rendering it**, so no test
  varied the dimension the bug lived in.
- A rejected form discarded everything the user had typed.

A third — password-manager integration — was diagnosed wrongly from a written
description and correctly only once the browser was in front of a person. The
fix was seven missing `autocomplete` attributes, not the `otpauth://` URI that
had been designed, built and tested first.

**The suite tests what the code does; only a person tests what using it is
like.** Budget for manual use before calling an epic done.

## Delegation: location work, not judgement

A read-only pre-audit of each task against the tree found a plan defect in
nearly every task — missing types, route collisions, staging omissions. That
is mechanical work and delegating it was cheap and effective.

The limit showed up in the last task. The audit was asked whether the CI status
section was accurate, and reported that it was. It was not: it described a world
that had since changed. **An audit reads what is written; it cannot tell that a
true-sounding sentence is stale.** That one was caught by running
`git remote -v` and `gh run list` — by checking the world, not the text.

Delegate finding things. Do not delegate deciding whether they are right.

## State a prediction as a hypothesis

Four times a confident prediction was wrong, and each time measurement caught it:

- That deleting an RFC 7239 node-rejection predicate would redden a test — it
  did not; `IPAddress.TryParse` already rejected those forms.
- That `--unlock-user` needed no security-stamp rotation — the spec required it,
  and a user locked by failed attempts can hold a live session.
- A citation of the wrong RFC section.
- An antiforgery failure diagnosed as a claims-hashing problem, when the cause
  was an un-refreshed cookie after a stamp rotation. The evidence that
  disproved it — a signed-out request in the same log — had already been read
  and dismissed.

The useful habit is phrasing an instruction as *"my hypothesis is X; measure it
before acting"*. Every one of these was corrected by someone who checked rather
than complied.
