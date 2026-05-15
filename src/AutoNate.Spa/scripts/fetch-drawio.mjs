#!/usr/bin/env node
// Fetches the jgraph/drawio webapp and stages it under public/drawio/. Vite
// serves /public/* as static assets at the SPA root, so the diagram editor's
// iframe can point at /drawio/?embed=1&... and run entirely against our own
// origin (no dependency on embed.diagrams.net at runtime).
//
// Run once after `npm install` (and any time you want to bump VERSION):
//   npm run fetch:drawio
//
// public/drawio/ is gitignored — every machine fetches its own copy. Pin a
// specific release here so the embed protocol's behavior stays stable; bump
// when you intentionally want a newer drawio.

import { execSync } from "node:child_process";
import { cp } from "node:fs/promises";
import { existsSync, mkdirSync, rmSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const VERSION = "27.0.5";
const TARBALL = `https://codeload.github.com/jgraph/drawio/tar.gz/refs/tags/v${VERSION}`;

const here = path.dirname(fileURLToPath(import.meta.url));
const spaRoot = path.resolve(here, "..");
const targetDir = path.join(spaRoot, "public", "drawio");
const stagingDir = path.join(spaRoot, ".drawio-staging");

console.log(`Fetching jgraph/drawio v${VERSION} → ${targetDir}`);

// Reset staging.
if (existsSync(stagingDir)) rmSync(stagingDir, { recursive: true, force: true });
mkdirSync(stagingDir, { recursive: true });

// Download tarball + extract directly into staging (strip the
// `drawio-<version>/` top-level dir).
try {
  execSync(
    `curl -fLsS '${TARBALL}' | tar -xz -C '${stagingDir}' --strip-components=1`,
    { stdio: "inherit", shell: "/bin/sh" }
  );
} catch (err) {
  console.error("Download or extract failed:", err);
  rmSync(stagingDir, { recursive: true, force: true });
  process.exit(1);
}

// Wipe any previous fetch and copy the webapp into place.
if (existsSync(targetDir)) rmSync(targetDir, { recursive: true, force: true });

const webappDir = path.join(stagingDir, "src", "main", "webapp");
if (!existsSync(webappDir)) {
  console.error(`Expected ${webappDir} in the downloaded archive but it's missing.`);
  rmSync(stagingDir, { recursive: true, force: true });
  process.exit(1);
}

await cp(webappDir, targetDir, { recursive: true });

// Clean up staging.
rmSync(stagingDir, { recursive: true, force: true });

console.log("Done. Drawio is now served at /drawio/ from the SPA.");
