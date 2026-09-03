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

## Ad-hoc — 2026-09-02

- **Change:** A real Keycloak enters the local stack behind a compose profile (#98), seeded with OIDC and SAML clients, users and groups; interop specs run against it (#99) and are excluded from CI by a `RequiresService=Keycloak` trait, exactly as the Flowable and Dapr specs are.
  **Why:** The owner asked whether a local Keycloak story existed. It did not, and it should have — #90 and #93 as planned test only against stubs written by the same agent implementing the flows, which proves the implementation matches its author's reading of the specifications and nothing more. For SAML in particular that is where real integrations fail: signature canonicalization, `NameID` formats and attribute encodings are all places where a naive implementation passes its own tests and is rejected by production software. The stubs are kept and keep the rejection matrix — minting unsigned, replayed or wrong-audience assertions is easy against a stub and impractical against a real IdP — so the two have different jobs rather than one replacing the other.
  **Affects:** #98 is the first thing to use invariant 5's documented-exception mechanism, if Keycloak's issuer-URL constraint forces a non-loopback binding. That is worth having happen: an exception mechanism nobody has exercised is one nobody knows works. #98 also interacts with M0's #57 — once Auton8 runs as a container, it reaches Keycloak over the compose network while the browser reaches it through the host, and the issuer URL must resolve identically for both.
  **Issue:** #98, #99
- **Decision:** Auton8 does **not** bundle an identity provider in v1.0. Captured as #100 against the Post-1.0 milestone rather than ruled out.
  **Why:** Owner's call. Bundling changes the promise from "Auton8 federates to your identity provider" to "Auton8 ships one", which means owning Keycloak's upgrades, its CVE stream and its admin console as part of the support surface — and adding a tenth container to a release M0 is working to make small and reproducible. The gap it closes is already covered, less well, by local accounts. #100 records a middle option worth weighing later: publish a tested compose fragment and realm export rather than shipping the container.
  **Issue:** #100
- **Note:** The gap was found by the owner, not by planning. The only prior Keycloak mentions in this repository are `CLAUDE.md`'s invariant 5 and the M0 entry in this log — both of which exist because the owner raised the same idea when approving that invariant. The connection back to the identity milestone was not made during `/n8-plan M2`.

## Ad-hoc — 2026-09-02 (cross-milestone review after the Keycloak addition)

- **Change:** #96 and #87 corrected — an identity provider's host is governed by an **allowlist** (`IProviderBaseUrlPolicy`), not by the address-classifying `IOutboundUrlGuard` that #96 originally prescribed. #87 gains an acceptance criterion for a Development-only plain-http accommodation on allowlisted hosts.
  **Why:** Two defects in the original guidance. First, it picked the weaker guard against the codebase's own documented preference — `OutboundUrlGuard`'s remarks say to prefer an allowlist where the legitimate hosts are known, because an allowlist cannot be defeated by a DNS answer and does not depend on classifying an address correctly; it also documents that it is not proof against DNS rebinding. An identity provider's host is typed in by an administrator, so it is exactly the known-hosts case. Second, and concretely: `OutboundUrlGuard` refuses loopback and RFC1918 *unconditionally* with no Development exemption, and `ProviderBaseUrlPolicy` requires https — so the seeded Keycloak in #98 at `http://127.0.0.1:8180` would have been refused by both, and #98's final acceptance criterion could never pass. The symptom would have looked like a Keycloak misconfiguration rather than a guard choice.
  **Affects:** #87, #96, #98. Flagged on #98 so whoever picks it up recognises the blocker.
- **Change:** #49's preflight port check now derives the port list from the compose file instead of hard-coding it.
  **Why:** The hard-coded list stops covering a service the moment one is added, and #98 is the first to add one. A check that silently omits a port is worse than no check, because it reports a clean machine.
- **Note:** The rest of M0 and M1 was reviewed against the M2 plan and needs no change. #50 (loopback) and #52 (pinned images) discover their inputs by glob, so both cover a new Keycloak service automatically — the glob was chosen for exactly this and it paid off within one milestone. #58's release-compose parity test already excludes profile-only services, and Keycloak is profile-only. #67 explicitly leaves the E2E job untouched, so #99's `RequiresService=Keycloak` filter change does not collide with the backend sharding work.

## /n8-plan M3 — 2026-09-02

- **Decision:** "Full BPMN support" means every element the studio can draw executes. Where the engine genuinely has no implementation, the studio stops drawing it.
  **Why:** The owner chose "implement everything the studio can draw" over curating a supported subset. That reading is not literally achievable for `bpmn:ComplexGateway` — it is in the BPMN specification and Flowable does not implement it, so "support" there would mean building gateway semantics the engine does not have. Removing it from the palette honours the same promise to an author (nothing you can draw silently does nothing) without pretending. Expected to be a very small set; #103 determines it.
  **Issue:** #40, #103, #107
- **Decision:** Decision tables are in scope for v1.0 — DMN execution plus an in-app authoring surface (#105), rather than marking business rule tasks non-executable.
  **Why:** Owner's call, taken with the cost stated: this is a new capability, not a gap fix. Authoring, storage, versioning and permissions all have to be built, and the engine may need another component.
  **Affects:** If DMN requires a separate Flowable component rather than a configuration flag, the **release** stack gains a service — unlike Keycloak (#98), which is development-only. M0's #58 (release compose), #52 (image pinning) and #49 (preflight ports) would all be affected. #106's first acceptance criterion settles this before anything is built on top, and requires cross-references to be left on those issues if the answer is "separate component".
  **Issue:** #105, #106
- **Decision:** Spike #25 resolved: the execution cache is the read model; Flowable stays the system of record and the write target but leaves the read path.
  **Why:** The alternative keeps the list absolutely current but makes #6 harder — a fast permission-filtered page would need a Flowable-side query able to express the authorization selector, and there is none; the selector compiles to SQL against the cache table. Choosing the cache makes the performance fix fall out of the design. The freshness objection is answered by machinery that already exists and is dead code: `FlowableReadThrough` with `ReadThroughFreshness` (30s default) was built for exactly this and is injected nowhere, which is #19. So the poll becomes a bulk warmer rather than the freshness guarantee, and #109 makes the remaining trade visible instead of hiding it.
  **Issue:** #25, #104, #108, #109
- **Decision:** The BPMN implementation work is sliced **by mechanism, not by element** — message correlation (#112), call activity (#113), error and escalation (#114), compensation and transaction (#115).
  **Why:** One mechanism unblocks a whole family. Message start events, message catch and boundary events, receive tasks and send tasks are five palette entries and one missing capability: an addressable way to reach a running instance. Slicing per element would have produced five stories that each half-build the same thing.
  **Affects:** All four are blocked by #103 and each carries an explicit instruction to close unimplemented, with the reason recorded, if the inventory clears its elements. Epic #40 gained the matching "each child implemented or closed with the reason it will not be" criterion so that is a correct outcome rather than an abandoned story.
- **Note:** Two elements were deliberately not given stories. `bpmn:ComplexGateway` is removed by #107 rather than implemented. Conditional event definitions have version-dependent engine support, so #103 establishes the facts before anyone writes a story against them.
- **Note:** M3 was planned in one pass rather than deferring the implementation stories until #103 closes, at the owner's request. The inventory's role is therefore confirmation rather than discovery: the expectations were formed from enumerating the studio palette in `src/AutoNate.Spa/src/lib/bpmn/workflow.js` and checking Flowable's documented constructs via context7. They are expectations, and #103 corrects them.

## /n8-plan M3 (second pass) — 2026-09-02

The owner challenged whether M3 was planned to the bar and was right: the first pass
used **Claude's Discretion** as a place to put decisions that should have been asked
about. That section is for what the user explicitly delegated; it had things in it
the user had never seen — most plainly #113, which said the version-binding choice
"is decided, documented and tested — not that it is one or the other", handing a
product decision to the executor after the equivalent question had been *asked* for
decision tables in #110. Seven decisions taken and applied:

- **Decision:** Messages address a running instance by an **explicit correlation key** configured on the message element, not by record identity or instance id.
  **Why:** Record correlation only covers processes started from a record, so anything else would need a second mechanism anyway; instance-id-only pushes the problem onto callers who know a business identifier and not an instance id. An explicit key is visible in the diagram, so an author can see why a message did or did not arrive.
  **Issue:** #112
- **Decision:** A correlation value matching more than one waiting instance **refuses**, reporting the count, and advances nothing. Broadcast is explicitly not a feature.
  **Why:** A correlation key is meant to be unique among waiting instances. A multi-match means the model or the key is wrong, and delivering to an arbitrary or oldest instance hides that until it causes something worse.
  **Issue:** #112
- **Decision:** Sending a message is **API only** for v1.0 — no UI. An operator who needs to unstick a process uses the existing execution admin controls.
  **Issue:** #112
- **Decision:** Advancing a process from outside it is gated by a **new `EntityKind`**, grantable independently of execution-operator permissions, so an integration account can hold exactly that. Decision tables likewise get their own kind.
  **Why:** Bundling a narrow integration capability with force-complete and bulk-delete would mean over-granting every integration.
  **Issue:** #112, #110
- **Decision:** Version binding is **pinned at deployment** for both call activities and decision tables. Republishing a child process or a table changes only newly deployed parents.
  **Why:** A running process never changes behaviour underneath its owner. Consistent with how #110 versions tables. The cost — propagating a fix in a shared sub-process needs parents redeployed — is documented rather than discovered.
  **Issue:** #113, #111
- **Decision:** A behaviour's exception is catchable by an error boundary event **only when the behaviour declares a BPMN error code**. Undeclared exceptions stay unhandled failures, surfaced and retryable.
  **Why:** Making every exception catchable routes "the database was briefly unreachable" down the "payment declined" branch. That is the hardest class of failure to diagnose, and the opt-in keeps infrastructure failures out of business error paths.
  **Issue:** #114
- **Decision:** No new **mutating** agent skills in M3. The assistant does not gain message-sending or decision-table authoring; that is M6's subject. Read-only exposure via the existing `Lookup*` pattern is optional, and a test asserts no mutating skill was added so the decision cannot be quietly reversed.
  **Why:** M3 is about the capability; the assistant surface is a milestone of its own, and the permissions just designed for these mutations deserve a deliberate confirmation-flow conversation rather than an incidental one.
  **Issue:** #112, #110
- **Note:** Two things were settled as conventions rather than asked: new mutations emit audit events through the existing `add-audit-event` path (#112, #110), and a compensation handler sees the variable values in scope when its compensated activity completed, per the BPMN specification (#115) — with an explicit instruction to document the limitation if the engine does not preserve that snapshot.
- **Note:** The first pass also missed the agent-skill surface entirely across all thirteen M3 issues, despite 30+ skills existing including `OperateWorkflowExecutionsSkill`. Found by the owner's challenge, not by the plan.

## Ad-hoc — 2026-09-02 (M0–M2 challenged after the M3 review)

The owner asked for the same audit against M0–M2 that exposed the M3 gaps. One real
finding, two decisions that were not mine to make, and one milestone that came back
clean.

- **Finding (M2, corrected):** None of #87, #90, #92 or #94 emitted audit events, and all four are privileged mutations on the identity surface. `EventCatalog.cs` **already** carries `auth.login.succeeded`, `auth.login.failed` and `auth.account.locked`, firing from `/account/login` — so M2 as planned would have shipped a second, unaudited login path. Local logins on the record, SSO logins not, with the enterprise path being the one an auditor actually asks about. JIT provisioning would likewise have created user accounts without the event the local creation path emits. Audit acceptance criteria added to all four, including the sign-in reconciliation in #92 (an IdP-driven grant or revocation is an access change) and the break-glass activation in #94 (precisely what an incident review needs to find).
  **Why it was missed:** the same class of omission caught in M3 earlier the same day and not back-propagated. The lesson is that a convention discovered mid-planning has to be swept across already-planned milestones, not only applied forward.
  **Issue:** #87, #90, #92, #94
- **Finding (M2, corrected):** M2 said nothing about the agent-skill surface while M3 explicitly states no new mutating skills. #87 now states it and asserts it with a test, so the two milestones are consistent.
- **Decision:** Published images carry the **exact release version only** — no `latest`, no floating major or minor tag, enforced by a test on the workflow.
  **Why:** A 0.x project makes no upgrade-compatibility promise, so a moving tag lets a routine `docker compose pull` jump an unpinned deployment across a breaking version. It is also what the digest-pinned release compose file in #58 already assumes.
  **Issue:** #56
- **Decision:** The release quickstart shows a command the deployer runs to generate the shared secrets, rather than the container generating them for itself.
  **Why:** The deployer is running in minutes and still chose their own secret. Having the software mint a credential for itself is close enough to the shipped-credential defect this project already had once (invariant 1) to rule out deliberately.
  **Issue:** #58
- **Note:** M1 came back clean. Every Claude's Discretion item across #65–#72 is genuinely builder-level — shard counts, merge tools, sample sizes, which YAML parser. No product decision was hidden there.
- **Note:** M0's remaining Discretion items are builder-level after the two above were lifted out.

## /n8-exec M0 — 2026-09-02

- **Decision:** #50's compose scanner is hand-rolled rather than built on a YAML package.
  **Why:** The rule turns on a *comment* — an exception is valid only when a written reason sits beside the port — and YAML parsers discard comments on load. A parser would have handled the easy half and lost the half that makes the exception mechanism auditable. Listed under the story's Claude's Discretion.
  **Issue:** #50
- **Decision:** #50 discovers compose files by globbing `*.yml`/`*.yaml` and filtering to files with a **top-level** `services:` key.
  **Why:** `.github/workflows/ci.yml` declares `services:` nested under a job. A naive content match would have treated it as a compose file and asserted on GitHub Actions service containers. Asserted in both directions.
  **Issue:** #50
- **Decision:** #50's exception marker attaches either to a whole `ports:` block or to a single entry.
  **Why:** The story specified the block form. Per-entry matters for a service publishing several ports where only one needs exposure, and it was cheap once the block form worked.
  **Issue:** #50
- **Correction (Rule 1, found by running it):** #49's first version-extraction implementation was greedy and read the *last* dotted number in each tool's output. Docker reported its build hash (`0.3` from `build 4debf41`), Compose reported `24.5` from `v2.24.5-desktop.1`, dapr reported its runtime rather than its CLI version, and .NET reported `0.201`. Replaced with awk's leftmost `match()`. Every one of those shapes is now a test case, because all five tools this checks would have been mis-read.
  **Issue:** #49
- **Correction (Rule 1, found by running it):** #49's first port check reported the stack's own running containers as conflicts. `make infra-ensure` exists precisely to be re-runnable against a stack that is already up, so gating it on preflight would have made it refuse every time after the first. Ports belonging to services this compose project already has running are now reported as already-running and are not failures.
  **Issue:** #49
- **Decision:** #49's preflight is POSIX `sh`, not bash.
  **Why:** macOS still ships bash 3.2, so a bash-first version wanting associative arrays would not run on the machines this most needs to work on. Listed under the story's Claude's Discretion.
  **Issue:** #49
- **Decision:** #49's port check treats a compose file that yields no ports as a failure.
  **Why:** Same reasoning as the non-empty discovery assertion in #50 — a check that silently finds nothing reports a clean machine, which is worse than not running it.
  **Issue:** #49
- **Discovered work (filed, not fixed):** #119 — interrupted test runs strand `autonate_test_*` databases; 1,560 on this machine. `PostgresTestDatabase.DisposeAsync` drops correctly on the happy path, but a cancelled or timed-out run never disposes. Outside #50's scope, so filed with `needs-triage` and cross-referenced rather than fixed inline. Becomes load-bearing if M1's #67 shards the suite, since an age-gated sweep is then the only safe form of cleanup.

- **Correction (Rule 1, found by the full suite):** #53's ledger initially skipped *every* recorded batch, including the ~20 data migrations gated by `auth_seed_state`. That made the ledger a second, wrong gate: clearing an `auth_seed_state` marker to re-enable a migration would have silently done nothing. `RebrandMigrationTests` encodes exactly that operator contract and failed with `Expected: "Auton8" / Actual: "Auto Nate"`. A batch whose SQL consults `auth_seed_state` is now never ledger-skipped — its own gate wins.
  **Affects:** the acceptance criterion "a second boot performs no schema work" is true for schema DDL and deliberately not for those migrations, which re-enter cheaply via a `NOT EXISTS` check. Stated in the closing comment rather than claimed as met.
  **Issue:** #53
- **Decision:** #53's back-fill lets the batches run once on the ledger-introducing boot rather than writing rows for an assumed-current database.
  **Why:** For a database predating the ledger we cannot know which batches were applied; marking an un-applied step as applied would skip it permanently and leave a silently half-migrated schema. The batches are idempotent, which is what makes running them safe. Raised in the plan comment before implementing, so it was a stated deviation rather than a discovered one.
  **Issue:** #53
- **Correction (Rule 1, found by the full suite):** #54's first attempt executed every schema batch through the raw `DbConnection` to avoid EF's `string.Format` pass. That broke nine tests. The inline batches are *written for* the format pass — 34 occurrences of `'{{}}'::jsonb`, doubled so it collapses them — while the base schema is an external `.sql` file with single braces that the format pass rejects. Both directions produce loud but opaque errors (`22P02: invalid input syntax for type json`; `Failure to parse near offset 4891`). Resolved with a `bypassFormatting` flag, documented at the branch.
  **Issue:** #54
- **Blocker (needs-owner-action):** #56 and #58 publish container images to the public `ghcr.io/nathanpond` namespace and cut a public GitHub release. `/n8-exec M0` authorizes building the milestone; it does not, on my reading, authorize publication to a public registry under the owner's account. Three options put to the owner on #56: a throwaway prerelease tag, build-everything-and-you-trigger, or defer both out of M0. #59 and #64 are blocked through them — #64 in particular is the story that exists because a runbook for a process nobody has performed is fiction.
  **Issue:** #56, #58, #59, #64
- **Discovered work (filed, not fixed):** #120 — `plugins/Directory.Build.props` does not chain to the root props file, so no analyzers run on plugin projects and they are stamped `1.0.0` rather than the product version. The root file's comment asserts the opposite.
- **Correction (Rule 1, found by the full suite):** #51's advisory lock opened a *second* dedicated connection and held it across all 71 batches. Combined with #54 lengthening `EnsureAsync`, the suite exhausted PostgreSQL's default `max_connections=100` — 903 failures reading `53300: sorry, too many clients already`. The lock now uses the DbContext's own connection. Production would have felt this too at a few instances per server, so it was not merely a test-harness concern.
  **Issue:** #51, #58
- **Decision:** `max_connections` raised from 100 to 300 in `infra/docker-compose.yml` and CI's Postgres service.
  **Why:** The suite builds a database per test class and runs them in parallel, so ~100 initialisations can be in flight. Idle usage is ~16 connections, so this is headroom for a known burst rather than a leak being papered over. Recorded because raising a limit *looks* like masking and needs its reasoning attached.
  **Affects:** It also removed what was hiding two latent test-isolation weaknesses — at 100 the classes queued rather than interleaving. Filed as #123.
- **Correction:** I twice attributed a failing suite to "environment" without reading the error, and was wrong the second time. `console;verbosity=minimal` prints a count and a stack trace and hides the message; switching to the `trx` logger turned "951 failures" into "903 × 53300: too many clients" in one step. Both project skills written in this milestone say to use `trx`.
- **Note:** `SystemIssueEndpointsTests.List_returns_open_issues_by_default` was verified as **pre-existing** by checking out `master` (`647dc55`), rebuilding and running it there. Filed as #122. Checked specifically because it surfaced during the schema work and looked like it might have been caused by it.
- **Blocker resolved:** the owner authorised publishing a throwaway `v0.1.1-rc0`. The publish path was exercised for real, verified from outside the workflow, and then the tag and all four GHCR packages were deleted. Package deletion needed `delete:packages`, which the session token lacked; the owner granted it rather than my working around it.

## Ad-hoc — 2026-09-03 — Semgrep pack choice: p/typescript over p/react (#66)

The story asked for "React/TypeScript" coverage and left the packs to my
discretion. I measured rather than assumed: `p/react` is 4 rules and finds
**zero** findings in this repository; `p/typescript` is 74 rules, a strict
superset of `p/react`'s, and finds four real wildcard-`postMessage` sites in
`DiagramEditor.tsx`. Same runtime cost. Shipped `p/typescript` and deliberately
left `p/react` out, since including both would be pure duplication.

`SemgrepWorkflowTests.The_chosen_packs_are_the_ones_that_were_measured` asserts
`p/react` stays out, so re-adding it is a deliberate act rather than a drift.

Affects: #70 (its baseline is 11 findings, not the 7 that `p/react` would have
produced).

## Ad-hoc — 2026-09-03 — The Semgrep engine is pinned; the rules cannot be (#66)

`#52` established digest-pinning for images and the story's Discretion leaned
toward pinning rule packs so #70's baseline would be stable. Only half of that
is achievable: Semgrep's registry packs are rolling and have **no version
syntax**. So the engine is pinned by digest
(`semgrep/semgrep@sha256:b94b53d0…`, semgrep 1.175.0) and the rules are not.

Rather than leave that implicit, the workflow says so, and both #66 and #70
carry the consequence: #70 must re-count when it starts rather than diff
against a recorded number. The alternative — vendoring the rule packs into the
repository to freeze them — buys reproducibility at the cost of never getting
new rules, which is the wrong trade for an advisory scanner.

Affects: #70.

## Ad-hoc — 2026-09-03 — Refused to bypass push protection to test a scanner (#66)

#66's detection-proof AC needed a deliberately vulnerable snippet pushed to a
scratch branch. The first attempt included a fake AWS key pair and GitHub's
push protection rejected the push, offering an unblock link.

I removed the credentials instead of clicking it. Whitelisting a credential to
prove a credential scanner works is backwards, and the rejection is itself
evidence the class is covered twice over. The `p/secrets` pack was proven
locally instead.

One trap worth recording: an earlier local attempt appeared to show `p/secrets`
was broken. It was not — I had used `AKIAIOSFODNN7EXAMPLE`, which is AWS's own
documentation key and is deliberately allowlisted by secret scanners. A
non-allowlisted pair fires both rules.

## Ad-hoc — 2026-09-03 — Semgrep's dangerouslySetInnerHTML rule is blind to this codebase (#129)

Found while proving detection for #66, filed as #129 rather than fixed. The
rule matches an untyped destructured prop but not a type-annotated one:

    export function A({ c })                  -> flagged
    export function E({ c }: { c: string })   -> NOT flagged

The annotation is the only difference; the file extension is not the variable.
Every real component here uses the annotated form, so **both** live call sites
(`noteEmbedBlock.tsx:503`, `DynamicPageRoute.tsx:103`) are invisible to the
scan. Confirmed under both `p/react` and `p/typescript`, so no pack choice
fixes it.

Left for triage because the scope boundary is real: #66 runs the scanner, #70
triages the findings it reports, and a finding that never appears is neither.
The consequence to remember is that "0 XSS findings" must not be read as "no
XSS".

## Ad-hoc — 2026-09-03 — Did NOT silence csharp-sqli despite it being 6-for-6 wrong (#70)

#70's Discretion note offered this guide: "a rule wrong more often than right
on this codebase is a configuration problem, not a triage problem." Applied
literally, `csharp-sqli` qualified — all six of its baseline findings were false
positives, every one the same shape (`cmd.CommandText = <variable>` inside an
execute helper that binds parameters separately, which the rule cannot
distinguish from real concatenation).

I departed from the guide and kept the rule enabled. Auton8 ships its own query
language (AQL) plus a records-query engine, so a genuine SQL injection in *new*
code is the single highest-value finding this scanner could ever produce.
Permanently blinding that rule to save six one-time dismissals is a bad trade.

Instead the regression guard is forward-looking:
`SemgrepWorkflowTests.Every_silenced_rule_carries_a_justification` permits a
future `--exclude-rule` only with a justification comment above it — the same
shape as #50's loopback exceptions. Reported as "0 rules silenced" with this
reasoning on #70.

## Ad-hoc — 2026-09-03 — Filed nothing and advised nothing from the first Semgrep pass (#70)

All 11 baseline findings were dismissed with written reasons; zero issues, zero
draft advisories. That is the AC applied rather than avoided: a finding is filed
only with a **concrete failure path**, and per project convention one that
cannot be stated that way is noise.

The closest call was the four `wildcard-postmessage-configuration` hits, where
the rule matches a genuinely real pattern. But drawio is vendored into
`public/drawio/` and the iframe `src` is a relative same-origin path, so the
frame cannot navigate cross-origin and there is no third party to leak to. The
only statable failure path is conditional, so it was dismissed with that
argument rather than filed.

Recorded as a follow-up rather than a security issue: replacing `"*"` with
`window.location.origin` in those four calls is strictly better and is not
precluded by the existing comment's stated reason (it resolves at runtime). Left
for the user to green-light, since SPA changes are outside a CI-triage story.

Two of the six SQL findings (`DuckDbAnalyticsRunner`, `RecordsQueryEntity`) are
only safe because their *callers* quote identifiers and parameterise values.
That assurance is owned by #69 and #72; cross-referenced rather than duplicated.

## Ad-hoc — 2026-09-03 — #70's dismissals still need confirming against master (#70)

The baseline alerts exist only on `refs/pull/130/merge`, the closed throwaway PR
from #66, because the Semgrep workflow has not merged to `master` yet — a
repo-wide alert query returns 0. The 11 dismissals were applied to those alerts
by number and are *expected* to carry over to `master`'s first analysis, but
that has not been observed.

Flagged rather than assumed: re-verify after the M1 PR merges, and redo the
dismissals against `master` if they did not stick. #70's summary says so
explicitly so a passing story does not imply a verified alert list.

## Ad-hoc — 2026-09-03 — Kept the hash partition after testing the alternative (#67)

#74 prescribed a stable hash of the class name. I hypothesised that weighting
by per-class test count would balance better, because the slowest classes
looked like the biggest ones, and measured before deciding: correlation between
test count and duration is **0.305**, and greedy longest-processing-time on
that weight produced 46 class-minutes on the worst shard against the hash's 45.

Kept the hash. Recording it because the *hypothesis* was reasonable and wrong,
and someone will have it again.

## Ad-hoc — 2026-09-03 — Shards share discovery's build but restore for themselves (#67)

#67's Discretion offered build-sharing as faster with "a cache-correctness
failure mode". I took the speed and argued the risk away on the grounds that an
artifact built fresh in the same run from the same commit is a handoff, not a
cache.

That reasoning was wrong, and it cost three CI cycles. `obj/*.nuget.g.props`
imports each package's build assets from `~/.nuget/packages` under
`Condition="Exists(...)"`. A shard that never restored has an empty cache, so
those imports are skipped **silently** — including
`xunit.runner.visualstudio.props`, the VSTest adapter. `dotnet test --no-build`
then has nothing to run and exits **0 with no output**, which presented as
eight green shards having run zero tests. The failure was a cache-correctness
one; just not the cache I had in mind.

Settled shape: shards unpack discovery's build output (every project's `bin`
and `obj`), download `spa-dist` for `wwwroot` (a source directory, so not in
the tarball), and run `dotnet restore --locked-mode` (~12s) before
`dotnet test --no-build`. The expensive half — the ~110s build — is still
skipped.

Local repro passed throughout because this machine runs SDK 10.0.201 while CI
resolves `10.0.x` to 10.0.400; the older SDK tolerates the missing adapter.
Worth remembering the next time "works locally" argues against a CI failure.

## Ad-hoc — 2026-09-03 — Silent test loss is now caught twice, not once (#67, #74)

Reconciliation catches a shard that ran nothing, and did — it is the only
reason eight green shards were not merged as a 4x speedup. But it catches it a
job later, with the cause off screen.

So a shard whose run produced no trx now fails at the step itself, whatever
`dotnet test` exited with, and the exit code is echoed to the log rather than
only into `$GITHUB_OUTPUT`. The test step deliberately swallows that exit code
so a red shard still publishes its count for reconciliation, which is precisely
what made "exited 0 having run nothing" indistinguishable from a pass.

## Ad-hoc — 2026-09-03 — Retuned to 10 shards from measurement (#67)

8 shards gave a slowest shard of 8m09s — inside the criterion, but with
discovery on top the milestone's claim of a backend verdict in under ten
minutes was still false end to end. Shard 6 held the heavy cluster and ran
8m09s where its load predicted 6m42s.

10 shards: slowest **4m20s**, spread down from 5m12s to 1m42s, end-to-end
**7m12s** against a 23-minute median before. Runner minutes are free on a public
repository, so the extra jobs cost nothing that matters.

Read the step summary's per-shard table before changing `BACKEND_SHARDS` again;
the hash is lumpy and its lumpiness moves with the class names.
