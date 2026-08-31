import { connect, NatsConnection, StringCodec } from "nats";
import { runJs } from "./jsRunner.js";
import { prewarmPython, runPython, shutdownPython } from "./pythonRunner.js";
import { CodeNodeReply, CodeNodeRequest } from "./wire.js";

// Entry point for the AutoNate executor sidecar. Connects to NATS,
// subscribes to `pipeline-code-run.>` as a core-NATS queue subscriber
// (queue group `executor`, so several replicas share the load), dispatches
// each request to the JS or Python runner, and replies on the supplied
// reply subject. Errors surface as `{ success: false, errorMessage: "..." }`
// payloads rather than disconnecting — the host expects a reply for every
// published message.
//
// It also answers `executor.health` (see healthcheck.ts) so docker-compose
// and `infra/ensure-up.sh` can tell "process is up" from "actually connected
// and serving". That subject deliberately lives outside the
// `pipeline-code-run.>` prefix, which a JetStream stream captures (#141).

const NATS_URL = process.env.NATS_URL ?? "nats://localhost:4222";
const SUBJECT = "pipeline-code-run.>";
export const HEALTH_SUBJECT = "executor.health";
const STARTED_AT = Date.now();

const codec = StringCodec();

async function main(): Promise<void> {
  // Start loading a warm Pyodide interpreter now so the first Python
  // request does not pay the ~0.8 s cold start (#58).
  prewarmPython();

  const nc: NatsConnection = await connect({ servers: NATS_URL });
  console.log(`[executor] Connected to NATS at ${NATS_URL}, subscribing to ${SUBJECT}.`);

  const stop = async () => {
    await nc.drain().catch(() => undefined);
    await shutdownPython();
    process.exit(0);
  };
  process.once("SIGTERM", () => void stop());
  process.once("SIGINT", () => void stop());

  const health = nc.subscribe(HEALTH_SUBJECT);
  void (async () => {
    for await (const message of health) {
      message.respond(codec.encode(JSON.stringify({
        ok: true,
        uptimeSeconds: Math.round((Date.now() - STARTED_AT) / 1000),
        subject: SUBJECT,
      })));
    }
  })();

  const subscription = nc.subscribe(SUBJECT, { queue: "executor" });
  for await (const message of subscription) {
    void handleMessage(message);
  }
}

async function handleMessage(message: {
  reply?: string;
  data: Uint8Array;
  respond: (data: Uint8Array) => void;
}): Promise<void> {
  if (!message.reply) {
    console.warn("[executor] Received message without a reply subject; dropping.");
    return;
  }
  let response: CodeNodeReply;
  try {
    const raw = codec.decode(message.data);
    const request = JSON.parse(raw) as CodeNodeRequest;
    if (request.version !== 1) {
      response = fail(`Unsupported wire version ${request.version}; this sidecar speaks v1.`);
    } else if (request.language === "js") {
      const output = await runJs(request);
      response = { success: true, errorMessage: null, output };
    } else if (request.language === "python") {
      const output = await runPython(request);
      response = { success: true, errorMessage: null, output };
    } else {
      response = fail(`Unknown language '${request.language}'.`);
    }
  } catch (err) {
    response = fail(err instanceof Error ? err.message : String(err));
  }
  try {
    message.respond(codec.encode(JSON.stringify(response)));
  } catch (err) {
    console.error("[executor] Failed to publish reply:", err);
  }
}

function fail(message: string): CodeNodeReply {
  return { success: false, errorMessage: message, output: null };
}

main().catch((err) => {
  console.error("[executor] Fatal:", err);
  process.exit(1);
});
