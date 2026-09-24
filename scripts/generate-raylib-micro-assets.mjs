import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { requiredEmitterProfile } from "./raylib-effekseer-profile-contracts.mjs";
import { effekseerRuntimeContract, emitterAssetKinds } from "./effekseer-runtime-contract.mjs";
import { computeRaylibMicroRuntimeInputSha256 } from "./raylib-micro-runtime-inputs.mjs";
import { loadRecordingContract, verifyRecordingEvidence } from "./raylib-micro-recording-evidence.mjs";

const repo = process.cwd();
const shared = path.join(repo, "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod");
const entryRoot = path.join(repo, "mods/showcases/performer_raylib_micro_showcases_entries");
const docsPath = "docs/prd/17-performer-authoring-runtime.html";
const materialAssetsPath = path.join(repo, "mods/LudotsCoreMod/assets/Presentation/material_assets.json");

const defaultAuthoringPath = path.join(
  shared,
  "assets/Presentation/Authoring/raylib-micro-showcases.authoring.json");
const identifierPattern = /^[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*$/;
const lowerIdentifierPattern = /^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$/;
const namespacePattern = /^[a-z][a-z0-9]*(?:[_-][a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:[_-][a-z0-9]+)*)+$/;
const sha256Pattern = /^[a-f0-9]{64}$/;
const authoringContractPath = path.join(
  shared,
  "assets/Presentation/Authoring/raylib-micro-showcases.contract.json");
const authoringContract = parseAuthoringContract(authoringContractPath);
const recordingContract = loadRecordingContract(repo);
const artifactRoot = recordingContract.artifactRoot;
const forbiddenAssetKinds = new Set(["effect", "vfx", "particleemitter", "ribbontrail"]);
const primitiveAssetKinds = new Set(authoringContract.raylibPrimitiveAssetKinds);
const concreteEmitterAssetKinds = new Set(emitterAssetKinds);
const motionBehaviorSlotByProperty = new Map(Object.entries(authoringContract.motionBehaviorSlots));
const maxVectorComponent = 1_000_000;
const semanticParameterContract = authoringContract.semanticParameters;
const emitterTargetParamKey = semanticParameterContract.targetParamKey;
const entryTargetFramework = authoringContract.entryTargetFramework;
const emitterDynamicInputSlotCount = effekseerRuntimeContract.dynamicInputSlotCount;
if (semanticParameterContract.targetComponentCount + 1 !== emitterDynamicInputSlotCount) {
  fail(
    "authoring contract.semanticParameters.targetComponentCount",
    `must leave one distance lane within the ${emitterDynamicInputSlotCount}-slot Effekseer dynamic input contract`);
}
const materialAssetIds = parseMaterialAssetIds(materialAssetsPath);

const cli = parseCli(process.argv.slice(2));
const authoringPath = path.resolve(repo, cli.authoringPath ?? defaultAuthoringPath);
if (cli.refreshEmitterHashes) refreshEmitterHashes(authoringPath);
const authoring = parseAuthoring(authoringPath);
const themeLabel = Object.fromEntries(authoring.themes.map(theme => [theme.id, theme.label]));
const motionClassesById = new Map(authoring.motionClasses.map(motion => [motion.id, motion]));
const emitterAssets = Object.fromEntries(authoring.emitters.map(asset => [asset.id, {
  kind: asset.assetKind,
  id: asset.id,
  file: asset.sourceFile,
  runtimeFormatVersion: asset.runtimeFormatVersion,
  sha256: asset.sha256
}]));
const catalog = expandShowcases(authoring);
validateExpandedCatalog(catalog);

function parseCli(args) {
  const result = { authoringPath: undefined, validateOnly: false, refreshEmitterHashes: false, skipReport: false };
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    if (arg === "--authoring") {
      if (result.authoringPath !== undefined) fail("CLI", "--authoring may only be specified once");
      const value = args[++index];
      if (!value || value.startsWith("--")) fail("CLI", "--authoring requires a file path");
      result.authoringPath = value;
    } else if (arg === "--validate-only") {
      if (result.validateOnly) fail("CLI", "--validate-only may only be specified once");
      result.validateOnly = true;
    } else if (arg === "--refresh-emitter-hashes") {
      if (result.refreshEmitterHashes) fail("CLI", "--refresh-emitter-hashes may only be specified once");
      result.refreshEmitterHashes = true;
    } else if (arg === "--skip-report") {
      if (result.skipReport) fail("CLI", "--skip-report may only be specified once");
      result.skipReport = true;
    } else {
      fail("CLI", `unknown argument ${JSON.stringify(arg)}`);
    }
  }
  return result;
}

function refreshEmitterHashes(file) {
  const config = readJson(file);
  assertObject(config, "authoring");
  assertKeys(config, [
    "schemaVersion",
    "namespace",
    "themes",
    "motionClasses",
    "emitters",
    "semanticPresets",
    "showcases"
  ], [], "authoring");
  if (!Array.isArray(config.emitters) || config.emitters.length === 0) {
    fail("authoring.emitters", "must be a non-empty array before hashes can be refreshed");
  }

  const assetRoot = path.resolve(path.dirname(file), "../Effekseer");
  for (let index = 0; index < config.emitters.length; index++) {
    const emitter = config.emitters[index];
    const at = `authoring.emitters[${index}]`;
    if (!emitter || typeof emitter !== "object" || Array.isArray(emitter)) {
      fail(at, "must be an object before hashes can be refreshed");
    }
    if (typeof emitter.sourceFile !== "string" || path.basename(emitter.sourceFile) !== emitter.sourceFile || !emitter.sourceFile.endsWith(".efkefc")) {
      fail(`${at}.sourceFile`, "must be a basename ending in .efkefc");
    }
    const assetPath = path.join(assetRoot, emitter.sourceFile);
    if (!fs.existsSync(assetPath) || !fs.statSync(assetPath).isFile()) {
      fail(`${at}.sourceFile`, `runtime asset does not exist: ${assetPath}`);
    }
    emitter.sha256 = hashFile(assetPath);
  }

  writeJson(file, config);
}

function parseAuthoring(file) {
  if (!fs.existsSync(file)) fail("authoring", `file does not exist: ${file}`);
  const config = readJson(file);
  assertObject(config, "authoring");
  assertKeys(config, [
    "schemaVersion",
    "namespace",
    "themes",
    "motionClasses",
    "emitters",
    "semanticPresets",
    "showcases"
  ], [], "authoring");
  if (config.schemaVersion !== authoringContract.authoringSchemaVersion) {
    fail(
      "authoring.schemaVersion",
      `expected ${authoringContract.authoringSchemaVersion}, received ${JSON.stringify(config.schemaVersion)}`);
  }
  assertIdentifier(config.namespace, "authoring.namespace", namespacePattern);

  const themes = validateThemes(config.themes);
  const themesById = new Map(themes.map(theme => [theme.id, theme]));
  const motionClasses = validateMotionClasses(config.motionClasses);
  const motionClassesById = new Map(motionClasses.map(motion => [motion.id, motion]));
  const emitters = validateEmitters(config.emitters);
  const emittersById = new Map(emitters.map(asset => [asset.id, asset]));
  const semanticPresets = validateSemanticPresets(config.semanticPresets, motionClassesById, emittersById);
  const semanticPresetsById = new Map(semanticPresets.map(preset => [preset.id, preset]));
  const showcases = validateShowcases(
    config.showcases,
    themesById,
    motionClassesById,
    emittersById,
    semanticPresetsById);
  validateEmitterOwners(emitters, showcases);

  return {
    schemaVersion: config.schemaVersion,
    namespace: config.namespace,
    themes,
    motionClasses,
    emitters,
    semanticPresets,
    showcases
  };
}

