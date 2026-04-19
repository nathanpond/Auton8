#!/usr/bin/env bash

set -euo pipefail

APP_ID="autonate-web"
DAPR_HTTP_PORT="3500"

log() {
  printf '[autonate-web-sidecar-check] %s\n' "$*"
}

fail() {
  printf '[autonate-web-sidecar-check] ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    fail "Required command '$1' is not available."
  fi
}

main() {
  require_command curl

  local metadata
  metadata="$(curl -fsS "http://127.0.0.1:${DAPR_HTTP_PORT}/v1.0/metadata" 2>/dev/null || true)"

  if [[ -z "$metadata" ]]; then
    fail "No Dapr sidecar is reachable on http://127.0.0.1:${DAPR_HTTP_PORT}. Run 'dapr: AutoNate.Web Sidecar' first."
  fi

  if ! grep -q "\"id\":\"${APP_ID}\"" <<< "$metadata"; then
    fail "A Dapr sidecar is listening on port ${DAPR_HTTP_PORT}, but it is not the '${APP_ID}' sidecar. Run 'dapr: AutoNate.Web Sidecar' first."
  fi

  log "Confirmed ${APP_ID} Dapr sidecar is running on port ${DAPR_HTTP_PORT}."
}

main "$@"
