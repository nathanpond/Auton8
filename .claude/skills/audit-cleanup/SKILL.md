---
name: audit-cleanup
description: Codebase-wide cleanup audit for AutoNate. Looks for dead code (unused classes/methods/files), files that don't belong in source control (build artifacts, debug scratch, IDE state), stale comments referencing renamed/removed APIs, duplicated helpers, TODO/FIXME markers in shipped code, and code that doesn't match the documented project skills. Distinct from `audit-stability` (correctness) and `audit-performance` (scaling). Invoked by `/audit cleanup`; can also be invoked directly.
---

# Cleanup audit (whole codebase)

A focused pass for the classes of debt that accumulate over time and slow new development without breaking anything outright: a 355-line module with no importers, a debug screenshot from three months ago tracked in git, a stale comment that points at a class that's been renamed twice since.

**Scope**: every project under `src/` and `plugins/`, plus the repo root, `.claude/`, `tests/`, and `infra/`.

## Strategy

Parallel `Explore` agents, one per concern. Then **verify every "unused" / "dead" claim with an independent build** before reporting it. The auto-memory rule about TS-module deletions (`feedback_unused_ts_module_verification.md`) applies to this skill more than any other — historical cleanup audits have surfaced false-positive "zero importers" findings that turned out to be real consumers via multi-line destructured imports or stale tsbuildinfo cache.

## Patterns to detect

### A. Dead code (C#)
- Public/internal classes, methods, properties that are never referenced anywhere else in `src/`, `tests/`, or `plugins/`.
- Verification protocol:
  1. `grep -rn "<symbol>" src tests plugins` — confirm zero usages.
  2. Check the symbol isn't a DI registration target (look for `AddSingleton/Scoped/Transient<...>` and reflection-based discovery).
  3. Check it isn't an entry point or hosted service.
  4. Run `dotnet build` after a *trial deletion* (in your head, not on disk) — if you can't reason that nothing breaks, leave it alone.
- Skip auto-generated code under `Persistence/Scaffolded/`.

### B. Dead code (TypeScript / SPA)
- Exports under `src/AutoNate.Spa/src/` never imported elsewhere.
- **Verification is critical here**: grep alone is unreliable for multi-line destructured imports and TypeScript path aliases. Mandatory before reporting:
  1. Delete `src/AutoNate.Spa/tsconfig.app.tsbuildinfo` and `tsconfig.node.tsbuildinfo`.
  2. Run `cd src/AutoNate.Spa && npx tsc -b --force`.
  3. If exit 0 with nothing that imports the symbol, then it's safe.
- Also check Vite config and SPA's pageTemplates/registry for dynamic-import-by-name patterns that grep won't catch.
- Files matching `*.old.*`, `*Backup*`, `*_v2.*`, `*-deprecated.*` are obvious candidates but verify before recommending deletion.

### C. Files that don't belong in source control
- Build artifacts: `*.tsbuildinfo`, `bin/`, `obj/`, `dist/`, `*.pdb`, `*.user`, `.suo`.
- Debug scratch: screenshots in repo root, `.playwright-mcp/` console logs, `Test Results/` snapshots.
- IDE state: `*.iml`, `.idea/` (sometimes intentional), `*.DotSettings.user`.
- Cross-check against `.gitignore`; flag files that should be ignored but were committed before the gitignore rule landed.
- Confirm by `git ls-files | grep ...` rather than just listing the working tree (an ignored file may still be locally present).

### D. Stale comments
- Comments referencing types/methods/files that no longer exist. Pattern: grep for class names mentioned in comments, then check those names still resolve.
- Comments saying "TODO: …" or "FIXME: …" that refer to a now-shipped or now-cancelled feature.
- "Phase X" markers (e.g. "Phase 4 replaces this with …") where Phase X has come and gone — flag for either deletion of the marker or fulfillment of the promise.

