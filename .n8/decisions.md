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

- **Change:** No user is seeded any more. `infra/postgres/init/02-create-autonate-app-schema.sql` used to `INSERT` an `admin` account with its `password_hash` **and** `password_salt` committed to this repository, ungated by environment; with `Authorization:AssignSuperAdminToAllExistingUsers` defaulting true, every install that ran the script came up with a super-admin whose password was public. The first administrator is now created at startup from `Bootstrap__AdminUsername` / `Bootstrap__AdminPassword` (`BootstrapAdminOptions`, `DatabaseSchemaInitializer.EnsureBootstrapAdminAsync`) only while `local_users` is empty and only when both are supplied; unset, it creates nothing and logs. That account grants itself SuperAdmin, so `AssignSuperAdminToAllExistingUsers` is no longer load-bearing and now ships **false** in both `appsettings.json` and `appsettings.Development.json` — turning it on promotes the entire existing user table at once, which is a migration aid, not first-run setup.
  **Why:** Blocker for making the repository public: publishing the repo publishes the credential. Removing the seed alone would have made a clean database unloginable (no registration page, no setup wizard, `POST /api/users` requires auth), so the removal and the bootstrap are one change. Test credentials moved into test code — `PostgresTestDatabase.CreateAsync(seedLocalAdmin: true)` seeds the row for the many suites that talk to the database with no host, and hashes it at runtime rather than storing a hash; `AutoNateWebApplicationFactory` opts out so the app's own bootstrap runs, and pins `Bootstrap:GrantSuperAdmin=false` because ~20 enforcement suites use that principal as their *limited* user.
  **Affects:** Any deployment doc or issue that assumes `admin`/`admin` exists. Existing installs are untouched (the bootstrap skips a non-empty `local_users`) — but their `admin` password is public and must be changed. `docs/DEVELOPMENT.md#first-administrator` and the `docs/DEPLOYMENT.md` checklist are the reference.

- **Change:** 0.1 release-readiness pass ahead of going public: Apache-2.0 `LICENSE`, `SECURITY.md` (private vulnerability reporting), `CONTRIBUTING.md`; README rewritten as a landing page with the runbook split into `docs/DEVELOPMENT.md` and `docs/DEPLOYMENT.md`; version stamped 0.1.0 across `Directory.Build.props` and the SPA; every published port in `infra/docker-compose.yml` bound to `127.0.0.1` (finishing the sweep NATS started); credential-shaped patterns added to `.gitignore`; the Form Mappings "coming soon" stub deleted (same defect class as #42) with its seeded menu row and page template disabled and a one-shot `retire_form_mappings_stub_v1` migration for existing installs; the Home page's four fake StatCards removed — three of them duplicated the quick links directly below, and the fourth read "THEME STATUS / Mantine".
  **Why:** The repository is about to be world-readable and a stranger's first impression is the README and the landing page. Also corrected two documented-but-nonexistent things found on the way: `AUTONATE_DATA_ROOT` (the real key is `Data__Root`) and five statements across `docs/codebase/` asserting the repo has no CI. Those map files now carry a provenance banner rather than being regenerated.
  **Affects:** `docs/codebase/*` remain a snapshot, not a current defect list. Two E2E specs asserted the old `Automation Dashboard` heading and now assert `Home`.

- **Note:** The plan for the public flip is `docs/plans/2026-09-01-auton8-0.1-public-release.md`. Two verified blockers remain before the repository can be made public: the paid ColorAdmin theme is still reachable in git history (21,921 blobs under `src/AutoNate.Web/ColorAdmin/`, plus 402 objects under `src/AutoNate.Spa/src/scss/` that the first path list missed), and 26 closed `security`-labelled issues become world-readable at the flip and need triage first. `.n8/config.yml:security_findings` must move from `issues` to `advisories` at the same time — its stated rationale ("private repo → issues are already maintainer-only") expires on the flip.

- **Change:** `AutoNate.Plugin.Abstractions` now pins `<AssemblyVersion>1.0.0.0</AssemblyVersion>`, deliberately not following the product version added to the repo-root `Directory.Build.props`.
  **Why:** Setting `<Version>0.1.0</Version>` at the root swept the plugin ABI along with it and **broke every already-built third-party plugin**. A plugin compiles against this assembly and ships without it (`Private=false`), so the host's copy defines type identity across the AssemblyLoadContext boundary; changing the version changes the identity the plugin's baked-in reference asks for. Caught by `AdminOperationsTests.Plugins_UploadEnableDisableUpdateAndDelete`, and worth recording because the symptom is misleading — enable returns 400 with `Type 'X' not found in 'X.dll'`, which reads as a badly-built plugin rather than a binding failure. 1.0.0.0 is the SDK default this assembly always had, so the pin restores the exact prior identity. Verified by rebuilding the sample plugin from pre-change sources and loading it against the new host; red-checked by removing the pin.
  **Affects:** CLAUDE.md already lists the plugin ABI among the identifiers that must not be renamed — its *version* is the same invariant, now guarded by `PluginAbiVersionTests`. Anyone bumping the product version does not need to think about it; anyone changing `AssemblyVersion` must do so as a deliberate breaking change.

## Ad-hoc — 2026-09-02

- **Change:** History rewritten and force-pushed; repository renamed to `nathanpond/Auton8`. Stripped `src/AutoNate.Web/ColorAdmin/`, `src/AutoNate.Spa/src/scss/`, `src/AutoNate.Web/wwwroot/`, `.playwright-mcp/`, `.idea/`, `tmpflowable/` and root-level dev screenshots. Remote pack **130 MiB → 8.63 MiB**; every commit SHA changed.
  **Why:** The paid ThemeForest theme was reachable in history and publishing it would have redistributed a commercial product under a licence that forbids it. Verified content-neutral the only way that really counts: `master^{tree}` is **`bd9b6180667a5566be897c7b1edb43a6e02e359e` before and after**, so history changed and the working tree did not — and that is the same tree CI had already validated green on all three jobs, which is why the suites were not re-run from the rewritten clone.
  **Affects:** Commit SHAs cited anywhere (`.n8/decisions.md`'s `01f0f174`, `a66c3069`, issue comments, the `docs/codebase/*` provenance banners) no longer resolve. The `pre-rewrite-backup` tag was deleted — it had itself been rewritten by the filter, so it pointed into the *new* history and was no longer a backup. The real backup is a verified bundle of the pre-rewrite remote at `~/auton8-pre-rewrite-backup-20260901.bundle` ("records a complete history"), which is the only way back and should be kept until confidence is high.
- **Note:** The first run of `docs/plans/2026-09-01-history-rewrite-runbook.sh` correctly **refused**. It clones the *local* repo and compares against local `master`, which was stale at `4a108d96` because #194 had been merged on GitHub and never pulled — and the clone's HEAD was on the feature branch, so the content check saw 70 files of difference and stopped before pushing. The guard did its job; the script's flaw is the source it clones. Fixed in the runbook: clone the remote, and compare against the same ref being rewritten.
- **Change:** `.n8/config.yml` — `repo: nathanpond/Auton8`, `visibility: public`, `wiki: enabled`, and `security_findings: issues → advisories`.
  **Why:** The old rationale ("private repo → issues are already maintainer-only") expired at the flip. An open `sev:high` issue on a public repo advertises an exploit against running deployments. The 26 closed security issues predate the flip and were every one of them closed as *fixed*, with zero open, so the existing register stays public as a record of diligence.
