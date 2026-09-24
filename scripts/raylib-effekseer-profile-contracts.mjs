import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  effekseerRuntimeContract,
  emitterAssetKinds
} from "./effekseer-runtime-contract.mjs";

const contractPath = fileURLToPath(new URL(
  "../src/Libraries/Effekseer/raylib-project-profile-contract.json",
  import.meta.url));
const supportedSchemaVersion = 1;
const usageNames = Object.freeze(["primitive", "signature"]);
const generatorAssetKinds = Object.freeze({
  spriteBurst: "SpriteEmitter",
  spriteIcon: "SpriteEmitter",
  ribbonTrail: "RibbonEmitter",
  trackBeam: "TrackEmitter",
  ringPulse: "RingEmitter",
  modelShard: "ModelEmitter"
});

export const raylibEffekseerProfileContract = readContract(contractPath);

export function requiredEmitterProfile(assetKind, usage) {
  const binding = raylibEffekseerProfileContract.assetKinds[assetKind];
  if (binding === undefined || !usageNames.includes(usage)) return undefined;
  return binding[usage];
}

export function emitterProfile(profileId) {
  const profile = raylibEffekseerProfileContract.profiles[profileId];
  if (profile === undefined) fail(`unknown profile ${JSON.stringify(profileId)}`);
  return profile;
}

export function emitterNodeType(assetKind) {
  const nodeType = effekseerRuntimeContract.emitterNodeTypes[assetKind];
  if (nodeType === undefined) fail(`runtime contract has no node type for ${JSON.stringify(assetKind)}`);
  return nodeType;
}

export function validateEmitterProjectParameters(project, at) {
  assertObject(project, at);
  assertCanonicalString(project.profile, `${at}.profile`);
  const profile = emitterProfile(project.profile);
  assertExactKeys(project, ["profile", ...Object.keys(profile.parameters)], at);

  const values = { profile: project.profile };
  for (const [name, typeId] of Object.entries(profile.parameters)) {
    const type = raylibEffekseerProfileContract.parameterTypes[typeId];
    const value = project[name];
    if (type.kind === "number") {
      assertFiniteNumber(value, `${at}.${name}`);
      if (value < type.minimum || value > type.maximum) {
        fail(`${at}.${name} must be between ${type.minimum} and ${type.maximum}`);
      }
      values[name] = value;
    } else if (type.kind === "resource") {
      values[name] = validateResource(value, type.extension, `${at}.${name}`);
    } else {
      fail(`parameter type ${JSON.stringify(typeId)} has unsupported kind ${JSON.stringify(type.kind)}`);
    }
  }
  return Object.freeze(values);
}

function readContract(file) {
  return validateRaylibEffekseerProfileContract(readJson(file));
}

export function validateRaylibEffekseerProfileContract(value) {
  assertExactKeys(value, [
    "schemaVersion",
    "project",
    "rendererDefaults",
    "color",
    "parameterTypes",
    "assetKinds",
    "profiles"
  ], "Raylib Effekseer profile contract");
  assertPositiveInteger(value.schemaVersion, "schemaVersion");
  if (value.schemaVersion !== supportedSchemaVersion) {
    fail(`schemaVersion ${value.schemaVersion} is unsupported; expected ${supportedSchemaVersion}`);
  }
  validateProject(value.project);
  validateRendererDefaults(value.rendererDefaults);
  validateColor(value.color);
  validateParameterTypes(value.parameterTypes);
  validateAssetKinds(value.assetKinds);
  validateProfiles(value.profiles, value.parameterTypes);
  validateReferences(value.assetKinds, value.profiles);
  return deepFreeze(value);
}

function validateProject(value) {
  assertExactKeys(value, ["version", "startFrame", "endFrame", "isLoop"], "project");
  assertPositiveInteger(value.version, "project.version");
  assertNonNegativeInteger(value.startFrame, "project.startFrame");
  assertPositiveInteger(value.endFrame, "project.endFrame");
  if (value.endFrame <= value.startFrame) fail("project.endFrame must be greater than project.startFrame");
  assertBoolean(value.isLoop, "project.isLoop");
}

function validateRendererDefaults(value) {
  assertExactKeys(value, ["zWrite", "zTest"], "rendererDefaults");
  assertBoolean(value.zWrite, "rendererDefaults.zWrite");
  assertBoolean(value.zTest, "rendererDefaults.zTest");
}

