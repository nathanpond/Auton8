#!/usr/bin/env bash
# Verify every file path and symbol this skill cites still exists.
#
# The skill tells its readers to treat each path and symbol as a claim that may
# have rotted. This is that check, run mechanically. #174 formalises it; run it
# any time you follow or edit the skill.
#
# Usage: .claude/skills/add-bpmn-element/scripts/verify-symbols.sh
# Exit 0 = every claim resolves. Exit 1 = at least one has rotted.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1

fail=0
ok()   { printf '  \033[32m✓\033[0m %s\n' "$1"; }
bad()  { printf '  \033[31m✗\033[0m %s — %s\n' "$1" "$2"; fail=1; }

check_file() { [ -f "$1" ] && ok "$1" || bad "$1" "file not found"; }

# symbol <name> <file...> — must appear at least <min> times (default 1)
check_symbol() {
  local sym="$1" min="$2"; shift 2
  local n; n=$(grep -ho -- "$sym" "$@" 2>/dev/null | wc -l | tr -d ' ')
  if [ "$n" -ge "$min" ]; then ok "$sym ($n)"; else bad "$sym" "found $n, expected >= $min"; fi
}

SPA=src/AutoNate.Spa/src
WEB=src/AutoNate.Web

echo "Files:"
check_file "$SPA/lib/bpmn/workflow.js"
check_file "$SPA/pages/workflow/WorkflowStudio.tsx"
check_file "$SPA/api/workflows.ts"
check_file "$SPA/components/notifications/toast.ts"
check_file "$WEB/Services/Workflow/WorkflowBpmnXml.cs"
check_file "$WEB/Services/Workflow/WorkflowElementSnapshot.cs"
check_file "$WEB/Endpoints/WorkflowEndpoints.cs"
check_file "$WEB/Endpoints/ExecutionEndpoints.cs"
check_file "tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs"
check_file "tests/AutoNate.Web.Tests/Invariants/DoNotRenameGuardTests.cs"
check_file "$SPA/../public/vendor/bpmn-js/bpmn-modeler.development.js"

echo "Symbols:"
check_symbol "describeBusinessObject"        2 "$SPA/lib/bpmn/workflow.js"
check_symbol "getElementSnapshots"           1 "$SPA/lib/bpmn/workflow.js"
check_symbol "describeTimerIntermediateCatchEvent" 2 "$SPA/lib/bpmn/workflow.js"
check_symbol "writeFlowableAttribute"        2 "$SPA/lib/bpmn/workflow.js"
check_symbol "updateTimerIntermediateCatchEventProperties" 1 "$SPA/lib/bpmn/workflow.js"
check_symbol "createModeler"                 1 "$SPA/lib/bpmn/workflow.js"
check_symbol "onRequestConfigure"            2 "$SPA/pages/workflow/WorkflowStudio.tsx"
check_symbol "ElementSelection"              1 "$SPA/pages/workflow/WorkflowStudio.tsx"
check_symbol "TimerIntermediateCatchEventModal" 1 "$SPA/pages/workflow/WorkflowStudio.tsx"
check_symbol "ApplyElementSnapshots"         1 "$WEB/Services/Workflow/WorkflowBpmnXml.cs"
check_symbol "BuildUnsupportedElementErrors"  2 "$WEB/Services/Workflow/WorkflowBpmnXml.cs"
check_symbol "ValidateProcess"               2 "$WEB/Services/Workflow/WorkflowBpmnXml.cs"
check_symbol "BpmnSupportManifest"           2 "$WEB/Services/Workflow/BpmnSupportManifest.cs"

echo "Claims:"
# BPMN_MENU_ENTRIES must stay dead — if it gains a consumer, step 2 needs rewriting.
n=$(grep -rho "BPMN_MENU_ENTRIES" "$SPA" 2>/dev/null | wc -l | tr -d ' ')
[ "$n" -eq 1 ] && ok "BPMN_MENU_ENTRIES still dead (1 occurrence)" \
  || bad "BPMN_MENU_ENTRIES" "now $n occurrences — step 2's premise has changed"

