# Cleanup audit checklist (AutoNate-specific)

Harvested from `.claude/skills/audit-cleanup` on 2026-08-30. Scope: `src/`, `plugins/`, `tests/`, `infra/`, `.claude/`, repo root.

**A. Dead C#** — grep `src tests plugins` for zero usages, then rule out DI registration (`AddSingleton/Scoped/Transient<…>`, reflection discovery), entry points, hosted services; skip `Persistence/Scaffolded/`. Verify by build after removal.
**B. Dead TS** — grep is unreliable (multi-line destructured imports, path aliases, `pageTemplates`/registry dynamic imports). Mandatory: `mv candidate candidate.bak && rm -f src/AutoNate.Spa/tsconfig.*.tsbuildinfo && npx tsc -b --force`. `*.old.*`, `*Backup*`, `*_v2.*`, `*-deprecated.*` are candidates only.
**C. Files that don't belong in git** — `*.tsbuildinfo`, `bin/obj/dist`, `*.pdb`, `*.user`, `.suo`; screenshots at repo root, `.playwright-mcp/` logs, `Test Results/`; `*.iml`, `*.DotSettings.user`. Confirm with `git ls-files` + `git check-ignore -v`, not the working tree. Scratch belongs under `/temp/` (root, ignored).
**D. Stale comments** — references to renamed/removed types, done/cancelled TODOs, "Phase X" markers whose phase has passed.
**E. Duplicated helpers** — historical: the `ActorId` / `GetActorId` / `GetUserId` triad in 19 endpoint files, consolidated into `HttpContextActorExtensions`. Detect `FindFirstValue` + `Guid.TryParse` one-liners; read bodies, not names.
**F. TODO / FIXME / HACK** in shipped paths — real work → issue; stale → delete; acknowledged tradeoff with rationale → leave. Cap 5. (`S1135` is suppressed in `.editorconfig` because this audit owns that signal.)
**G. Skill drift** — for each `.claude/skills/<name>/SKILL.md`, the referenced files still match the pattern described, and the code has no features the skill omits (historical: `IAutoNatePlugin.Cleanup` missing from `plugin-creator`). Surfaces, doesn't fix — see auto-memory `feedback_skill_drift`.
**H. Referenced-but-missing files** — literal path strings in `src/` / `plugins/` that don't resolve (plugin manifests, migrations, config defaults).
**I. Tests for vanished subjects** — `XyzTests.cs` with no `Xyz`; verify by build.
**J. Stale auto-memory** — `~/.claude/projects/-Users-npond-RiderProjects-AutoNate/memory/MEMORY.md` and linked files: paths exist, behavior still matches.

Severity: High = actively misleading (stale comment → deleted type, config → missing file). Medium = bloat/confusion (tracked screenshots, large dead module). Low = cosmetic. Output stages `git rm --cached` / `.gitignore` commands for review; the audit never runs them.
