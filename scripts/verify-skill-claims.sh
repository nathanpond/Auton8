#!/usr/bin/env bash
# Verify that every file path cited by a project skill still exists.
#
# Project skills are grounded in real paths and symbols, and those rot as the code
# moves. This catches the rot class mechanically. It does NOT catch semantic errors —
# a skill can pass this while claiming something the code does the opposite of. Those
# are found by cold-testing a skill against a real task; see .claude/skills/README.md.
#
# Usage: scripts/verify-skill-claims.sh
# Exit 0 = every cited path resolves. Exit 1 = at least one has rotted.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1

fail=0
checked=0

# Paths that are deliberately not real: template placeholders and generic examples.
is_placeholder() {
  case "$1" in
    *Xxx*|*MyComponent*|*MyPlugin*|*'<'*|*'{'*|*'$'*|*example*|*Example*) return 0 ;;
    *) return 1 ;;
  esac
}

for skill_md in .claude/skills/*/SKILL.md; do
  skill_dir="$(dirname "$skill_md")"
  skill_name="$(basename "$skill_dir")"
  bad=()
  # Paths a skill names on purpose without them existing — counter-examples
  # ("there is no X"), and artifacts that live outside the repo (release assets).
  # Declare them with:  <!-- verify-ignore: a.cs b.yml -->
  ignore=" $(grep -rhoE '<!-- verify-ignore:[^>]*-->' "$skill_dir" 2>/dev/null \
             | sed -E 's/<!-- verify-ignore:|-->//g' | tr -s ' \n' ' ') "

  # Every backticked token that looks like a path with a known source extension.
  while IFS= read -r p; do
    [ -z "$p" ] && continue
    is_placeholder "$p" && continue
    case "$ignore" in *" $p "*) continue ;; esac
    checked=$((checked + 1))
    # Resolve relative to the repo root, then to the skill's own directory.
    [ -e "$p" ] && continue
    [ -e "$skill_dir/$p" ] && continue
    [ -e "$skill_dir/references/$p" ] && continue
    # Partial paths (e.g. Endpoints/Foo.cs) — accept if a unique suffix match exists.
    if [ -n "$(find src tests infra plugins docs .github -path "*/$p" -print -quit 2>/dev/null)" ]; then
      continue
    fi
    bad+=("$p")
  done < <(grep -rhoE '`[A-Za-z0-9_@./-]+\.(cs|ts|tsx|js|mjs|json|sql|md|ya?ml|csproj|props|targets|sh|template)`' \
             "$skill_dir" 2>/dev/null | tr -d '`' | sort -u)

  if [ ${#bad[@]} -eq 0 ]; then
    printf '  \033[32m✓\033[0m %-30s\n' "$skill_name"
  else
    printf '  \033[31m✗\033[0m %-30s %d dead path(s)\n' "$skill_name" "${#bad[@]}"
    for b in "${bad[@]}"; do printf '        %s\n' "$b"; done
    fail=1
  fi

  # Run the skill's own deeper check, if it ships one.
  if [ -x "$skill_dir/scripts/verify-symbols.sh" ]; then
    if ! "$skill_dir/scripts/verify-symbols.sh" >/dev/null 2>&1; then
      printf '        \033[31mits own verify-symbols.sh fails — run it for detail\033[0m\n'
      fail=1
    fi
  fi
done

echo
echo "checked $checked cited paths across $(ls -d .claude/skills/*/ | wc -l | tr -d ' ') skills"
[ "$fail" -eq 0 ] && echo "All cited paths resolve." || echo "Some paths have rotted — fix the skill in the same commit as whatever moved."
exit "$fail"