function parseAuthoringContract(file) {
  if (!fs.existsSync(file)) fail("authoring contract", `file does not exist: ${file}`);
  const contract = readJson(file);
  assertObject(contract, "authoring contract");
  assertKeys(contract, [
    "schemaVersion",
    "authoringSchemaVersion",
    "themePackageVersion",
    "semanticPresetVersion",
    "entryTargetFramework",
    "maxChildPerformers",
    "semanticParameters",
    "effekseerExport",
    "raylibPrimitiveAssetKinds",
    "motionBehaviorSlots",
    "recording",
    "showcases"
  ], [], "authoring contract");
  if (contract.schemaVersion !== 2) {
    fail("authoring contract.schemaVersion", `expected 2, received ${JSON.stringify(contract.schemaVersion)}`);
  }
  assertIntegerInRange(contract.authoringSchemaVersion, "authoring contract.authoringSchemaVersion", 1, Number.MAX_SAFE_INTEGER);
  assertIntegerInRange(contract.themePackageVersion, "authoring contract.themePackageVersion", 1, Number.MAX_SAFE_INTEGER);
  assertIntegerInRange(contract.semanticPresetVersion, "authoring contract.semanticPresetVersion", 1, Number.MAX_SAFE_INTEGER);
  if (typeof contract.entryTargetFramework !== "string" || !/^net[1-9][0-9]*\.[0-9]+$/.test(contract.entryTargetFramework)) {
    fail("authoring contract.entryTargetFramework", "must be a canonical .NET target framework such as net8.0");
  }
  if (!Number.isSafeInteger(contract.maxChildPerformers) || contract.maxChildPerformers <= 0) {
    fail("authoring contract.maxChildPerformers", "must be a positive integer");
  }
  assertObject(contract.semanticParameters, "authoring contract.semanticParameters");
  assertKeys(contract.semanticParameters, [
    "sourceComponentCount",
    "targetComponentCount",
    "coordinateSpace",
    "targetParamKey",
    "vectorComponentMin",
    "vectorComponentMax",
    "alphaMin",
    "alphaMax",
    "multiplierMin",
    "multiplierMax"
  ], [], "authoring contract.semanticParameters");
  if (contract.semanticParameters.sourceComponentCount !== 3) {
    fail("authoring contract.semanticParameters.sourceComponentCount", "must be 3 for performer local offsets");
  }
  if (contract.semanticParameters.targetComponentCount !== 3) {
    fail("authoring contract.semanticParameters.targetComponentCount", "must be 3 for performer target positions");
  }
  if (contract.semanticParameters.coordinateSpace !== "PerformerLocal") {
    fail("authoring contract.semanticParameters.coordinateSpace", "must be PerformerLocal");
  }
  assertNamespacedIdentifier(
    contract.semanticParameters.targetParamKey,
    "authoring contract.semanticParameters.targetParamKey");
  assertAscendingRange(contract.semanticParameters, "vectorComponentMin", "vectorComponentMax", "authoring contract.semanticParameters");
  assertAscendingRange(contract.semanticParameters, "alphaMin", "alphaMax", "authoring contract.semanticParameters");
  assertAscendingRange(contract.semanticParameters, "multiplierMin", "multiplierMax", "authoring contract.semanticParameters");
  if (contract.semanticParameters.alphaMin < 0 || contract.semanticParameters.alphaMax > 1) {
    fail("authoring contract.semanticParameters", "alpha bounds must stay within [0, 1]");
  }
  if (contract.semanticParameters.multiplierMin <= 0) {
    fail("authoring contract.semanticParameters.multiplierMin", "must be greater than zero");
  }
  assertObject(contract.effekseerExport, "authoring contract.effekseerExport");
  assertKeys(contract.effekseerExport, [
    "timeoutMilliseconds",
    "outputStableCheckCount",
    "outputStableMaxAttempts",
    "outputStablePollMilliseconds"
  ], [], "authoring contract.effekseerExport");
  for (const field of Object.keys(contract.effekseerExport)) {
    assertIntegerInRange(
      contract.effekseerExport[field],
      `authoring contract.effekseerExport.${field}`,
      1,
      Number.MAX_SAFE_INTEGER);
  }
  if (contract.effekseerExport.outputStableCheckCount > contract.effekseerExport.outputStableMaxAttempts) {
    fail(
      "authoring contract.effekseerExport.outputStableCheckCount",
      "must not exceed outputStableMaxAttempts");
  }
  assertArray(contract.raylibPrimitiveAssetKinds, "authoring contract.raylibPrimitiveAssetKinds", 1);
  const primitiveKinds = new Set();
  for (let index = 0; index < contract.raylibPrimitiveAssetKinds.length; index++) {
    const kind = contract.raylibPrimitiveAssetKinds[index];
    assertIdentifier(kind, `authoring contract.raylibPrimitiveAssetKinds[${index}]`, identifierPattern);
    assertUnique(primitiveKinds, kind, `authoring contract.raylibPrimitiveAssetKinds[${index}]`, "primitive AssetKind");
  }
  assertObject(contract.motionBehaviorSlots, "authoring contract.motionBehaviorSlots");
  assertKeys(contract.motionBehaviorSlots, ["scale", "alpha"], [], "authoring contract.motionBehaviorSlots");
  for (const [property, slot] of Object.entries(contract.motionBehaviorSlots)) {
    assertIdentifier(slot, `authoring contract.motionBehaviorSlots.${property}`, lowerIdentifierPattern);
  }
  assertObject(contract.recording, "authoring contract.recording");
  assertKeys(contract.recording, [
    "artifactRoot",
    "reportPath",
    "recordingSummaryPath",
    "contactSheetPath",
    "workRoot",
    "logRoot",
    "raylibAppProject",
    "raylibAppOutput",
    "raylibAppEntryAssembly",
    "launcherCliProject",
    "launcherCliOutput",
    "launcherCliEntryAssembly",
    "launcherUserConfig",
    "launcherUserConfigEnvironmentVariable",
    "captureEnvironmentVariables",
    "launcherConfigFiles",
    "runtimeProjectRoots",
    "runtimeNonMsBuildSourceRoots",
    "runtimeAdditionalInputs",
    "captureFrameCount",
    "posterFrame",
    "autoExitGraceFrames",
    "framesPerSecond",
    "durationSeconds",
    "width",
    "height",
    "launcherCliBuildTimeoutMilliseconds",
    "raylibAppBuildTimeoutMilliseconds",
    "entryBuildTimeoutMilliseconds",
    "launchTimeoutMilliseconds",
    "encodeTimeoutMilliseconds",
    "contactSheetColumns",
    "contactSheetTileWidth",
    "contactSheetTileHeight",
    "contactSheetLabelHeight"
  ], [], "authoring contract.recording");
  assertArray(contract.showcases, "authoring contract.showcases", 1);
  const ids = new Set();
  const semanticPresetIds = new Set();
  const showcases = contract.showcases.map((entry, index) => {
    const at = `authoring contract.showcases[${index}]`;
    assertObject(entry, at);
    if (entry.group !== "primitive" && entry.group !== "semantic") {
      fail(`${at}.group`, "must be primitive or semantic");
    }
    assertKeys(entry, entry.group === "semantic" ? ["group", "id", "presetId"] : ["group", "id"], [], at);
    assertIdentifier(entry.id, `${at}.id`, lowerIdentifierPattern);
    assertUnique(ids, entry.id, `${at}.id`, "covered showcase id");
    if (entry.group === "semantic") {
      assertNamespacedIdentifier(entry.presetId, `${at}.presetId`);
      assertUnique(semanticPresetIds, entry.presetId, `${at}.presetId`, "covered semantic preset id");
    }
    return { ...entry };
  });
  return { ...contract, showcases };
}

function validateMotionClasses(value) {
  assertArray(value, "authoring.motionClasses", 1);
  const seen = new Set();
  return value.map((motion, index) => {
    const at = `authoring.motionClasses[${index}]`;
    assertObject(motion, at);
    assertKeys(motion, [
      "id",
      "property",
      "from",
      "to",
      "durationSeconds",
      "delaySeconds",
      "easing",
      "loop",
      "pingPong"
    ], [], at);
    assertIdentifier(motion.id, `${at}.id`, lowerIdentifierPattern);
    assertUnique(seen, motion.id, `${at}.id`, "motion class id");
    if (motion.property !== "alpha" && motion.property !== "scale") {
      fail(`${at}.property`, "must be alpha or scale");
    }
    const from = assertNumberInRange(motion.from, `${at}.from`, 0, 8);
    const to = assertNumberInRange(motion.to, `${at}.to`, 0, 8);
    if (motion.property === "alpha" && (from > 1 || to > 1)) {
      fail(at, "alpha motion endpoints must be between 0 and 1");
    }
    const durationSeconds = assertNumberInRange(motion.durationSeconds, `${at}.durationSeconds`, 0.000001, 60);
    const delaySeconds = assertNumberInRange(motion.delaySeconds, `${at}.delaySeconds`, 0, 60);
    if (motion.easing !== "Linear" && motion.easing !== "SmoothStep") {
      fail(`${at}.easing`, "must be Linear or SmoothStep");
    }
    if (typeof motion.loop !== "boolean") fail(`${at}.loop`, "must be boolean");
    if (typeof motion.pingPong !== "boolean") fail(`${at}.pingPong`, "must be boolean");
    if (motion.pingPong && !motion.loop) fail(`${at}.pingPong`, "requires loop=true");
    return { ...motion, from, to, durationSeconds, delaySeconds };
  });
}

function validateThemes(value) {
  assertArray(value, "authoring.themes", 1);
  const seen = new Set();
  return value.map((theme, index) => {
    const at = `authoring.themes[${index}]`;
    assertObject(theme, at);
    assertKeys(theme, [
      "id",
      "version",
      "label",
      "palette",
      "alphaMultiplier",
      "sizeMultiplier",
      "widthMultiplier",
      "tempoMultiplier",
      "meshMaterialToken"
    ], [], at);
    assertNamespacedIdentifier(theme.id, `${at}.id`);
    assertUnique(seen, theme.id, `${at}.id`, "theme id");
    if (theme.version !== authoringContract.themePackageVersion) {
      fail(
        `${at}.version`,
        `expected theme package version ${authoringContract.themePackageVersion}, received ${JSON.stringify(theme.version)}`);
    }
    assertText(theme.label, `${at}.label`, 128);
    assertArray(theme.palette, `${at}.palette`, 1);
    const palette = theme.palette.map((color, colorIndex) =>
      validateColor(color, `${at}.palette[${colorIndex}]`));
    const alphaMultiplier = assertNumberInRange(
      theme.alphaMultiplier,
      `${at}.alphaMultiplier`,
      semanticParameterContract.alphaMin,
      semanticParameterContract.alphaMax);
    const sizeMultiplier = validateMultiplier(theme.sizeMultiplier, `${at}.sizeMultiplier`);
    const widthMultiplier = validateMultiplier(theme.widthMultiplier, `${at}.widthMultiplier`);
    const tempoMultiplier = validateMultiplier(theme.tempoMultiplier, `${at}.tempoMultiplier`);
    assertIdentifier(theme.meshMaterialToken, `${at}.meshMaterialToken`, lowerIdentifierPattern);
    if (!materialAssetIds.has(theme.meshMaterialToken)) {
      fail(`${at}.meshMaterialToken`, `unknown material asset ${JSON.stringify(theme.meshMaterialToken)}`);
    }
    return {
      ...theme,
      palette,
      alphaMultiplier,
      sizeMultiplier,
      widthMultiplier,
      tempoMultiplier
    };
  });
}

function validateSemanticPresets(value, motionClassesById, emittersById) {
  assertArray(value, "authoring.semanticPresets", 1);
  const ids = new Set();
  const signatureIds = new Set();
  const presets = value.map((preset, index) => {
    const at = `authoring.semanticPresets[${index}]`;
    assertObject(preset, at);
    assertKeys(preset, ["id", "version", "signatureEmitterId", "parts"], [], at);
    assertNamespacedIdentifier(preset.id, `${at}.id`);
    assertUnique(ids, preset.id, `${at}.id`, "semantic preset id");
    if (preset.version !== authoringContract.semanticPresetVersion) {
      fail(
        `${at}.version`,
        `expected semantic preset version ${authoringContract.semanticPresetVersion}, received ${JSON.stringify(preset.version)}`);
    }
    assertIdentifier(preset.signatureEmitterId, `${at}.signatureEmitterId`, lowerIdentifierPattern);
    const signature = emittersById.get(preset.signatureEmitterId);
    if (!signature) {
      fail(`${at}.signatureEmitterId`, `unknown emitter ${JSON.stringify(preset.signatureEmitterId)}`);
    }
    if (signature.usage !== "signature") {
      fail(`${at}.signatureEmitterId`, "must reference an emitter with usage=signature");
    }
    assertUnique(signatureIds, preset.signatureEmitterId, `${at}.signatureEmitterId`, "semantic preset signature emitter");
    assertArray(preset.parts, `${at}.parts`, 1);
    if (preset.parts.length > authoringContract.maxChildPerformers) {
      fail(
        `${at}.parts`,
        `declares ${preset.parts.length} children, but the authoring contract capacity is ${authoringContract.maxChildPerformers}`);
    }
    const parts = preset.parts.map((part, partIndex) =>
      validateSemanticPresetPart(part, motionClassesById, emittersById, `${at}.parts[${partIndex}]`));
    const signatureReferences = parts.filter(
      part => part.type === "emitter" && part.emitterId === preset.signatureEmitterId).length;
    if (signatureReferences !== 1) {
      fail(`${at}.parts`, `must reference signatureEmitterId exactly once; received ${signatureReferences}`);
    }
    return { ...preset, parts };
  });
  const orphanSignatureIds = [...emittersById.values()]
    .filter(emitter => emitter.usage === "signature" && !signatureIds.has(emitter.id))
    .map(emitter => emitter.id);
  if (orphanSignatureIds.length > 0) {
    fail("authoring.semanticPresets", `does not reference signature emitters: ${orphanSignatureIds.join(", ")}`);
  }
  return presets;
}

