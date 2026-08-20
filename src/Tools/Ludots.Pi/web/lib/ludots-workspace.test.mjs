import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { isLudotsRepositoryRoot, resolveLudotsWorkspace } from "./ludots-workspace.ts";

test("rejects a missing workspace variable", () => {
  assert.throws(
    () => resolveLudotsWorkspace({}),
    /LUDOTS_PI_WORKSPACE is not set/,
  );
});

test("rejects a folder that is not a Ludots repository", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "ludots-pi-not-repo-"));
  assert.equal(isLudotsRepositoryRoot(dir), false);
  assert.throws(
    () => resolveLudotsWorkspace({ LUDOTS_PI_WORKSPACE: dir }),
    /is not a Ludots repository/,
  );
});

test("accepts a folder with the Ludots repository markers", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "ludots-pi-repo-"));
  mkdirSync(path.join(dir, "skills"), { recursive: true });
  mkdirSync(path.join(dir, "src/Core"), { recursive: true });
  writeFileSync(path.join(dir, "skills/registry.json"), "{}\n");
  assert.equal(isLudotsRepositoryRoot(dir), true);
  assert.equal(resolveLudotsWorkspace({ LUDOTS_PI_WORKSPACE: dir }), path.resolve(dir));
});
