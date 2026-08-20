import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";
import { Type } from "typebox";

const UNSAFE_ARG = /[;&|`$<>]/;

function findLudotsRoot(cwd: string): string {
  let current = resolve(cwd);
  for (let i = 0; i < 12; i += 1) {
    if (existsSync(resolve(current, "skills/registry.json")) && existsSync(resolve(current, "src/Core"))) {
      return current;
    }
    const parent = resolve(current, "..");
    if (parent === current) break;
    current = parent;
  }
  throw new Error(`Not inside a Ludots repository (started at ${cwd})`);
}

function assertSafeArgs(args: string[]): void {
  for (const arg of args) {
    if (UNSAFE_ARG.test(arg)) {
      throw new Error(`Rejected launcher argument: ${arg}`);
    }
  }
}

function runCommand(command: string, args: string[], cwd: string, signal: AbortSignal): Promise<{ code: number; stdout: string; stderr: string }> {
  return new Promise((resolvePromise, reject) => {
    const child = spawn(command, args, { cwd, signal });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => {
      stdout += String(chunk);
    });
    child.stderr.on("data", (chunk) => {
      stderr += String(chunk);
    });
    child.on("error", reject);
    child.on("close", (code) => {
      resolvePromise({ code: code ?? 1, stdout, stderr });
    });
  });
}

export default function (pi: ExtensionAPI) {
  pi.registerTool({
    name: "ludots_workspace",
    label: "Ludots workspace",
    description: "Confirm the current folder is a Ludots repository and return its root path.",
    parameters: Type.Object({}),
    async execute(_toolCallId, _params, _signal, _onUpdate, ctx) {
      const root = findLudotsRoot(ctx.cwd);
      return {
        content: [{ type: "text", text: JSON.stringify({ root }, null, 2) }],
        details: { root },
      };
    },
  });

  pi.registerTool({
    name: "ludots_list_showcases",
    label: "List Ludots showcases",
    description: "Read showcase.registry.json and return id, title, status, and binding. Do not invent showcase names.",
    parameters: Type.Object({}),
    async execute(_toolCallId, _params, _signal, _onUpdate, ctx) {
      const root = findLudotsRoot(ctx.cwd);
      const registryPath = resolve(root, "showcase.registry.json");
      const registry = JSON.parse(readFileSync(registryPath, "utf8")) as {
        showcases?: Array<{ id?: string; title?: string; status?: string; binding?: string }>;
      };
      const showcases = (registry.showcases ?? []).map((item) => ({
        id: item.id,
        title: item.title,
        status: item.status,
        binding: item.binding,
      }));
      return {
        content: [{ type: "text", text: JSON.stringify({ count: showcases.length, showcases }, null, 2) }],
        details: { count: showcases.length },
      };
    },
  });

  pi.registerTool({
    name: "ludots_launch_cli",
    label: "Ludots launcher CLI",
    description: "Run the official Ludots launcher wrapper. Pass the same arguments a person would type after scripts/run-mod-launcher, for example [\"cli\", \"launch\", \"mass_navigation\", \"--adapter\", \"raylib\"].",
    parameters: Type.Object({
      args: Type.Array(Type.String(), {
        description: "Arguments after the official launcher wrapper",
      }),
    }),
    async execute(_toolCallId, params, signal, _onUpdate, ctx) {
      const root = findLudotsRoot(ctx.cwd);
      const args = params.args ?? [];
      assertSafeArgs(args);
      const script = resolve(root, "scripts/run-mod-launcher.ps1");
      if (!existsSync(script)) {
        throw new Error(`Official launcher is missing: ${script}`);
      }
      const result = await runCommand("pwsh", ["-NoProfile", "-File", script, ...args], root, signal).catch((error: NodeJS.ErrnoException) => {
        if (error.code === "ENOENT") {
          throw new Error("pwsh is required to run scripts/run-mod-launcher.ps1. Install PowerShell, or run that wrapper yourself.");
        }
        throw error;
      });
      if (result.code !== 0) {
        throw new Error(result.stderr || result.stdout || `Launcher exited ${result.code}`);
      }
      return {
        content: [{ type: "text", text: result.stdout || "Launcher finished with no output." }],
        details: { code: result.code },
      };
    },
  });
}
