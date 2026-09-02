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
    # First dotted number in the input, e.g. "Docker version 27.3.1, build ..." -> 27.3.1
    sed -n 's/.*[^0-9]\([0-9][0-9]*\.[0-9][0-9]*\(\.[0-9][0-9]*\)*\).*/\1/p' | head -n1
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
    PORTS=$(awk -v want_profiles=" $PROFILES " '
        # Track the current service, whether it declares profiles, and its ports.
        /^services:/ { in_services = 1; next }
        !in_services { next }
        /^[^[:space:]]/ { in_services = 0; next }

        # A service key is exactly two spaces of indent.
        /^  [A-Za-z0-9_.-]+:[[:space:]]*$/ {
            service = $1; sub(/:$/, "", service)
            in_ports = 0; in_profiles = 0
            profile_ok[service] = 1          # no profile block == always started
            next
        }

        /^    profiles:/ { in_profiles = 1; in_ports = 0; profile_ok[service] = 0; next }
        in_profiles && /^      - / {
            p = $2
            if (index(want_profiles, " " p " ") > 0) profile_ok[service] = 1
            next
        }
        in_profiles && /^    [A-Za-z]/ { in_profiles = 0 }

        /^    ports:/ { in_ports = 1; next }
        in_ports && /^      - / {
            line = $0
            gsub(/^[[:space:]]*-[[:space:]]*/, "", line)
            gsub(/"/, "", line); gsub(/'\''/, "", line)
            # Host port is the field before the final colon-separated container
            # port. Strip ${VAR:-default} first, keeping the default.
            while (match(line, /\$\{[^}]*\}/)) {
                token = substr(line, RSTART, RLENGTH)
                dflt = token
                sub(/^\$\{[^:]*:-/, "", dflt); sub(/\}$/, "", dflt)
                if (dflt == token) dflt = ""      # ${VAR} with no default
                line = substr(line, 1, RSTART - 1) dflt substr(line, RSTART + RLENGTH)
            }
            n = split(line, parts, ":")
            if (n >= 2) {
                host = parts[n - 1]
                if (host ~ /^[0-9]+$/) print service "|" host
            }
            next
        }
        in_ports && /^    [A-Za-z]/ { in_ports = 0 }
        END { }
    ' "$COMPOSE_FILE" | while IFS='|' read -r svc port; do
        echo "$svc|$port"
    done)

    # Re-filter by profile in the shell: awk cannot easily emit its profile map.
    ACTIVE=$(awk -v want_profiles=" $PROFILES " '
        /^services:/ { in_services = 1; next }
        !in_services { next }
        /^[^[:space:]]/ { in_services = 0; next }
        /^  [A-Za-z0-9_.-]+:[[:space:]]*$/ {
            service = $1; sub(/:$/, "", service); active[service] = 1; in_profiles = 0; next
        }
        /^    profiles:/ { in_profiles = 1; active[service] = 0; next }
        in_profiles && /^      - / {
            p = $2
            if (index(want_profiles, " " p " ") > 0) active[service] = 1
            next
        }
        in_profiles && /^    [A-Za-z]/ { in_profiles = 0 }
        END { for (s in active) if (active[s]) print s }
    ' "$COMPOSE_FILE")

    CHECKED=0
    echo "$PORTS" | while IFS='|' read -r svc port; do
        [ -n "$port" ] || continue
        case "
$ACTIVE
" in
            *"
$svc
"*) ;;
            *) continue ;;
        esac
        CHECKED=$((CHECKED + 1))
        if port_in_use "$port"; then
            holder=$(port_holder "$port")
            if [ -n "$holder" ]; then
                printf '  %-16s %-6s IN USE by %s\n' "$svc" "$port" "$holder"
                echo "$svc: port $port already in use by '$holder'
    remedy: stop it, or remap the port in infra/docker-compose.override.yml" >> "$SCRIPT_DIR/.preflight-port-failures"
            else
                printf '  %-16s %-6s IN USE\n' "$svc" "$port"
                echo "$svc: port $port already in use
    remedy: stop it, or remap the port in infra/docker-compose.override.yml" >> "$SCRIPT_DIR/.preflight-port-failures"
            fi
        else
            printf '  %-16s %-6s free\n' "$svc" "$port"
        fi
    done
fi

# The port loop runs in a subshell (pipeline), so its failures come back
# through a file rather than through FAILURES.
PORT_FAIL_FILE="$SCRIPT_DIR/.preflight-port-failures"
if [ -f "$PORT_FAIL_FILE" ]; then
    while IFS= read -r line; do
        case "$line" in
            "    remedy: "*) FAILURE_TEXT="${FAILURE_TEXT}${line}
" ;;
            *) FAILURES=$((FAILURES + 1)); FAILURE_TEXT="${FAILURE_TEXT}${line}
" ;;
        esac
    done < "$PORT_FAIL_FILE"
    rm -f "$PORT_FAIL_FILE"
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