### E. Duplicated helpers
- Identical or near-identical private static helpers across multiple files. Canonical historical example: the `ActorId` / `GetActorId` / `GetUserId` triad that lived in 19 endpoint files until the cleanup batch consolidated it into `HttpContextActorExtensions`.
- Detection: structural search for short functions whose body is a single `FindFirstValue` + `Guid.TryParse` pattern, or any tight repeat. Manual inspection of the resulting candidates is required (false positives are easy here).

### F. TODO / FIXME / HACK markers in shipped code paths
- `grep -rn -E "TODO|FIXME|HACK|XXX" src/ plugins/`. Filter out comments in test fixtures.
- For each, judge:
  - **Real outstanding work** → it's been tracked or should be promoted to an issue.
  - **Stale, no longer relevant** → the cleanup is to delete the comment.
  - **Acknowledged design tradeoff** ("HACK: we accept this because …") → leave alone.
- Cap at the 5 most concerning items in the report.

### G. Skill drift (code drift away from documented skill)
- For each `.claude/skills/<name>/SKILL.md`, scan the files the skill references. If the canonical pattern the skill describes no longer matches reality (e.g., the skill says "endpoints register via X.RequirePermission(...)" but new endpoints in the same area use a different pattern), flag.
- Also flag the inverse direction: places where the live code has features the skill doesn't mention (e.g., an `IAutoNatePlugin.Cleanup` method that the `plugin-creator` skill doesn't document — historical real example).
- This concern overlaps with the `feedback_skill_drift.md` rule. The audit doesn't fix drift; it surfaces it as findings the user can choose to address.

### H. Files in source control that are referenced but missing
- Assets, migrations, fixtures named in code/configs that don't exist on disk. Detection: extract every literal path-looking string from `src/` and `plugins/`, check it resolves.
- Often surfaces broken plugin manifest references, missing migration files, dead config defaults pointing at deleted assets.

### I. Tests for code that no longer exists
- Test files whose subject (`XyzTests.cs` testing `Xyz`) doesn't exist as a target type anymore. Either the test should be deleted or it's testing something that's been renamed.
- Same hazard as concern A — verify by build, not by grep.

### J. Stale auto-memory entries
- Read `~/.claude/projects/-Users-npond-RiderProjects-AutoNate/memory/MEMORY.md` and the linked memory files. For each, verify the cited facts still hold (paths exist, line numbers are roughly right, behavior described still matches).
- Stale memories drive bad recommendations in future sessions; flagging them is itself a cleanup.

## Verification before reporting

- Every "X is dead / unused" claim verified per the protocol in concern A or B (whichever applies).
- Every "file should be untracked" claim verified by `git check-ignore -v` and inspection of `.gitignore`.
- Every "stale comment" claim verified by checking the referenced symbol no longer resolves.
- Every "duplicated helper" claim verified by reading both copies (not just the function name) — same name, different bodies happens.

## Output

### 1. Punch list
Grouped by concern (A–J). Each finding:

```
**[H/M/L] path:NN — short title**
- What: one-line description
- Why it matters: one-line concrete consequence (typically: maintenance friction, repo bloat, or developer confusion)
- Action: `git rm --cached path/file`, or "delete after verifying no DI registration", or "update comment to reference new type", etc.
```

Severity rubric for cleanup specifically:
- **High** — actively misleading (stale comment pointing at deleted type, missing file referenced by config). Causes bugs.
- **Medium** — repo bloat or developer confusion (debug screenshots in git, large dead module).
- **Low** — cosmetic or nice-to-have (small unused private helper, single-file dead export).

### 2. What I checked and found clean
Bulleted list per concern so the surface examined is visible.

### 3. Suggested git operations
Concrete `git rm --cached` / `.gitignore` updates the user can copy-paste, batched by concern. The audit doesn't run them — it just stages the commands so the user can review before executing.

### 4. Out of scope
- Behavioral/semantic correctness → `/audit stability`.
- Performance debt → `/audit performance`.
- Adding new tests for under-covered code → not in this checklist; needs a future `audit-tests` skill.
- Fixing the drift identified in concern G → use the matching project skill (`/add-permission-gate` etc.) rather than this audit.
