import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { requiredEmitterProfile } from "./raylib-effekseer-profile-contracts.mjs";

const repo = process.cwd();
const shared = path.join(repo, "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod");
const entryRoot = path.join(repo, "mods/showcases/performer_raylib_micro_showcases_entries");
const artifactRoot = "artifacts/raylib-performer-micro-showcases";
const docsPath = "docs/prd/17-performer-authoring-runtime.html";

const defaultAuthoringPath = path.join(
  shared,
  "assets/Presentation/Authoring/raylib-micro-showcases.authoring.json");
const expectedShowcaseCounts = { primitive: 7, semantic: 29 };
const forbiddenAssetKinds = new Set(["effect", "vfx", "particleemitter", "ribbontrail"]);
const primitiveAssetKinds = new Set(["Ring", "Line"]);
const emitterAssetKinds = new Set([
  "SpriteEmitter",
  "RibbonEmitter",
  "TrackEmitter",
  "RingEmitter",
  "ModelEmitter"
]);
const motionBehaviorSlotByProperty = new Map([
  ["scale", "motion_scale"],
  ["alpha", "motion_alpha"]
]);
const identifierPattern = /^[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*$/;
const lowerIdentifierPattern = /^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$/;
const namespacePattern = /^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)+$/;
const sha256Pattern = /^[a-f0-9]{64}$/;
const maxVectorComponent = 1_000_000;

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

function parseCli(args) {
  const result = { authoringPath: undefined, validateOnly: false, refreshEmitterHashes: false };
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
    } else {
      fail("CLI", `unknown argument ${JSON.stringify(arg)}`);
    }
  }
  return result;
}

function refreshEmitterHashes(file) {
  const config = readJson(file);
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

  writeJson(file, {
    schemaVersion: config.schemaVersion,
    namespace: config.namespace,
    themes: config.themes,
    motionClasses: config.motionClasses,
    emitters: config.emitters,
    showcases: config.showcases
  });
}

