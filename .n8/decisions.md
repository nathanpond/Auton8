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
- **Decision:** Filed all 15 concerns the owner approved (archived-112–archived-126): 4 bugs, 4 cleanup/perf, 4 doc/config drift, 3 design spikes (`spike` + `needs-triage`, no `sev:`). Each was re-verified by the orchestrator before the question was put (the rebuild-400 claim by the mapper's own throw-away xunit probe; NUL bytes by `perl`; the rest by targeted greps and reading the cited lines).
  **Why:** The user chose to file every candidate, including design spikes, so `/n8-roadmap` sees existing debt alongside features.
- **Decision:** Rejected the mapper's "audit outbox bypassed by default" claim (`AuditOutboxOptions.Enabled` defaults to `true`) and left two architecture-mapper observations unfiled as unverified: "EntityTypeDefinition.Actions is advisory at grant-creation time" and "add-page-context-provider's pageKey-mismatch 400 no longer exists" (`AgentSession.cs:880` still handles a page-key mismatch).
- **Note:** Enabling Dependabot security updates + version updates produced 22 PRs (archived-5, archived-6, archived-94–archived-111) within a day, several of them majors (TypeScript 5→7, mantine-datatable 8→9) and one implausible version (`pyodide 0.26.4 → 314.0.6`) that must be checked against the npm registry before anyone merges it. Consider tightening `dependabot.yml` (ignore majors, `open-pull-requests-limit`) in M0.

## Ad-hoc — 2026-08-31

- **Change:** Node.js runtime standardised on 24 (Active LTS) — `.nvmrc`, `engines.node` in all four `package.json`s, both sidecar images on `node:24-alpine`; `isolated-vm` 5 → 7 in the executor (5 cannot compile against Node 24's C++20 V8 headers), install-script approval (`allowScripts`) added for npm ≥ 11.19, executor lockfile added.
  **Why:** Nothing pinned Node; images were on 22 (Maintenance) while dev ran 24. Node 26 stays Current until October 2026 — revisit then (Dependabot now tracks the base images).
  **Affects:** archived-102 superseded, archived-105/archived-101 closed as types-track-runtime, archived-39 (executor lockfile) resolved, archived-114 (executor not in compose) still open. Issue archived-139.

## Ad-hoc — 2026-09-01

- **Change:** CI (`.github/workflows/ci.yml`, archived-79) runs 155 of 163 E2E specs. Seven that need services the runner does not host are excluded by trait — `RequiresService=Flowable` (workflow execution + studio) and `RequiresService=Dapr` (the bus-event log). The backend suite runs in full (1645) and the SPA gates run in full.
  **Why:** Flowable is a Spring Boot service built from source and Dapr is a second sidecar; standing both up in GitHub Actions is its own piece of work, and a permanently-red job is a gate people learn to ignore. Owner reviewed the trade-off and accepted it, with the full suite to run in a separate environment where the whole compose stack can be stood up.
  **Affects:** archived-79 is closed against the scoped job, not full-stack CI. Anyone re-enabling those specs in this workflow needs the services first — the exclusion is deliberate, not an oversight. The traits are the contract: a new Flowable-dependent spec is excluded automatically rather than turning the job red. If the separate full-E2E environment is built, it should run `dotnet test tests/AutoNate.E2E.Tests` with no filter (163 specs today).
- **Note:** Three environment dependencies the E2E suite had never had to state were discovered by running it on a clean machine: the Hocuspocus sidecar on :1234 (without it ConsoleErrorGuard fails every editor spec on `ERR_CONNECTION_REFUSED`), a built `plugins/HelloPlugin/dist/HelloPlugin.zip`, and the SPA bundle in `AutoNate.Web/wwwroot` (the backend suite needs it too — the post-login redirect is served by `MapFallbackToFile`).

- **Change:** No user is seeded any more. `infra/postgres/init/02-create-autonate-app-schema.sql` used to `INSERT` an `admin` account with its `password_hash` **and** `password_salt` committed to this repository, ungated by environment; with `Authorization:AssignSuperAdminToAllExistingUsers` defaulting true, every install that ran the script came up with a super-admin whose password was public. The first administrator is now created at startup from `Bootstrap__AdminUsername` / `Bootstrap__AdminPassword` (`BootstrapAdminOptions`, `DatabaseSchemaInitializer.EnsureBootstrapAdminAsync`) only while `local_users` is empty and only when both are supplied; unset, it creates nothing and logs. That account grants itself SuperAdmin, so `AssignSuperAdminToAllExistingUsers` is no longer load-bearing and now ships **false** in both `appsettings.json` and `appsettings.Development.json` — turning it on promotes the entire existing user table at once, which is a migration aid, not first-run setup.
  **Why:** Blocker for making the repository public: publishing the repo publishes the credential. Removing the seed alone would have made a clean database unloginable (no registration page, no setup wizard, `POST /api/users` requires auth), so the removal and the bootstrap are one change. Test credentials moved into test code — `PostgresTestDatabase.CreateAsync(seedLocalAdmin: true)` seeds the row for the many suites that talk to the database with no host, and hashes it at runtime rather than storing a hash; `AutoNateWebApplicationFactory` opts out so the app's own bootstrap runs, and pins `Bootstrap:GrantSuperAdmin=false` because ~20 enforcement suites use that principal as their *limited* user.
  **Affects:** Any deployment doc or issue that assumes `admin`/`admin` exists. Existing installs are untouched (the bootstrap skips a non-empty `local_users`) — but their `admin` password is public and must be changed. `docs/DEVELOPMENT.md#first-administrator` and the `docs/DEPLOYMENT.md` checklist are the reference.

- **Change:** 0.1 release-readiness pass ahead of going public: Apache-2.0 `LICENSE`, `SECURITY.md` (private vulnerability reporting), `CONTRIBUTING.md`; README rewritten as a landing page with the runbook split into `docs/DEVELOPMENT.md` and `docs/DEPLOYMENT.md`; version stamped 0.1.0 across `Directory.Build.props` and the SPA; every published port in `infra/docker-compose.yml` bound to `127.0.0.1` (finishing the sweep NATS started); credential-shaped patterns added to `.gitignore`; the Form Mappings "coming soon" stub deleted (same defect class as archived-42) with its seeded menu row and page template disabled and a one-shot `retire_form_mappings_stub_v1` migration for existing installs; the Home page's four fake StatCards removed — three of them duplicated the quick links directly below, and the fourth read "THEME STATUS / Mantine".
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
- **Note:** The first run of `docs/plans/2026-09-01-history-rewrite-runbook.sh` correctly **refused**. It clones the *local* repo and compares against local `master`, which was stale at `4a108d96` because archived-194 had been merged on GitHub and never pulled — and the clone's HEAD was on the feature branch, so the content check saw 70 files of difference and stopped before pushing. The guard did its job; the script's flaw is the source it clones. Fixed in the runbook: clone the remote, and compare against the same ref being rewritten.
- **Change:** `.n8/config.yml` — `repo: nathanpond/Auton8`, `visibility: public`, `wiki: enabled`, and `security_findings: issues → advisories`.
  **Why:** The old rationale ("private repo → issues are already maintainer-only") expired at the flip. An open `sev:high` issue on a public repo advertises an exploit against running deployments. The 26 closed security issues predate the flip and were every one of them closed as *fixed*, with zero open, so the existing register stays public as a record of diligence.

- **Change:** The repository was made public and **reverted to private within minutes**. It stays private until GitHub purges the pre-rewrite objects.
  **Why:** The rewrite was clean on every branch and tag — 130 MiB → 8.63 MiB, zero ColorAdmin objects, `master^{tree}` unchanged — but GitHub keeps a read-only `refs/pull/<n>/head` for every pull request ever opened. Those refs are server-managed: `filter-repo` cannot rewrite them and they cannot be deleted. After the force-push they still pointed at the original commits, so **all 21,922 ColorAdmin blobs remained fetchable** by anyone who could read the repo, through any of **73** pull refs — verified directly with `git fetch origin 'refs/pull/194/head'`. That is exactly the redistribution the rewrite existed to prevent, so the flip was reverted immediately. Exposure window was a few minutes at 0 forks / 0 stars.
  **Affects:** **Going public is blocked** until this is resolved. The rewrite itself was not wasted — branches and tags are clean, and it is a precondition for any fix. Two ways forward: (a) a GitHub Support request to garbage-collect unreachable objects and stale pull refs after a history rewrite, which preserves issues, PRs and their numbers — the n8SDLC register depends on those numbers, since every `Closes #N` and milestone reference points at them; or (b) a fresh repository containing only the rewritten history, which is guaranteed clean but renumbers every issue and loses PR history. (a) is much cheaper and should be tried first. Re-verify with the pull-ref check now documented in `docs/plans/2026-09-01-history-rewrite-runbook.sh` before flipping again.
- **Note:** Everything else in Phase 4 is done and survives the revert: repo renamed to `Auton8`, Dependabot alerts + security updates, secret scanning + push protection, private vulnerability reporting, CodeQL default setup (languages auto-detected: actions, csharp, java-kotlin, javascript, javascript-typescript, typescript), the `master-pr-required` ruleset, fork-PR approval set to `all_external_contributors`, wiki enabled, and the `v0.1.0` tag and release. The wiki's git repo still needs one page created in the web UI before it can be cloned — there is no API for it.

- **Change:** Migrated to a **fresh repository** rather than waiting on GitHub Support. `nathanpond/Auton8` was renamed to `nathanpond/Auton8-archive` (private, permanently — its pull refs still carry the paid theme) and a new `nathanpond/Auton8` created from the rewritten history alone. Public since 2026-09-02.
  **Why:** A new repository has no `refs/pull/*`, so it is clean by construction and needs nothing from Support. Verified before flipping: 7.12 MiB, and **zero ColorAdmin and zero scss objects reachable from every ref the server offers** — checked with `git fetch '+refs/*:refs/allrefs/*'`, not just branches, and re-checked after Dependabot immediately opened 9 PRs (20 pull refs), since those are the exact ref class that defeated the first attempt. They branch from clean master, so they are clean.
  **Affects:** **74 pull requests are gone** — GitHub can transfer issues but not PRs. They were exported first, with all 85 comments, to `docs/history/`; that is where the engineering rationale for the whole pre-migration history now lives, and it is public rather than locked in a private repo's PR tab. The 20 open issues were transferred (labels and bodies intact) and **renumbered** — the 101 closed ones stay in the archive. Numbering is the sharp edge: transferred `archived-126` became `archived-26`, so a pre-migration `Closes archived-85` in a commit message can now resolve to a *different, real* issue. Flagged in `docs/history/README.md` and `CONTRIBUTING.md`.
- **Note:** Settings re-applied and verified on the new repo: Apache-2.0 detected, secret scanning + push protection, private vulnerability reporting, Dependabot alerts + security updates, fork-PR approval `all_external_contributors`, `master-pr-required` ruleset, wiki enabled, `v0.1.0` tag and release. CodeQL default setup is configured and detecting languages. The wiki's git repo still needs one page created in the web UI before it can be cloned — there is no API for it. Dependabot opened 9 PRs on arrival; one CI run already failed on a `@eigenpal/docx-editor-*` major bump, which is ordinary dependency triage, not migration fallout.

- **Change:** The codebase map moved from `docs/codebase/` into the GitHub wiki, and the repository copy was deleted. The wiki also gained a real `Home` and a `_Sidebar`.
  **Why:** `.n8/config.yml` now says `wiki: enabled`, which is where `/n8-map` and `/n8-wiki` write. Leaving `docs/codebase/` in place would have produced a second copy that drifts from the first the next time either skill runs — and the pages had already drifted once, asserting the repo had no CI months after `.github/workflows/ci.yml` landed. One copy, in the place the tooling targets.
  **Affects:** `README.md` and `CONTRIBUTING.md` point at the wiki. Cross-references between map pages are wiki links now. Historical mentions of `docs/codebase/` in `.n8/decisions.md`, `docs/plans/` and `docs/history/` were deliberately left alone — they are records of what was true then.

## /n8-roadmap — 2026-09-02

- **Decision:** Nine milestones — M0 Infrastructure, M1 CI/quality, M2 Identity + front-end, M3 Full BPMN, M4 Trusted Data Repository, M5 Documents + RAG, M6 Assistant platform, M7 v1.0 audit, then a Post-1.0 milestone for deployment targets. Twelve epics (#36–#47).
  **Why:** Auth is a gating enterprise requirement so it comes early; the two "finish the capability" epics (data, documents) each open with a capability matrix, because *finish it* is not a testable statement until someone writes down what is missing. The Post-1.0 milestone sits after the audit deliberately — it is beyond the v1.0 line, recorded now only so v1.0 decisions do not foreclose it.
- **Decision:** "TDR" is the owner's umbrella term — **Trusted Data Repository** — for how data is stored and accessed. Not a feature and not a name in the codebase, so the milestone is named for it but nothing gets renamed.
  **Why:** The acronym appears nowhere in the tree; planning it as a feature would have invented scope.
- **Decision:** Full E2E in CI (standing up Flowable and Dapr) and a deployment pipeline are both **out of v1.0 scope**. Offered explicitly during roadmap Q&A and not selected. The chosen CI bar is quality gates, faster feedback, and the tooling a public repo unlocks — Semgrep alongside CodeQL, and **FsCheck** property-based testing aimed at the AQL parser and the authorization selector grammar.
  **Why:** Recorded because the earlier `/n8-audit` ad-hoc entry says full E2E "should run in a separate environment", and a future reader could reasonably assume CI was meant to close that gap. It is not, for v1.0.
- **Decision:** Deployment is Docker Compose only for v1.0, dev environment only — no staging, no production hosting. Kubernetes and cloud PaaS are post-1.0 epics.
  **Why:** Owner: "eventually anywhere. Keep docker compose for now." v1.0 ships a release artifact others run.
- **Decision:** Four project invariants recorded in `CLAUDE.md` and confirmed by the owner: no credential ships in the repo; the plugin ABI's assembly identity is pinned; every endpoint carries an explicit authorization decision; the do-not-rename identifiers stay put. Three are already test-enforced; the fourth is honor-system and gets a guard planned into M1.
  **Why:** Every one of them is a constraint this project has already breached or nearly breached — the credential shipped, the ABI version broke plugin loading during the 0.1 work, and the authorization gates exist because a route without one answered 403 to its own owner.
- **Decision:** All 20 pre-existing open issues assigned to milestones rather than left in a backlog — the four `sev:high` hot paths pulled into the milestone whose surface they sit under, the three spikes placed where their answer is needed (schema-init → M0, executions → M3, dependency surface → Post-1.0), and the low-severity remainder swept into M7.
  **Why:** The `/n8-map` pass filed this debt so it would compete with features for milestones. Leaving it unassigned would have made it invisible to planning.
- **Note:** `context7` is `installed` in config but its tools were not available this session, so no library choices were verified against current documentation. The consequential one is the .NET SAML library — left explicitly open for `/n8-plan M2`.

## /n8-plan M0 — 2026-09-02

- **Decision:** The container path is added *alongside* the host-run dev loop, not instead of it. `make app` stays the inner loop; a new `app` compose profile runs the whole product with Docker as the only prerequisite (#55, #57).
  **Why:** The two audiences have opposite needs — a developer wants fast rebuilds and a Rider debugger attached to the process, a user wants one command. Collapsing to the container path alone would have forced every developer through an image rebuild; collapsing to the host path alone leaves v1.0 requiring the .NET SDK, Node and the Dapr CLI on the target machine, which is the barrier M0 exists to remove.
  **Issue:** #36
- **Decision:** A release publishes multi-architecture container images to GHCR on a `v*` tag (linux/amd64 + linux/arm64), with a digest-pinned compose file and quickstart attached as release assets (#56, #58). Not self-contained binaries.
  **Why:** The stack is nine services; a binary still leaves the consumer assembling the rest. One compose file pinned by digest is the whole install. arm64 is included so the owner runs locally what is actually published rather than testing under emulation.
  **Affects:** This narrowly extends the roadmap's recorded "no deployment pipeline in v1.0" decision. Publishing a release artifact is not deploying anything, and it was chosen deliberately — but a future reader comparing the two entries should read them together. `.n8/config.yml:ci.release` ("not yet automated") is now stale and is corrected by #56.
  **Issue:** #56
- **Decision:** "Verifiable — same input, same output" means pinned inputs plus SLSA build provenance, not byte-identical rebuilds. Digest-pinned base images, locked `dotnet restore`, `npm ci` only, and `actions/attest-build-provenance` on every published image (#52, #56).
  **Why:** Byte-reproducible .NET and npm builds are a research project that becomes a recurring source of red builds. Provenance answers the question a consumer actually has — did this image come from that repository at that commit — and pinning answers the maintainer's, which is whether a rebuild resolves the same graph.
  **Issue:** #52
- **Decision:** Spike #24 resolved without a prototype: advisory lock plus a `schema_versions` ledger, keeping the existing idempotent SQL. Not EF Core migrations.
  **Why:** The EF port is 4,127 lines of hand-written DDL plus ~20 one-shot data migrations, plus the problem of adopting existing 0.1 installs into a migration history — a milestone of its own, spent on a mechanism the project does not need yet. `dotnet-ef` stays pinned; the ledger makes a later move easier rather than harder. Follow-ups #51, #53, #54, #60.
  **Issue:** #24
- **Decision:** v1.0 makes **no** upgrade promise — a 1.0 install is a fresh database, and 0.1 → 1.0 is unsupported. Clean upgrade paths begin after 1.0. The `schema_versions` ledger ships in 1.0 anyway.
  **Why:** Owner's call. The ledger is what makes post-1.0 upgrades possible at all — 1.1 can only know what a 1.0 database holds if 1.0 recorded it. Shipping the mechanism without promising the outcome is the cheap half.
  **Affects:** The v0.1.0 release notes tell people how to upgrade an existing install; that guidance does not survive into 1.0. #59 states the reversal in the wiki, `docs/DEPLOYMENT.md` and the release notes.
  **Issue:** #53, #59
- **Decision:** A fifth project invariant added to CLAUDE.md — every published port in a shipped compose file binds to loopback, with documented per-port exceptions allowed.
  **Why:** All ports are compliant today and nothing enforces it; the next service added will be written with a bare `"8080:8080"` because that is what every compose example looks like. The exception clause exists because a future Keycloak instance may legitimately need to sit outside the compose network to mimic a real IdP configuration — the requirement is that such a choice is written down next to the port, not that it is forbidden. Guard: #50.
  **Issue:** #50
- **Note:** #50's guard test discovers compose files by glob, and #52's floating-tag guard does the same. Both assert their discovery is non-empty — a glob matching nothing would make every other assertion in them vacuously true, which is the failure mode that makes an infrastructure guard worse than no guard.
- **Decision:** The two project skills suggested by planning — `add-schema-change` and `cut-a-release` — are filed as M0 stories (#63, #64) blocked by the work they describe, rather than written now.
  **Why:** Neither subject exists yet in the form the skill would document. #53 and #54 change `DatabaseSchemaInitializer`'s ledger step names and move the base schema to an embedded resource, which are the two things a schema-change skill turns on; the release process does not exist at all until #56 and #58. `/n8-skill`'s own standing rule is to fix a skill in the same commit as the change that invalidates it — a skill written ahead of the change is a drift bug filed in advance, which is exactly what #1, #2 and #3 are. Epic #36 gained an acceptance criterion so the two stories have an owner rather than being orphans.
  **Affects:** A third candidate — a skill for adding a service to the compose stack — was considered and not filed. After M0 a new service must be registered in the compose file, the digest pins, the release template's parity check, `.env.example` and the preflight port list, which is the shape of thing that gets missed; it is worth revisiting at `/n8-plan M1` once those five places actually exist.
  **Issue:** #63, #64

## /n8-plan M1 — 2026-09-02

- **Decision:** Coverage is a whole-repo line threshold, set from a measured run and blocking a PR that drops below it — not diff coverage, not report-only.
  **Why:** It is the ratchet pattern this project already uses twice (`--max-warnings=110`, the a11y directory list): set from a measurement so it lands green, and it only moves one way. The known weakness is recorded in #71 rather than left to be rediscovered — on a 1,650-test suite a small untested file may not move the aggregate. Diff coverage is the tighter instrument and the natural next ratchet turn.
  **Issue:** #71
- **Decision:** Semgrep arrives advisory — SARIF into code scanning, no PR blocked — with triage as its own story.
  **Why:** A new scanner's first pass on a codebase this size is mostly noise, and a noisy blocking gate gets disabled within a week. The epic's AC asks for findings triaged rather than merely enabled, so #70 exists to make that real; #66 records the baseline count so #70's size is known before it starts.
  **Issue:** #66, #70
- **Decision:** Sharding (#67) is sequenced before coverage (#71).
  **Why:** Coverage has to merge across shards. Building the plumbing first means building it twice, the second time to un-build the first.
- **Decision:** The a11y ratchet widens only to directories whose violations are lint-level fixes. The editor surfaces — notes, documents, assistant, shell, workflow studio — are out of scope and captured as #75.
  **Why:** Those violations are `click-events-have-key-events` and `no-static-element-interactions` on pointer-driven editing UI. Fixing them means designing keyboard equivalents, which is interaction design with UX consequences, not a lint pass. Owner's call, taken explicitly.
  **Issue:** #68, #75
- **Change:** #5 (`/api/auth/check` N+1) moved from M1 to M2.
  **Why:** It is a performance fix on the authorization surface, not CI work; #9 and #10 are the same class of finding and were already in M2. The roadmap sweep put it in M1 and nothing depended on that.
- **Change:** #15's blocked E2E journeys were re-decomposed. Two of its facts were stale — 14 blocked rows, not 19, and `RecordsAdvancedTests.cs` now exists, so two spec files are missing rather than three. More importantly the proposed fix (create the missing spec files) was aimed at the wrong axis: reading the blocker column, the rows cluster by **fixture capability**, not by file, and one hook unblocks journeys across several files. Split into six capability stories (#76–#81), each filed against the milestone that owns the surface, plus three product gaps (#82–#84) that are not test work at all. #15 stays as the umbrella.
  **Why:** A fixture hook for the BPMN canvas is cheapest to build while someone is already in the BPMN canvas, and M3 is a full-BPMN milestone that will be there anyway. Building all six from a CI milestone means reaching into four subsystems nobody is otherwise touching. And filing "test the share-revoke control" as a test story, when no revoke control exists, would have produced a permanently-blocked issue rather than a missing feature.
  **Affects:** M3, M4, M5 and M6 each gained one or two `area:tests` stories before being planned. `/n8-plan` treats a milestone with stories assigned as planned, so those milestones must still be planned explicitly — `/n8-plan *` would skip them. They already carried pre-existing bug and performance issues from the roadmap sweep, so this is not a new condition, but it is worth stating.
  **Issue:** #15

## /n8-plan M2 — 2026-09-02

- **Decision:** SAML library must be open-source and licence-compatible with Apache-2.0 redistribution. Commercial options (ComponentSpace) are out. The choice itself is a time-boxed spike (#86), not a planning decision.
  **Why:** Owner's constraint. Auton8 is redistributed and self-hosted by third parties, so a copyleft or "free for non-commercial use" licence is a problem for its users, not just for this repository — and that turns on actual licence text, which documentation does not settle. context7 was available this session and confirmed ITfoxtec.Identity.Saml2 is maintained, documented and endpoint-driven (a good fit, since this app already owns its cookie sign-in and would not use the library's `CreateSessionAsync`). Sustainsys.Saml2 is the other open-source candidate. The spike verifies licences and SP-side coverage before anything is committed.
  **Issue:** #86, #93
- **Decision:** A first-time federated sign-in creates the account with **no roles**. Not create-and-map, not refuse, not per-provider configurable.
  **Why:** It solves the actual pain — there is no self-registration, so every user is hand-created — without federation becoming a second bulk-grant path. `AssignSuperAdminToAllExistingUsers` is the precedent: this project has already shipped one accidental bulk grant, and a claim-mapping misconfiguration granting privilege on first contact is the same defect wearing different clothes.
  **Issue:** #90
- **Decision:** IdP groups map to Auton8 groups through an explicit admin-configured table. Never by name matching.
  **Why:** Name matching means a group created in the IdP grants access here with nobody here deciding, and renaming a group in either system silently changes who can do what. `role_assignments` already keys on `(PrincipalKind, PrincipalId)`, so group→role needs no new concept; the mapping is the whole gate, and an unmapped group grants nothing. Reconciliation runs on every sign-in, not just the first, or revocation never propagates — and IdP-derived membership is marked so it can never remove what an administrator granted by hand.
  **Issue:** #92
- **Decision:** Local, OIDC and SAML are each independently enabled in any combination; the login page shows exactly what is enabled. Because that makes total lockout reachable for the first time, two guards ship with it: local sign-in cannot be disabled until at least one federated provider is enabled *and* has completed a successful sign-in, and an `AUTONATE_*` break-glass environment variable forces local sign-in on regardless of stored configuration, logged loudly and documented in `docs/DEPLOYMENT.md`.
  **Why:** The owner asked for full combinability; epic #38's AC forbids a misconfigured provider locking every administrator out. Both guards were proposed and approved rather than picking one — the first prevents the common mistake, the second recovers from the uncommon one, and neither covers the other's case.
  **Issue:** #94
- **Change:** #87 introduces a **new do-not-rename identifier** — the DataProtection purpose for identity-provider secrets. It gets its own purpose rather than reusing `AutoNate.ExternalConnections.v1`, because a purpose string is part of key derivation and sharing one across unrelated secret classes means a rotation forced by one requires re-entering the other's secrets.
  **Affects:** CLAUDE.md's Naming section and the guard test in #65 both need it. Flagged on #65 and prescribed in #96. The failure mode to avoid is an identifier that reaches the prose list but not the guard — it then reads as protected and is not.
  **Issue:** #87, #96, #65
- **Note:** Triage sweep of the four captures from M1 planning: #82 (notes share revoke) → M5; #75 (editor keyboard a11y) and #84 (note-tab reorder) → M7, folded together because fixing the keyboard path supplies the test handles as a side effect; #83 (form editor Save disabled) **deliberately held** with `needs-triage` intact, because no milestone clearly owns forms yet and placing it now would be a guess — the M3 and M4 sweeps will both see it.
