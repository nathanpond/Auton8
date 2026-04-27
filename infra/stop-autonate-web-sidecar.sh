#!/usr/bin/env bash

# Companion to start-autonate-web-sidecar.sh: terminates the daprd process the
# start script launched (PID recorded in the PID file under $TMPDIR) and waits
# until the sidecar's HTTP port is no longer listening before returning. Idempotent.

set -euo pipefail

PID_FILE="${TMPDIR:-/tmp}/autonate-web-daprd.pid"
DAPR_HTTP_PORT="3500"
STOP_TIMEOUT_SECONDS="${AUTONATE_DAPR_SIDECAR_STOP_TIMEOUT_SECONDS:-15}"

log() {
  printf '[autonate-web-sidecar] %s\n' "$*"
}

port_listening() {
  nc -z 127.0.0.1 "$1" >/dev/null 2>&1
}

pid_running() {
  kill -0 "$1" >/dev/null 2>&1
}

wait_until_stopped() {
  local pid="$1"
  local start_time
  start_time="$(date +%s)"

  while pid_running "$pid" || port_listening "$DAPR_HTTP_PORT"; do
    local now
    now="$(date +%s)"
    if (( now - start_time >= STOP_TIMEOUT_SECONDS )); then
      log "Sidecar still running after ${STOP_TIMEOUT_SECONDS}s; sending SIGKILL."
      kill -9 "$pid" >/dev/null 2>&1 || true
      sleep 1
      return
    fi
    sleep 1
  done
}

main() {
  if [[ ! -f "$PID_FILE" ]]; then
    if port_listening "$DAPR_HTTP_PORT"; then
      log "No PID file at ${PID_FILE}, but port ${DAPR_HTTP_PORT} is in use. Leaving it alone."
      exit 0
    fi
    log "Sidecar is not running."
    exit 0
  fi

  local pid
  pid="$(cat "$PID_FILE" 2>/dev/null || true)"

  if [[ -z "$pid" ]]; then
    rm -f "$PID_FILE"
    log "Cleared empty PID file."
    exit 0
  fi

  if ! pid_running "$pid"; then
    rm -f "$PID_FILE"
    log "Cleared stale PID ${pid} (process not running)."
    exit 0
  fi

  log "Stopping sidecar (PID ${pid})..."
  kill "$pid" >/dev/null 2>&1 || true
  wait_until_stopped "$pid"
  rm -f "$PID_FILE"
  log "Sidecar stopped."
}

main "$@"