function validateEmitterOwners(emitters, showcases) {
  const showcasesById = new Map(showcases.map(showcase => [showcase.id, showcase]));
  for (let index = 0; index < emitters.length; index++) {
    const emitter = emitters[index];
    const owner = showcasesById.get(emitter.ownerShowcase);
    const at = `authoring.emitters[${index}].ownerShowcase`;
    if (owner === undefined) fail(at, `unknown showcase ${JSON.stringify(emitter.ownerShowcase)}`);
    const expectedGroup = emitter.usage === "signature" ? "semantic" : "primitive";
    if (owner.group !== expectedGroup) {
      fail(at, `${emitter.usage} emitters must be owned by a ${expectedGroup} showcase`);
    }
  }
}

function validateEmitters(value) {
  assertArray(value, "authoring.emitters", 1);
  const seen = new Set();
  const sourceFiles = new Set();
  const signatureHashes = new Set();
  return value.map((asset, index) => {
    const at = `authoring.emitters[${index}]`;
    assertObject(asset, at);
    assertKeys(asset, ["id", "assetKind", "sourceFile", "runtimeFormatVersion", "sha256", "usage", "ownerShowcase", "project"], [], at);
    assertIdentifier(asset.id, `${at}.id`, lowerIdentifierPattern);
    assertUnique(seen, asset.id, `${at}.id`, "emitter id");
    assertAssetKind(asset.assetKind, `${at}.assetKind`, concreteEmitterAssetKinds);
    assertText(asset.sourceFile, `${at}.sourceFile`, 260);
    if (path.basename(asset.sourceFile) !== asset.sourceFile || !asset.sourceFile.endsWith(".efkefc")) {
      fail(`${at}.sourceFile`, "must be a basename ending in .efkefc");
    }
    assertUnique(sourceFiles, asset.sourceFile, `${at}.sourceFile`, "emitter source file");
    if (asset.runtimeFormatVersion !== effekseerRuntimeContract.runtimeFormatVersion) {
      fail(
        `${at}.runtimeFormatVersion`,
        `expected pinned Effekseer runtime format ${effekseerRuntimeContract.runtimeFormatVersion}, received ${JSON.stringify(asset.runtimeFormatVersion)}`);
    }
    if (typeof asset.sha256 !== "string" || !sha256Pattern.test(asset.sha256)) {
      fail(`${at}.sha256`, "must be a lowercase 64-character SHA-256 digest");
    }
    const assetPath = path.resolve(path.dirname(authoringPath), "../Effekseer", asset.sourceFile);
    if (!fs.existsSync(assetPath) || !fs.statSync(assetPath).isFile()) {
      fail(`${at}.sourceFile`, `runtime asset does not exist: ${assetPath}`);
    }
    const actualSha256 = hashFile(assetPath);
    if (actualSha256 !== asset.sha256) {
      fail(`${at}.sha256`, `declares ${asset.sha256}, but ${asset.sourceFile} is ${actualSha256}`);
    }
    if (asset.usage !== "primitive" && asset.usage !== "signature") {
      fail(`${at}.usage`, "must be primitive or signature");
    }
    assertIdentifier(asset.ownerShowcase, `${at}.ownerShowcase`, lowerIdentifierPattern);
    assertObject(asset.project, `${at}.project`);
    assertText(asset.project.profile, `${at}.project.profile`, 128);
    const requiredProfile = requiredEmitterProfile(asset.assetKind, asset.usage);
    if (requiredProfile === undefined) {
      fail(`${at}.assetKind`, `unsupported concrete emitter kind ${JSON.stringify(asset.assetKind)}`);
    }
    if (asset.project.profile !== requiredProfile) {
      fail(`${at}.project.profile`, `${asset.usage} ${asset.assetKind} requires ${JSON.stringify(requiredProfile)}`);
    }
    if (asset.usage === "signature") {
      assertUnique(signatureHashes, asset.sha256, `${at}.sha256`, "signature emitter binary");
    }
    return { ...asset };
  });
}

function validateShowcases(value, themesById, motionClassesById, emittersById, semanticPresetsById) {
  assertArray(value, "authoring.showcases", 1);
  const ids = new Set();
  const rootIds = new Set();
  const referencedPresetIds = new Set();
  const expectedById = new Map(authoringContract.showcases.map(entry => [entry.id, entry]));
  const showcases = value.map((showcase, index) => {
    const at = `authoring.showcases[${index}]`;
    assertObject(showcase, at);
    if (showcase.group !== "primitive" && showcase.group !== "semantic") {
      fail(`${at}.group`, "must be primitive or semantic");
    }
    assertKeys(
      showcase,
      showcase.group === "semantic"
        ? ["id", "title", "group", "presetId", "themeId", "intent", "rootDefinitionId", "camera", "parameters", "scene"]
        : ["id", "title", "group", "themeId", "intent", "rootDefinitionId", "camera", "parts"],
      [],
      at);
    assertIdentifier(showcase.id, `${at}.id`, lowerIdentifierPattern);
    assertUnique(ids, showcase.id, `${at}.id`, "showcase id");
    assertText(showcase.title, `${at}.title`, 256);
    if (!showcase.id.startsWith(`${showcase.group}_`)) {
      fail(`${at}.id`, `must start with ${showcase.group}_`);
    }
    const expected = expectedById.get(showcase.id);
    if (expected === undefined) {
      fail(`${at}.id`, `is not declared by ${path.basename(authoringContractPath)}`);
    }
    if (expected.group !== showcase.group) {
      fail(`${at}.group`, `coverage contract requires ${JSON.stringify(expected.group)}`);
    }
    assertNamespacedIdentifier(showcase.themeId, `${at}.themeId`);
    const theme = themesById.get(showcase.themeId);
    if (!theme) fail(`${at}.themeId`, `unknown theme ${JSON.stringify(showcase.themeId)}`);
    assertText(showcase.intent, `${at}.intent`, 1_000);
    assertIdentifier(showcase.rootDefinitionId, `${at}.rootDefinitionId`, identifierPattern);
    assertUnique(rootIds, showcase.rootDefinitionId, `${at}.rootDefinitionId`, "root definition id");
    const camera = validateCamera(showcase.camera, `${at}.camera`);

    if (showcase.group === "primitive") {
      assertArray(showcase.parts, `${at}.parts`, 1);
      if (showcase.parts.length > authoringContract.maxChildPerformers) {
        fail(
          `${at}.parts`,
          `declares ${showcase.parts.length} children, but the authoring contract capacity is ${authoringContract.maxChildPerformers}`);
      }
      const parts = showcase.parts.map((part, partIndex) =>
        validatePrimitivePart(part, theme, motionClassesById, emittersById, `${at}.parts[${partIndex}]`));
      return { ...showcase, camera, parts };
    }

    assertNamespacedIdentifier(showcase.presetId, `${at}.presetId`);
    if (showcase.presetId !== expected.presetId) {
      fail(`${at}.presetId`, `coverage contract requires ${JSON.stringify(expected.presetId)}`);
    }
    const preset = semanticPresetsById.get(showcase.presetId);
    if (!preset) fail(`${at}.presetId`, `unknown semantic preset ${JSON.stringify(showcase.presetId)}`);
    referencedPresetIds.add(showcase.presetId);
    const signature = emittersById.get(preset.signatureEmitterId);
    if (signature.ownerShowcase !== showcase.id) {
      fail(
        `${at}.presetId`,
        `preset signature emitter is owned by ${JSON.stringify(signature.ownerShowcase)}, not ${JSON.stringify(showcase.id)}`);
    }
    const parameters = validateSemanticParameters(showcase.parameters, `${at}.parameters`);
    assertArray(showcase.scene, `${at}.scene`, 0);
    const scene = showcase.scene.map((part, partIndex) =>
      validateScenePart(part, theme, motionClassesById, `${at}.scene[${partIndex}]`));
    for (let partIndex = 0; partIndex < preset.parts.length; partIndex++) {
      validateColorThemeReference(
        preset.parts[partIndex].color,
        theme,
        `${at}.preset(${preset.id}).parts[${partIndex}].color`);
    }
    const childCount = scene.length + preset.parts.length;
    if (childCount > authoringContract.maxChildPerformers) {
      fail(
        at,
        `expands to ${childCount} children, but the authoring contract capacity is ${authoringContract.maxChildPerformers}`);
    }
    assertContiguousPartOrder([...scene, ...preset.parts], at);
    return { ...showcase, camera, parameters, scene, preset };
  });

  const actualIds = new Set(showcases.map(showcase => showcase.id));
  const missingIds = authoringContract.showcases
    .map(entry => entry.id)
    .filter(id => !actualIds.has(id));
  if (missingIds.length > 0) {
    fail("authoring.showcases", `is missing coverage contract ids: ${missingIds.join(", ")}`);
  }
  const undeclaredPresetIds = [...semanticPresetsById.keys()]
    .filter(id => !referencedPresetIds.has(id));
  if (undeclaredPresetIds.length > 0) {
    fail("authoring.semanticPresets", `contains unreferenced presets: ${undeclaredPresetIds.join(", ")}`);
  }
  return showcases;
}

