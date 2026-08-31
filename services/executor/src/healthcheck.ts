import { connect, StringCodec } from "nats";

// Docker healthcheck for the executor sidecar: asks the running process (over
// NATS, the only interface it has) whether it is connected and serving. Exit 0
// on a well-formed `{ ok: true }` reply within the timeout, 1 otherwise.
//
// Used by infra/docker-compose.yml (`healthcheck.test`) and, through the
// container health state, by infra/ensure-up.sh's readiness wait.

const NATS_URL = process.env.NATS_URL ?? "nats://localhost:4222";
const HEALTH_SUBJECT = "executor.health";
const TIMEOUT_MS = 3000;

const codec = StringCodec();

try {
  const nc = await connect({ servers: NATS_URL, timeout: TIMEOUT_MS, maxReconnectAttempts: 0 });
  try {
    const reply = await nc.request(HEALTH_SUBJECT, codec.encode("{}"), { timeout: TIMEOUT_MS });
    const parsed = JSON.parse(codec.decode(reply.data)) as { ok?: boolean };
    if (parsed.ok !== true) {
      console.error("[executor-health] unexpected reply:", codec.decode(reply.data));
      process.exit(1);
    }
    process.exit(0);
  } finally {
    await nc.close();
  }
} catch (err) {
  console.error("[executor-health]", err instanceof Error ? err.message : String(err));
  process.exit(1);
}