function validateColor(value) {
  assertExactKeys(value, ["red", "green", "blue", "channelMax"], "color");
  assertPositiveInteger(value.channelMax, "color.channelMax");
  for (const channel of ["red", "green", "blue"]) {
    assertNonNegativeInteger(value[channel], `color.${channel}`);
    if (value[channel] > value.channelMax) fail(`color.${channel} must not exceed color.channelMax`);
  }
}

function validateParameterTypes(value) {
  assertNonEmptyObject(value, "parameterTypes");
  for (const [typeId, type] of Object.entries(value)) {
    assertCanonicalString(typeId, "parameter type id");
    assertObject(type, `parameterTypes.${typeId}`);
    if (type.kind === "number") {
      assertExactKeys(type, ["kind", "minimum", "maximum"], `parameterTypes.${typeId}`);
      assertFiniteNumber(type.minimum, `parameterTypes.${typeId}.minimum`);
      assertFiniteNumber(type.maximum, `parameterTypes.${typeId}.maximum`);
      if (type.maximum < type.minimum) fail(`parameterTypes.${typeId}.maximum must not be less than minimum`);
    } else if (type.kind === "resource") {
      assertExactKeys(type, ["kind", "extension"], `parameterTypes.${typeId}`);
      if (typeof type.extension !== "string" || !/^\.[a-z0-9]+$/.test(type.extension)) {
        fail(`parameterTypes.${typeId}.extension must be a lowercase file extension`);
      }
    } else {
      fail(`parameterTypes.${typeId}.kind must be number or resource`);
    }
  }
}

function validateAssetKinds(value) {
  assertNonEmptyObject(value, "assetKinds");
  const profileAssetKinds = Object.keys(value).sort();
  const runtimeAssetKinds = [...emitterAssetKinds].sort();
  if (JSON.stringify(profileAssetKinds) !== JSON.stringify(runtimeAssetKinds)) {
    fail(`AssetKinds ${JSON.stringify(profileAssetKinds)} do not match runtime contract AssetKinds ${JSON.stringify(runtimeAssetKinds)}`);
  }
  for (const [assetKind, usages] of Object.entries(value)) {
    assertExactKeys(usages, usageNames, `assetKinds.${assetKind}`);
    for (const usage of usageNames) {
      assertCanonicalString(usages[usage], `assetKinds.${assetKind}.${usage}`);
    }
  }
}

function validateProfiles(value, parameterTypes) {
  assertNonEmptyObject(value, "profiles");
  for (const [profileId, profile] of Object.entries(value)) {
    const at = `profiles.${profileId}`;
    assertCanonicalString(profileId, "profile id");
    assertExactKeys(profile, ["assetKind", "generator", "parameters", "visual"], at);
    assertCanonicalString(profile.assetKind, `${at}.assetKind`);
    assertCanonicalString(profile.generator, `${at}.generator`);
    const expectedAssetKind = generatorAssetKinds[profile.generator];
    if (expectedAssetKind === undefined) fail(`${at}.generator is unsupported`);
    if (profile.assetKind !== expectedAssetKind) {
      fail(`${at}.assetKind must be ${JSON.stringify(expectedAssetKind)} for generator ${JSON.stringify(profile.generator)}`);
    }
    assertNonEmptyObject(profile.parameters, `${at}.parameters`);
    for (const [parameter, typeId] of Object.entries(profile.parameters)) {
      assertCanonicalString(parameter, `${at} parameter name`);
      assertCanonicalString(typeId, `${at}.parameters.${parameter}`);
      if (!Object.hasOwn(parameterTypes, typeId)) {
        fail(`${at}.parameters.${parameter} references unknown parameter type ${JSON.stringify(typeId)}`);
      }
    }
    validateVisual(profile.generator, profile.visual, `${at}.visual`);
  }
}

