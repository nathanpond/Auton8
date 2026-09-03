#!/bin/sh
# Preflight check for the Auton8 local stack.
#
# Verifies the documented prerequisites and the ports the stack needs, and
# reports EVERY problem rather than the first — a contributor fixing a machine
# should need one pass, not one pass per missing tool.
#
# Required versions come from infra/prerequisites, which is the single source
# for them. Ports are derived from infra/docker-compose.yml rather than listed
# here, so a service added later is covered without editing this script.
#
# Usage:
#   infra/preflight.sh              check the default stack
#   infra/preflight.sh --profile X  also check ports for services in profile X
#
# POSIX sh on purpose: macOS still ships bash 3.2, so a bash-first version
# wanting associative arrays would not run on the machines this most needs to
# work on.

set -u

SCRIPT_DIR=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
PREREQ_FILE="${AUTONATE_PREREQ_FILE:-$SCRIPT_DIR/prerequisites}"
COMPOSE_FILE="${AUTONATE_COMPOSE_FILE:-$SCRIPT_DIR/docker-compose.yml}"

PROFILES=""
while [ $# -gt 0 ]; do
    case "$1" in
        --profile)
            shift
            [ $# -gt 0 ] || { echo "preflight: --profile needs a value" >&2; exit 2; }
            PROFILES="$PROFILES $1"
            ;;
        --profile=*)
            PROFILES="$PROFILES ${1#--profile=}"
            ;;
        *)
            echo "preflight: unknown argument '$1'" >&2
            exit 2
            ;;
    esac
    shift
done

FAILURES=0
FAILURE_TEXT=""

fail() {
    FAILURES=$((FAILURES + 1))
    FAILURE_TEXT="${FAILURE_TEXT}$1
"
}

# Compare dotted versions. Returns 0 when $1 >= $2.
version_ge() {
    [ "$1" = "$2" ] && return 0
    lower=$(printf '%s\n%s\n' "$1" "$2" | sort -t. -k1,1n -k2,2n -k3,3n | head -n1)
    [ "$lower" = "$2" ]
}

first_version() {
    # FIRST dotted number in the input. Greedy matching grabs the wrong one on
    # every tool here: "Docker version 25.0.3, build 4debf41" yields the build
    # hash, "v2.24.5-desktop.1" yields "24.5", and dapr prints two versions on
    # two lines. awk's match() is leftmost, which is the one we want.
    awk '{
        if (match($0, /[0-9]+\.[0-9]+(\.[0-9]+)?/)) {
            print substr($0, RSTART, RLENGTH); exit
        }
    }'
}

echo "Auton8 preflight"
echo "----------------"

# ── Tools ───────────────────────────────────────────────────────────────────

while IFS='|' read -r name command version_flag minimum install; do
    case "$name" in ''|\#*) continue ;; esac

    # `docker compose` is two words; splitting on whitespace is deliberate.
    # shellcheck disable=SC2086
    set -- $command
    binary=$1

    if ! command -v "$binary" >/dev/null 2>&1; then
        printf '  %-16s NOT FOUND (need >= %s)\n' "$name" "$minimum"
        fail "$name: not found; need >= $minimum
    install: $install"
        continue
    fi

    found=$($command $version_flag 2>/dev/null | first_version)

    if [ -z "$found" ]; then
        printf '  %-16s present, version unreadable\n' "$name"
        fail "$name: installed but '$command $version_flag' produced no version; need >= $minimum
    install: $install"
        continue
    fi

    if version_ge "$found" "$minimum"; then
        printf '  %-16s %s\n' "$name" "$found"
    else
        printf '  %-16s %s  TOO OLD (need >= %s)\n' "$name" "$found" "$minimum"
        fail "$name: found $found, need >= $minimum
    install: $install"
    fi
done < "$PREREQ_FILE"

# ── Ports ───────────────────────────────────────────────────────────────────
#
# Derived from the compose file. A service that belongs to a profile is only
# checked when that profile was requested — otherwise `make infra-up` would
# demand a free port for a container it is not going to start.

port_in_use() {
    if command -v lsof >/dev/null 2>&1; then
        lsof -nP -iTCP:"$1" -sTCP:LISTEN >/dev/null 2>&1 && return 0
        return 1
    fi
    if command -v nc >/dev/null 2>&1; then
        nc -z 127.0.0.1 "$1" >/dev/null 2>&1 && return 0
        return 1
    fi
    return 1
}

