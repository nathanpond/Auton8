import { build } from "esbuild";
import { cp, mkdir, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");
const bpmnJsDist = path.join(repoRoot, "node_modules", "bpmn-js", "dist");
const targetRoot = path.join(repoRoot, "src", "AutoNate.Web", "wwwroot", "vendor", "bpmn-js");
const entry = path.join(__dirname, "bpmn-entry.mjs");

await rm(targetRoot, { recursive: true, force: true });
await mkdir(path.join(targetRoot, "bpmn-font"), { recursive: true });

await build({
  entryPoints: [entry],
  outfile: path.join(targetRoot, "bpmn-modeler.development.js"),
  bundle: true,
  format: "iife",
  globalName: "__AutoNateBpmnJS__",
  target: "es2020",
  sourcemap: false,
  minify: false,
  legalComments: "inline",
  logLevel: "info"
});

await cp(path.join(bpmnJsDist, "assets", "diagram-js.css"), path.join(targetRoot, "diagram-js.css"));
await cp(path.join(bpmnJsDist, "assets", "bpmn-js.css"), path.join(targetRoot, "bpmn-js.css"));
await cp(path.join(bpmnJsDist, "assets", "bpmn-font"), path.join(targetRoot, "bpmn-font"), { recursive: true });
