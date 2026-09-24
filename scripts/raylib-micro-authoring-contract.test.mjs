import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { spawnSync } from "node:child_process";
import test from "node:test";

const repo = process.cwd();
const authoringDirectory = path.join(
  repo,
  "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Authoring");
const authoringPath = path.join(authoringDirectory, "raylib-micro-showcases.authoring.json");
const contractPath = path.join(authoringDirectory, "raylib-micro-showcases.contract.json");
const generatedPerformersPath = path.join(authoringDirectory, "../performers.json");
const generatorPath = path.join(repo, "scripts/generate-raylib-micro-assets.mjs");
const exporterPath = path.join(repo, "scripts/export-raylib-effekseer-assets.ps1");
const authoring = readJson(authoringPath);
const contract = readJson(contractPath);

test("formal authoring validates through semantic expansion and parent-child Performer generation", () => {
  const semanticCoverage = contract.showcases.filter(entry => entry.group === "semantic");
  const semanticShowcases = authoring.showcases.filter(showcase => showcase.group === "semantic");
  const expectedPresetIds = semanticCoverage.map(entry => entry.presetId).sort();
  const authoredPresetIds = authoring.semanticPresets.map(preset => preset.id).sort();

  assert.equal(semanticCoverage.length, 29);
  assert.equal(semanticCoverage.every(entry => typeof entry.presetId === "string"), true);
  assert.equal(authoring.semanticPresets.length, 29);
  assert.deepEqual(authoredPresetIds, expectedPresetIds);
  assert.equal(semanticShowcases.length, 29);
  assert.deepEqual(
    semanticShowcases.map(showcase => showcase.id).sort(),
    semanticCoverage.map(entry => entry.id).sort());
  assert.equal(semanticShowcases.every(showcase =>
    Object.hasOwn(showcase, "presetId") &&
    Object.hasOwn(showcase, "themeId") &&
    Object.hasOwn(showcase, "parameters") &&
    !Object.hasOwn(showcase, "parts") &&
    !Object.hasOwn(showcase, "theme") &&
    !Object.hasOwn(showcase, "signatureEmitterId")), true);

  const result = runGenerator(authoringPath);
  assert.equal(result.status, 0, diagnostic(result));
  assert.match(result.stdout, /Validated 36 Raylib micro showcases/);
});

test("optional motions are accepted only through the declared part schemas", () => {
  const semanticParts = authoring.semanticPresets.flatMap(preset => preset.parts);
  assert.equal(semanticParts.some(part => !Object.hasOwn(part, "motions")), true);
  const generatorSource = fs.readFileSync(generatorPath, "utf8");
  assert.doesNotMatch(generatorSource, /\?\?\s*\[\]/);
  assert.match(generatorSource, /\["rotation", "motions"\]/);
  assert.match(generatorSource, /part\.motions\.map/);
});

test("unknown authoring properties fail", () => {
  expectFixtureFailure(document => {
    firstSemanticShowcase(document).backendSelector = "raylib";
  }, ".backendSelector: unknown property");
});

test("unknown semantic preset references fail", () => {
  expectFixtureFailure(document => {
    firstSemanticShowcase(document).presetId = "ludots.semantic.unknown";
  }, ".presetId: coverage contract requires");
});

test("unknown theme references fail", () => {
  expectFixtureFailure(document => {
    firstSemanticShowcase(document).themeId = "ludots.theme.unknown";
  }, ".themeId: unknown theme");
});

test("missing logical parameters fail", () => {
  expectFixtureFailure(document => {
    delete firstSemanticShowcase(document).parameters.alpha;
  }, ".parameters.alpha: required property is missing");
});

test("theme packages reject Behavior, AssetKind, and backend selection", () => {
  for (const [field, value] of [
    ["behavior", "Pulse"],
    ["assetKind", "SpriteEmitter"],
    ["backendSelector", "raylib"]
  ]) {
    expectFixtureFailure(document => {
      document.themes[0][field] = value;
    }, `authoring.themes[0].${field}: unknown property`);
  }
});

test("theme and semantic preset ids must remain namespaced", () => {
  expectFixtureFailure(document => {
    document.themes[0].id = "tech";
  }, "authoring.themes[0].id: contains unsupported identifier characters");
  expectFixtureFailure(document => {
    document.semanticPresets[0].id = "explosion";
  }, "authoring.semanticPresets[0].id: contains unsupported identifier characters");
});

test("theme and semantic preset versions are pinned", () => {
  expectFixtureFailure(document => {
    document.themes[0].version += 1;
  }, "authoring.themes[0].version: expected theme package version");
  expectFixtureFailure(document => {
    document.semanticPresets[0].version += 1;
  }, "authoring.semanticPresets[0].version: expected semantic preset version");
});

test("unknown theme Mesh material references fail", () => {
  expectFixtureFailure(document => {
    document.themes[0].meshMaterialToken = "missing_surface";
  }, "authoring.themes[0].meshMaterialToken: unknown material asset");
});

test("unknown concrete emitter references fail", () => {
  expectFixtureFailure(document => {
    document.semanticPresets[0].parts[0].emitterId = "raylib.emitter.unknown";
  }, "authoring.semanticPresets[0].parts[0].emitterId: unknown emitter");
});

