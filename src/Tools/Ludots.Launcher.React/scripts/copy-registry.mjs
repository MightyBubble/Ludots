// Copies the repo-root showcase registry into `public/` so the launcher can
// serve it as a static asset in both `vite dev` and `vite build` output.
// Wired into npm lifecycle via `predev` / `prebuild` in package.json.
import { copyFileSync, existsSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const launcherRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(launcherRoot, "..", "..", "..");
const source = resolve(repoRoot, "showcase.registry.json");
const targetDir = resolve(launcherRoot, "public");
const target = resolve(targetDir, "showcase.registry.json");

if (!existsSync(source)) {
  console.warn(`[copy-registry] skipped: ${source} not found.`);
  process.exit(0);
}

mkdirSync(targetDir, { recursive: true });
copyFileSync(source, target);
console.log(`[copy-registry] ${source} -> ${target}`);
