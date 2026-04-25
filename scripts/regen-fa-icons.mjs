import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");
const source = path.join(repoRoot, "src", "AutoNate.Spa", "node_modules", "@fortawesome", "fontawesome-free", "metadata", "icon-families.json");
const target = path.join(repoRoot, "src", "AutoNate.Spa", "src", "lib", "fa-icons.json");

const raw = await readFile(source, "utf8");
const data = JSON.parse(raw);

const out = [];
for (const [name, entry] of Object.entries(data)) {
  const styles = (entry.familyStylesByLicense?.free ?? [])
    .filter((s) => s.family === "classic")
    .map((s) => `fa-${s.style}`);
  if (styles.length === 0) continue;
  const terms = (entry.search?.terms ?? []).filter((t) => t !== name);
  out.push({ name, styles, terms });
}

out.sort((a, b) => a.name.localeCompare(b.name));

await writeFile(target, JSON.stringify(out));
console.log(`Wrote ${out.length} icons to ${path.relative(repoRoot, target)}`);