function validateSemanticParameters(value, at) {
  assertObject(value, at);
  assertKeys(value, ["source", "target", "alpha", "size", "width", "tempo"], [], at);
  return {
    source: validateVector(
      value.source,
      semanticParameterContract.sourceComponentCount,
      `${at}.source`,
      semanticParameterContract.vectorComponentMin,
      semanticParameterContract.vectorComponentMax),
    target: validateVector(
      value.target,
      semanticParameterContract.targetComponentCount,
      `${at}.target`,
      semanticParameterContract.vectorComponentMin,
      semanticParameterContract.vectorComponentMax),
    alpha: assertNumberInRange(
      value.alpha,
      `${at}.alpha`,
      semanticParameterContract.alphaMin,
      semanticParameterContract.alphaMax),
    size: validateMultiplier(value.size, `${at}.size`),
    width: validateMultiplier(value.width, `${at}.width`),
    tempo: validateMultiplier(value.tempo, `${at}.tempo`)
  };
}

function validateCamera(camera, at) {
  assertObject(camera, at);
  assertKeys(camera, ["TargetXCm", "TargetYCm", "Yaw", "Pitch", "DistanceCm", "FovYDeg"], [], at);
  return {
    TargetXCm: assertNumberInRange(camera.TargetXCm, `${at}.TargetXCm`, -maxVectorComponent, maxVectorComponent),
    TargetYCm: assertNumberInRange(camera.TargetYCm, `${at}.TargetYCm`, -maxVectorComponent, maxVectorComponent),
    Yaw: assertNumberInRange(camera.Yaw, `${at}.Yaw`, -360, 360),
    Pitch: assertNumberInRange(camera.Pitch, `${at}.Pitch`, 0.01, 89.9),
    DistanceCm: assertNumberInRange(camera.DistanceCm, `${at}.DistanceCm`, 0.01, maxVectorComponent),
    FovYDeg: assertNumberInRange(camera.FovYDeg, `${at}.FovYDeg`, 1, 179)
  };
}

function validatePrimitivePart(part, theme, motionClassesById, emittersById, at) {
  assertObject(part, at);
  if (part.type === "mesh") {
    assertKeys(part, ["type", "role", "assetId", "offset", "scale", "widthAxes", "color"], ["rotation", "motions"], at);
    if (part.role !== "scene") fail(`${at}.role`, "must be scene; presentation geometry belongs in a concrete emitter asset");
    assertIdentifier(part.assetId, `${at}.assetId`, identifierPattern);
  } else if (part.type === "primitive") {
    assertKeys(part, ["type", "assetKind", "offset", "scale", "widthAxes", "color"], ["rotation", "motions"], at);
    assertAssetKind(part.assetKind, `${at}.assetKind`, primitiveAssetKinds);
  } else if (part.type === "emitter") {
    assertKeys(part, ["type", "emitterId", "offset", "scale", "widthAxes", "color"], ["rotation", "target", "motions"], at);
    assertIdentifier(part.emitterId, `${at}.emitterId`, lowerIdentifierPattern);
    if (!emittersById.has(part.emitterId)) {
      fail(`${at}.emitterId`, `unknown emitter ${JSON.stringify(part.emitterId)}`);
    }
  } else {
    fail(`${at}.type`, "must be mesh, primitive, or emitter");
  }
  validateBooleanVector(part.widthAxes, 3, `${at}.widthAxes`, part.type !== "mesh");
  return validatePartValues(part, theme, motionClassesById, at);
}

function validateSemanticPresetPart(part, motionClassesById, emittersById, at) {
  assertObject(part, at);
  if (part.type === "mesh") {
    assertKeys(
      part,
      ["type", "order", "role", "assetId", "offset", "scale", "widthAxes", "color"],
      ["rotation", "motions"],
      at);
    if (part.role !== "presentation") fail(`${at}.role`, "must be presentation inside a semantic preset");
    assertIdentifier(part.assetId, `${at}.assetId`, identifierPattern);
  } else if (part.type === "emitter") {
    assertKeys(
      part,
      ["type", "order", "emitterId", "offset", "scale", "widthAxes", "color"],
      ["rotation", "motions"],
      at);
    assertIdentifier(part.emitterId, `${at}.emitterId`, lowerIdentifierPattern);
    if (!emittersById.has(part.emitterId)) {
      fail(`${at}.emitterId`, `unknown emitter ${JSON.stringify(part.emitterId)}`);
    }
  } else {
    fail(`${at}.type`, "must be mesh or a concrete emitter");
  }
  assertIntegerInRange(part.order, `${at}.order`, 0, authoringContract.maxChildPerformers - 1);
  validateBooleanVector(part.widthAxes, 3, `${at}.widthAxes`, true);
  return validatePartValues(part, undefined, motionClassesById, at);
}

function validateScenePart(part, theme, motionClassesById, at) {
  assertObject(part, at);
  if (part.type !== "mesh") fail(`${at}.type`, "showcase scene parts must be mesh");
  assertKeys(
    part,
    ["type", "order", "role", "assetId", "offset", "scale", "widthAxes", "color"],
    ["rotation", "motions"],
    at);
  assertIntegerInRange(part.order, `${at}.order`, 0, authoringContract.maxChildPerformers - 1);
  if (part.role !== "scene") fail(`${at}.role`, "must be scene");
  assertIdentifier(part.assetId, `${at}.assetId`, identifierPattern);
  validateBooleanVector(part.widthAxes, 3, `${at}.widthAxes`, false);
  if (part.widthAxes.some(Boolean)) fail(`${at}.widthAxes`, "scene references must not consume semantic width");
  return validatePartValues(part, theme, motionClassesById, at);
}

function validatePartValues(part, theme, motionClassesById, at) {
  const offset = validateVector(part.offset, 3, `${at}.offset`, -1_000, 1_000);
  const scale = validateVector(part.scale, 3, `${at}.scale`, 0.000001, 1_000);
  const color = validateColorDeclaration(part.color, theme, `${at}.color`);
  // Each part schema explicitly lists motions as optional; expansion only sees this validated array.
  const result = { ...part, offset, scale, color, motions: [] };
  if (part.rotation !== undefined) result.rotation = validateRotation(part.rotation, `${at}.rotation`);
  if (part.target !== undefined) {
    result.target = validateVector(
      part.target,
      emitterDynamicInputSlotCount,
      `${at}.target`,
      -1_000,
      1_000);
  }
  if (part.motions !== undefined) {
    assertArray(part.motions, `${at}.motions`, 1);
    const properties = new Set();
    result.motions = part.motions.map((motionRef, motionIndex) => {
      const motionAt = `${at}.motions[${motionIndex}]`;
      assertObject(motionRef, motionAt);
      assertKeys(motionRef, ["class"], ["delaySeconds"], motionAt);
      assertIdentifier(motionRef.class, `${motionAt}.class`, lowerIdentifierPattern);
      const motionClass = motionClassesById.get(motionRef.class);
      if (!motionClass) fail(`${motionAt}.class`, `unknown motion class ${JSON.stringify(motionRef.class)}`);
      assertUnique(properties, motionClass.property, motionAt, "animated property");
      const delaySeconds = motionRef.delaySeconds === undefined
        ? motionClass.delaySeconds
        : assertNumberInRange(motionRef.delaySeconds, `${motionAt}.delaySeconds`, 0, 60);
      return { class: motionRef.class, delaySeconds };
    });
  }
  return result;
}

function validateColorDeclaration(value, theme, at) {
  if (Array.isArray(value)) return validateColor(value, at);
  assertObject(value, at);
  assertKeys(value, ["themeSlot"], [], at);
  const maximumSlot = theme === undefined ? Number.MAX_SAFE_INTEGER : theme.palette.length - 1;
  assertIntegerInRange(value.themeSlot, `${at}.themeSlot`, 0, maximumSlot);
  return { themeSlot: value.themeSlot };
}

function validateColorThemeReference(color, theme, at) {
  if (!Array.isArray(color)) {
    assertIntegerInRange(color.themeSlot, `${at}.themeSlot`, 0, theme.palette.length - 1);
  }
}

function validateBooleanVector(value, length, at, requireSelectedAxis) {
  if (!Array.isArray(value) || value.length !== length) {
    fail(at, `must be an array of exactly ${length} booleans`);
  }
  for (let index = 0; index < value.length; index++) {
    if (typeof value[index] !== "boolean") fail(`${at}[${index}]`, "must be boolean");
  }
  if (requireSelectedAxis && !value.includes(true)) fail(at, "must select at least one width axis");
}

function assertContiguousPartOrder(parts, at) {
  const orders = parts.map(part => part.order).sort((left, right) => left - right);
  for (let index = 0; index < orders.length; index++) {
    if (orders[index] !== index) {
      fail(`${at}.order`, `scene and preset part order must contain each integer from 0 through ${parts.length - 1}`);
    }
  }
}

function expandShowcases(config) {
  const emittersById = new Map(config.emitters.map(asset => [asset.id, asset]));
  const themesById = new Map(config.themes.map(theme => [theme.id, theme]));
  const motionsById = new Map(config.motionClasses.map(motion => [motion.id, motion]));
  return config.showcases.map(showcase => {
    const theme = themesById.get(showcase.themeId);
    if (showcase.group === "primitive") {
      const modifiers = themeModifiers(theme, undefined);
      return expandedShowcase(
        showcase,
        theme,
        undefined,
        undefined,
        showcase.parts.map(part => expandPart(part, theme, modifiers, motionsById, emittersById, undefined)));
    }

    const modifiers = themeModifiers(theme, showcase.parameters);
    const sceneModifiers = { alpha: 1, size: 1, width: 1, tempo: 1 };
    const scene = showcase.scene.map(part =>
      expandPart(part, theme, sceneModifiers, motionsById, emittersById, undefined));
    const presentation = showcase.preset.parts.map(part =>
      expandPart(part, theme, modifiers, motionsById, emittersById, showcase.parameters));
    const parts = [...scene, ...presentation]
      .sort((left, right) => left.order - right.order)
      .map(part => {
        const { order, ...expanded } = part;
        return expanded;
      });
    return expandedShowcase(showcase, theme, showcase.preset, showcase.parameters, parts);
  });
}

function expandedShowcase(showcase, theme, semanticPreset, parameters, parts) {
  return {
    id: showcase.id,
    title: showcase.title,
    group: showcase.group,
    themeId: theme.id,
    themeVersion: theme.version,
    meshMaterialToken: theme.meshMaterialToken,
    intent: showcase.intent,
    rootDefinitionId: showcase.rootDefinitionId,
    camera: showcase.camera,
    semanticPresetId: semanticPreset === undefined ? undefined : semanticPreset.id,
    semanticPresetVersion: semanticPreset === undefined ? undefined : semanticPreset.version,
    signatureEmitterId: semanticPreset === undefined ? undefined : semanticPreset.signatureEmitterId,
    parameters,
    parts
  };
}