test("semantic preset parts reject undeclared fields", () => {
  expectFixtureFailure(document => {
    document.semanticPresets[0].parts[0].backendId = "raylib";
  }, "authoring.semanticPresets[0].parts[0].backendId: unknown property");
});

test("semantic presets may compose ordinary Mesh parts", () => {
  expectFixtureSuccess(document => {
    const part = document.semanticPresets[0].parts[0];
    part.type = "mesh";
    part.role = "presentation";
    part.assetId = "cube";
    delete part.emitterId;
  });
});

test("retired generic AssetKinds fail", () => {
  expectFixtureFailure(document => {
    document.emitters[0].assetKind = "VFX";
  }, "retired AssetKind \"VFX\" is forbidden");
});

test("width axes and motion references are strict", () => {
  expectFixtureFailure(document => {
    delete document.semanticPresets[0].parts[0].widthAxes;
  }, ".widthAxes: required property is missing");
  expectFixtureFailure(document => {
    document.semanticPresets[0].parts[0].motions = [{ class: "unknown_motion" }];
  }, ".motions[0].class: unknown motion class");
});

test("combined theme and instance multipliers cannot exceed the contract", () => {
  expectFixtureFailure(document => {
    document.themes[0].sizeMultiplier = 1000;
    const showcase = document.showcases.find(entry => entry.group === "semantic" && entry.themeId === document.themes[0].id);
    showcase.parameters.size = 1000;
  }, "expanded semantic size: must be between");
});

test("Effekseer export timing and stability policy comes from the authoring contract", () => {
  const exporterSource = fs.readFileSync(exporterPath, "utf8");
  for (const field of [
    "timeoutMilliseconds",
    "outputStableCheckCount",
    "outputStableMaxAttempts",
    "outputStablePollMilliseconds"
  ]) {
    assert.equal(Number.isInteger(contract.effekseerExport[field]), true, `effekseerExport.${field}`);
    assert.match(exporterSource, new RegExp(`exportContract\\.${field}`));
  }
  assert.doesNotMatch(exporterSource, /\$stableChecks\s+-lt\s+\d+/);
});

test("source and target expand in explicit PerformerLocal space", () => {
  const laser = authoring.showcases.find(showcase => showcase.id === "semantic_laser");
  assert.deepEqual(laser.parameters.source, [-1.7, 0.56, 0]);
  assert.deepEqual(laser.parameters.target, [1.7, 0.55, 0]);
  assert.equal(contract.semanticParameters.coordinateSpace, "PerformerLocal");

  const generated = readJson(path.resolve(generatedPerformersPath));
  const beam = generated.find(definition => definition.id === "raylib_micro_semantic_laser_root_part_07");
  const binding = beam.behaviors.find(behavior => behavior.kind === "AssetBinding").assetBinding;
  const targetDefault = beam.paramDefaults.find(
    entry => entry.paramKey === contract.semanticParameters.targetParamKey);
  assert.deepEqual(binding.localOffset, [-1.55, 0.64, 0]);
  assert.equal(binding.targetSpace, "PerformerLocal");
  assert.deepEqual(targetDefault.vectorValue.slice(0, 3), [1.7, 0.55, 0]);
  assert.ok(Math.abs(targetDefault.vectorValue[3] - Math.hypot(3.4, -0.01, 0)) < 1e-12);
});

function expectFixtureFailure(mutate, expectedMessage) {
  const fixture = structuredClone(authoring);
  mutate(fixture);
  const fixturePath = path.join(authoringDirectory, `raylib-micro-authoring-contract-${randomUUID()}.json`);
  fs.writeFileSync(fixturePath, JSON.stringify(fixture, null, 2) + "\n", "utf8");
  try {
    const result = runGenerator(fixturePath);
    assert.notEqual(result.status, 0, "invalid fixture unexpectedly passed validation");
    assert.match(diagnostic(result), new RegExp(escapeRegExp(expectedMessage)));
  } finally {
    fs.rmSync(fixturePath, { force: true });
  }
}

function expectFixtureSuccess(mutate) {
  const fixture = structuredClone(authoring);
  mutate(fixture);
  const fixturePath = path.join(authoringDirectory, `raylib-micro-authoring-contract-${randomUUID()}.json`);
  fs.writeFileSync(fixturePath, JSON.stringify(fixture, null, 2) + "\n", "utf8");
  try {
    const result = runGenerator(fixturePath);
    assert.equal(result.status, 0, diagnostic(result));
  } finally {
    fs.rmSync(fixturePath, { force: true });
  }
}

function firstSemanticShowcase(document) {
  return document.showcases.find(showcase => showcase.group === "semantic");
}

function runGenerator(inputPath) {
  return spawnSync(
    process.execPath,
    [generatorPath, "--validate-only", "--authoring", inputPath],
    { cwd: repo, encoding: "utf8", timeout: 120_000 });
}

function diagnostic(result) {
  if (result.error !== undefined) return `${result.error.message}\n${result.stdout}\n${result.stderr}`;
  return `${result.stdout}\n${result.stderr}`;
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8"));
}
