# Audit conventions (harvested from the removed `/audit` dispatcher, 2026-08-30)

Codebase-wide audits for AutoNate now run through `/n8-audit`, which files findings as GitHub issues. The per-area checklists that used to live in `.claude/skills/audit-*` are preserved in the sibling `audit-*-checklist.md` files here. Conventions that applied across all of them:

- **Scope is the whole codebase**, not the diff — `code-review` / `security-review` cover pending changes.
- **Delegate breadth, verify depth**: parallel `Explore` agents, one concern each, hard cap on findings; then read every cited `file:line` yourself before filing. Agent grep results are candidates, not findings.
- **"Unused / dead / zero callers" claims need a trial delete**: `mv candidate.ts candidate.ts.bak && rm -f src/AutoNate.Spa/tsconfig.*.tsbuildinfo && npx tsc -b --force` (TS) or `dotnet build` after removal (C#). A passing build with the file still present proves nothing. See auto-memory `feedback_unused_ts_module_verification`.
- **Severity rubric** (maps onto `sev:*` labels): High = exploitable / scales-poorly / actively misleading today; Medium = exploitable or costly under future growth or the next refactor; Low = defense-in-depth / cosmetic.
- **Report shape**: punch list (what / why it matters / fix, ≤15), "checked and found clean" per concern, out-of-scope pointers to the adjacent audit. `/n8-audit` turns the punch list into one issue per finding with a fingerprint.
- Historical note: `dotnet_diagnostic` suppressions in `.editorconfig` (S1135, S1144, S3459, S4487, VSTHRD002/003…) defer to the cleanup/stability audits for the verified version of those findings — keep that relationship when tuning rules.