function themeModifiers(theme, parameters) {
  if (parameters === undefined) {
    return {
      alpha: theme.alphaMultiplier,
      size: theme.sizeMultiplier,
      width: theme.widthMultiplier,
      tempo: theme.tempoMultiplier
    };
  }
  return {
    alpha: assertNumberInRange(
      parameters.alpha * theme.alphaMultiplier,
      "expanded semantic alpha",
      semanticParameterContract.alphaMin,
      semanticParameterContract.alphaMax),
    size: validateMultiplier(parameters.size * theme.sizeMultiplier, "expanded semantic size"),
    width: validateMultiplier(parameters.width * theme.widthMultiplier, "expanded semantic width"),
    tempo: validateMultiplier(parameters.tempo * theme.tempoMultiplier, "expanded semantic tempo")
  };
}

function expandPart(part, theme, modifiers, motionsById, emittersById, semanticParameters) {
  const color = Array.isArray(part.color)
    ? [...part.color]
    : [...theme.palette[part.color.themeSlot]];
  color[3] = assertNumberInRange(
    color[3] * modifiers.alpha,
    "expanded part alpha",
    semanticParameterContract.alphaMin,
    semanticParameterContract.alphaMax);
  const widthAxes = part.widthAxes;
  const scale = part.scale.map((component, index) =>
    assertNumberInRange(
      component * modifiers.size * (widthAxes[index] ? modifiers.width : 1),
      `expanded part scale[${index}]`,
      semanticParameterContract.multiplierMin,
      maxVectorComponent));
  const motions = part.motions.map(motionRef => {
    const motion = motionsById.get(motionRef.class);
    return {
      ...motion,
      durationSeconds: assertNumberInRange(
        motion.durationSeconds / modifiers.tempo,
        "expanded motion durationSeconds",
        Number.MIN_VALUE,
        60),
      delaySeconds: assertNumberInRange(
        motionRef.delaySeconds / modifiers.tempo,
        "expanded motion delaySeconds",
        0,
        60)
    };
  });
  const offset = semanticParameters !== undefined
    ? part.offset.map((component, index) => assertNumberInRange(
      component + semanticParameters.source[index],
      `expanded part offset[${index}]`,
      -maxVectorComponent,
      maxVectorComponent))
    : [...part.offset];
  const common = {
    order: part.order,
    offset,
    scale,
    color,
    rotation: part.rotation,
    motions,
    meshMaterialToken: theme.meshMaterialToken
  };
  if (part.type === "mesh") {
    return { ...common, t: "mesh", role: part.role, assetId: part.assetId };
  }
  if (part.type === "primitive") {
    return { ...common, t: "primitive", kind: part.assetKind };
  }
  const asset = emittersById.get(part.emitterId);
  let target = part.target;
  if (semanticParameters !== undefined) {
    const targetDelta = semanticParameters.target.map(
      (component, index) => component - semanticParameters.source[index]);
    target = [...semanticParameters.target, Math.hypot(...targetDelta)];
  }
  return {
    ...common,
    t: "emitter",
    kind: asset.assetKind,
    assetId: asset.id,
    sourceFile: asset.sourceFile,
    target
  };
}

function validateExpandedCatalog(items) {
  for (let showcaseIndex = 0; showcaseIndex < items.length; showcaseIndex++) {
    const item = items[showcaseIndex];
    const at = `catalog[${showcaseIndex}](${item.id})`;
    assertNamespacedIdentifier(item.themeId, `${at}.themeId`);
    assertIdentifier(item.meshMaterialToken, `${at}.meshMaterialToken`, lowerIdentifierPattern);
    assertArray(item.parts, `${at}.parts`, 1);
    if (item.group === "semantic") {
      assertNamespacedIdentifier(item.semanticPresetId, `${at}.semanticPresetId`);
      assertIdentifier(item.signatureEmitterId, `${at}.signatureEmitterId`, lowerIdentifierPattern);
    }
    for (let partIndex = 0; partIndex < item.parts.length; partIndex++) {
      const part = item.parts[partIndex];
      const partAt = `${at}.parts[${partIndex}]`;
      assertArray(part.motions, `${partAt}.motions`, 0);
      assertIdentifier(part.meshMaterialToken, `${partAt}.meshMaterialToken`, lowerIdentifierPattern);
      if (part.t === "mesh") {
        assertIdentifier(part.assetId, `${partAt}.assetId`, identifierPattern);
      } else if (part.t === "primitive") {
        assertAssetKind(part.kind, `${partAt}.kind`, primitiveAssetKinds);
      } else if (part.t === "emitter") {
        assertAssetKind(part.kind, `${partAt}.kind`, concreteEmitterAssetKinds);
        if (item.group === "semantic") {
          validateVector(
            part.target,
            emitterDynamicInputSlotCount,
            `${partAt}.target`,
            -maxVectorComponent,
            maxVectorComponent);
        }
      } else {
        fail(`${partAt}.t`, "must be mesh, primitive, or emitter after expansion");
      }
    }
  }
}

function validateColor(value, at) {
  return validateVector(value, 4, at, 0, 1);
}

function validateRotation(value, at) {
  const rotation = validateVector(value, 4, at, -1, 1);
  const magnitude = Math.hypot(...rotation);
  if (magnitude < 0.999 || magnitude > 1.001) {
    fail(at, `must be a normalized quaternion; magnitude was ${magnitude}`);
  }
  return rotation;
}

function validateVector(value, length, at, min, max) {
  if (!Array.isArray(value) || value.length !== length) {
    fail(at, `must be an array of exactly ${length} numbers`);
  }
  return value.map((component, index) =>
    assertNumberInRange(component, `${at}[${index}]`, min, max));
}

function assertAssetKind(value, at, allowed) {
  assertText(value, at, 128);
  if (forbiddenAssetKinds.has(value.toLowerCase())) {
    fail(at, `retired AssetKind ${JSON.stringify(value)} is forbidden`);
  }
  if (!allowed.has(value)) {
    fail(at, `unsupported concrete AssetKind ${JSON.stringify(value)}; expected one of ${[...allowed].join(", ")}`);
  }
}

function assertObject(value, at) {
  if (value === null || typeof value !== "object" || Array.isArray(value) || Object.getPrototypeOf(value) !== Object.prototype) {
    fail(at, "must be an object");
  }
}

function assertArray(value, at, minimumLength) {
  if (!Array.isArray(value) || value.length < minimumLength) {
    fail(at, `must be an array with at least ${minimumLength} item(s)`);
  }
}

function assertKeys(value, required, optional, at) {
  const allowed = new Set([...required, ...optional]);
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) fail(`${at}.${key}`, "unknown property");
  }
  for (const key of required) {
    if (!Object.hasOwn(value, key)) fail(`${at}.${key}`, "required property is missing");
  }
}

function assertIdentifier(value, at, pattern) {
  assertText(value, at, 160);
  if (!pattern.test(value)) fail(at, "contains unsupported identifier characters");
}

function assertNamespacedIdentifier(value, at) {
  assertIdentifier(value, at, namespacePattern);
}

function assertText(value, at, maxLength) {
  if (typeof value !== "string" || value.trim().length === 0 || value.length > maxLength) {
    fail(at, `must be a non-empty string no longer than ${maxLength} characters`);
  }
}

function assertUnique(seen, value, at, label) {
  if (seen.has(value)) fail(at, `duplicate ${label} ${JSON.stringify(value)}`);
  seen.add(value);
}

function assertIntegerInRange(value, at, min, max) {
  if (!Number.isInteger(value)) fail(at, "must be an integer");
  return assertNumberInRange(value, at, min, max);
}

function assertNumberInRange(value, at, min, max) {
  if (typeof value !== "number" || !Number.isFinite(value)) fail(at, "must be a finite number");
  if (value < min || value > max) fail(at, `must be between ${min} and ${max}`);
  return value;
}

function assertAscendingRange(value, minKey, maxKey, at) {
  const minimum = value[minKey];
  const maximum = value[maxKey];
  if (typeof minimum !== "number" || !Number.isFinite(minimum)) fail(`${at}.${minKey}`, "must be a finite number");
  if (typeof maximum !== "number" || !Number.isFinite(maximum)) fail(`${at}.${maxKey}`, "must be a finite number");
  if (minimum >= maximum) fail(at, `${minKey} must be less than ${maxKey}`);
}

function validateMultiplier(value, at) {
  return assertNumberInRange(
    value,
    at,
    semanticParameterContract.multiplierMin,
    semanticParameterContract.multiplierMax);
}

function parseMaterialAssetIds(file) {
  if (!fs.existsSync(file)) fail("material assets", `file does not exist: ${file}`);
  const assets = readJson(file);
  assertArray(assets, "material assets", 1);
  const ids = new Set();
  for (let index = 0; index < assets.length; index++) {
    const asset = assets[index];
    const at = `material assets[${index}]`;
    assertObject(asset, at);
    assertIdentifier(asset.id, `${at}.id`, lowerIdentifierPattern);
    assertUnique(ids, asset.id, `${at}.id`, "material asset id");
  }
  return ids;
}

function fail(at, message) {
  throw new Error(`${at}: ${message}`);
}

const mapId = item => `raylib_micro_${item.id}`;
const rootId = item => item.rootDefinitionId;
const entityId = item => `${mapId(item)}_entity`;
const bootstrapId = item => `raylib_micro_bootstrap_${item.id}`;
const bindingId = item => `performer_raylib_micro_${item.id}`;
const presetId = item => `${bindingId(item)}_raylib`;
const pascal = value => value.split("_").map(x => x[0].toUpperCase() + x.slice(1)).join("");
const projectName = item => `PerformerRaylibMicro${pascal(item.id)}EntryMod`;
const entryDir = item => `${bindingId(item)}_entry`;
const entryProjectPath = item => `mods/showcases/performer_raylib_micro_showcases_entries/${entryDir(item)}/${projectName(item)}`;
const performerDefinitions = buildDefinitions();

