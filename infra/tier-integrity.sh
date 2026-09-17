#!/usr/bin/env bash
#
# The full tier polices its own size (#453).
#
# `make test-full-local` could report success while running almost nothing.
# Three ways, all three observed on a real run:
#
#   * A `[Theory(Skip = "...")]` on the live-engine oracle. The run prints
#     `Skipped: 1` and the tier exits 0. Every other gate stays green, because
#     the guard on the oracle (`ExecutionOracleSizeTests`) counts rows in a
#     JSON file -- the theory's *input data* -- and asserts nothing about
#     whether the theory runs.
#   * Deleting a Flowable-traited class, or re-traiting it out of the tier.
#     Nothing had a count floor, and `backend-reconcile` compares the shards
#     against the same discovery run, so it cannot see a test that was never
#     discovered in the first place.
#   * Plain red. The measured pre-fix run was
#     `Failed: 2, Passed: 340, Skipped: 1` and the target still exited 0,
#     because every `dotnet test` was piped into `tee` and a pipeline's status
#     is its last command's. That is fixed in the Makefile; this script is the
#     part that catches the first two, which stay invisible even once the
#     status plumbing is right.
#
# Two questions, asked after the tier has run:
#
#   1. Did anything skip?
#   2. Is the tier still the size it was pinned at?
#
# Exact pins, not floors, matching the house style (`ExecutionOracleSizeTests`'
# 29, the coverage ratchet): growth has to be as visible as loss, or the pin
# drifts upward silently and stops meaning anything.
#
# Usage:
#   infra/tier-integrity.sh <suite-log>...                both checks
#   infra/tier-integrity.sh --skips-only <suite-log>...   no dotnet needed
#   infra/tier-integrity.sh --counts-only                 discovery only

set -u

# `pwd -P`, not `pwd` (#515). The logical form keeps symlinks in the path, and
# on macOS /tmp IS a symlink to /private/tmp -- which is where this project's
# scratch worktrees live. MSBuild then writes the reference assembly under the
# physical path and looks for it under the logical one:
#
#   CSC : error CS0006: Metadata file
#     '/tmp/<worktree>/src/AutoNate.Web/obj/Debug/net10.0/ref/AutoNate.Web.dll'
#     could not be found
#
# The build fails, discovery prints nothing, and the count check reported
# "discovered 0 tests" -- naming a filter problem for a broken build.
ROOT="$(cd "$(dirname "$0")/.." && pwd -P)"
TIERS="${AUTONATE_TIERS_FILE:-$ROOT/tests/tiers.env}"
E2E="$ROOT/tests/AutoNate.E2E.Tests"

mode=both
case "${1:-}" in
  --skips-only)  mode=skips;  shift ;;
  --counts-only) mode=counts; shift ;;
esac

# NOT `. tests/tiers.env`. The filter values contain unquoted `&`, which a shell
# reads as "run this in the background" -- sourcing the file both mangles the
# filters and forks. Read the assignments instead.
tier_value() {
  sed -n "s/^$1=//p" "$TIERS" | tail -1
}

fail=0
note() { printf '  %s\n' "$*"; }

# --- 1. Nothing skipped -------------------------------------------------------
#
# A filtered-out test is not a skipped one: `dotnet test --filter` never
# discovers it, so it is absent from Total rather than counted in Skipped.
# Everything Skipped counts is a test this tier was meant to run and did not.

check_skips() {
  echo "== tier integrity: skips =="
  if [ "$#" -eq 0 ]; then
    echo "FAIL no suite logs passed; nothing to check."
    fail=1
    return
  fi

  for log in "$@"; do
    name="$(basename "$log")"
    if [ ! -s "$log" ]; then
      echo "FAIL $name is missing or empty -- the suite produced no output at all."
      fail=1
      continue
    fi

    # A crashed or killed run prints no summary line, and then a grep for a
    # non-zero skip count finds nothing and the check passes for the wrong
    # reason. A missing summary is not zero skips; it is an unknown number.
    summaries="$(grep -cE '(Passed|Failed|Skipped)! +- +Failed:' "$log")"
    if [ "$summaries" -eq 0 ]; then
      echo "FAIL $name has no test-run summary line -- the run did not finish."
      fail=1
      continue
    fi

    skipped="$(grep -oE 'Skipped: +[0-9]+' "$log" | grep -oE '[0-9]+' | awk '{t+=$1} END {print t+0}')"
    if [ "$skipped" -ne 0 ]; then
      echo "FAIL $name reports $skipped skipped test(s). The full tier runs everything or it fails."
      grep -E '^[[:space:]]+Skipped ' "$log" | head -20 | while IFS= read -r line; do note "$line"; done
      fail=1
    else
      echo "ok   $name  0 skipped ($summaries summary line(s))"
    fi
  done
}

# --- 2. The tier is still its pinned size ------------------------------------

