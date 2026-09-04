#!/bin/sh
# Preconditions for the `keycloak` compose profile.
#
# Both of these fail late and confusingly if left to chance — one as a Java
# stack trace on startup, the other as an issuer mismatch inside an OIDC library
# — so they are checked here where the message can say what to do about it.
#
# POSIX sh, like infra/preflight.sh: macOS still ships bash 3.2.

set -u

ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/../.." && pwd)
ENV_FILE="$ROOT/.env"
PROBLEMS=0

note() {
    printf '\n  %s\n' "$1"
    PROBLEMS=$((PROBLEMS + 1))
}

read_env() {
    [ -f "$ENV_FILE" ] || return 0
    sed -n "s/^$1=//p" "$ENV_FILE" | tail -1
}

# ── 1. Admin credentials, with no working default ───────────────────────────
KC_USER=$(read_env AUTONATE_KEYCLOAK_ADMIN_USER)
KC_PASS=$(read_env AUTONATE_KEYCLOAK_ADMIN_PASSWORD)

if [ -z "${KC_USER:-}" ] || [ -z "${KC_PASS:-}" ]; then
    note "Keycloak has no admin credentials.

  Set both in $ENV_FILE (see .env.example):

      AUTONATE_KEYCLOAK_ADMIN_USER=kcadmin
      AUTONATE_KEYCLOAK_ADMIN_PASSWORD=\$(openssl rand -base64 18)

  There is deliberately no default that works. A committed admin password is
  the defect this project already shipped once, and the same rule applies to
  development dependencies."
fi

# ── 2. The hostname the issuer URL depends on ───────────────────────────────
PORT=$(read_env AUTONATE_KEYCLOAK_PORT)
[ -n "${PORT:-}" ] || PORT=8082

RESOLVED=$(getent hosts keycloak 2>/dev/null | awk '{print $1}' | head -1)
if [ -z "$RESOLVED" ]; then
    # getent is absent on macOS; fall back to the resolver Python already has.
    RESOLVED=$(python3 -c "import socket,sys
try: sys.stdout.write(socket.gethostbyname('keycloak'))
except OSError: pass" 2>/dev/null)
fi

case "$RESOLVED" in
    127.0.0.1|::1) ;;
    "")
        note "The name 'keycloak' does not resolve on this machine.

  Keycloak's issuer is http://keycloak:$PORT, chosen so that ONE url works from
  the browser, from Auton8 on the host, and from Auton8 in a container. The
  compose network resolves it for containers; the host needs one line:

      echo '127.0.0.1 keycloak' | sudo tee -a /etc/hosts

  Without it the browser cannot reach Keycloak at all, and the failure shows up
  as an issuer mismatch inside an OIDC library rather than as a name that does
  not resolve."
        ;;
    *)
        note "The name 'keycloak' resolves to $RESOLVED, not 127.0.0.1.

  The published port binds to loopback, so the browser will not reach it at
  that address. Fix the /etc/hosts entry for 'keycloak'."
        ;;
esac

if [ "$PROBLEMS" -gt 0 ]; then
    printf '\n%s problem(s) to fix before the keycloak profile will work.\n\n' "$PROBLEMS"
    exit 1
fi

exit 0