port_holder() {
    if command -v lsof >/dev/null 2>&1; then
        lsof -nP -iTCP:"$1" -sTCP:LISTEN -Fcn 2>/dev/null \
            | sed -n 's/^c//p' | head -n1
    fi
}

echo ""
echo "Ports (from $(basename "$COMPOSE_FILE"))"

if [ ! -f "$COMPOSE_FILE" ]; then
    fail "compose file not found at $COMPOSE_FILE"
else
    # Services this stack already has running. Their ports being bound is
    # expected, not a conflict — `make infra-ensure` exists precisely to be
    # re-runnable against a stack that is already up, and reporting our own
    # Postgres as a collision would make it refuse every time after the first.
    RUNNING=$(docker compose -f "$COMPOSE_FILE" ps --services --status running 2>/dev/null || true)

    PORT_LIST=$(mktemp)
    awk -v want_profiles=" $PROFILES " '
        /^services:/ { in_services = 1; next }
        !in_services { next }
        /^[^[:space:]]/ { in_services = 0; next }

        /^  [A-Za-z0-9_.-]+:[[:space:]]*$/ {
            service = $1; sub(/:$/, "", service)
            gated[service] = 0; started[service] = 1
            in_ports = 0; in_profiles = 0
            next
        }

        /^    profiles:/ { in_profiles = 1; in_ports = 0; gated[service] = 1; started[service] = 0; next }
        in_profiles && /^      - / {
            p = $2
            if (index(want_profiles, " " p " ") > 0) started[service] = 1
            next
        }
        in_profiles && /^    [A-Za-z]/ { in_profiles = 0 }

        /^    ports:/ { in_ports = 1; next }
        in_ports && /^      - / {
            line = $0
            sub(/^[[:space:]]*-[[:space:]]*/, "", line)
            gsub(/"/, "", line); gsub(/\047/, "", line)
            while (match(line, /\$\{[^}]*\}/)) {
                token = substr(line, RSTART, RLENGTH)
                dflt = token
                sub(/^\$\{[^:]*:-/, "", dflt); sub(/\}$/, "", dflt)
                if (dflt == token) dflt = ""
                line = substr(line, 1, RSTART - 1) dflt substr(line, RSTART + RLENGTH)
            }
            n = split(line, parts, ":")
            if (n >= 2) {
                host = parts[n - 1]
                if (host ~ /^[0-9]+$/) ports[service] = ports[service] " " host
            }
            next
        }
        in_ports && /^    [A-Za-z]/ { in_ports = 0 }

        END {
            for (s in ports) {
                if (!started[s]) continue
                n = split(ports[s], list, " ")
                for (i = 1; i <= n; i++) if (list[i] != "") print s "|" list[i]
            }
        }
    ' "$COMPOSE_FILE" | sort > "$PORT_LIST"

    if [ ! -s "$PORT_LIST" ]; then
        fail "no published ports were found in $COMPOSE_FILE — the port check would pass vacuously, which is worse than not running it"
    fi

    # Read in this shell, not a pipeline: a `while read` on the right of a pipe
    # runs in a subshell and its FAILURES increments are lost on exit.
    while IFS='|' read -r svc port; do
        [ -n "$port" ] || continue

        case "
$RUNNING
" in
            *"
$svc
"*)
                printf '  %-16s %-6s in use by this stack (already running)\n' "$svc" "$port"
                continue
                ;;
        esac

        if port_in_use "$port"; then
            holder=$(port_holder "$port")
            [ -n "$holder" ] || holder="an unidentified process"
            printf '  %-16s %-6s IN USE by %s\n' "$svc" "$port" "$holder"
            fail "$svc: port $port is already in use by '$holder'
    remedy: stop it, or remap the port in infra/docker-compose.override.yml"
        else
            printf '  %-16s %-6s free\n' "$svc" "$port"
        fi
    done < "$PORT_LIST"

    rm -f "$PORT_LIST"
fi

echo ""
if [ "$FAILURES" -eq 0 ]; then
    echo "All checks passed."
    exit 0
fi

echo "$FAILURES problem(s) found:"
echo ""
printf '%s' "$FAILURE_TEXT"
echo ""
echo "Fix all of the above, then re-run: make preflight"
exit 1