function parseAuthoring(file) {
  if (!fs.existsSync(file)) fail("authoring", `file does not exist: ${file}`);
  const config = readJson(file);
  assertObject(config, "authoring");
  assertKeys(config, ["schemaVersion", "namespace", "themes", "motionClasses", "emitters", "showcases"], [], "authoring");
  if (config.schemaVersion !== 1) {
    fail("authoring.schemaVersion", `expected 1, received ${JSON.stringify(config.schemaVersion)}`);
  }
  assertIdentifier(config.namespace, "authoring.namespace", namespacePattern);

  const themes = validateThemes(config.themes);
  const themesById = new Map(themes.map(theme => [theme.id, theme]));
  const motionClasses = validateMotionClasses(config.motionClasses);
  const motionClassesById = new Map(motionClasses.map(motion => [motion.id, motion]));
  const emitters = validateEmitters(config.emitters);
  const emittersById = new Map(emitters.map(asset => [asset.id, asset]));
  const showcases = validateShowcases(config.showcases, themesById, motionClassesById, emittersById);

  return {
    schemaVersion: config.schemaVersion,
    namespace: config.namespace,
    themes,
    motionClasses,
    emitters,
    showcases
  };
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
    assertKeys(theme, ["id", "label", "palette"], [], at);
    assertIdentifier(theme.id, `${at}.id`, lowerIdentifierPattern);
    assertUnique(seen, theme.id, `${at}.id`, "theme id");
    assertText(theme.label, `${at}.label`, 128);
    assertArray(theme.palette, `${at}.palette`, 1);
    const palette = theme.palette.map((color, colorIndex) =>
      validateColor(color, `${at}.palette[${colorIndex}]`));
    return { id: theme.id, label: theme.label, palette };
  });
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
    assertAssetKind(asset.assetKind, `${at}.assetKind`, emitterAssetKinds);
    assertText(asset.sourceFile, `${at}.sourceFile`, 260);
    if (path.basename(asset.sourceFile) !== asset.sourceFile || !asset.sourceFile.endsWith(".efkefc")) {
      fail(`${at}.sourceFile`, "must be a basename ending in .efkefc");
    }
    assertUnique(sourceFiles, asset.sourceFile, `${at}.sourceFile`, "emitter source file");
    if (asset.runtimeFormatVersion !== 1810) {
      fail(`${at}.runtimeFormatVersion`, `expected pinned Effekseer runtime format 1810, received ${JSON.stringify(asset.runtimeFormatVersion)}`);
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

function validateShowcases(value, themesById, motionClassesById, emittersById) {
  assertArray(value, "authoring.showcases", 1);
  const ids = new Set();
  const rootIds = new Set();
  const counts = { primitive: 0, semantic: 0 };
  const showcases = value.map((showcase, index) => {
    const at = `authoring.showcases[${index}]`;
    assertObject(showcase, at);
    assertKeys(showcase, [
      "id",
      "title",
      "group",
      "theme",
      "intent",
      "rootDefinitionId",
      "camera",
      "parts"
    ], ["signatureEmitterId"], at);
    assertIdentifier(showcase.id, `${at}.id`, lowerIdentifierPattern);
    assertUnique(ids, showcase.id, `${at}.id`, "showcase id");
    assertText(showcase.title, `${at}.title`, 256);
    if (showcase.group !== "primitive" && showcase.group !== "semantic") {
      fail(`${at}.group`, "must be primitive or semantic");
    }
    if (!showcase.id.startsWith(`${showcase.group}_`)) {
      fail(`${at}.id`, `must start with ${showcase.group}_`);
    }
    counts[showcase.group]++;
    assertIdentifier(showcase.theme, `${at}.theme`, lowerIdentifierPattern);
    const theme = themesById.get(showcase.theme);
    if (!theme) fail(`${at}.theme`, `unknown theme ${JSON.stringify(showcase.theme)}`);
    assertText(showcase.intent, `${at}.intent`, 1_000);
    assertIdentifier(showcase.rootDefinitionId, `${at}.rootDefinitionId`, identifierPattern);
    assertUnique(rootIds, showcase.rootDefinitionId, `${at}.rootDefinitionId`, "root definition id");
    const camera = validateCamera(showcase.camera, `${at}.camera`);
    assertArray(showcase.parts, `${at}.parts`, 1);
    if (showcase.parts.length > 16) {
      fail(`${at}.parts`, `declares ${showcase.parts.length} children, but PerformerChildren capacity is 16`);
    }
    const parts = showcase.parts.map((part, partIndex) =>
      validatePart(part, theme, motionClassesById, emittersById, `${at}.parts[${partIndex}]`));
    if (showcase.group === "semantic") {
      assertIdentifier(showcase.signatureEmitterId, `${at}.signatureEmitterId`, lowerIdentifierPattern);
      const signature = emittersById.get(showcase.signatureEmitterId);
      if (!signature) fail(`${at}.signatureEmitterId`, `unknown emitter ${JSON.stringify(showcase.signatureEmitterId)}`);
      if (signature.usage !== "signature" || signature.ownerShowcase !== showcase.id) {
        fail(`${at}.signatureEmitterId`, `must reference the signature emitter owned by ${JSON.stringify(showcase.id)}`);
      }
      const signatureReferences = parts.filter(part => part.type === "emitter" && part.emitterId === showcase.signatureEmitterId).length;
      if (signatureReferences !== 1) {
        fail(`${at}.parts`, `must reference signatureEmitterId exactly once; received ${signatureReferences}`);
      }
      if (parts.some(part => part.type === "primitive")) {
        fail(`${at}.parts`, "semantic showcases must express their presentation body with concrete Effekseer emitters");
      }
      if (parts.some(part => part.type === "mesh" && part.role !== "scene")) {
        fail(`${at}.parts`, "semantic mesh parts are limited to explicit scene references");
      }
    } else if (showcase.signatureEmitterId !== undefined) {
      fail(`${at}.signatureEmitterId`, "is only valid for semantic showcases");
    }
    return { ...showcase, camera, parts };
  });

  for (const [group, expected] of Object.entries(expectedShowcaseCounts)) {
    if (counts[group] !== expected) {
      fail("authoring.showcases", `expected ${expected} ${group} declarations, received ${counts[group]}`);
    }
  }
  return showcases;
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

function validatePart(part, theme, motionClassesById, emittersById, at) {
  assertObject(part, at);
  if (part.type === "mesh") {
    assertKeys(part, ["type", "role", "assetId", "offset", "scale", "color"], ["rotation", "motions"], at);
    if (part.role !== "scene") fail(`${at}.role`, "must be scene; presentation geometry belongs in a concrete emitter asset");
    assertIdentifier(part.assetId, `${at}.assetId`, identifierPattern);
  } else if (part.type === "primitive") {
    assertKeys(part, ["type", "assetKind", "offset", "scale", "color"], ["rotation", "motions"], at);
    assertAssetKind(part.assetKind, `${at}.assetKind`, primitiveAssetKinds);
  } else if (part.type === "emitter") {
    assertKeys(part, ["type", "emitterId", "offset", "scale", "color"], ["rotation", "target", "motions"], at);
    assertIdentifier(part.emitterId, `${at}.emitterId`, lowerIdentifierPattern);
    if (!emittersById.has(part.emitterId)) {
      fail(`${at}.emitterId`, `unknown emitter ${JSON.stringify(part.emitterId)}`);
    }
  } else {
    fail(`${at}.type`, "must be mesh, primitive, or emitter");
  }

  const offset = validateVector(part.offset, 3, `${at}.offset`, -1_000, 1_000);
  const scale = validateVector(part.scale, 3, `${at}.scale`, 0.000001, 1_000);
  const color = validateColorDeclaration(part.color, theme, `${at}.color`);
  const result = { ...part, offset, scale, color };
  if (part.rotation !== undefined) result.rotation = validateRotation(part.rotation, `${at}.rotation`);
  if (part.target !== undefined) result.target = validateVector(part.target, 4, `${at}.target`, -1_000, 1_000);
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
  assertIntegerInRange(value.themeSlot, `${at}.themeSlot`, 0, theme.palette.length - 1);
  return { themeSlot: value.themeSlot };
}

function expandShowcases(config) {
  const emittersById = new Map(config.emitters.map(asset => [asset.id, asset]));
  const themesById = new Map(config.themes.map(theme => [theme.id, theme]));
  const motionsById = new Map(config.motionClasses.map(motion => [motion.id, motion]));
  return config.showcases.map(showcase => {
    const theme = themesById.get(showcase.theme);
    return {
      ...showcase,
      parts: showcase.parts.map(part => {
        const color = Array.isArray(part.color)
          ? [...part.color]
          : [...theme.palette[part.color.themeSlot]];
        const motions = (part.motions ?? []).map(motionRef => ({
          ...motionsById.get(motionRef.class),
          delaySeconds: motionRef.delaySeconds
        }));
        if (part.type === "mesh") return { t: "mesh", role: part.role, assetId: part.assetId, offset: part.offset, scale: part.scale, color, rotation: part.rotation, motions };
        if (part.type === "primitive") return { t: "primitive", kind: part.assetKind, offset: part.offset, scale: part.scale, color, rotation: part.rotation, motions };
        const asset = emittersById.get(part.emitterId);
        return { t: "emitter", kind: asset.assetKind, assetId: asset.id, sourceFile: asset.sourceFile, offset: part.offset, scale: part.scale, color, rotation: part.rotation, target: part.target, motions };
      })
    };
  });
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
        ? { assetKind: "Mesh", assetId: part.assetId, materialId: "default_surface", renderPath: "InstancedStaticMesh", mobility: "Static", localOffset: part.offset, localScale: part.scale }
        : part.t === "emitter"
          ? { assetKind: part.kind, assetId: part.assetId, renderPath: "Primitive", mobility: "Static", localOffset: part.offset, localScale: part.scale }
          : { assetKind: part.kind, renderPath: "Primitive", mobility: "Static", localOffset: part.offset, localScale: part.scale };
      if (part.rotation) assetBinding.localRotation = part.rotation;
      const behaviors = [{ slot: "body", kind: "AssetBinding", activeByDefault: true, assetBinding }];
      const paramDefaults = [];
      const definition = { id, defaultColor: part.color, behaviors };
      if (part.target) {
        assetBinding.targetParamKey = "raylib.micro.emitter.target";
        paramDefaults.push({ paramKey: "raylib.micro.emitter.target", lane: "Vector", vectorValue: part.target });
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

  return defs;
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
    theme: item.theme,
    themeLabel: themeLabel[item.theme],
    intent: item.intent,
    binding: bindingId(item),
    preset: presetId(item),
    artifactDir: `${artifactRoot}/${item.id}`,
    recording: `${artifactRoot}/${item.id}/${item.id}.mp4`,
    poster: `${artifactRoot}/${item.id}/poster.png`,
    signatureEmitterId: item.signatureEmitterId ?? null
  })));
  writeJson(path.join(shared, "assets/Presentation/performers.json"), buildDefinitions());
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
      Tags: ["showcase", "performer", "raylib", "micro", item.group, item.theme, "focus", "Raylib.Background:Deep", "Raylib.DebugGuides:Off"],
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
    <TargetFramework>net8.0</TargetFramework>
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
      main: `bin/net8.0/${projectName(item)}.dll`,
      priority: 0,
      dependencies: { PerformerRaylibMicroShowcasesMod: "^1.0.0" },
      author: "Ludots Team",
      tags: ["showcase", "performer", "raylib", "micro", item.group, item.theme]
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

