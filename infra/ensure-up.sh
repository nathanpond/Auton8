#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/infra/docker-compose.yml"
COMPOSE=(docker compose -f "$COMPOSE_FILE")
FLOWABLE_BUILD_STAMP_FILE="$REPO_ROOT/infra/mounts/flowable/.build-input-hash"

POSTGRES_PORT="${AUTONATE_POSTGRES_PORT:-5432}"
FLOWABLE_PORT="${AUTONATE_FLOWABLE_PORT:-8080}"
REDIS_PORT="${AUTONATE_REDIS_PORT:-6379}"
NATS_PORT="${AUTONATE_NATS_PORT:-4222}"
DAPR_PLACEMENT_PORT="${AUTONATE_DAPR_PLACEMENT_PORT:-50006}"
DAPR_SCHEDULER_PORT="${AUTONATE_DAPR_SCHEDULER_PORT:-50007}"
HOCUSPOCUS_PORT="${AUTONATE_HOCUSPOCUS_PORT:-1234}"
WAIT_TIMEOUT_SECONDS="${AUTONATE_INFRA_WAIT_TIMEOUT_SECONDS:-120}"
POLL_INTERVAL_SECONDS=2

REQUIRED_SERVICES=(
  postgres
  flowable
  flowable-dapr
  redis
  nats
  nats-init
  dapr-placement
  dapr-scheduler
  hocuspocus
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

compute_flowable_build_hash() {
  local path
  local hash_input=()

  hash_input+=("$REPO_ROOT/infra/flowable/Dockerfile")
  hash_input+=("$REPO_ROOT/flowable-extension/pom.xml")

  while IFS= read -r path; do
    hash_input+=("$path")
  done < <(find "$REPO_ROOT/flowable-extension/src" -type f | LC_ALL=C sort)

  for path in "${hash_input[@]}"; do
    printf '%s\n' "$path"
    shasum "$path"
  done | shasum | awk '{print $1}'
}

current_flowable_build_hash() {
  if [[ -f "$FLOWABLE_BUILD_STAMP_FILE" ]]; then
    cat "$FLOWABLE_BUILD_STAMP_FILE"
    return 0
  fi

  return 1
}

flowable_build_required() {
  local desired_hash="$1"
  local current_hash

  if ! current_hash="$(current_flowable_build_hash)"; then
    return 0
  fi

  [[ "$current_hash" != "$desired_hash" ]]
}

record_flowable_build_hash() {
  printf '%s\n' "$1" > "$FLOWABLE_BUILD_STAMP_FILE"
}

compose_service_container_id() {
  "${COMPOSE[@]}" ps -a -q "$1"
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
    flowable-dapr)
      [[ "$(container_health_or_status "$container_id")" == "running" ]]
      ;;
    dapr-placement)
      tcp_reachable "$DAPR_PLACEMENT_PORT"
      ;;
    dapr-scheduler)
      tcp_reachable "$DAPR_SCHEDULER_PORT"
      ;;
    nats)
      tcp_reachable "$NATS_PORT"
      ;;
    nats-init)
      [[ "$(container_health_or_status "$container_id")" == "exited" ]]
      ;;
    hocuspocus)
      # The sidecar has no docker healthcheck; an open TCP socket on the
      # WebSocket port is the cheapest "actually accepting connections"
      # signal we have.
      tcp_reachable "$HOCUSPOCUS_PORT"
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
      flowable-dapr)
        log "$service: $(container_health_or_status "$container_id")"
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
      nats)
        if tcp_reachable "$NATS_PORT"; then
          log "$service: reachable on 127.0.0.1:${NATS_PORT}"
        else
          log "$service: container present, port ${NATS_PORT} not ready"
        fi
        ;;
      nats-init)
        log "$service: $(container_health_or_status "$container_id")"
        ;;
      hocuspocus)
        if tcp_reachable "$HOCUSPOCUS_PORT"; then
          log "$service: reachable on 127.0.0.1:${HOCUSPOCUS_PORT}"
        else
          log "$service: container present, port ${HOCUSPOCUS_PORT} not ready"
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
  require_command shasum
  require_command awk

  cd "$REPO_ROOT"

  docker info >/dev/null 2>&1 || fail "Docker is not available. Start Docker Desktop and try again."

  mkdir -p \
    "$REPO_ROOT/infra/mounts/postgres/data" \
    "$REPO_ROOT/infra/mounts/redis/data" \
    "$REPO_ROOT/infra/mounts/nats/data" \
    "$REPO_ROOT/infra/mounts/dapr-scheduler/data" \
    "$REPO_ROOT/infra/mounts/dapr-dashboard/components" \
    "$REPO_ROOT/infra/mounts/flowable-dapr/components" \
    "$REPO_ROOT/infra/mounts/flowable" \
    "$REPO_ROOT/infra/mounts/dapr-placement"

  cp "$REPO_ROOT"/infra/dapr/components/*.yaml "$REPO_ROOT/infra/mounts/dapr-dashboard/components/"
  cp "$REPO_ROOT"/infra/dapr/components/pubsub.yaml "$REPO_ROOT/infra/mounts/flowable-dapr/components/"
  sed -i.bak 's|nats://localhost:4222|nats://host.docker.internal:4222|' "$REPO_ROOT/infra/mounts/flowable-dapr/components/pubsub.yaml"
  rm -f "$REPO_ROOT/infra/mounts/flowable-dapr/components/pubsub.yaml.bak"

  local desired_flowable_hash
  desired_flowable_hash="$(compute_flowable_build_hash)"

  local should_rebuild_flowable=0
  if flowable_build_required "$desired_flowable_hash"; then
    should_rebuild_flowable=1
    log "Flowable build inputs changed. Rebuilding the Flowable image."
    "${COMPOSE[@]}" build flowable
    record_flowable_build_hash "$desired_flowable_hash"
  fi

  if (( should_rebuild_flowable == 0 )) && all_services_ready; then
    log "Required infrastructure is already running and ready."
    exit 0
  fi

  if all_services_bootstrapped; then
    if (( should_rebuild_flowable == 1 )); then
      log "Recreating Flowable services to apply the rebuilt image."
      "${COMPOSE[@]}" up -d --no-deps --force-recreate flowable flowable-dapr
    else
      log "Infrastructure containers exist but are not ready yet. Waiting for readiness."
    fi
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

  "$REPO_ROOT/infra/ensure-nats-stream.sh"

  log "Required infrastructure is ready."
}

main "$@"
