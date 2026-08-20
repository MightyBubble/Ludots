import { existsSync } from "fs";
import { resolve } from "path";

export const LUDOTS_PI_WORKSPACE_ENV = "LUDOTS_PI_WORKSPACE";

export function isLudotsRepositoryRoot(workspace: string): boolean {
  return existsSync(resolve(workspace, "skills/registry.json"))
    && existsSync(resolve(workspace, "src/Core"));
}

export function resolveLudotsWorkspace(env: NodeJS.ProcessEnv = process.env): string {
  const raw = env[LUDOTS_PI_WORKSPACE_ENV]?.trim();
  if (!raw) {
    throw new Error(
      `${LUDOTS_PI_WORKSPACE_ENV} is not set. Start Ludots Pi with scripts/run-ludots-pi so it opens the Ludots repository.`,
    );
  }

  const workspace = resolve(raw);
  if (!isLudotsRepositoryRoot(workspace)) {
    throw new Error(
      `${LUDOTS_PI_WORKSPACE_ENV} is not a Ludots repository: ${workspace}`,
    );
  }

  return workspace;
}