function validateReferences(assetKinds, profiles) {
  const referencedProfiles = new Set();
  for (const [assetKind, usages] of Object.entries(assetKinds)) {
    for (const usage of usageNames) {
      const profileId = usages[usage];
      const profile = profiles[profileId];
      if (profile === undefined) {
        fail(`assetKinds.${assetKind}.${usage} references unknown profile ${JSON.stringify(profileId)}`);
      }
      if (profile.assetKind !== assetKind) {
        fail(`assetKinds.${assetKind}.${usage} references profile for ${JSON.stringify(profile.assetKind)}`);
      }
      referencedProfiles.add(profileId);
    }
  }
  for (const profileId of Object.keys(profiles)) {
    if (!referencedProfiles.has(profileId)) fail(`profiles.${profileId} is not referenced by an AssetKind usage`);
  }
}

function validateVisual(generator, value, at) {
  assertObject(value, at);
  if (generator === "spriteBurst") {
    assertExactKeys(value, [
      "lifeFrames", "generationIntervalFrames", "generationOffsetFrames",
      "locationHorizontalRadius", "locationVerticalRadius", "horizontalVelocity",
      "verticalVelocity", "rotationDegrees", "spinVelocity", "baseScale",
      "scaleRange", "scaleVelocity", "renderer", "billboard"
    ], at);
    assertPositiveOrderedTuple(value.lifeFrames, 3, `${at}.lifeFrames`);
    assertPositiveOrderedTuple(value.generationIntervalFrames, 3, `${at}.generationIntervalFrames`);
    assertFiniteNumber(value.generationOffsetFrames, `${at}.generationOffsetFrames`);
    assertPositiveNumber(value.locationHorizontalRadius, `${at}.locationHorizontalRadius`);
    assertPositiveNumber(value.locationVerticalRadius, `${at}.locationVerticalRadius`);
    assertPositiveNumber(value.horizontalVelocity, `${at}.horizontalVelocity`);
    assertPositiveOrderedTuple(value.verticalVelocity, 3, `${at}.verticalVelocity`);
    assertOrderedTuple(value.rotationDegrees, 2, `${at}.rotationDegrees`);
    assertPositiveOrderedTuple(value.spinVelocity, 3, `${at}.spinVelocity`);
    assertPositiveNumber(value.baseScale, `${at}.baseScale`);
    assertPositiveOrderedTuple(value.scaleRange, 2, `${at}.scaleRange`);
    assertPositiveOrderedTuple(value.scaleVelocity, 3, `${at}.scaleVelocity`);
    validateRenderer(value.renderer, `${at}.renderer`);
    assertNonNegativeInteger(value.billboard, `${at}.billboard`);
  } else if (generator === "spriteIcon") {
    assertExactKeys(value, [
      "maxGeneration", "removeWhenLifeIsExtinct", "lifeFrames",
      "generationIntervalFrames", "generationOffsetFrames", "renderer", "alpha", "billboard"
    ], at);
    assertPositiveInteger(value.maxGeneration, `${at}.maxGeneration`);
    assertBoolean(value.removeWhenLifeIsExtinct, `${at}.removeWhenLifeIsExtinct`);
    assertPositiveOrderedTuple(value.lifeFrames, 3, `${at}.lifeFrames`);
    assertPositiveOrderedTuple(value.generationIntervalFrames, 3, `${at}.generationIntervalFrames`);
    assertOrderedTuple(value.generationOffsetFrames, 3, `${at}.generationOffsetFrames`);
    validateRenderer(value.renderer, `${at}.renderer`);
    assertUnit(value.alpha, `${at}.alpha`);
    assertNonNegativeInteger(value.billboard, `${at}.billboard`);
  } else if (generator === "ribbonTrail") {
    assertExactKeys(value, [
      "lifeFrames", "generationIntervalFrames", "generationOffsetFrames", "forwardVelocity",
      "renderer", "viewpointDependent", "positionMode", "halfWidth", "splineDivision"
    ], at);
    validateRepeatingProfile(value, at);
    assertPositiveNumber(value.forwardVelocity, `${at}.forwardVelocity`);
    validateRenderer(value.renderer, `${at}.renderer`);
    assertBoolean(value.viewpointDependent, `${at}.viewpointDependent`);
    assertNonNegativeInteger(value.positionMode, `${at}.positionMode`);
    assertPositiveNumber(value.halfWidth, `${at}.halfWidth`);
    assertPositiveInteger(value.splineDivision, `${at}.splineDivision`);
  } else if (generator === "trackBeam") {
    assertExactKeys(value, [
      "lifeFrames", "generationIntervalFrames", "generationOffsetFrames", "forwardVelocity",
      "renderer", "minimumLayerAlpha", "edgeAlphaMultiplier", "middleAlphaMultiplier",
      "widths", "splineDivision"
    ], at);
    validateRepeatingProfile(value, at);
    assertPositiveNumber(value.forwardVelocity, `${at}.forwardVelocity`);
    validateRenderer(value.renderer, `${at}.renderer`);
    assertUnit(value.minimumLayerAlpha, `${at}.minimumLayerAlpha`);
    assertUnit(value.edgeAlphaMultiplier, `${at}.edgeAlphaMultiplier`);
    assertUnit(value.middleAlphaMultiplier, `${at}.middleAlphaMultiplier`);
    assertPositiveTuple(value.widths, 3, `${at}.widths`);
    assertPositiveInteger(value.splineDivision, `${at}.splineDivision`);
  } else if (generator === "ringPulse") {
    assertExactKeys(value, [
      "lifeFrames", "generationIntervalFrames", "generationOffsetFrames", "initialScale",
      "scaleVelocity", "renderer", "billboard", "vertexCount", "outerRadius",
      "innerWidthMultiplier", "minimumInnerRadius", "centerRatio", "edgeAlphaMultiplier"
    ], at);
    validateRepeatingProfile(value, at);
    assertPositiveNumber(value.initialScale, `${at}.initialScale`);
    assertPositiveNumber(value.scaleVelocity, `${at}.scaleVelocity`);
    validateRenderer(value.renderer, `${at}.renderer`);
    assertNonNegativeInteger(value.billboard, `${at}.billboard`);
    assertPositiveInteger(value.vertexCount, `${at}.vertexCount`);
    assertPositiveNumber(value.outerRadius, `${at}.outerRadius`);
    assertPositiveNumber(value.innerWidthMultiplier, `${at}.innerWidthMultiplier`);
    assertPositiveNumber(value.minimumInnerRadius, `${at}.minimumInnerRadius`);
    assertUnit(value.centerRatio, `${at}.centerRatio`);
    assertUnit(value.edgeAlphaMultiplier, `${at}.edgeAlphaMultiplier`);
    if (value.minimumInnerRadius >= value.outerRadius) fail(`${at}.minimumInnerRadius must be less than outerRadius`);
  } else if (generator === "modelShard") {
    assertExactKeys(value, [
      "lifeFrames", "generationIntervalFrames", "generationOffsetFrames",
      "locationHorizontalRadius", "locationVerticalRadius", "verticalVelocity",
      "rotationDegrees", "rotationVelocityY", "rotationVelocityZ", "baseScale",
      "renderer", "model", "colorMode"
    ], at);
    assertPositiveOrderedTuple(value.lifeFrames, 3, `${at}.lifeFrames`);
    assertPositiveOrderedTuple(value.generationIntervalFrames, 3, `${at}.generationIntervalFrames`);
    assertFiniteNumber(value.generationOffsetFrames, `${at}.generationOffsetFrames`);
    assertPositiveNumber(value.locationHorizontalRadius, `${at}.locationHorizontalRadius`);
    assertPositiveNumber(value.locationVerticalRadius, `${at}.locationVerticalRadius`);
    assertPositiveOrderedTuple(value.verticalVelocity, 3, `${at}.verticalVelocity`);
    assertOrderedTuple(value.rotationDegrees, 2, `${at}.rotationDegrees`);
    assertPositiveOrderedTuple(value.rotationVelocityY, 3, `${at}.rotationVelocityY`);
    assertOrderedTuple(value.rotationVelocityZ, 3, `${at}.rotationVelocityZ`);
    assertPositiveNumber(value.baseScale, `${at}.baseScale`);
    validateRenderer(value.renderer, `${at}.renderer`);
    validateResource(value.model, ".efkmodel", `${at}.model`);
    assertNonNegativeInteger(value.colorMode, `${at}.colorMode`);
  } else {
    fail(`${at} uses unsupported generator ${JSON.stringify(generator)}`);
  }
}

