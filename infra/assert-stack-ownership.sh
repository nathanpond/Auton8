#!/usr/bin/env bash
# Refuse to touch a stack that is serving a different checkout's data (#517).
#
# The compose project name is pinned to `infra` on every path, so a second
# checkout does not get a second stack -- it REPLACES the first one's
# containers. That is the mechanism behind #505, and it is not limited to git
# worktrees: `mounts-root.sh` makes every worktree resolve to one root, but two
# CLONES of the repository on one machine each have their own `infra/mounts`,
# and whichever starts last takes the container names.
#
# This exists as a script rather than a shell function because it has to be
# reachable three ways: from `ensure-up.sh`, from `tier-preflight.sh`, and as a
# make prerequisite for the targets that mutate the stack without going through
# either (#517 -- `infra-up`, `infra-up-dashboard`, `app-container` and
# `keycloak-up` all did). Three copies of a check is how two of them drift, and
# the two that existed already had: one hard-failed on an unreadable mount while
# the other returned 0 (#519).
#
# Exit codes: 0 = safe to proceed (either the stack is ours, or nothing is
# running). 1 = a different checkout owns it, or we could not tell.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
MOUNTS_ROOT="${AUTONATE_MOUNTS_ROOT:-$("$SCRIPT_DIR/mounts-root.sh")}"

# The data mount is the one that carries state worth losing. A stack cannot have
# it right while having the others wrong -- they all come from one compose file
# and one variable -- so checking it alone is sufficient and keeps the failure
# message short.
PGDATA_DESTINATION="${AUTONATE_PGDATA_DESTINATION:-/var/lib/postgresql/data}"

fail() {
    printf '[stack-ownership] ERROR: %s\n' "$1" >&2
    exit 1
}

command -v docker >/dev/null 2>&1 || exit 0
docker info >/dev/null 2>&1 || exit 0

# Prefer the compose project, fall back to the pinned container name. The two
# previous copies of this check disagreed on which to use, and each was blind
# where the other was not: a container outside project `infra` is invisible to
# the first, and a renamed container is invisible to the second. Asking both
# closes that seam (#519).
container_id="$(docker compose -f "$SCRIPT_DIR/docker-compose.yml" -p infra ps -a -q postgres 2>/dev/null | head -1)"
if [ -z "$container_id" ]; then
    container_id="$(docker inspect --format '{{.Id}}' autonate-postgres 2>/dev/null || true)"
fi

# Nothing running is not a conflict -- it is the ordinary first start.
[ -n "$container_id" ] || exit 0

actual="$(docker inspect "$container_id" \
    --format "{{range .Mounts}}{{if eq .Destination \"$PGDATA_DESTINATION\"}}{{.Source}}{{end}}{{end}}" \
    2>/dev/null || true)"

# An empty answer means the mount destination moved -- a Postgres image bump
# relocates PGDATA, and the official images did exactly that at 18. That is
# precisely when a silent pass would be worst, so it fails rather than shrugs.
# The `tier-preflight.sh` copy used to `return 0` here (#519 item 1).
[ -n "$actual" ] || fail "Could not read the running postgres container's data mount.
  Looked for a mount at: $PGDATA_DESTINATION
  If the postgres image changed, the data directory inside the container moved
  and this check needs updating alongside infra/docker-compose.yml. Until then
  it cannot tell whose data the stack is serving, so it will not let you start
  one over it."

expected="$MOUNTS_ROOT/postgres/data"
# Resolved, because /tmp is a symlink to /private/tmp on macOS and two names for
# one directory would otherwise read as a mismatch.
actual_real="$(cd "$actual" 2>/dev/null && pwd -P || printf '%s' "$actual")"
expected_real="$(cd "$expected" 2>/dev/null && pwd -P || printf '%s' "$expected")"

if [ "$actual_real" != "$expected_real" ]; then
    fail "The running stack serves a different checkout's data.
  stack serves  : $actual_real
  this checkout : $expected_real
  Both cannot run at once: the compose project name is 'infra' either way, so
  starting this one would REPLACE the other's containers. Its data survives on
  disk, but the running stack -- and anything mid-test against it -- does not.
  Stop that stack first, or run from the checkout that owns it."
fi

if [ "${AUTONATE_STACK_OWNERSHIP_QUIET:-}" != "1" ]; then
    printf '[stack-ownership] ok  %s\n' "$actual_real"
fi
