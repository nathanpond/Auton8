#!/bin/sh
# Wait for the Dapr sidecar before starting the app.
#
# AutoNate.Web refuses to start without a reachable sidecar, and daprd needs
# the app container's network namespace to exist before it can bind. Neither
# can be "first": on the host, `dapr run` resolves this by starting the sidecar
# and then launching the app as its child. In compose there is no such parent,
# and relying on the app crashing until the sidecar happens to be ready is a
# race that resolves slowly, noisily, or not at all.
#
# /v1.0/healthz/outbound is the endpoint for exactly this question. Plain
# /v1.0/healthz includes the *application's* health, so waiting on it would
# deadlock — the app is what we are about to start. The outbound variant
# reports only that the sidecar is ready to serve calls.
#
# Skipped entirely when the app is deliberately running without Dapr.
set -e

if [ "${AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR:-}" != "true" ]; then
    DAPR_PORT="${DAPR_HTTP_PORT:-3500}"
    DEADLINE=$(( $(date +%s) + ${DAPR_WAIT_TIMEOUT_SECONDS:-90} ))

    echo "entrypoint: waiting for the Dapr sidecar on 127.0.0.1:${DAPR_PORT}"
    until curl -fsS "http://127.0.0.1:${DAPR_PORT}/v1.0/healthz/outbound" >/dev/null 2>&1; do
        if [ "$(date +%s)" -ge "$DEADLINE" ]; then
            echo "entrypoint: the Dapr sidecar did not become ready on 127.0.0.1:${DAPR_PORT}." >&2
            echo "entrypoint: the app requires one. Check the autonate-web-dapr container's logs" >&2
            echo "entrypoint: — a component that fails to initialise makes daprd exit fatally." >&2
            exit 1
        fi
        sleep 1
    done
    echo "entrypoint: Dapr sidecar ready"
fi

exec dotnet AutoNate.Web.dll "$@"
