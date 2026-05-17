import { Server } from "@hocuspocus/server";
import { createAuthHook } from "./auth.js";
import { createPostgresPersistence } from "./persistence.js";
import { createWebhookExtension } from "./webhook.js";

function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    console.error(`Missing required environment variable: ${name}`);
    process.exit(1);
  }
  return value;
}

const port = Number.parseInt(process.env.HOCUSPOCUS_PORT ?? "1234", 10);
const sharedSecret = requireEnv("YJS_INTERNAL_SHARED_SECRET");
const autonateBaseUrl = requireEnv("AUTONATE_WEB_URL");

const pgConfig = {
  host: process.env.POSTGRES_HOST ?? "postgres",
  port: Number.parseInt(process.env.POSTGRES_PORT ?? "5432", 10),
  database: requireEnv("POSTGRES_DB"),
  user: requireEnv("POSTGRES_USER"),
  password: requireEnv("POSTGRES_PASSWORD")
};

const persistence = createPostgresPersistence(pgConfig);
const webhook = createWebhookExtension({ autonateBaseUrl, sharedSecret });
const onAuthenticate = createAuthHook({ autonateBaseUrl, sharedSecret });

const server = new Server({
  port,
  // We're not running under a process supervisor that handles signals
  // for us — let our own SIGTERM/SIGINT handlers drive shutdown so the
  // pg pool drains cleanly before exit.
  stopOnSignals: false,
  // Hocuspocus persists per-document state via the persistence extension
  // and notifies .NET via the webhook extension. The auth hook is wired
  // directly so its return value populates the context for downstream
  // hooks (in particular the webhook payload's userId).
  extensions: [persistence, webhook],
  onAuthenticate
});

console.log(
  `[server] Configured with autonateBaseUrl=${autonateBaseUrl}, ` +
  `pg=${pgConfig.host}:${pgConfig.port}/${pgConfig.database}.`
);

server
  .listen()
  .then(() => {
    console.log(`Hocuspocus listening on port ${port}.`);
  })
  .catch((err: unknown) => {
    console.error("Hocuspocus failed to start:", err);
    process.exit(1);
  });

async function shutdown(): Promise<void> {
  console.log("Hocuspocus shutting down…");
  try {
    await server.destroy();
  } catch (err) {
    console.error("Error during Hocuspocus shutdown:", err);
  }
  try {
    await persistence.shutdown();
  } catch (err) {
    console.error("Error closing pg pool:", err);
  }
  process.exit(0);
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

// Belt-and-suspenders: @hocuspocus/server does not catch errors from every
// extension hook (notably onDisconnect). A bare unhandled rejection there
// would otherwise terminate the process and wedge the container until a
// full `compose down`. Log and keep running — individual hooks own their
// own retry/data-loss semantics.
process.on("unhandledRejection", (reason) => {
  console.error("[process] unhandledRejection:", reason);
});
process.on("uncaughtException", (err) => {
  console.error("[process] uncaughtException:", err);
});
