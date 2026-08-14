#!/usr/bin/env node
import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../../../..");
const webDir = resolve(here, "../web");
const workspaceMarker = resolve(repoRoot, "skills/registry.json");
const coreMarker = resolve(repoRoot, "src/Core");

function fail(message) {
  console.error(message);
  process.exit(1);
}

function majorMinor(version) {
  const match = /^v?(\d+)\.(\d+)/.exec(version);
  if (!match) return null;
  return { major: Number(match[1]), minor: Number(match[2]) };
}

function isSupportedNode(version) {
  const parsed = majorMinor(version);
  return Boolean(parsed && (parsed.major > 22 || (parsed.major === 22 && parsed.minor >= 19)));
}

if (!existsSync(workspaceMarker) || !existsSync(coreMarker)) {
  fail(`Ludots Pi must start from the Ludots repository. Missing markers under ${repoRoot}`);
}

if (!isSupportedNode(process.versions.node)) {
  fail(`Ludots Pi needs Node.js 22.19 or newer. Current process is ${process.versions.node}.`);
}

if (!existsSync(resolve(webDir, "package.json"))) {
  fail(`Ludots Pi frontend is missing: ${webDir}`);
}

process.env.LUDOTS_PI_WORKSPACE = repoRoot;
process.env.PI_WEB_NO_OPEN = process.env.PI_WEB_NO_OPEN ?? "1";

const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";
const hasModules = existsSync(resolve(webDir, "node_modules"));
const install = hasModules ? Promise.resolve(0) : new Promise((resolvePromise, reject) => {
  const child = spawn(npmCommand, ["ci"], { cwd: webDir, stdio: "inherit" });
  child.on("error", reject);
  child.on("close", (code) => resolvePromise(code ?? 1));
});

const installCode = await install;
if (installCode !== 0) {
  fail("npm ci failed in src/Tools/Ludots.Pi/web");
}

const port = process.env.PORT ?? "30141";
const hostname = process.env.PI_WEB_HOSTNAME ?? "127.0.0.1";
const url = `http://${hostname}:${port}/?cwd=${encodeURIComponent(repoRoot)}`;
console.log(`Ludots Pi workspace: ${repoRoot}`);
console.log(`Ludots Pi URL: ${url}`);

const child = spawn(npmCommand, ["run", "dev", "--", "-H", hostname, "-p", port], {
  cwd: webDir,
  stdio: "inherit",
  env: process.env,
});
child.on("exit", (code) => process.exit(code ?? 0));
