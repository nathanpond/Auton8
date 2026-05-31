import { connect, NatsConnection, StringCodec } from "nats";
import { runJs } from "./jsRunner.js";
import { runPython } from "./pythonRunner.js";
import { CodeNodeReply, CodeNodeRequest } from "./wire.js";

// Entry point for the AutoNate executor sidecar. Connects to NATS,
// subscribes to `pipeline-code-run.>` via a durable consumer named
// `executor`, dispatches each request to the JS or Python runner, and
// replies on the supplied reply subject. Errors surface as
// `{ success: false, errorMessage: "..." }` payloads rather than
// disconnecting — the host expects a reply for every published message.

const NATS_URL = process.env.NATS_URL ?? "nats://localhost:4222";
const SUBJECT = "pipeline-code-run.>";

const codec = StringCodec();

async function main(): Promise<void> {
  const nc: NatsConnection = await connect({ servers: NATS_URL });
  console.log(`[executor] Connected to NATS at ${NATS_URL}, subscribing to ${SUBJECT}.`);

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
