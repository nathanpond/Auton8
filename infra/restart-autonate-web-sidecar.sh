#!/usr/bin/env bash

# Stop, then start the AutoNate web Dapr sidecar. Use this after publishing a
# workflow with a new signal start event topic — Dapr only fetches subscriptions
# at sidecar startup, so a restart is what makes the new topic flow through.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"$SCRIPT_DIR/stop-autonate-web-sidecar.sh"
"$SCRIPT_DIR/start-autonate-web-sidecar.sh"
