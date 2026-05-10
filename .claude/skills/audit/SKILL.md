---
name: audit
description: Run a focused codebase audit. Sub-audits today are `security`, `performance`, `stability`, `cleanup`. Type `/audit` with no argument (or `/audit --help`) to see the current menu. Designed as a dispatcher so adding a new audit type only requires creating a sibling `audit-<name>` skill and a one-line entry below.
---

# /audit dispatcher

`/audit <name>` runs the matching focused audit across the AutoNate codebase. Each sub-audit is its own skill (`audit-<name>`) with its own checklist, delegation strategy, and output format — this skill is just the router.

## Sub-audits available

| Name | What it checks |
|---|---|
| `security` | OWASP-shaped checklist tailored to AutoNate: CSRF posture (especially pre-auth endpoints), missing/incorrect endpoint authorization, hardcoded secrets, SQL/path/command injection, unsafe deserialization, open redirect, mass assignment, plugin-load isolation, cookie + TLS posture. |
| `performance` | Hot-path scaling: N+1 queries, load-all-then-filter, missing/unused indexes, per-request DB calls that should be cached, sync-over-async, unbounded materialization, threadpool starvation. |
| `stability` | Crash/hang/leak/silent-failure surface: async void, BackgroundService loops without try/catch, swallowed exceptions, fire-and-forget Task.Run, IDisposable misuse, race conditions on singleton state, missing timeouts/cancellation, cancellation-token propagation. |
| `cleanup` | Maintenance debt: dead code (C# + TS, with mandatory verification protocol), files that don't belong in source control, stale comments referencing renamed/removed APIs, duplicated helpers, TODO/FIXME markers in shipped paths, skill/code drift, broken file references, stale auto-memory entries. |

## Dispatch

The user invoked this skill with the trailing argument string captured by the harness. Treat the first whitespace-separated token as the sub-audit name (case-insensitive). Possible cases:

1. **Empty** or `--help` / `-h` / `help` — print the table above plus a one-liner like `Try: /audit security` and stop. Do not run any audit.

2. **A known sub-audit** — invoke the matching skill via the `Skill` tool:

   ```
   Skill(skill="audit-security")     # for /audit security
   Skill(skill="audit-performance")  # for /audit performance
   Skill(skill="audit-stability")    # for /audit stability
   Skill(skill="audit-cleanup")      # for /audit cleanup
   ```

   The sub-skill's instructions take over from there. Pass any remaining arguments through if the sub-audit accepts them (e.g. `/audit security --since=v1.4` would forward `--since=v1.4`).

3. **An unknown sub-audit** — don't guess. Print the available list, suggest the closest match if one is obvious (Levenshtein-ish judgment is fine; `secur` → `security`, `perf` → `performance`, `clean` → `cleanup`), and stop.

## Adding a new sub-audit

1. Create `.claude/skills/audit-<name>/SKILL.md` following the conventions below.
2. Add a one-line row to the table above with the sub-audit name and a short "what it checks" description.
3. Add the dispatch case to step 2 of the **Dispatch** section above.

### Conventions every `audit-<name>` skill should follow

- **Scope**: codebase-wide (not just pending changes — `review` and `security-review` skills already cover the diff case). Make this explicit in the skill body so a reader doesn't expect a PR review.

- **Delegation**: prefer parallel `Explore` agents for breadth-first scanning when the audit covers more than one project area. Hand each agent a focused prompt (one concern per agent) and a hard cap on findings. Then verify the agents' findings yourself before presenting — don't trust agent grep results blindly (see the auto-memory rule about the "zero importers" trap; the same skepticism applies to `audit-*` skills).

- **Output structure**: produce a markdown report with:
  1. **Punch list** — grouped by area or severity. Each finding cites `file:line`, gives a one-line "what's wrong," a one-line "why it matters," and a concrete "how to fix" line. Cap at the most impactful items (≤15 unless the surface really warrants more).
  2. **What I checked but found clean** — short bulleted reassurance so the user knows what was actually examined.
  3. **What's out of scope for this audit** — points to the right adjacent audit if applicable (e.g., perf audit punts CSRF to security audit).

- **Severity rubric** (so audits are comparable):
  - **High** — exploitable today / scales-poorly today / actively misleading.
  - **Medium** — exploitable / costly under future growth.
  - **Low** — defense-in-depth / cosmetic / scale-when-very-large.

- **Verification before reporting**: the auto-memory rule about TS module deletions applies here too. If the audit finds something like "X is unused" or "Y has zero callers," verify with an independent build (`dotnet build`, `npx tsc -b --force`) before listing it. Reporting verified findings is what separates this from a bare grep.

## Notes

- This dispatcher does NOT call `review` or `security-review`. Those are scoped to pending changes on the current branch; this is a whole-codebase audit and is run on demand (typically before a release, or when an external scanner surfaces a class of issue worth checking everywhere).

- The dispatcher itself doesn't run agents or read files — its only job is routing. Sub-audits do the actual work.
