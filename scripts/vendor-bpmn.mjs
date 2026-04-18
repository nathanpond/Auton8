import { cp, mkdir, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");
const sourceRoot = path.join(repoRoot, "node_modules", "bpmn-js", "dist");
const targetRoot = path.join(repoRoot, "src", "AutoNate.Web", "wwwroot", "vendor", "bpmn-js");

await rm(targetRoot, { recursive: true, force: true });
await mkdir(path.join(targetRoot, "bpmn-font"), { recursive: true });

await cp(path.join(sourceRoot, "bpmn-modeler.development.js"), path.join(targetRoot, "bpmn-modeler.development.js"));
await cp(path.join(sourceRoot, "assets", "diagram-js.css"), path.join(targetRoot, "diagram-js.css"));
await cp(path.join(sourceRoot, "assets", "bpmn-js.css"), path.join(targetRoot, "bpmn-js.css"));
await cp(path.join(sourceRoot, "assets", "bpmn-font"), path.join(targetRoot, "bpmn-font"), { recursive: true });