function buildDefinitions() {
  const defs = [];
  for (const item of catalog) {
    const root = rootId(item);
    defs.push({
      id: bootstrapId(item),
      rules: [
        { event: { kind: "EntitySpawned", key: entityId(item) }, condition: { inline: "SourceHasVisualTransform" }, command: { kind: "CreatePerformer", definitionId: root, scopeSource: "EventPayloadA" } },
        { event: { kind: "EntityDestroyed", key: entityId(item) }, command: { kind: "DestroyPerformerScope", scopeSource: "EventPayloadA" } }
      ]
    });

    const children = [];
    item.parts.forEach((part, index) => {
      const id = `${root}_part_${String(index + 1).padStart(2, "0")}`;
      children.push({ definitionId: id, scopeTag: item.group });
      const assetBinding = part.t === "mesh"
        ? { assetKind: "Mesh", assetId: part.assetId, materialId: part.meshMaterialToken, renderPath: "InstancedStaticMesh", mobility: "Static", localOffset: part.offset, localScale: part.scale }
        : part.t === "emitter"
          ? { assetKind: part.kind, assetId: part.assetId, renderPath: "Primitive", mobility: "Static", localOffset: part.offset, localScale: part.scale }
          : { assetKind: part.kind, renderPath: "Primitive", mobility: "Static", localOffset: part.offset, localScale: part.scale };
      if (part.rotation) assetBinding.localRotation = part.rotation;
      const behaviors = [{ slot: "body", kind: "AssetBinding", activeByDefault: true, assetBinding }];
      const paramDefaults = [];
      const definition = { id, defaultColor: part.color, behaviors };
      if (part.target) {
        assetBinding.targetParamKey = emitterTargetParamKey;
        assetBinding.targetSpace = semanticParameterContract.coordinateSpace;
        paramDefaults.push({ paramKey: emitterTargetParamKey, lane: "Vector", vectorValue: part.target });
      }
      part.motions.forEach((motion, motionIndex) => {
        const behaviorSlot = motionBehaviorSlotByProperty.get(motion.property);
        if (!behaviorSlot) {
          fail(`catalog.${item.id}.parts[${index}].motions[${motionIndex}]`, `unsupported property ${motion.property}`);
        }
        const paramKey = `raylib.micro.${item.id}.part.${String(index + 1).padStart(2, "0")}.${motion.property}`;
        const paramTween = {
          paramKey,
          durationSeconds: motion.durationSeconds,
          delaySeconds: motion.delaySeconds,
          easing: motion.easing,
          loop: motion.loop,
          pingPong: motion.pingPong
        };
        if (motion.property === "scale") {
          assetBinding.scaleParamKey = paramKey;
          paramTween.lane = "Float";
          paramTween.fromFloat = motion.from;
          paramTween.toFloat = motion.to;
          paramDefaults.push({ paramKey, lane: "Float", floatValue: motion.from });
        } else if (motion.property === "alpha") {
          const fromColor = [...part.color];
          const toColor = [...part.color];
          fromColor[3] *= motion.from;
          toColor[3] *= motion.to;
          assetBinding.colorParamKey = paramKey;
          paramTween.lane = "Vector";
          paramTween.fromVector = fromColor;
          paramTween.toVector = toColor;
          paramDefaults.push({ paramKey, lane: "Vector", vectorValue: fromColor });
        } else {
          fail(`catalog.${item.id}.parts[${index}].motions[${motionIndex}]`, `unsupported property ${motion.property}`);
        }
        behaviors.push({
          slot: behaviorSlot,
          kind: "ParamTween",
          activeByDefault: true,
          paramTween
        });
      });
      if (paramDefaults.length > 0) {
        definition.paramDefaults = paramDefaults;
      }
      defs.push(definition);
    });

    defs.splice(defs.length - item.parts.length, 0, { id: root, children });
  }

  validateGeneratedDefinitions(defs);
  return defs;
}

function validateGeneratedDefinitions(definitions) {
  const definitionsById = new Map();
  for (let index = 0; index < definitions.length; index++) {
    const definition = definitions[index];
    const at = `generated performerDefinitions[${index}]`;
    assertObject(definition, at);
    assertIdentifier(definition.id, `${at}.id`, identifierPattern);
    if (definitionsById.has(definition.id)) fail(`${at}.id`, `duplicate generated definition ${JSON.stringify(definition.id)}`);
    definitionsById.set(definition.id, definition);
  }

  const concreteAssetKinds = new Set(["Mesh", ...primitiveAssetKinds, ...concreteEmitterAssetKinds]);
  for (const item of catalog) {
    const root = definitionsById.get(rootId(item));
    if (root === undefined || !Array.isArray(root.children) || root.children.length !== item.parts.length) {
      fail(`generated catalog.${item.id}`, `root must declare exactly ${item.parts.length} child performers`);
    }
    for (let index = 0; index < root.children.length; index++) {
      const childReference = root.children[index];
      assertObject(childReference, `generated catalog.${item.id}.children[${index}]`);
      if (childReference.scopeTag !== item.group) {
        fail(`generated catalog.${item.id}.children[${index}].scopeTag`, `expected ${JSON.stringify(item.group)}`);
      }
      const child = definitionsById.get(childReference.definitionId);
      if (child === undefined) {
        fail(`generated catalog.${item.id}.children[${index}].definitionId`, "references an unknown child performer");
      }
      const assetBindings = child.behaviors?.filter(behavior => behavior.kind === "AssetBinding");
      if (!Array.isArray(assetBindings) || assetBindings.length !== 1) {
        fail(`generated catalog.${item.id}.children[${index}]`, "must contain exactly one AssetBinding behavior");
      }
      const assetKind = assetBindings[0].assetBinding?.assetKind;
      assertAssetKind(assetKind, `generated catalog.${item.id}.children[${index}].assetKind`, concreteAssetKinds);
      const expectedAssetKind = item.parts[index].t === "mesh" ? "Mesh" : item.parts[index].kind;
      if (assetKind !== expectedAssetKind) {
        fail(
          `generated catalog.${item.id}.children[${index}].assetKind`,
          `expected concrete ${JSON.stringify(expectedAssetKind)}, received ${JSON.stringify(assetKind)}`);
      }
    }
  }
}

function writeSharedMod() {
  const concreteEmitters = Object.values(emitterAssets);
  writeJson(path.join(shared, "assets/Presentation/emitter_assets.json"), concreteEmitters.map(asset => ({
    id: asset.id,
    assetKind: asset.kind,
    runtimeFormatVersion: asset.runtimeFormatVersion,
    sha256: asset.sha256
  })));
  writeJson(path.join(shared, "assets/Presentation/host_assets.json"), concreteEmitters.map(asset => ({
    id: `${asset.id}.raylib`,
    assetKind: asset.kind,
    assetId: asset.id,
    backendId: "raylib",
    sourceUris: [`PerformerRaylibMicroShowcasesMod:assets/Presentation/Effekseer/${asset.file}`]
  })));
  writeJson(path.join(shared, "assets/Presentation/showcases.manifest.json"), catalog.map(item => ({
    id: item.id,
    mapId: mapId(item),
    rootDefinitionId: rootId(item),
    title: item.title,
    group: item.group,
    themeId: item.themeId,
    themeVersion: item.themeVersion,
    meshMaterialToken: item.meshMaterialToken,
    themeLabel: themeLabel[item.themeId],
    intent: item.intent,
    binding: bindingId(item),
    preset: presetId(item),
    artifactDir: `${artifactRoot}/${item.id}`,
    recording: `${artifactRoot}/${item.id}/${item.id}.mp4`,
    poster: `${artifactRoot}/${item.id}/poster.png`,
    signatureEmitterId: item.signatureEmitterId === undefined ? null : item.signatureEmitterId,
    semanticPresetId: item.semanticPresetId === undefined ? null : item.semanticPresetId,
    semanticPresetVersion: item.semanticPresetVersion === undefined ? null : item.semanticPresetVersion,
    parameters: item.parameters === undefined ? null : item.parameters
  })));
  writeJson(path.join(shared, "assets/Presentation/performers.json"), performerDefinitions);
  writeJson(path.join(shared, "assets/Entities/templates.json"), catalog.map(item => ({
    id: entityId(item),
    components: {
      Name: { Value: `RaylibMicro.${item.id}` },
      WorldPositionCm: { Value: { X: 0, Y: 0 } },
      PresentationStaticTransform: {},
      FacingDirection: { AngleRad: 0 },
      AttributeBuffer: { base: {}, current: {} },
      GameplayTagContainer: {},
      TagCountContainer: {}
    }
  })));
  writeJson(path.join(shared, "assets/game.json"), { startupMapId: mapId(catalog[0]) });

  const mapsDir = path.join(shared, "assets/Maps");
  fs.mkdirSync(mapsDir, { recursive: true });
  for (const file of fs.readdirSync(mapsDir)) {
    if (file.startsWith("raylib_micro_")) fs.rmSync(path.join(mapsDir, file), { force: true });
  }

  for (const item of catalog) {
    writeJson(path.join(mapsDir, `${mapId(item)}.json`), {
      Id: mapId(item),
      Tags: ["showcase", "performer", "raylib", "micro", item.group, item.themeId, "focus", "Raylib.Background:Deep", "Raylib.DebugGuides:Off"],
      Boards: [{ Name: "default", SpatialType: "Grid", WidthInMacroTiles: 1, HeightInMacroTiles: 1, GridCellSizeCm: 100 }],
      DefaultCamera: item.camera,
      Entities: [{ Template: entityId(item), Overrides: { WorldPositionCm: { Value: { X: 0, Y: 0 } } } }]
    });
    fs.mkdirSync(path.join(repo, artifactRoot, item.id), { recursive: true });
  }
}