function updateLauncher() {
  const config = readJson(path.join(repo, "launcher.config.json"));
  config.bindings = (config.bindings ?? []).filter(x => !String(x.name).startsWith("performer_raylib_micro_"));
  for (const item of catalog) {
    config.bindings.push({ name: bindingId(item), target: { type: "path", value: entryProjectPath(item), projectPath: `${projectName(item)}.csproj` } });
  }
  writeJson(path.join(repo, "launcher.config.json"), config);

  const presets = readJson(path.join(repo, "launcher.presets.json"));
  presets.presets = (presets.presets ?? []).filter(x => !String(x.id).startsWith("performer_raylib_micro_"));
  for (const item of catalog) {
    presets.presets.push({ id: presetId(item), name: `Raylib Micro - ${item.title}`, selectors: [`$${bindingId(item)}`], adapterId: "raylib", buildMode: "auto" });
  }
  writeJson(path.join(repo, "launcher.presets.json"), presets);

  const registry = readJson(path.join(repo, "showcase.registry.json"));
  registry.showcases = (registry.showcases ?? []).filter(x => !String(x.id).startsWith("performer_raylib_micro_"));
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
      tags: ["performer", "raylib", "micro", item.group, item.theme],
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
  const authoringSha256 = hashFile(authoringPath);
  const generatorSha256 = hashFile(path.resolve(process.argv[1]));
  const recordingSummaryPath = path.join(repo, artifactRoot, "recording-summary.json");
  const recordingSummary = fs.existsSync(recordingSummaryPath) ? readJson(recordingSummaryPath) : [];
  const currentEvidence = new Map(recordingSummary
    .filter(entry => entry.authoringSha256 === authoringSha256 && entry.generatorSha256 === generatorSha256)
    .map(entry => [entry.id, entry]));
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
    const color = themeById[themeId]?.palette?.[0] ?? [1, 1, 1, 1];
    return `rgb(${color.slice(0, 3).map(value => Math.round(value * 255)).join(",")})`;
  };
  const card = item => `<article class="showcase-card" id="${item.id}" data-group="${item.group}" data-theme="${item.theme}" data-search="${item.title} ${item.intent} ${item.id} ${themeLabel[item.theme]}">
  <div class="media">${item.hasVideo ? `<video controls preload="none" ${item.hasPoster ? `poster="${rel(item.poster)}"` : ""} aria-label="${item.title} 录屏"><source src="${rel(item.video)}" type="video/mp4"></video>` : item.hasPoster ? `<img src="${rel(item.poster)}" alt="${item.title}">` : `<div class="missing">缺少录屏与海报<br><code>${item.id}</code></div>`}</div>
  <div class="card-body">
    <div class="card-meta"><span>${item.group === "primitive" ? "实现图元" : "玩家语义"}</span><span class="theme"><i style="--swatch:${themeColor(item.theme)}"></i>${themeLabel[item.theme]}</span></div>
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
      <div><strong>逻辑参数</strong><span>source、target、alpha、size、duration</span></div>
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
    ${section("primitive", "实现图元", "Raylib 实际消费的七种明确绘制入口。", primitiveCards)}
    ${section("semantic", "玩家语义", "每段录屏只证明一种反馈；特效主体来自 Effekseer，Mesh 只保留场景参照。", semanticCards)}
  </main>
  <footer class="footer shell"><span>1280×720 · 16 帧 · 2 秒 · ${recorded}/${cards.length} MP4</span><span><a href="performer-authoring-runtime-pipeline.html">查看生产管线</a> · <a href="../docs/prd/17-performer-authoring-runtime.html">查看 PRD</a></span></footer>
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
  writeText(path.join(repo, "artifacts/raylib-performer-micro-showcases-report.html"), html);
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
  console.log(`Validated ${catalog.length} Raylib micro showcases from ${authoringPath} (${authoring.namespace}).`);
} else {
  writeSharedMod();
  writeEntries();
  updateLauncher();
  writeReport();
  console.log(`Generated ${catalog.length} Raylib micro showcases from ${authoringPath} (${authoring.namespace}).`);
}
