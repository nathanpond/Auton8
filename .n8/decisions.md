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

## /n8-map — 2026-08-31

- **Decision:** Map written to `docs/codebase/` (wiki opted out): Stack, Integrations, Architecture, Structure, Conventions, Testing, Concerns — each stamped `Generated from commit 01f0f174`. Freshness check: `git log 01f0f174..HEAD --stat`.
- **Decision:** Filed all 15 concerns the owner approved (#112–#126): 4 bugs, 4 cleanup/perf, 4 doc/config drift, 3 design spikes (`spike` + `needs-triage`, no `sev:`). Each was re-verified by the orchestrator before the question was put (the rebuild-400 claim by the mapper's own throw-away xunit probe; NUL bytes by `perl`; the rest by targeted greps and reading the cited lines).
  **Why:** The user chose to file every candidate, including design spikes, so `/n8-roadmap` sees existing debt alongside features.
- **Decision:** Rejected the mapper's "audit outbox bypassed by default" claim (`AuditOutboxOptions.Enabled` defaults to `true`) and left two architecture-mapper observations unfiled as unverified: "EntityTypeDefinition.Actions is advisory at grant-creation time" and "add-page-context-provider's pageKey-mismatch 400 no longer exists" (`AgentSession.cs:880` still handles a page-key mismatch).
- **Note:** Enabling Dependabot security updates + version updates produced 22 PRs (#5, #6, #94–#111) within a day, several of them majors (TypeScript 5→7, mantine-datatable 8→9) and one implausible version (`pyodide 0.26.4 → 314.0.6`) that must be checked against the npm registry before anyone merges it. Consider tightening `dependabot.yml` (ignore majors, `open-pull-requests-limit`) in M0.

## Ad-hoc — 2026-08-31

- **Change:** Node.js runtime standardised on 24 (Active LTS) — `.nvmrc`, `engines.node` in all four `package.json`s, both sidecar images on `node:24-alpine`; `isolated-vm` 5 → 7 in the executor (5 cannot compile against Node 24's C++20 V8 headers), install-script approval (`allowScripts`) added for npm ≥ 11.19, executor lockfile added.
  **Why:** Nothing pinned Node; images were on 22 (Maintenance) while dev ran 24. Node 26 stays Current until October 2026 — revisit then (Dependabot now tracks the base images).
  **Affects:** #102 superseded, #105/#101 closed as types-track-runtime, #39 (executor lockfile) resolved, #114 (executor not in compose) still open. Issue #139.

## Ad-hoc — 2026-09-01

- **Change:** CI (`.github/workflows/ci.yml`, #79) runs 155 of 163 E2E specs. Seven that need services the runner does not host are excluded by trait — `RequiresService=Flowable` (workflow execution + studio) and `RequiresService=Dapr` (the bus-event log). The backend suite runs in full (1645) and the SPA gates run in full.
  **Why:** Flowable is a Spring Boot service built from source and Dapr is a second sidecar; standing both up in GitHub Actions is its own piece of work, and a permanently-red job is a gate people learn to ignore. Owner reviewed the trade-off and accepted it, with the full suite to run in a separate environment where the whole compose stack can be stood up.
  **Affects:** #79 is closed against the scoped job, not full-stack CI. Anyone re-enabling those specs in this workflow needs the services first — the exclusion is deliberate, not an oversight. The traits are the contract: a new Flowable-dependent spec is excluded automatically rather than turning the job red. If the separate full-E2E environment is built, it should run `dotnet test tests/AutoNate.E2E.Tests` with no filter (163 specs today).
- **Note:** Three environment dependencies the E2E suite had never had to state were discovered by running it on a clean machine: the Hocuspocus sidecar on :1234 (without it ConsoleErrorGuard fails every editor spec on `ERR_CONNECTION_REFUSED`), a built `plugins/HelloPlugin/dist/HelloPlugin.zip`, and the SPA bundle in `AutoNate.Web/wwwroot` (the backend suite needs it too — the post-login redirect is served by `MapFallbackToFile`).