function writeEntries() {
  fs.mkdirSync(entryRoot, { recursive: true });
  writeText(path.join(entryRoot, "Directory.Build.props"), `<Project>
  <Import Project="..\\..\\Directory.Build.props" />
  <PropertyGroup>
    <BaseIntermediateOutputPath>$(TEMP)\\ludots-raylib-micro-obj\\$(MSBuildProjectName)\\</BaseIntermediateOutputPath>
  </PropertyGroup>
</Project>
`);

  const keep = new Set(catalog.map(entryDir));
  for (const dirent of fs.readdirSync(entryRoot, { withFileTypes: true })) {
    if (dirent.isDirectory() && /^performer_raylib_micro_.*_entry$/.test(dirent.name) && !keep.has(dirent.name)) {
      fs.rmSync(path.join(entryRoot, dirent.name), { recursive: true, force: true });
    }
  }

  for (const item of catalog) {
    const dir = path.join(entryRoot, entryDir(item), projectName(item));
    fs.mkdirSync(path.join(dir, "assets"), { recursive: true });
    writeText(path.join(dir, `${projectName(item)}.csproj`), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>${entryTargetFramework}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
    <BaseIntermediateOutputPath>$(TEMP)\\ludots-raylib-micro-obj\\$(MSBuildProjectName)\\</BaseIntermediateOutputPath>
    <IntermediateOutputPath>$(BaseIntermediateOutputPath)$(Configuration)\\</IntermediateOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Remove="obj\\**\\*.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\..\\performer_raylib_micro_showcases\\PerformerRaylibMicroShowcasesMod\\PerformerRaylibMicroShowcasesMod.csproj" />
  </ItemGroup>
  <Target Name="ExposeLauncherReferenceAssembly" AfterTargets="Build" Condition="'$(ProduceReferenceAssembly)' == 'true'">
    <PropertyGroup>
      <_LauncherReferenceDir>$(MSBuildProjectDirectory)\\obj\\$(Configuration)\\ref\\</_LauncherReferenceDir>
      <_LauncherReferenceAssembly>$(_LauncherReferenceDir)$(AssemblyName).dll</_LauncherReferenceAssembly>
      <_ProducedReferenceAssembly>$(IntermediateOutputPath)ref\\$(AssemblyName).dll</_ProducedReferenceAssembly>
      <_ProducedReferenceIntAssembly>$(IntermediateOutputPath)refint\\$(AssemblyName).dll</_ProducedReferenceIntAssembly>
      <_ProducedMainAssembly>$(IntermediateOutputPath)$(AssemblyName).dll</_ProducedMainAssembly>
    </PropertyGroup>
    <MakeDir Directories="$(_LauncherReferenceDir)" />
    <Copy SourceFiles="$(_ProducedReferenceAssembly)" DestinationFiles="$(_LauncherReferenceAssembly)" SkipUnchangedFiles="true" Condition="Exists('$(_ProducedReferenceAssembly)')" />
    <Copy SourceFiles="$(_ProducedReferenceIntAssembly)" DestinationFiles="$(_LauncherReferenceAssembly)" SkipUnchangedFiles="true" Condition="!Exists('$(_ProducedReferenceAssembly)') and Exists('$(_ProducedReferenceIntAssembly)')" />
    <Copy SourceFiles="$(_ProducedMainAssembly)" DestinationFiles="$(_LauncherReferenceAssembly)" SkipUnchangedFiles="true" Condition="!Exists('$(_ProducedReferenceAssembly)') and !Exists('$(_ProducedReferenceIntAssembly)') and Exists('$(_ProducedMainAssembly)')" />
  </Target>
</Project>
`);
    writeJson(path.join(dir, "mod.json"), {
      name: projectName(item),
      version: "1.0.0",
      description: `Entry mod for Raylib micro showcase: ${item.title}.`,
      main: `bin/${entryTargetFramework}/${projectName(item)}.dll`,
      priority: 0,
      dependencies: { PerformerRaylibMicroShowcasesMod: "^1.0.0" },
      author: "Ludots Team",
      tags: ["showcase", "performer", "raylib", "micro", item.group, item.themeId]
    });
    writeText(path.join(dir, `${projectName(item)}Entry.cs`), `using Ludots.Core.Modding;

namespace ${projectName(item)};

public sealed class ${projectName(item)}Entry : IMod
{
    public void OnLoad(IModContext context)
    {
    }

    public void OnUnload()
    {
    }
}
`);
    writeJson(path.join(dir, "assets/game.json"), { startupMapId: mapId(item) });
  }
}

function validateDocumentCollection(document, collectionKey, entryKey, documentName) {
  assertObject(document, documentName);
  assertArray(document[collectionKey], `${documentName}.${collectionKey}`, 0);
  for (let index = 0; index < document[collectionKey].length; index++) {
    const entry = document[collectionKey][index];
    const at = `${documentName}.${collectionKey}[${index}]`;
    assertObject(entry, at);
    assertText(entry[entryKey], `${at}.${entryKey}`, 256);
  }
  return document[collectionKey];
}

function updateLauncher() {
  const config = readJson(path.join(repo, "launcher.config.json"));
  config.bindings = validateDocumentCollection(config, "bindings", "name", "launcher.config.json")
    .filter(entry => !entry.name.startsWith("performer_raylib_micro_"));
  for (const item of catalog) {
    config.bindings.push({ name: bindingId(item), target: { type: "path", value: entryProjectPath(item), projectPath: `${projectName(item)}.csproj` } });
  }
  writeJson(path.join(repo, "launcher.config.json"), config);

  const presets = readJson(path.join(repo, "launcher.presets.json"));
  presets.presets = validateDocumentCollection(presets, "presets", "id", "launcher.presets.json")
    .filter(entry => !entry.id.startsWith("performer_raylib_micro_"));
  for (const item of catalog) {
    presets.presets.push({ id: presetId(item), name: `Raylib Micro - ${item.title}`, selectors: [`$${bindingId(item)}`], adapterId: "raylib", buildMode: "auto" });
  }
  writeJson(path.join(repo, "launcher.presets.json"), presets);

  const registry = readJson(path.join(repo, "showcase.registry.json"));
  registry.showcases = validateDocumentCollection(registry, "showcases", "id", "showcase.registry.json")
    .filter(entry => !entry.id.startsWith("performer_raylib_micro_"));
  for (const item of catalog) {
    registry.showcases.push({
      id: bindingId(item),
      path: entryProjectPath(item),
      projectPath: `${projectName(item)}.csproj`,
      title: `Raylib 微展示 · ${item.title}`,
      summary: item.group === "primitive"
        ? `只展示“${item.title}”这一种 Raylib 具体实现图元。`
        : `只展示“${item.title}”这一种玩家语义，并保留必要上下文。`,
      tier: "T2",
      category: "showcase",
      tags: ["performer", "raylib", "micro", item.group, item.themeId],
      binding: bindingId(item),
      preset: presetId(item),
      docsPath,
      readmePath: null,
      acceptanceTest: null,
      artifactDir: `${artifactRoot}/${item.id}`,
      screenshot: `${artifactRoot}/${item.id}/poster.png`,
      status: "active",
      notes: `独立录屏目标: ${artifactRoot}/${item.id}/${item.id}.mp4`
    });
  }
  writeJson(path.join(repo, "showcase.registry.json"), registry);
}

function writeReport() {
  const runtimeInputSha256 = computeRaylibMicroRuntimeInputSha256(repo);
  const evidence = verifyRecordingEvidence({ repo, runtimeInputSha256, requireComplete: false });
  const currentEvidence = evidence.currentById;
  const cards = catalog.map(item => {
    const video = `${artifactRoot}/${item.id}/${item.id}.mp4`;
    const poster = `${artifactRoot}/${item.id}/poster.png`;
    return {
      ...item,
      binding: bindingId(item),
      mapId: mapId(item),
      video,
      poster,
      hasVideo: currentEvidence.has(item.id) && fs.existsSync(path.join(repo, video)),
      hasPoster: currentEvidence.has(item.id) && fs.existsSync(path.join(repo, poster))
    };
  });

  const primitiveCards = cards.filter(x => x.group === "primitive");
  const semanticCards = cards.filter(x => x.group === "semantic");
  const recorded = cards.filter(x => x.hasVideo).length;
  const delivered = cards.filter(x => x.hasVideo && x.hasPoster).length;
  const themeById = Object.fromEntries(authoring.themes.map(theme => [theme.id, theme]));
  const themeColor = themeId => {
    const theme = themeById[themeId];
    if (theme === undefined) fail("report theme", `unknown theme ${JSON.stringify(themeId)}`);
    const color = theme.palette[0];
    return `rgb(${color.slice(0, 3).map(value => Math.round(value * 255)).join(",")})`;
  };
  const card = item => `<article class="showcase-card" id="${item.id}" data-group="${item.group}" data-theme="${item.themeId}" data-search="${item.title} ${item.intent} ${item.id} ${themeLabel[item.themeId]}">
  <div class="media">${item.hasVideo ? `<video controls preload="none" ${item.hasPoster ? `poster="${rel(item.poster)}"` : ""} aria-label="${item.title} 录屏"><source src="${rel(item.video)}" type="video/mp4"></video>` : item.hasPoster ? `<img src="${rel(item.poster)}" alt="${item.title}">` : `<div class="missing">缺少录屏与海报<br><code>${item.id}</code></div>`}</div>
  <div class="card-body">
    <div class="card-meta"><span>${item.group === "primitive" ? "实现图元" : "玩家语义"}</span><span class="theme"><i style="--swatch:${themeColor(item.themeId)}"></i>${themeLabel[item.themeId]}</span></div>
    <h3>${item.title}</h3>
    <p>${item.intent}</p>
    <details><summary>实现来源</summary><code>${item.group === "semantic" ? `专属资源: ${item.signatureEmitterId}\n` : ""}${[...new Set(item.parts.filter(part => part.t !== "mesh").map(part => part.t === "emitter" ? `${part.kind}: ${part.assetId} (${part.sourceFile})` : `Raylib ${part.kind}`))].join("\n")}\n入口: ${item.binding}</code></details>
  </div>
</article>`;
  const section = (id, title, description, items) => `<section class="catalog-section" id="${id}" data-section="${items[0].group}"><div class="section-title"><div><h2>${title}</h2><p>${description}</p></div><strong>${items.length}</strong></div><div class="grid">${items.map(card).join("")}</div></section>`;
  const themeFilters = authoring.themes.map(theme => `<button class="theme-filter" type="button" data-theme-filter="${theme.id}" aria-pressed="false"><i style="--swatch:${themeColor(theme.id)}"></i>${theme.label}</button>`).join("");

  const html = `<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Raylib Performer 表现目录</title>
  <style>
    :root{color-scheme:dark;--bg:#090c0f;--surface:#11161b;--surface2:#171d23;--line:#303840;--line2:#20272e;--text:#f5f7f8;--muted:#aeb8c0;--cyan:#48d8f0;--amber:#ffc85a;--green:#70e39a;--red:#ff715b}
    *{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:var(--bg);color:var(--text);font-family:"Microsoft YaHei","Segoe UI",Arial,sans-serif;letter-spacing:0}
    button,input{font:inherit;letter-spacing:0}.shell{width:min(1520px,calc(100% - 40px));margin:0 auto}
    .masthead{min-height:92px;display:flex;align-items:center;justify-content:space-between;gap:24px;border-bottom:1px solid var(--line);padding:18px 0}
    .masthead h1{font-size:28px;line-height:1.2;margin:0 0 6px}.masthead p{color:var(--muted);font-size:14px;margin:0}.delivery{display:flex;align-items:center;gap:10px;color:var(--green);font-weight:700;white-space:nowrap}.delivery.incomplete{color:var(--red)}.delivery::before{content:"";width:9px;height:9px;border-radius:50%;background:currentColor;box-shadow:0 0 16px currentColor}
    .contract{display:grid;grid-template-columns:repeat(4,1fr);border-bottom:1px solid var(--line)}.contract div{padding:15px 18px;border-right:1px solid var(--line2)}.contract div:first-child{padding-left:0}.contract div:last-child{border-right:0}.contract strong{display:block;font-size:13px;margin-bottom:5px}.contract span{color:var(--muted);font-size:13px;line-height:1.5}
    .toolbar-wrap{position:sticky;top:0;z-index:10;background:rgba(9,12,15,.96);backdrop-filter:blur(14px);border-bottom:1px solid var(--line)}.toolbar{display:flex;align-items:center;gap:12px;min-height:64px;padding:10px 0;flex-wrap:wrap}.segment{display:flex;border:1px solid var(--line);border-radius:6px;overflow:hidden}.segment button,.theme-filter{border:0;background:transparent;color:var(--muted);cursor:pointer;min-height:38px;padding:0 13px}.segment button+button{border-left:1px solid var(--line)}.segment button[aria-pressed="true"]{background:var(--text);color:#11161b}.theme-filter{display:inline-flex;align-items:center;gap:7px;border-left:1px solid var(--line2)}.theme-filter[aria-pressed="true"]{color:var(--text);background:var(--surface2)}.theme-filter i,.theme i{display:inline-block;width:10px;height:10px;background:var(--swatch);border-radius:2px;box-shadow:0 0 10px color-mix(in srgb,var(--swatch),transparent 50%)}
    .theme-options{display:flex;align-items:center;border:1px solid var(--line);border-radius:6px;overflow:hidden}.theme-options .theme-filter:first-child{border-left:0}.search{margin-left:auto;width:min(300px,100%);height:40px;border:1px solid var(--line);border-radius:6px;background:var(--surface);color:var(--text);padding:0 12px;outline:none}.search:focus{border-color:var(--cyan)}.result-count{font-size:13px;color:var(--muted);white-space:nowrap}
    main{padding:24px 0 72px}.catalog-section{margin:0 0 38px}.section-title{display:flex;align-items:end;justify-content:space-between;gap:20px;padding:0 0 12px;border-bottom:1px solid var(--line);margin-bottom:14px}.section-title h2{font-size:22px;margin:0 0 4px}.section-title p{font-size:13px;color:var(--muted);margin:0}.section-title>strong{font-size:30px;color:var(--amber)}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:14px}.showcase-card{min-width:0;border:1px solid var(--line);border-radius:6px;overflow:hidden;background:var(--surface)}.showcase-card[hidden]{display:none}.media{aspect-ratio:16/9;background:#030507;border-bottom:1px solid var(--line);overflow:hidden}.media video,.media img{display:block;width:100%;height:100%;object-fit:cover}.missing{height:100%;display:grid;place-items:center;text-align:center;color:var(--red)}
    .card-body{padding:13px 14px 15px}.card-meta{display:flex;justify-content:space-between;gap:10px;color:var(--cyan);font-size:12px;margin-bottom:8px}.theme{display:inline-flex;align-items:center;gap:6px;color:var(--muted)}.card-body h3{font-size:18px;line-height:1.35;margin:0 0 7px}.card-body p{min-height:42px;color:var(--muted);font-size:13px;line-height:1.6;margin:0 0 10px}.card-body details{border-top:1px solid var(--line2);padding-top:9px}.card-body summary{cursor:pointer;color:var(--muted);font-size:12px}.card-body code{display:block;color:#d8e9ee;font-size:11px;line-height:1.5;margin-top:8px;overflow-wrap:anywhere}
    .footer{display:flex;justify-content:space-between;gap:20px;align-items:center;border-top:1px solid var(--line);padding:20px 0 42px;color:var(--muted);font-size:13px}.footer a{color:var(--text)}button:focus-visible,input:focus-visible,summary:focus-visible,a:focus-visible{outline:2px solid var(--cyan);outline-offset:2px}
    @media(max-width:900px){.contract{grid-template-columns:repeat(2,1fr)}.contract div:nth-child(2){border-right:0}.search{order:3;margin-left:0;flex:1 1 100%}}
    @media(max-width:620px){.shell{width:min(100% - 24px,1520px)}.masthead{align-items:flex-start;flex-direction:column;gap:10px}.masthead h1{font-size:23px}.contract{grid-template-columns:1fr}.contract div,.contract div:first-child{padding:12px 0;border-right:0;border-bottom:1px solid var(--line2)}.contract div:last-child{border-bottom:0}.toolbar-wrap{position:static}.toolbar{align-items:stretch}.segment,.theme-options{width:100%;overflow-x:auto}.segment button{flex:1}.theme-filter{flex:0 0 auto}.result-count{width:100%}.grid{grid-template-columns:1fr}.footer{align-items:flex-start;flex-direction:column}}
  </style>
</head>
<body>
  <header class="shell">
    <div class="masthead">
      <div><h1>Raylib Performer 表现目录</h1><p>一个实现一个入口，一个玩家语义一个入口。</p></div>
      <div class="delivery${delivered === cards.length ? "" : " incomplete"}">${delivered}/${cards.length} 当前版本录屏与海报${delivered === cards.length ? "齐全" : "缺失"}</div>
    </div>
    <div class="contract" aria-label="四层声明边界">
      <div><strong>逻辑参数</strong><span>source、target、alpha、size、width、tempo</span></div>
      <div><strong>具体实现</strong><span>原生 Ring、Line；Effekseer 五种具体发射器</span></div>
      <div><strong>玩家语义</strong><span>专属 Effekseer 资源由 child performer 组合并管理生命周期</span></div>
      <div><strong>主题参数</strong><span>离线展开，不是 Behavior，不选择后端</span></div>
    </div>
  </header>
  <div class="toolbar-wrap">
    <div class="toolbar shell">
      <div class="segment" aria-label="目录分组">
        <button type="button" data-group-filter="all" aria-pressed="true">全部 ${cards.length}</button>
        <button type="button" data-group-filter="primitive" aria-pressed="false">实现 ${primitiveCards.length}</button>
        <button type="button" data-group-filter="semantic" aria-pressed="false">语义 ${semanticCards.length}</button>
      </div>
      <div class="theme-options" aria-label="主题筛选">
        <button class="theme-filter" type="button" data-theme-filter="all" aria-pressed="true">全部主题</button>${themeFilters}
      </div>
      <input class="search" type="search" aria-label="搜索展示" placeholder="搜索名称或语义" autocomplete="off">
      <span class="result-count" aria-live="polite">显示 ${cards.length} 项</span>
    </div>
  </div>
  <main class="shell">
    ${section("primitive", "实现图元", `Raylib 实际消费的 ${primitiveCards.length} 种明确绘制入口。`, primitiveCards)}
    ${section("semantic", "玩家语义", "每段录屏只证明一种反馈；特效主体来自 Effekseer，Mesh 只保留场景参照。", semanticCards)}
  </main>
  <footer class="footer shell"><span>${evidence.recording.width}×${evidence.recording.height} · ${evidence.recording.videoFrameCount} 帧 · ${evidence.recording.durationSeconds} 秒 · ${recorded}/${cards.length} MP4</span><span><a href="performer-authoring-runtime-pipeline.html">查看生产管线</a> · <a href="../docs/prd/17-performer-authoring-runtime.html">查看 PRD</a></span></footer>
  <script>
    (function () {
      var state = { group: "all", theme: "all", query: "" };
      var cards = Array.from(document.querySelectorAll(".showcase-card"));
      var sections = Array.from(document.querySelectorAll(".catalog-section"));
      var count = document.querySelector(".result-count");
      function applyFilters() {
        var visible = 0;
        cards.forEach(function (card) {
          var matchesGroup = state.group === "all" || card.dataset.group === state.group;
          var matchesTheme = state.theme === "all" || card.dataset.theme === state.theme;
          var matchesQuery = !state.query || card.dataset.search.toLocaleLowerCase("zh-CN").includes(state.query);
          card.hidden = !(matchesGroup && matchesTheme && matchesQuery);
          if (!card.hidden) visible += 1;
        });
        sections.forEach(function (section) {
          section.hidden = !section.querySelector(".showcase-card:not([hidden])");
        });
        count.textContent = "显示 " + visible + " 项";
      }
      document.querySelectorAll("[data-group-filter]").forEach(function (button) {
        button.addEventListener("click", function () {
          state.group = button.dataset.groupFilter;
          document.querySelectorAll("[data-group-filter]").forEach(function (candidate) { candidate.setAttribute("aria-pressed", String(candidate === button)); });
          applyFilters();
        });
      });
      document.querySelectorAll("[data-theme-filter]").forEach(function (button) {
        button.addEventListener("click", function () {
          state.theme = button.dataset.themeFilter;
          document.querySelectorAll("[data-theme-filter]").forEach(function (candidate) { candidate.setAttribute("aria-pressed", String(candidate === button)); });
          applyFilters();
        });
      });
      document.querySelector(".search").addEventListener("input", function (event) {
        state.query = event.target.value.trim().toLocaleLowerCase("zh-CN");
        applyFilters();
      });
      document.querySelectorAll("video").forEach(function (video) {
        video.addEventListener("play", function () {
          document.querySelectorAll("video").forEach(function (candidate) { if (candidate !== video) candidate.pause(); });
        });
      });
    }());
  </script>
</body>
</html>`;
  writeText(path.join(repo, recordingContract.reportPath), html);
}

function rel(artifactPath) {
  return artifactPath.replace(/^artifacts\//, "");
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
  } catch (error) {
    throw new Error(`Failed to parse JSON ${file}: ${error.message}`, { cause: error });
  }
}

function hashFile(file) {
  return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

function writeJson(file, data) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(data, null, 2) + "\n", "utf8");
}

function writeText(file, data) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, data, "utf8");
}

if (cli.validateOnly) {
  buildDefinitions();
  console.log(`Validated ${catalog.length} Raylib micro showcases from ${authoringPath} (${authoring.namespace}).`);
} else {
  writeSharedMod();
  writeEntries();
  updateLauncher();
  if (!cli.skipReport) writeReport();
  console.log(`Generated ${catalog.length} Raylib micro showcases from ${authoringPath} (${authoring.namespace}).`);
}