discovered() {
  # --list-tests prints one indented line per test after a header. Counting the
  # indented lines is stable across the header's wording; counting every line is
  # not.
  #
  # The project is an argument (#507). It used to be hard-wired to $E2E, so the
  # size half of this script only ever measured one of the two projects the tier
  # runs -- the skip half checked both logs, and the counts silently did not.
  project="$1"; filter="$2"
  out="$(mktemp)"
  if [ -n "$filter" ]; then
    dotnet test "$project" --nologo --list-tests --filter "$filter" > "$out" 2>&1
  else
    dotnet test "$project" --nologo --list-tests > "$out" 2>&1
  fi
  rc=$?

  count="$(sed -n 's/^    [A-Za-z].*/x/p' "$out" | grep -c x)"

  # A failed build discovers nothing, which is not the same fact as a filter
  # matching nothing -- and this function used to send stderr to /dev/null, so
  # the two were indistinguishable and the caller confidently reported the
  # wrong one (#515). Report the build failure through a sentinel the caller
  # can tell apart from a real zero.
  if [ "$rc" -ne 0 ] && [ "$count" -eq 0 ]; then
    printf 'BUILD_FAILED %s\n' "$out"
    return 0
  fi
  rm -f "$out"
  printf '%s\n' "$count"
}

check_count() {
  label="$1"; filter="$2"; pinned="$3"; project="${4:-$E2E}"
  if [ -z "$pinned" ]; then
    echo "FAIL $label has no pin in tests/tiers.env. An unpinned tier cannot shrink visibly."
    fail=1
    return
  fi
  actual="$(discovered "$project" "$filter")"
  case "$actual" in
    "BUILD_FAILED "*)
      echo "FAIL $label could not be discovered: the project did not build."
      note "This is a broken build, not a smaller tier. First error:"
      grep -m1 -E ': error |error [A-Z]+[0-9]+' "${actual#BUILD_FAILED }" \
        | sed 's/^/    /' | while IFS= read -r line; do note "$line"; done
      note "Full output: ${actual#BUILD_FAILED }"
      fail=1
      return
      ;;
  esac
  if [ "$actual" -eq 0 ]; then
    echo "FAIL $label discovered 0 tests. A filter that matches nothing is not a passing tier."
    fail=1
  elif [ "$actual" -ne "$pinned" ]; then
    echo "FAIL $label discovered $actual, pinned at $pinned."
    if [ "$actual" -lt "$pinned" ]; then
      note "Tests left this tier. Deleted, renamed, re-traited out, or skipped --"
      note "a [Theory(Skip)] stops its cells being enumerated, so 29 becomes 1 here."
    else
      note "Tests joined this tier. That is fine -- update the pin in tests/tiers.env"
      note "in the same commit, so the growth is in the diff rather than absorbed."
    fi
    fail=1
  else
    echo "ok   $label  $actual (pinned)"
  fi
}

check_counts() {
  echo "== tier integrity: discovered counts =="

  # Per service, so that re-traiting a test OUT of Flowable is caught. The tier
  # total alone cannot see it: the test stays in full-local, it just stops
  # needing the engine -- which is exactly how an oracle gets quietly defanged.
  for svc in $(tier_value AUTONATE_TIER_SERVICES); do
    upper="$(printf '%s' "$svc" | tr '[:lower:]' '[:upper:]')"
    check_count "RequiresService=$svc" "RequiresService=$svc" "$(tier_value "AUTONATE_TIER_COUNT_$upper")"
  done

  # And the tier total, so that deleting an untraited test is caught too.
  check_count "full-local (E2E)" "$(tier_value AUTONATE_TIER_FULL_LOCAL_FILTER)" \
    "$(tier_value AUTONATE_TIER_COUNT_FULL_LOCAL)"

  # The backend project, which this script measured not at all until #507.
  # CLAUDE.md promises the pins are exact "per service as well as per tier", and
  # for full-local that was true of one project out of two: a deleted backend
  # test left `Tier integrity ok: nothing skipped, every count at its pin`
  # printed over a strictly smaller suite.
  #
  # It reads the SLIM_BACKEND pin on purpose rather than introducing a second
  # number. The backend project carries no RequiresService trait at all --
  # TestTierDefinitionTests.The_backend_project_carries_no_service_trait fails
  # the build if one appears -- so slim and full-local run the identical set,
  # and two pins for one number is just a second thing to drift.
  check_count "full-local (backend)" "" \
    "$(tier_value AUTONATE_TIER_COUNT_SLIM_BACKEND)" \
    "$ROOT/tests/AutoNate.Web.Tests"
}

case "$mode" in
  skips)  check_skips "$@" ;;
  counts) check_counts ;;
  both)   check_skips "$@"; check_counts ;;
esac

echo ""
if [ "$fail" -ne 0 ]; then
  echo "TIER INTEGRITY FAILED."
  echo "This is a FAILURE, not a smaller run. A tier that skips or loses tests and"
  echo "still exits 0 reports success for the wrong reason -- which is the only"
  echo "kind of green nobody goes looking behind."
  exit 1
fi

echo "Tier integrity ok: nothing skipped, every count at its pin."
