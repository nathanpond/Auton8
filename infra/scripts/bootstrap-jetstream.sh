#!/bin/sh

set -eu

STREAM_NAME="workflow-execution"
# Must stay in sync with NatsStreamProvisioner.cs in AutoNate.Web. The .NET
# provisioner re-asserts the same set on every app boot, but this script
# runs from the nats-init container on every `compose up` and used to
# narrow the filter back to just `workflow.execution.>`. When that
# happened, publishes to `record.*` / `application.*` started failing with
# `nats: no response from stream` because no stream covered the subject.
SUBJECTS="workflow.execution.> record.> application.>"

until nc -z nats 4222 >/dev/null 2>&1; do
  sleep 1
done

if nats --server nats://nats:4222 stream info "${STREAM_NAME}" >/dev/null 2>&1; then
  nats --server nats://nats:4222 stream edit "${STREAM_NAME}" --subjects "${SUBJECTS}" --force >/dev/null
else
  nats --server nats://nats:4222 stream add "${STREAM_NAME}" --subjects "${SUBJECTS}" --retention limits --defaults >/dev/null
fi
