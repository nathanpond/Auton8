#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/infra/docker-compose.yml"
COMPOSE=(docker compose -f "$COMPOSE_FILE")

POSTGRES_PORT="${AUTONATE_POSTGRES_PORT:-5432}"
FLOWABLE_PORT="${AUTONATE_FLOWABLE_PORT:-8080}"
REDIS_PORT="${AUTONATE_REDIS_PORT:-6379}"
DAPR_PLACEMENT_PORT="${AUTONATE_DAPR_PLACEMENT_PORT:-50006}"
DAPR_SCHEDULER_PORT="${AUTONATE_DAPR_SCHEDULER_PORT:-50007}"
WAIT_TIMEOUT_SECONDS="${AUTONATE_INFRA_WAIT_TIMEOUT_SECONDS:-120}"
POLL_INTERVAL_SECONDS=2

REQUIRED_SERVICES=(
  postgres
  flowable
  redis
  dapr-placement
  dapr-scheduler
)

log() {
  printf '[infra-ensure] %s\n' "$*"
}

fail() {
  printf '[infra-ensure] ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    fail "Required command '$1' is not available."
  fi
}

compose_service_container_id() {
  "${COMPOSE[@]}" ps -q "$1"
}

container_health_or_status() {
  docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$1"
}

tcp_reachable() {
  nc -z 127.0.0.1 "$1" >/dev/null 2>&1
}

flowable_reachable() {
  curl --fail --silent --show-error --output /dev/null "http://127.0.0.1:${FLOWABLE_PORT}/flowable-rest"
}

service_bootstrapped() {
  local service="$1"
  local container_id
  container_id="$(compose_service_container_id "$service")"

  [[ -n "$container_id" ]]
}

service_ready() {
  local service="$1"
  local container_id
  container_id="$(compose_service_container_id "$service")"

  if [[ -z "$container_id" ]]; then
    return 1
  fi

  case "$service" in
    postgres|redis)
      [[ "$(container_health_or_status "$container_id")" == "healthy" ]]
      ;;
    flowable)
      flowable_reachable
      ;;
    dapr-placement)
      tcp_reachable "$DAPR_PLACEMENT_PORT"
      ;;
    dapr-scheduler)
      tcp_reachable "$DAPR_SCHEDULER_PORT"
      ;;
    *)
      [[ "$(container_health_or_status "$container_id")" == "running" ]]
      ;;
  esac
}

all_services_bootstrapped() {
  local service
  for service in "${REQUIRED_SERVICES[@]}"; do
    if ! service_bootstrapped "$service"; then
      return 1
    fi
  done
}

all_services_ready() {
  local service
  for service in "${REQUIRED_SERVICES[@]}"; do
    if ! service_ready "$service"; then
      return 1
    fi
  done
}

print_status_snapshot() {
  local service

  for service in "${REQUIRED_SERVICES[@]}"; do
    local container_id
    container_id="$(compose_service_container_id "$service")"

    if [[ -z "$container_id" ]]; then
      log "$service: missing"
      continue
    fi

    case "$service" in
      postgres|redis)
        log "$service: $(container_health_or_status "$container_id")"
        ;;
      flowable)
        if flowable_reachable; then
          log "$service: reachable on http://127.0.0.1:${FLOWABLE_PORT}/flowable-rest"
        else
          log "$service: container present, endpoint not ready"
        fi
        ;;
      dapr-placement)
        if tcp_reachable "$DAPR_PLACEMENT_PORT"; then
          log "$service: reachable on 127.0.0.1:${DAPR_PLACEMENT_PORT}"
        else
          log "$service: container present, port ${DAPR_PLACEMENT_PORT} not ready"
        fi
        ;;
      dapr-scheduler)
        if tcp_reachable "$DAPR_SCHEDULER_PORT"; then
          log "$service: reachable on 127.0.0.1:${DAPR_SCHEDULER_PORT}"
        else
          log "$service: container present, port ${DAPR_SCHEDULER_PORT} not ready"
        fi
        ;;
    esac
  done
}

main() {
  require_command docker
  require_command curl
  require_command nc
  require_command mkdir
  require_command cp

  cd "$REPO_ROOT"

  docker info >/dev/null 2>&1 || fail "Docker is not available. Start Docker Desktop and try again."

  mkdir -p \
    "$REPO_ROOT/infra/mounts/postgres/data" \
    "$REPO_ROOT/infra/mounts/redis/data" \
    "$REPO_ROOT/infra/mounts/dapr-scheduler/data" \
    "$REPO_ROOT/infra/mounts/dapr-dashboard/components" \
    "$REPO_ROOT/infra/mounts/flowable" \
    "$REPO_ROOT/infra/mounts/dapr-placement"

  cp "$REPO_ROOT"/infra/dapr/components/*.yaml "$REPO_ROOT/infra/mounts/dapr-dashboard/components/"

  if all_services_ready; then
    log "Required infrastructure is already running and ready."
    exit 0
  fi

  if all_services_bootstrapped; then
    log "Infrastructure containers exist but are not ready yet. Waiting for readiness."
  else
    log "Starting required infrastructure from infra/docker-compose.yml."
    "${COMPOSE[@]}" up -d "${REQUIRED_SERVICES[@]}"
  fi

  local start_time
  start_time="$(date +%s)"

  while ! all_services_ready; do
    local now
    now="$(date +%s)"

    if (( now - start_time >= WAIT_TIMEOUT_SECONDS )); then
      log "Timed out waiting for local infrastructure."
      print_status_snapshot
      fail "Compose stack did not become ready within ${WAIT_TIMEOUT_SECONDS}s."
    fi

    sleep "$POLL_INTERVAL_SECONDS"
  done

  log "Required infrastructure is ready."
}

main "$@"