function validateRepeatingProfile(value, at) {
  assertPositiveNumber(value.lifeFrames, `${at}.lifeFrames`);
  assertPositiveNumber(value.generationIntervalFrames, `${at}.generationIntervalFrames`);
  assertFiniteNumber(value.generationOffsetFrames, `${at}.generationOffsetFrames`);
}

function validateRenderer(value, at) {
  assertExactKeys(value, ["texture", "alphaBlend", "fadeInFrames", "fadeOutFrames"], at);
  if (value.texture !== null && value.texture !== "authoring") {
    validateResource(value.texture, ".png", `${at}.texture`);
  }
  assertNonNegativeInteger(value.alphaBlend, `${at}.alphaBlend`);
  assertNonNegativeNumber(value.fadeInFrames, `${at}.fadeInFrames`);
  assertNonNegativeNumber(value.fadeOutFrames, `${at}.fadeOutFrames`);
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch (error) {
    fail(`cannot read ${file}: ${error.message}`);
  }
}

function validateResource(value, extension, at) {
  if (typeof value !== "string" || value.length === 0 || value !== value.trim()) {
    fail(`${at} must be a canonical non-empty resource path`);
  }
  if (value.includes("\\") || path.posix.isAbsolute(value) || value.split("/").includes("..")) {
    fail(`${at} must be a portable relative resource path without parent traversal`);
  }
  if (path.posix.extname(value).toLowerCase() !== extension) fail(`${at} must end in ${extension}`);
  return value;
}

