#!/usr/bin/env bash
#
# Phase 3 of docs/plans/2026-09-01-auton8-0.1-public-release.md — strip the
# paid ColorAdmin theme (and other never-shipped material) from git history.
#
# THIS IS IRREVERSIBLE AND REWRITES EVERY COMMIT SHA. Read it before running.
# It does NOT push. It rewrites a throwaway clone, runs the verification, and
# stops, so you can inspect the result and force-push yourself.
#
# Preconditions:
#   * Phases 1 and 2 are merged and master is green.
#   * brew install git-filter-repo   (not currently installed)
#   * No open PRs you care about — a rewrite orphans them.
#
set -euo pipefail

# ---------------------------------------------------------------------------
# READ THIS FIRST — a force-push is NOT sufficient on GitHub.
#
# GitHub keeps a read-only `refs/pull/<n>/head` for every pull request ever
# opened. They are server-managed: filter-repo cannot rewrite them and you
# cannot delete them. After a force-push they still point at the ORIGINAL
# commits, so the stripped content stays fetchable by anyone who can read the
# repository:
#
#     git fetch origin 'refs/pull/194/head:refs/pr194'
#
# This was verified on this repository on 2026-09-02: after a clean rewrite
# (130 MiB -> 8.63 MiB, zero ColorAdmin objects on every branch), all 21,922
# theme blobs were still reachable through pull refs, across 73 of them.
#
# So the rewrite alone does NOT make it safe to go public. After pushing, you
# must ALSO get GitHub Support to purge the unreachable objects and stale pull
# refs, and confirm they are gone, BEFORE flipping visibility. Keep the
# repository private until that is confirmed.
# ---------------------------------------------------------------------------

ORIGIN="$(git -C "$(dirname "$0")/../.." remote get-url origin)"
WORK="${TMPDIR:-/tmp}/auton8-rewrite-$(date +%s)"
SRC="$(cd "$(dirname "$0")/../.." && pwd)"
OLD_MASTER="$(git -C "$SRC" rev-parse master)"

echo "Source repo:  $SRC"
echo "Old master:   $OLD_MASTER"
echo "Scratch:      $WORK"
echo

command -v git-filter-repo >/dev/null || {
  echo "git-filter-repo is not installed. brew install git-filter-repo" >&2
  exit 1
}

# A fresh mirror clone, never the working repo. filter-repo refuses to run on a
# repo with a remote by default, and rewriting in place would leave you with no
# way back.
git clone --no-local "$SRC" "$WORK"
cd "$WORK"

# Every path below MUST have zero tracked files at HEAD, or the rewrite deletes
# live content. Verified at the time of writing; re-verified here because the
# whole point is that this step cannot be undone.
PATHS=(
  src/AutoNate.Web/ColorAdmin/      # the paid ThemeForest theme — 21,921 blobs
  src/AutoNate.Spa/src/scss/        # 402 objects: the same theme's SCSS
  src/AutoNate.Web/wwwroot/         # 105 objects of built SPA output
  .playwright-mcp/                  # 171 captured console logs, one 4 MB
  .idea/                            # 98 personal Rider settings
  tmpflowable/                      # 7 build artifacts
)

echo "Checking each path is absent from HEAD..."
for p in "${PATHS[@]}"; do
  n=$(git ls-files "$p" | wc -l | tr -d ' ')
  printf '  %-34s %s tracked at HEAD\n' "$p" "$n"
  [ "$n" = "0" ] || { echo "REFUSING: $p has tracked files at HEAD." >&2; exit 1; }
done

n=$(git ls-files | grep -cE '^[^/]+\.(png|jpg|jpeg|gif)$' || true)
printf '  %-34s %s tracked at HEAD\n' "root-level images" "$n"
[ "$n" = "0" ] || { echo "REFUSING: root-level images are tracked at HEAD." >&2; exit 1; }
echo

ARGS=()
for p in "${PATHS[@]}"; do ARGS+=(--path "$p"); done
# Anchored to the repo root, so public/favicon.png and
# plugins/Auditor/PageTemplates/AuditLog.png survive.
ARGS+=(--path-regex '^[^/]+\.(png|jpg|jpeg|gif)$')

git filter-repo --force --invert-paths "${ARGS[@]}"

echo
echo "=== Verification ==="

echo -n "1. No coloradmin path anywhere in history: "
if git rev-list --objects --all | grep -qi coloradmin; then
  echo "FAIL"; exit 1
else
  echo "pass"
fi

echo -n "2. Repack size: "
git reflog expire --expire=now --all
git gc --prune=now --aggressive --quiet
git count-objects -vH | grep size-pack

echo -n "3. Content unchanged (the check that matters): "
git remote add old "$SRC" 2>/dev/null || true
git fetch -q old master
if [ -z "$(git diff --stat "old/master" HEAD)" ]; then
  echo "pass — tree is byte-identical to pre-rewrite master"
else
  echo "FAIL — the rewrite changed live content:"
  git diff --stat old/master HEAD
  exit 1
fi
git remote remove old

echo -n "4. Commit count: "
git rev-list --count HEAD

cat <<EOF

=== Rewrite complete, NOT pushed ===

Scratch clone: $WORK

Before pushing, build and test from that clone:
  cd $WORK
  dotnet build AutoNate.sln
  (cd src/AutoNate.Spa && npm ci && npm run lint && npx tsc -b && npm run build)
  dotnet test tests/AutoNate.Web.Tests

Then, and only then:
  cd $WORK
  git remote add origin $ORIGIN
  git push --force --all origin
  git push --force --tags origin

Afterwards: delete your local clone and re-clone. Do NOT merge an old clone
into the new history — it would reintroduce every stripped object.

THEN, before making the repository public, verify the pull refs:

  git ls-remote origin 'refs/pull/*/head' | wc -l
  git fetch origin 'refs/pull/<any>/head:refs/prcheck'
  git rev-list --objects refs/prcheck | grep -ci coloradmin   # must be 0

If that is not 0, the stripped content is still published. Open a GitHub
Support request asking them to garbage-collect unreachable objects and stale
pull-request refs after a history rewrite, and wait for confirmation.
Finally, delete the safety tag once you are satisfied:
  git push origin :refs/tags/pre-rewrite-backup
EOF
