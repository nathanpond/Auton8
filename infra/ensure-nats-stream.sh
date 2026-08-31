#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/infra/docker-compose.yml"
COMPOSE=(docker compose -f "$COMPOSE_FILE")
# Only the workflow-execution stream needs out-of-band bootstrap — the
# Flowable extension publishes onto it before AutoNate.Web has a chance to
# start. AutoNate.Web's NatsStreamProvisioner ensures the rest of the streams
# (autonate-records, etc.) at app startup.
STREAM_NAME="workflow-execution"
# Subjects for the CREATE case only. AutoNate.Web's NatsStreamProvisioner owns
# the authoritative set (it covers more prefixes than this: notification, auth,
# iam, site, system, agent, query, dashboards, datastore, …) and widens the
# stream at app startup. This list only has to be wide enough for whatever
# publishes before the app boots — the Flowable extension plus the topics the
# bootstrap script already documented (#113).
SUBJECTS="workflow.execution.> record.> application.> content.>"
NATS_CONTAINER="autonate-nats"

log() {
  printf '[nats-stream] %s\n' "$*"
}

container_running() {
  local status
  status="$(docker inspect -f '{{.State.Status}}' "$NATS_CONTAINER" 2>/dev/null || true)"
  [[ "$status" == "running" ]]
}

bootstrap_stream() {
  # Create-if-absent only. The previous version ran `stream edit --subjects`
  # on every invocation, which narrowed an already-provisioned stream back to
  # workflow.execution.> and dropped record/application/content publishes until
  # the next app boot re-widened it — reintroducing, on every `make
  # infra-ensure`, the exact "no response from stream" regression that
  # infra/scripts/bootstrap-jetstream.sh exists to prevent (#113).
  docker run --rm --network "container:${NATS_CONTAINER}" natsio/nats-box:0.16.0 \
    /bin/sh -lc "
      if nats --server nats://127.0.0.1:4222 stream info ${STREAM_NAME} >/dev/null 2>&1; then
        echo 'exists'
      else
        nats --server nats://127.0.0.1:4222 stream add ${STREAM_NAME} --subjects '${SUBJECTS}' --retention limits --defaults >/dev/null
      fi
    "
}

main() {
  if ! container_running; then
    log "Skipping stream bootstrap because ${NATS_CONTAINER} is not running yet."
    exit 0
  fi

  bootstrap_stream
  log "JetStream stream '${STREAM_NAME}' is ready."
}

main "$@"
