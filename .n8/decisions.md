# Decisions log

Append-only. One `##` section per skill run (or `## Ad-hoc — <date>` for changes made outside the n8SDLC commands), entries in chronological order. Record real decisions — choices between alternatives, assumptions, deviations from plan — not routine actions.

Entry format:

```markdown
## /n8-exec M1 — 2026-08-27

- **Decision:** <what was chosen>
  **Why:** <the reasoning, and the cost if wrong>
  **Issue:** #N
```

Ad-hoc entries (changes that deviate from what planned issues assume — different library, provider, architecture, dropped/added scope, amended invariant):

```markdown
## Ad-hoc — 2026-08-27

- **Change:** <what changed>
  **Why:** <why>
  **Affects:** <milestones / issues whose plans may now be stale>
```

`/n8-replan` appends `— reconciled by /n8-replan <date>` to each ad-hoc entry it processes.

## /n8-init — 2026-08-30

- **Decision:** Existing codebase (334 commits, ~162k LOC C# / ~81k LOC TS) — no scaffold; recorded stack as `dotnet`. Analyzers were already wired in `Directory.Build.props` (NetAnalyzers, VSTHRD, AsyncFixer, Sonar) with per-rule rationale in `.editorconfig`, so no build-quality changes were made.
  **Why:** Init only fills gaps. Recommendation: run `/n8-map` before `/n8-roadmap` so planning works from a real map.
- **Decision:** Removed the homegrown `/audit` dispatcher and six `audit-*` project skills after harvesting their AutoNate-specific checklists into `.n8/memory/audit-*.md`.
  **Why:** They overlap `/n8-audit`'s domain (whole-codebase audits). The checklists — hot-path inventory, canonical remediation patterns, historical gotchas — are the load-bearing part and are preserved; the report-shaped output is replaced by `/n8-audit` filing fingerprinted issues. User chose harvest-then-remove; removal is one `git revert` away.
- **Decision:** Kept `add-*`, `plugin-creator`, `mantine-*` project skills and `Agents.codex.md` untouched.
  **Why:** Complementary capability recipes / outside n8SDLC's domains.
- **Decision:** Kept `docs/plans/` (six historical plan files).
  **Why:** User asked to leave plans. Under n8SDLC, GitHub Issues are the plan going forward; the folder is historical context for `/n8-map`.
- **Decision:** Wiki opted out; security findings routed to `issues`; rulesets / CodeQL default setup / secret scanning skipped.
  **Why:** Repo is private on a plan without those features. Revisit all four when the repo goes public (user intends to).
- **Decision:** Left GitHub's default labels (`duplicate`, `invalid`, `wontfix`, `enhancement`, `good first issue`) in place; overwrote `documentation`'s description to the n8SDLC wording.
  **Why:** Never delete labels init didn't create; the rest are GitHub defaults, not a curated taxonomy.
- **Decision:** Init changes committed on the user-created `n8-proj-mgmt` branch rather than pushed straight to `master`.
  **Why:** The user created the branch for this work immediately before running init.

## /n8-audit all — 2026-08-31

- **Decision:** Ran all eight areas (security, authorization, stability, performance, cleanup, 508, tests, integration) plus dependency CVEs in one pass, and filed every verified finding (user asked for "anything you find"; the "start narrow on a first run" advice was set aside on that instruction).
  **Why:** First n8SDLC audit of a 334-commit codebase; the user wanted the full register. Each area ran as a parallel sweep agent seeded with the harvested `.n8/memory/audit-*-checklist.md`; every claim was then re-verified against the source by the auditor before filing (line reads, independent greps, live gate-presence test, computed contrast ratios, a real trial-delete for dead-code claims).
- **Decision:** Rejected six agent findings after verification: four "zero-importer" dead-module claims + one "provider never mounted" claim (all imported by `WorkflowStudio.tsx`, which `grep` classifies as binary — see auto-memory `feedback_unused_ts_module_verification`), and one ".agents/skills duplicates .claude/skills" claim (they are symlinks). Merged overlapping findings (Python-runner sandbox ×3, executions load-all ×2, code-transformer gating ×2).
  **Why:** Evidence bar — a grep hit is a lead, not a finding.
- **Decision:** Issues were filed without a milestone.
  **Why:** No milestones exist yet; `/n8-roadmap` creates `M<N>: Audit` and should sweep every open `sev:*` issue into it (and pull the critical/high security ones into an earlier milestone).
- **Decision:** Memory-drift findings (3) were fixed directly in the auto-memory files rather than filed.
  **Why:** They are the agent's own notes, not source; filing them as project issues would be noise.
- **Decision:** Labels used: `sev:*` + closest baseline (`security` / `performance` / `bug` / `documentation`) + one `area:*`; audit area travels in the fingerprint rule-id (`<!-- fingerprint: rule|path|symbol -->`), so re-runs dedupe three-way.
- **Decision:** Added `.n8/memory/hot-paths.md` (performance inventory) and the `maven` ecosystem to `dependabot.yml` (flowable-extension was unscanned).
