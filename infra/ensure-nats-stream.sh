#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/infra/docker-compose.yml"
COMPOSE=(docker compose -f "$COMPOSE_FILE")
STREAM_NAME="workflow-execution"
SUBJECT="workflow.execution.>"
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
  docker run --rm --network "container:${NATS_CONTAINER}" natsio/nats-box:0.16.0 \
    /bin/sh -lc "
      if nats --server nats://127.0.0.1:4222 stream info ${STREAM_NAME} >/dev/null 2>&1; then
        nats --server nats://127.0.0.1:4222 stream edit ${STREAM_NAME} --subjects '${SUBJECT}' --force >/dev/null
      else
        nats --server nats://127.0.0.1:4222 stream add ${STREAM_NAME} --subjects '${SUBJECT}' --retention limits --defaults >/dev/null
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