# ValidateProcess runs at BOTH /prepare and /publish since #225. Two call sites is
# the correct state; one means publish stopped validating and fact 4 is stale in the
# other direction — which is the regression #225 itself caused once, silently.
n=$(grep -rho "WorkflowBpmnXml.ValidateProcess" "$WEB" 2>/dev/null | wc -l | tr -d ' ')
[ "$n" -eq 2 ] && ok "ValidateProcess has 2 call sites (/prepare and /publish)" \
  || bad "ValidateProcess call sites" "now $n, expected 2 — load-bearing fact 4 may be stale"

# The two validation sets must not drift apart again. #225 pointed publish at
# ValidateProcess, which did not contain the promoted structure rules, and three
# stopped running with nothing to say so.
if grep -q "BuildStructureErrors" "$WEB/Services/Workflow/WorkflowBpmnXml.cs" 2>/dev/null; then
  ok "the promoted structure rules are shared (BuildStructureErrors)"
else
  bad "shared structure rules" "BuildStructureErrors is gone — the two validation sets can diverge again"
fi

# The lint ratchet, matched in context rather than as a bare substring — a bare
# grep for the number matches a line number or an issue number and passes on a stale skill.
r=$(grep -o 'max-warnings=[0-9]*' "$SPA/../package.json" 2>/dev/null | head -1)
if grep -qE "currently ${r#*=}|max-warnings=${r#*=}" .claude/skills/add-bpmn-element/SKILL.md; then
  ok "lint ratchet ($r) matches SKILL.md"
else
  bad "lint ratchet" "package.json says $r; SKILL.md quotes something else"
fi

# MENU_GROUP_ORDER carries the same dead-code claim as BPMN_MENU_ENTRIES.
n=$(grep -rho "MENU_GROUP_ORDER" "$SPA" 2>/dev/null | wc -l | tr -d ' ')
[ "$n" -eq 1 ] && ok "MENU_GROUP_ORDER still dead (1 occurrence)" \
  || bad "MENU_GROUP_ORDER" "now $n occurrences — step 2's premise has changed"

# The support manifest is one file with 68 entries, and both sides read it (#107).
# Counted from the JSON rather than eyeballed, which is what the old check asked for.
MANIFEST=src/shared/bpmn-support.json
if [ -f "$MANIFEST" ]; then
  n=$(grep -c '"localName":' "$MANIFEST")
  [ "$n" -eq 68 ] && ok "support manifest has 68 entries" \
    || bad "support manifest" "has $n entries, not the 68 SKILL.md quotes"
else
  bad "support manifest" "src/shared/bpmn-support.json is gone — step 1 no longer applies"
fi

# Step 1's whole premise: the studio derives its list and does not keep one.
# Matched as a declaration, not a substring: the file's comments name the old
# constants when explaining what replaced them, and a bare grep flags that prose.
if grep -qE '^(const|let|var) (SUPPORTED|COMING_SOON)_BPMN_TYPES' "$SPA/pages/workflow/WorkflowStudio.tsx"; then
  bad "one source of truth" "WorkflowStudio.tsx declares a BPMN type list again — step 1 is stale"
else
  ok "WorkflowStudio.tsx keeps no BPMN type list of its own"
fi

# Both consumers read the same file: the SPA by import, the backend by embedding.
grep -q '@shared/bpmn-support.json' "$SPA/lib/bpmn/support.ts" 2>/dev/null \
  && ok "SPA imports the shared manifest" \
  || bad "SPA manifest import" "src/lib/bpmn/support.ts does not import @shared/bpmn-support.json"
grep -q 'shared.bpmn-support.json' "$WEB/AutoNate.Web.csproj" \
  && ok "AutoNate.Web embeds the shared manifest" \
  || bad "backend manifest embed" "AutoNate.Web.csproj no longer embeds src/shared/bpmn-support.json"

echo
[ "$fail" -eq 0 ] && echo "All claims resolve." || echo "Some claims have rotted — fix the skill."
exit "$fail"
