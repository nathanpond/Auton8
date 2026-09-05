# Project skills

Skills that encode how *this* codebase does a recurring thing. They are grounded in
real paths, symbols and gotchas, which is what makes them useful and also what makes
them rot.

## Two failure modes, two defences

**Rot** — the code moved and the skill still describes where it used to be.
Caught mechanically:

```bash
scripts/verify-skill-claims.sh
```

It checks every file path cited by every skill, and runs any skill's own
`scripts/verify-symbols.sh` for deeper per-skill claims. Run it when you edit a skill,
and when you move something a skill names.

**Being wrong from the start** — the skill was written from reading the code and
describes something the code does not do. Nothing automated catches this. The 2026-09-05
audit found, in skills that had never been exercised:

- a step pointing at a constant nothing consumes (`BPMN_MENU_ENTRIES`, one occurrence: its own declaration)
- a namespace instruction that was exactly backwards
- "errors block publishing" when the publish endpoint never validates
- "the evaluator rejects unregistered actions" when it never consults the registry — in two skills, quoted by five planning issues
- two copy-pasteable SQL examples that are test-enforced to fail

The only thing that finds these is **exercising the skill**. Before landing a new or
substantially rewritten skill, have an agent follow it — given only the skill files
and one realistic task — and report what was wrong, unclear, or missing. Every defect
above was found that way, and none would have been found by re-reading.

## Conventions

- **Fix the skill in the same commit** as the change that invalidated it. "Later" does not happen.
- **State facts before steps.** Where a skill has traps that look fine until they don't, put them above the steps rather than inline — a reader skims steps and reads preamble.
- **Map skipped steps to symptoms.** "Skip this → the panel opens empty and loses settings on reselect" is worth more than "this step is required", because the reader meets the symptom, not the step.
- **Cite a real exemplar**, and check it does not contradict the rule you are stating. Several audited skills named exemplars that violated their own advice.
- `<!-- verify-ignore: a.cs b.yml -->` exempts paths a skill names deliberately without them existing — counter-examples, and artifacts outside the repo.

## Maintenance history

- **2026-09-05** — all 13 skills audited; none was accurate as written. Three Mantine skills deleted (symlinks into `.agents/skills/`, zero project content, duplicating `docs/mantine/llms.txt` and the live `mantine` MCP server). Ten corrected. Two product defects surfaced and filed separately.
