#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PID_FILE="${TMPDIR:-/tmp}/autonate-web-daprd.pid"
LOG_FILE="${TMPDIR:-/tmp}/autonate-web-daprd.log"
APP_ID="autonate-web"
APP_PORT="5108"
DAPR_HTTP_PORT="3500"
DAPR_GRPC_PORT="50001"
PLACEMENT_HOST_ADDRESS="127.0.0.1:50006"
SCHEDULER_HOST_ADDRESS="127.0.0.1:50007"
RESOURCES_PATH="$REPO_ROOT/infra/mounts/dapr-dashboard/components"
START_TIMEOUT_SECONDS="${AUTONATE_DAPR_SIDECAR_WAIT_TIMEOUT_SECONDS:-20}"

log() {
  printf '[autonate-web-sidecar] %s\n' "$*"
}

fail() {
  printf '[autonate-web-sidecar] ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    fail "Required command '$1' is not available."
  fi
}

port_listening() {
  nc -z 127.0.0.1 "$1" >/dev/null 2>&1
}

sidecar_metadata() {
  curl -fsS "http://127.0.0.1:${DAPR_HTTP_PORT}/v1.0/metadata"
}

sidecar_healthy() {
  local metadata
  metadata="$(sidecar_metadata 2>/dev/null || true)"
  [[ -n "$metadata" ]] && grep -q "\"id\":\"${APP_ID}\"" <<< "$metadata"
}

pid_running() {
  local pid="$1"
  kill -0 "$pid" >/dev/null 2>&1
}

cleanup_stale_pid_file() {
  if [[ ! -f "$PID_FILE" ]]; then
    return
  fi

  local pid
  pid="$(cat "$PID_FILE" 2>/dev/null || true)"

  if [[ -z "$pid" ]]; then
    rm -f "$PID_FILE"
    return
  fi

  if pid_running "$pid"; then
    return
  fi

  rm -f "$PID_FILE"
}

resolve_daprd_path() {
  if command -v daprd >/dev/null 2>&1; then
    command -v daprd
    return
  fi

  if [[ -x "$HOME/.dapr/bin/daprd" ]]; then
    printf '%s\n' "$HOME/.dapr/bin/daprd"
    return
  fi

  fail "Could not find 'daprd'. Run 'dapr init --slim' first."
}

start_sidecar() {
  local daprd_path="$1"

  mkdir -p "$(dirname "$LOG_FILE")"

  nohup "$daprd_path" \
    --app-id "$APP_ID" \
    --app-port "$APP_PORT" \
    --app-channel-address "127.0.0.1" \
    --dapr-http-port "$DAPR_HTTP_PORT" \
    --dapr-grpc-port "$DAPR_GRPC_PORT" \
    --placement-host-address "$PLACEMENT_HOST_ADDRESS" \
    --scheduler-host-address "$SCHEDULER_HOST_ADDRESS" \
    --resources-path "$RESOURCES_PATH" \
    >"$LOG_FILE" 2>&1 < /dev/null &

  local pid=$!
  echo "$pid" > "$PID_FILE"
}

wait_for_sidecar_port() {
  local start_time
  start_time="$(date +%s)"

  while ! port_listening "$DAPR_HTTP_PORT"; do
    local now
    now="$(date +%s)"

    if (( now - start_time >= START_TIMEOUT_SECONDS )); then
      fail "Timed out waiting for the Dapr HTTP port ${DAPR_HTTP_PORT} to open. See ${LOG_FILE}."
    fi

    if [[ -f "$PID_FILE" ]]; then
      local pid
      pid="$(cat "$PID_FILE" 2>/dev/null || true)"
      if [[ -n "$pid" ]] && ! pid_running "$pid"; then
        fail "The sidecar process exited during startup. See ${LOG_FILE}."
      fi
    fi

    sleep 1
  done
}

main() {
  require_command curl
  require_command nc

  cd "$REPO_ROOT"

  cleanup_stale_pid_file

  if sidecar_metadata >/dev/null 2>&1; then
    if sidecar_healthy; then
      log "Reusing existing Dapr sidecar on port ${DAPR_HTTP_PORT}."
      exit 0
    fi

    fail "A Dapr sidecar is reachable on port ${DAPR_HTTP_PORT}, but it is not the '${APP_ID}' sidecar."
  fi

  if sidecar_healthy; then
    log "Reusing existing Dapr sidecar on port ${DAPR_HTTP_PORT}."
    exit 0
  fi

  if port_listening "$DAPR_HTTP_PORT"; then
    fail "Port ${DAPR_HTTP_PORT} is already in use by something other than the ${APP_ID} sidecar."
  fi

  if port_listening "$DAPR_GRPC_PORT"; then
    fail "Port ${DAPR_GRPC_PORT} is already in use by something other than the ${APP_ID} sidecar."
  fi

  if [[ ! -d "$RESOURCES_PATH" ]]; then
    fail "Expected resources path '${RESOURCES_PATH}' does not exist. Run ./infra/ensure-up.sh first."
  fi

  local daprd_path
  daprd_path="$(resolve_daprd_path)"

  start_sidecar "$daprd_path"
  wait_for_sidecar_port

  log "Started Dapr sidecar for ${APP_ID}. Logs: ${LOG_FILE}"
}

main "$@"
