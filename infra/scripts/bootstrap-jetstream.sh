#!/bin/sh

set -eu

STREAM_NAME="workflow-execution"
SUBJECT="workflow.execution.>"

until nc -z nats 4222 >/dev/null 2>&1; do
  sleep 1
done

if nats --server nats://nats:4222 stream info "${STREAM_NAME}" >/dev/null 2>&1; then
  nats --server nats://nats:4222 stream edit "${STREAM_NAME}" --subjects "${SUBJECT}" --force >/dev/null
else
  nats --server nats://nats:4222 stream add "${STREAM_NAME}" --subjects "${SUBJECT}" --retention limits --defaults >/dev/null
fi