function assertExactKeys(value, expected, label) {
  assertObject(value, label);
  const actual = Object.keys(value).sort();
  const required = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(required)) {
    fail(`${label} fields must be exactly ${required.join(", ")}; received ${actual.join(", ")}`);
  }
}

function assertNonEmptyObject(value, label) {
  assertObject(value, label);
  if (Object.keys(value).length === 0) fail(`${label} must not be empty`);
}

function assertObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) fail(`${label} must be an object`);
}

function assertCanonicalString(value, label) {
  if (typeof value !== "string" || value.length === 0 || value !== value.trim()) {
    fail(`${label} must be a canonical non-empty string`);
  }
}

function assertFiniteNumber(value, label) {
  if (typeof value !== "number" || !Number.isFinite(value)) fail(`${label} must be a finite number`);
}

function assertPositiveNumber(value, label) {
  assertFiniteNumber(value, label);
  if (value <= 0) fail(`${label} must be greater than zero`);
}

function assertNonNegativeNumber(value, label) {
  assertFiniteNumber(value, label);
  if (value < 0) fail(`${label} must not be negative`);
}

function assertPositiveInteger(value, label) {
  if (!Number.isSafeInteger(value) || value <= 0) fail(`${label} must be a positive integer`);
}

function assertNonNegativeInteger(value, label) {
  if (!Number.isSafeInteger(value) || value < 0) fail(`${label} must be a non-negative integer`);
}

function assertBoolean(value, label) {
  if (typeof value !== "boolean") fail(`${label} must be a boolean`);
}

function assertUnit(value, label) {
  assertFiniteNumber(value, label);
  if (value < 0 || value > 1) fail(`${label} must be between zero and one`);
}

function assertTuple(value, length, label) {
  if (!Array.isArray(value) || value.length !== length) fail(`${label} must contain exactly ${length} numbers`);
  for (let index = 0; index < value.length; index++) assertFiniteNumber(value[index], `${label}[${index}]`);
}

function assertPositiveTuple(value, length, label) {
  assertTuple(value, length, label);
  for (let index = 0; index < value.length; index++) assertPositiveNumber(value[index], `${label}[${index}]`);
}

function assertOrderedTuple(value, length, label) {
  assertTuple(value, length, label);
  for (let index = 1; index < value.length; index++) {
    if (value[index] < value[index - 1]) fail(`${label} must be ordered from minimum to maximum`);
  }
}

function assertPositiveOrderedTuple(value, length, label) {
  assertOrderedTuple(value, length, label);
  for (let index = 0; index < value.length; index++) assertPositiveNumber(value[index], `${label}[${index}]`);
}

function deepFreeze(value) {
  if (value && typeof value === "object" && !Object.isFrozen(value)) {
    Object.freeze(value);
    for (const child of Object.values(value)) deepFreeze(child);
  }
  return value;
}

function fail(message) {
  throw new Error(`Raylib Effekseer profile contract: ${message}`);
}
