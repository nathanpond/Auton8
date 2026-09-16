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
  echo "full-local cannot run. These services did not answer:"
  for entry in "${missing[@]}"; do echo "  - $entry"; done
  echo
  echo "Bring them up with 'make infra-ensure', or 'make app-dapr' for the sidecar."
  echo "This is a FAILURE, not a smaller run: a tier that skips what it cannot"
  echo "reach reports success for the wrong reason, which is the defect this"
  echo "milestone exists to end."
  exit 1
fi

echo "All full-local services answered."
