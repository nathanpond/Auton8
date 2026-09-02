# Archived register

Auton8 was developed in a repository whose git history contained a
commercially-licensed theme (`src/AutoNate.Web/ColorAdmin/`, a paid ThemeForest
asset that was never part of the shipped product). Rewriting history removed it
from every branch and tag, but **GitHub keeps a read-only
`refs/pull/<n>/head` for every pull request ever opened**, and those are
server-managed: they cannot be rewritten or deleted, and they still pointed at
the original commits. Publishing that repository would have redistributed the
theme regardless of the rewrite.

So the project moved to a fresh repository on 2026-09-02, containing only the
rewritten history. The original stays private permanently.

GitHub can transfer issues between repositories but **cannot transfer pull
requests**. These files exist so the reasoning does not disappear with them:

- [`pull-requests.md`](pull-requests.md) — all 74 pull requests, with bodies and
  comments. This is where most of the engineering rationale lives: why a fix
  was shaped the way it was, what a test was actually pinning, which issue
  premises turned out to be wrong.
- [`issues.md`](issues.md) — all 121 issues, with bodies and comments. Open
  issues were carried over to the new repository and renumbered; closed ones
  are history, including 26 security findings, every one closed as fixed.

## The `archived-N` convention

References to this register are written **`archived-N`**, not `#N`. A bare `#N`
in a file or commit message is turned into a link by GitHub, and it would point
at issue N *in the current repository* — a different, real issue. `archived-N`
carries the same information and links to nothing.

`#N` still appears in a few places where it never meant an issue: `§11b #1`
section references in `docs/plans/`, CSS colours, and ordinals like
`Read-back #1`.

## Reading `archived-N` references

Numbers in these files, and in any commit message written before 2026-09-02,
refer to the **pre-migration** register archived here — not to issue numbers in
the current repository. The two number spaces are unrelated, so a pre-migration
`Closes archived-85` may coincide with a different, real issue today. When in doubt,
look it up here.
