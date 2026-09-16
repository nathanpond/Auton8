#!/usr/bin/env bash
# Probe the services a test tier needs, and fail NAMING the one that is missing.
#
# This exists because `ensure-up.sh` reports only "Compose stack did not become
# ready within 120s" -- true, unhelpful, and indistinguishable between "Docker
# is not running" and "Flowable crashed on boot". It also runs BEFORE the
# compose-up, so a genuinely dead endpoint fails in a second rather than after
# the full wait.
#
# The tier this milestone exists for is the one where a missing service must
# never read as a smaller green run.
set -uo pipefail

PORT_POSTGRES="${AUTONATE_POSTGRES_PORT:-5432}"
PORT_NATS="${AUTONATE_NATS_PORT:-4222}"
PORT_REDIS="${AUTONATE_REDIS_PORT:-6379}"
PORT_FLOWABLE="${AUTONATE_FLOWABLE_PORT:-8080}"
PORT_DAPR_PLACEMENT="${AUTONATE_DAPR_PLACEMENT_PORT:-50006}"
PORT_DAPR_SCHEDULER="${AUTONATE_DAPR_SCHEDULER_PORT:-50007}"
PORT_HOCUSPOCUS="${AUTONATE_HOCUSPOCUS_PORT:-1234}"

missing=()

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXPECTED_MOUNTS_ROOT="${AUTONATE_MOUNTS_ROOT:-$("$SCRIPT_DIR/mounts-root.sh")}"

# Does the stack that is already running belong to THIS checkout's data?
#
# Ports answering is not the same question. A stack serving a different
# directory answers every probe below and is still the wrong stack: it was
# measured standing up an empty Postgres cluster and a Flowable with no schema,
# after which 161 of 407 E2E tests failed with "The workflow engine refused this
# workflow" -- a product-regression-shaped message for an environment fault
# (#505).
#
# `mounts-root.sh` makes every worktree resolve to one root, so this check
# should now only fire when a stack was started by hand with a different
# AUTONATE_MOUNTS_ROOT, or by a checkout that predates this fix. That is
# exactly when the operator needs to be told, by name.
check_stack_ownership() {
    local actual
    actual=$(docker inspect autonate-postgres \
        --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Source}}{{end}}{{end}}' \
        2>/dev/null) || return 0
    [ -n "$actual" ] || return 0

    local expected="$EXPECTED_MOUNTS_ROOT/postgres/data"
    # Compare by resolved path: /tmp is a symlink to /private/tmp on macOS, so
    # a string compare reports a mismatch between two names for one directory.
    local actual_real expected_real
    actual_real=$(cd "$actual" 2>/dev/null && pwd -P) || actual_real="$actual"
    expected_real=$(cd "$expected" 2>/dev/null && pwd -P) || expected_real="$expected"

    if [ "$actual_real" != "$expected_real" ]; then
        printf '  MISMATCH %-17s %s\n' "running stack" "$actual_real"
        printf '  %-26s %s\n' "this checkout expects" "$expected_real"
        missing+=("the running stack serves a different data directory")
    else
        printf '  ok      %-18s %s\n' "stack ownership" "$actual_real"
    fi
}

probe_port() { # name host port
  if nc -z "$2" "$3" >/dev/null 2>&1; then
    printf '  ok      %-18s %s:%s\n' "$1" "$2" "$3"
  else
    printf '  MISSING %-18s %s:%s\n' "$1" "$2" "$3"
    missing+=("$1 (tried $2:$3)")
  fi
}

probe_http() { # name url
  if curl --fail --silent --show-error --max-time 5 --output /dev/null "$2"; then
    printf '  ok      %-18s %s\n' "$1" "$2"
  else
    printf '  MISSING %-18s %s\n' "$1" "$2"
    missing+=("$1 (tried $2)")
  fi
}

echo "Preflight for the full-local tier:"
check_stack_ownership
probe_port postgres       127.0.0.1 "$PORT_POSTGRES"
probe_port nats           127.0.0.1 "$PORT_NATS"
probe_port redis          127.0.0.1 "$PORT_REDIS"
probe_port dapr-placement 127.0.0.1 "$PORT_DAPR_PLACEMENT"
probe_port dapr-scheduler 127.0.0.1 "$PORT_DAPR_SCHEDULER"
probe_port hocuspocus     127.0.0.1 "$PORT_HOCUSPOCUS"
probe_http flowable       "http://127.0.0.1:${PORT_FLOWABLE}/flowable-rest"

# SEVEN of ensure-up's ten, and the other three on purpose (#487):
#   executor, flowable-dapr  -- no published port; they are reachable only on
#                               the compose network, so there is nothing a
#                               host-side probe could ask.
#   nats-init                -- a one-shot init container. It has no endpoint
#                               and is expected to have exited.
# ensure-up still waits on all ten, so a missing one still fails the tier; what
# it does not get from here is the fast, named diagnosis.

if [ ${#missing[@]} -gt 0 ]; then
  echo
  echo "full-local cannot run. These checks did not pass:"
  for entry in "${missing[@]}"; do echo "  - $entry"; done
  echo
  echo "Bring them up with 'make infra-ensure', or 'make app-dapr' for the sidecar."
  echo "If the failure above is a data-directory MISMATCH, the stack that is"
  echo "running was started against a different checkout: stop it and re-run"
  echo "'make infra-ensure' from here, or unset AUTONATE_MOUNTS_ROOT."
  echo "This is a FAILURE, not a smaller run: a tier that skips what it cannot"
  echo "reach reports success for the wrong reason, which is the defect this"
  echo "milestone exists to end."
  exit 1
fi

echo "All full-local services answered."
