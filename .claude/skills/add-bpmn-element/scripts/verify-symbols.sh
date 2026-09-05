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
check_symbol "SUPPORTED_BPMN_TYPES"          2 "$SPA/pages/workflow/WorkflowStudio.tsx"
check_symbol "COMING_SOON_BPMN_TYPES"        2 "$SPA/pages/workflow/WorkflowStudio.tsx"
check_symbol "TimerIntermediateCatchEventModal" 1 "$SPA/pages/workflow/WorkflowStudio.tsx"
check_symbol "ApplyElementSnapshots"         1 "$WEB/Services/Workflow/WorkflowBpmnXml.cs"
check_symbol "BuildUnsupportedRuntimeWarnings" 2 "$WEB/Services/Workflow/WorkflowBpmnXml.cs"
check_symbol "ValidateProcess"               2 "$WEB/Services/Workflow/WorkflowBpmnXml.cs"
check_symbol "UnsupportedRuntimeControlElementNames" 2 "$WEB/Services/Workflow/WorkflowBpmnXml.cs"

echo "Claims:"
# BPMN_MENU_ENTRIES must stay dead — if it gains a consumer, step 2 needs rewriting.
n=$(grep -rho "BPMN_MENU_ENTRIES" "$SPA" 2>/dev/null | wc -l | tr -d ' ')
[ "$n" -eq 1 ] && ok "BPMN_MENU_ENTRIES still dead (1 occurrence)" \
  || bad "BPMN_MENU_ENTRIES" "now $n occurrences — step 2's premise has changed"

# ValidateProcess must still have exactly one call site outside its own file.
n=$(grep -rho "WorkflowBpmnXml.ValidateProcess" "$WEB" 2>/dev/null | wc -l | tr -d ' ')
[ "$n" -eq 1 ] && ok "ValidateProcess has 1 external call site (/prepare only)" \
  || bad "ValidateProcess call sites" "now $n — load-bearing fact 4 may be stale"

# The lint ratchet number quoted in the skill.
r=$(grep -o 'max-warnings=[0-9]*' "$SPA/../package.json" 2>/dev/null | head -1)
grep -q "${r#*=}" .claude/skills/add-bpmn-element/SKILL.md \
  && ok "lint ratchet ($r) matches SKILL.md" \
  || bad "lint ratchet" "package.json says $r; SKILL.md quotes something else"

echo
[ "$fail" -eq 0 ] && echo "All claims resolve." || echo "Some claims have rotted — fix the skill."
exit "$fail"
