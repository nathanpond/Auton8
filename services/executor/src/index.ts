import { connect, NatsConnection, StringCodec } from "nats";
import { runJs } from "./jsRunner.js";
import { runScriptTask } from "./scriptTaskRunner.js";
import { prewarmPython, runPython, runPythonScriptTask, shutdownPython } from "./pythonRunner.js";
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
// `pipeline-code-run.>` prefix, which a JetStream stream captures (archived-141).

const NATS_URL = process.env.NATS_URL ?? "nats://localhost:4222";
const SUBJECT = "pipeline-code-run.>";
export const HEALTH_SUBJECT = "executor.health";
const STARTED_AT = Date.now();

const codec = StringCodec();

async function main(): Promise<void> {
  // Start loading a warm Pyodide interpreter now so the first Python
  // request does not pay the ~0.8 s cold start (archived-58).
  prewarmPython();

  // nats.js defaults to maxReconnectAttempts: 10, so a NATS restart that takes
  // longer than ~10x2s closes the connection for good: the subscription
  // iterator completes normally, main() resolves, and the process either exits
  // 0 or idles with an empty loop while every code-node pipeline fails with the
  // generic 30 s timeout. -1 means keep trying, which is the right posture for
  // a sidecar whose only job is to serve that subject (archived-69).
  const nc: NatsConnection = await connect({
    servers: NATS_URL,
    maxReconnectAttempts: -1,
    reconnectTimeWait: 2_000,
  });
  console.log(`[executor] Connected to NATS at ${NATS_URL}, subscribing to ${SUBJECT}.`);

  // If the connection does close despite that, say so and exit non-zero so the
  // compose restart policy brings us back, rather than lingering as a healthy
  // looking process with no subscription.
  void nc.closed().then((err) => {
    if (err) {
      console.error("[executor] NATS connection closed with error:", err);
    } else {
      console.error("[executor] NATS connection closed unexpectedly.");
    }
    process.exit(1);
  });

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
    } else if (request.kind === "scripttask") {
      // BPMN script tasks (#147) return variable mutations rather than a frame.
      // Routed before the language branches because the reply shape differs,
      // not the language.
      // Both languages are front-ends onto the same host surface (#154).
      if (request.language === "js") {
        const scriptTask = await runScriptTask(request);
        response = { success: true, errorMessage: null, output: null, scriptTask };
      } else if (request.language === "python") {
        const scriptTask = await runPythonScriptTask(request);
        response = { success: true, errorMessage: null, output: null, scriptTask };
      } else {
        response = fail(`Script tasks support 'js' and 'python'; got '${request.language}'.`);
      }
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

// A rejected promise nobody awaited used to take the default action for the
// Node version rather than being reported; the same for a synchronous throw
// off the event loop. Both mean this sidecar is no longer serving, so make
// them loud and let the restart policy handle it (archived-69).
process.on("unhandledRejection", (reason) => {
  console.error("[executor] Unhandled rejection:", reason);
  process.exit(1);
});
process.on("uncaughtException", (err) => {
  console.error("[executor] Uncaught exception:", err);
  process.exit(1);
});

main()
  .then(() => {
    // main() resolving means the subscription iterator ended — there is no
    // healthy path where that happens while the process should keep running.
    console.error("[executor] Subscription loop ended; exiting so the supervisor restarts us.");
    process.exit(1);
  })
  .catch((err) => {
    console.error("[executor] Fatal:", err);
    process.exit(1);
  });
