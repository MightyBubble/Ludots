import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { requiredEmitterProfile } from "./raylib-effekseer-profile-contracts.mjs";

const repo = process.cwd();
const defaultAuthoringPath = path.join(
  repo,
  "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Authoring/raylib-micro-showcases.authoring.json");
const cli = parseCli(process.argv.slice(2));
const authoringPath = path.resolve(repo, cli.authoringPath ?? defaultAuthoringPath);
const assetRoot = path.resolve(path.dirname(authoringPath), "../Effekseer");
const config = readJson(authoringPath);

if (!Array.isArray(config.emitters) || config.emitters.length === 0) {
  fail("authoring.emitters", "must be a non-empty array");
}

const projects = config.emitters.map((emitter, index) => buildProject(emitter, index));
validateSignatureProjects(projects);

if (cli.check) {
  for (const project of projects) {
    const file = path.join(assetRoot, project.file);
    if (!fs.existsSync(file)) fail(`Effekseer/${project.file}`, "generated project source is missing");
    if (fs.readFileSync(file, "utf8") !== project.xml) {
      fail(`Effekseer/${project.file}`, "does not match its declarative project parameters");
    }
  }
} else if (!cli.validateOnly) {
  fs.mkdirSync(assetRoot, { recursive: true });
  for (const project of projects) {
    fs.writeFileSync(path.join(assetRoot, project.file), project.xml, "utf8");
  }
}

console.log(`${cli.check ? "Checked" : cli.validateOnly ? "Validated" : "Generated"} ${projects.length} Effekseer project source(s) from ${authoringPath}.`);

function parseCli(args) {
  const result = { authoringPath: undefined, validateOnly: false, check: false };
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
    } else if (arg === "--check") {
      if (result.check) fail("CLI", "--check may only be specified once");
      result.check = true;
    } else {
      fail("CLI", `unknown argument ${JSON.stringify(arg)}`);
    }
  }
  if (result.validateOnly && result.check) fail("CLI", "--validate-only and --check are mutually exclusive");
  return result;
}

function buildProject(emitter, index) {
  const at = `authoring.emitters[${index}]`;
  assertObject(emitter, at);
  if (typeof emitter.sourceFile !== "string" || path.basename(emitter.sourceFile) !== emitter.sourceFile || !emitter.sourceFile.endsWith(".efkefc")) {
    fail(`${at}.sourceFile`, "must be a basename ending in .efkefc");
  }
  if (typeof emitter.id !== "string" || emitter.id.length === 0) fail(`${at}.id`, "must be a non-empty string");
  if (emitter.usage !== "primitive" && emitter.usage !== "signature") {
    fail(`${at}.usage`, "must be primitive or signature");
  }
  if (typeof emitter.ownerShowcase !== "string" || emitter.ownerShowcase.length === 0) {
    fail(`${at}.ownerShowcase`, "must be a non-empty string");
  }
  assertObject(emitter.project, `${at}.project`);
  const requiredProfile = requiredEmitterProfile(emitter.assetKind, emitter.usage);
  if (requiredProfile === undefined) {
    fail(`${at}.assetKind`, `unsupported concrete emitter kind ${JSON.stringify(emitter.assetKind)}`);
  }
  if (emitter.project.profile !== requiredProfile) {
    fail(`${at}.project.profile`, `${emitter.usage} ${emitter.assetKind} requires ${JSON.stringify(requiredProfile)}`);
  }

  const builders = {
    sprite_burst: buildSpriteBurst,
    sprite_icon: buildSpriteIcon,
    ribbon_trail: buildRibbonTrail,
    track_beam: buildTrackBeam,
    ring_pulse: buildRingPulse,
    model_shard: buildModelShard
  };
  const node = builders[emitter.project.profile](emitter.project, `${at}.project`);
  const sourceName = path.basename(emitter.sourceFile, ".efkefc");
  const canonicalNode = node.replaceAll("__NODE_NAME__", "semantic_signature");
  return {
    file: `${sourceName}.efkproj`,
    xml: wrapProject(node.replaceAll("__NODE_NAME__", xmlText(sourceName))),
    id: emitter.id,
    usage: emitter.usage,
    ownerShowcase: emitter.ownerShowcase,
    fingerprint: crypto.createHash("sha256").update(canonicalNode).digest("hex")
  };
}

function validateSignatureProjects(projects) {
  const signatures = projects.filter(project => project.usage === "signature");
  const semanticShowcases = new Set((config.showcases ?? []).filter(showcase => showcase.group === "semantic").map(showcase => showcase.id));
  if (signatures.length !== semanticShowcases.size) {
    fail("authoring.emitters", `expected ${semanticShowcases.size} signature emitters, received ${signatures.length}`);
  }
  const owners = new Set();
  const fingerprints = new Set();
  for (const signature of signatures) {
    if (!semanticShowcases.has(signature.ownerShowcase)) {
      fail(`authoring.emitters.${signature.id}.ownerShowcase`, `unknown semantic showcase ${JSON.stringify(signature.ownerShowcase)}`);
    }
    if (owners.has(signature.ownerShowcase)) {
      fail(`authoring.emitters.${signature.id}.ownerShowcase`, `duplicate signature owner ${JSON.stringify(signature.ownerShowcase)}`);
    }
    if (fingerprints.has(signature.fingerprint)) {
      fail(`authoring.emitters.${signature.id}.project`, "duplicates another signature's normalized visual parameters");
    }
    owners.add(signature.ownerShowcase);
    fingerprints.add(signature.fingerprint);
  }
  for (const showcaseId of semanticShowcases) {
    if (!owners.has(showcaseId)) fail("authoring.emitters", `missing signature emitter for ${JSON.stringify(showcaseId)}`);
  }
}

function buildSpriteBurst(project, at) {
  assertKeys(project, ["profile", "tempo", "density", "size", "alpha", "spread", "rise", "spin", "growth", "texture"], at);
  const p = common(project, at);
  const spread = factor(project.spread, `${at}.spread`);
  const rise = factor(project.rise, `${at}.rise`);
  const spin = factor(project.spin, `${at}.spin`);
  const growth = factor(project.growth, `${at}.growth`);
  const texture = relativeResource(project.texture, `${at}.texture`, ".png");
  const life = triplet(36 / p.tempo, 42 / p.tempo, 48 / p.tempo);
  const generation = triplet(3 / p.density, 4 / p.density, 5 / p.density);
  const locationXz = 0.2 * spread;
  const velocityXz = 0.015 * spread;
  const scale = 0.24 * p.size;
  return `      <Node>
        ${commonValues(life, generation, -12 / p.tempo)}
        <LocationValues><Type>1</Type><PVA>
          <Location><X>${minMax(-locationXz, locationXz)}</X><Y>${minMax(-0.1 * spread, 0.1 * spread)}</Y><Z>${minMax(-locationXz, locationXz)}</Z></Location>
          <Velocity><X>${minMax(-velocityXz, velocityXz)}</X><Y>${centerMinMax(0.025 * rise, 0.01 * rise, 0.04 * rise)}</Y><Z>${minMax(-velocityXz, velocityXz)}</Z></Velocity>
        </PVA></LocationValues>
        <RotationValues><Type>1</Type><PVA><Rotation><Z>${minMax(-180, 180)}</Z></Rotation><Velocity><Z>${centerMinMax(2 * spin, spin, 3 * spin)}</Z></Velocity></PVA></RotationValues>
        <ScalingValues><Type>1</Type><PVA><Scale><X>${centerMinMax(scale, scale * 0.75, scale * 1.25)}</X><Y>${centerMinMax(scale, scale * 0.75, scale * 1.25)}</Y><Z>${centerMinMax(scale, scale * 0.75, scale * 1.25)}</Z></Scale><Velocity><X>${centerMinMax(0.003 * growth, 0.002 * growth, 0.004 * growth)}</X><Y>${centerMinMax(0.003 * growth, 0.002 * growth, 0.004 * growth)}</Y><Z>${centerMinMax(0.003 * growth, 0.002 * growth, 0.004 * growth)}</Z></Velocity></PVA></ScalingValues>
        ${rendererCommon(texture, 2, 2 / p.tempo, 18 / p.tempo)}
        <DrawingValues><Type>2</Type><ColorAll><Type>0</Type><Fixed>${color(p.alpha)}</Fixed></ColorAll><Sprite><Billboard>0</Billboard></Sprite></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildSpriteIcon(project, at) {
  assertKeys(project, ["profile", "size", "texture"], at);
  const size = factor(project.size, `${at}.size`);
  const texture = relativeResource(project.texture, `${at}.texture`, ".png");
  return `      <Node>
        <CommonValues><MaxGeneration><Value>1</Value><Infinite>False</Infinite></MaxGeneration><RemoveWhenLifeIsExtinct>False</RemoveWhenLifeIsExtinct><Life>${triplet(1000, 1000, 1000)}</Life><GenerationTime>${triplet(1, 1, 1)}</GenerationTime><GenerationTimeOffset>${triplet(0, 0, 0)}</GenerationTimeOffset></CommonValues>
        <ScalingValues><Fixed><Scale><X>${num(size)}</X><Y>${num(size)}</Y><Z>${num(size)}</Z></Scale></Fixed></ScalingValues>
        ${rendererCommon(texture, 1, 0, 0)}
        <DrawingValues><Type>2</Type><ColorAll><Type>0</Type><Fixed>${color(1)}</Fixed></ColorAll><Sprite><Billboard>0</Billboard></Sprite></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildRibbonTrail(project, at) {
  assertKeys(project, ["profile", "tempo", "density", "size", "alpha", "length", "width"], at);
  const p = common(project, at);
  const length = factor(project.length, `${at}.length`);
  const width = factor(project.width, `${at}.width`);
  return `      <Node>
        ${commonValues(triplet(36 * length / p.tempo, 36 * length / p.tempo, 36 * length / p.tempo), triplet(1 / p.density, 1 / p.density, 1 / p.density), -20 / p.tempo)}
        <LocationValues><Type>1</Type><PVA><Velocity><X>${centerMinMax(0.02 * p.tempo, 0.02 * p.tempo, 0.02 * p.tempo)}</X></Velocity></PVA></LocationValues>
        ${rendererCommon("textures/laser_beam_profile.png", 2, 2 / p.tempo, 12 / p.tempo)}
        <DrawingValues><Type>3</Type><Ribbon><ViewpointDependent>True</ViewpointDependent><ColorAll_Fixed>${color(p.alpha)}</ColorAll_Fixed><Position>1</Position><Position_Fixed_L>${num(-0.06 * width * p.size)}</Position_Fixed_L><Position_Fixed_R>${num(0.06 * width * p.size)}</Position_Fixed_R><SplineDivision>1</SplineDivision></Ribbon></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildTrackBeam(project, at) {
  assertKeys(project, ["profile", "tempo", "density", "size", "alpha", "length", "width"], at);
  const p = common(project, at);
  const length = factor(project.length, `${at}.length`);
  const width = factor(project.width, `${at}.width`);
  const edgeAlpha = Math.max(0.1, p.alpha * 0.38);
  const middleAlpha = Math.max(0.1, p.alpha * 0.78);
  return `      <Node>
        ${commonValues(triplet(31 * length / p.tempo, 31 * length / p.tempo, 31 * length / p.tempo), triplet(1 / p.density, 1 / p.density, 1 / p.density), -30 / p.tempo)}
        <LocationValues><Type>1</Type><PVA><Velocity><X>${centerMinMax(0.25 * p.tempo, 0.25 * p.tempo, 0.25 * p.tempo)}</X></Velocity></PVA></LocationValues>
        ${rendererCommon("textures/laser_beam_profile.png", 2, 2 / p.tempo, 4 / p.tempo)}
        <DrawingValues><Type>6</Type>
          <TrailColorLeft><Type>0</Type><Fixed>${color(edgeAlpha)}</Fixed></TrailColorLeft><TrailColorLeftMiddle><Type>0</Type><Fixed>${color(middleAlpha)}</Fixed></TrailColorLeftMiddle>
          <TrailColorCenter><Type>0</Type><Fixed>${color(p.alpha)}</Fixed></TrailColorCenter><TrailColorCenterMiddle><Type>0</Type><Fixed>${color(p.alpha)}</Fixed></TrailColorCenterMiddle>
          <TrailColorRight><Type>0</Type><Fixed>${color(edgeAlpha)}</Fixed></TrailColorRight><TrailColorRightMiddle><Type>0</Type><Fixed>${color(middleAlpha)}</Fixed></TrailColorRightMiddle>
          <Track><TrackSizeFor_Fixed>${num(0.16 * width * p.size)}</TrackSizeFor_Fixed><TrackSizeMiddle_Fixed>${num(0.3 * width * p.size)}</TrackSizeMiddle_Fixed><TrackSizeBack_Fixed>${num(0.16 * width * p.size)}</TrackSizeBack_Fixed><SplineDivision>1</SplineDivision></Track>
        </DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildRingPulse(project, at) {
  assertKeys(project, ["profile", "tempo", "density", "size", "alpha", "width", "growth"], at);
  const p = common(project, at);
  const width = factor(project.width, `${at}.width`);
  const growth = factor(project.growth, `${at}.growth`);
  const inner = Math.max(0.15, 1 - 0.28 * width);
  return `      <Node>
        ${commonValues(triplet(60 / p.tempo, 60 / p.tempo, 60 / p.tempo), triplet(45 / p.density, 45 / p.density, 45 / p.density), -15 / p.tempo)}
        <ScalingValues><Type>1</Type><PVA><Scale><X>${centerMinMax(0.3 * p.size, 0.3 * p.size, 0.3 * p.size)}</X><Y>${centerMinMax(0.3 * p.size, 0.3 * p.size, 0.3 * p.size)}</Y><Z>${centerMinMax(0.3 * p.size, 0.3 * p.size, 0.3 * p.size)}</Z></Scale><Velocity><X>${centerMinMax(0.01 * growth, 0.01 * growth, 0.01 * growth)}</X><Y>${centerMinMax(0.01 * growth, 0.01 * growth, 0.01 * growth)}</Y><Z>${centerMinMax(0.01 * growth, 0.01 * growth, 0.01 * growth)}</Z></Velocity></PVA></ScalingValues>
        ${rendererCommon(null, 1, 0, 18 / p.tempo)}
        <DrawingValues><Type>4</Type><Ring><Billboard>0</Billboard><VertexCount>48</VertexCount><Outer_Fixed><Location><X>1</X></Location></Outer_Fixed><Inner_Fixed><Location><X>${num(inner)}</X></Location></Inner_Fixed><CenterRatio_Fixed>0.5</CenterRatio_Fixed><OuterColor_Fixed>${color(p.alpha * 0.2)}</OuterColor_Fixed><CenterColor_Fixed>${color(p.alpha)}</CenterColor_Fixed><InnerColor_Fixed>${color(p.alpha * 0.2)}</InnerColor_Fixed></Ring></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildModelShard(project, at) {
  assertKeys(project, ["profile", "tempo", "density", "size", "alpha", "spread", "rise", "spin"], at);
  const p = common(project, at);
  const spread = factor(project.spread, `${at}.spread`);
  const rise = factor(project.rise, `${at}.rise`);
  const spin = factor(project.spin, `${at}.spin`);
  return `      <Node>
        ${commonValues(triplet(60 / p.tempo, 72 / p.tempo, 84 / p.tempo), triplet(4 / p.density, 6 / p.density, 8 / p.density), -30 / p.tempo)}
        <LocationValues><Type>1</Type><PVA><Location><X>${minMax(-0.45 * spread, 0.45 * spread)}</X><Y>${minMax(0, 0.18 * spread)}</Y><Z>${minMax(-0.45 * spread, 0.45 * spread)}</Z></Location><Velocity><Y>${centerMinMax(0.025 * rise, 0.01 * rise, 0.04 * rise)}</Y></Velocity></PVA></LocationValues>
        <RotationValues><Type>1</Type><PVA><Rotation><Y>${minMax(-180, 180)}</Y></Rotation><Velocity><Y>${centerMinMax(4 * spin, 2 * spin, 6 * spin)}</Y><Z>${centerMinMax(2 * spin, -spin, 4 * spin)}</Z></Velocity></PVA></RotationValues>
        <ScalingValues><Fixed><Scale><X>${num(0.28 * p.size)}</X><Y>${num(0.28 * p.size)}</Y><Z>${num(0.28 * p.size)}</Z></Scale></Fixed></ScalingValues>
        ${rendererCommon(null, 2, 3 / p.tempo, 24 / p.tempo)}
        <DrawingValues><Type>5</Type><Model><Model>models/energy_shard.efkmodel</Model><Color>0</Color><Color_Fixed>${color(p.alpha)}</Color_Fixed></Model></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function common(project, at) {
  return {
    tempo: factor(project.tempo, `${at}.tempo`),
    density: factor(project.density, `${at}.density`),
    size: factor(project.size, `${at}.size`),
    alpha: unit(project.alpha, `${at}.alpha`)
  };
}

function commonValues(life, generation, offset) {
  return `<CommonValues><MaxGeneration><Infinite>True</Infinite></MaxGeneration><Life>${life}</Life><GenerationTime>${generation}</GenerationTime><GenerationTimeOffset>${centerMinMax(offset, offset, offset)}</GenerationTimeOffset></CommonValues>`;
}

function rendererCommon(texture, blend, fadeIn, fadeOut) {
  return `<RendererCommonValues>${texture ? `<ColorTexture>${xmlText(texture)}</ColorTexture>` : ""}<AlphaBlend>${blend}</AlphaBlend><ZWrite>False</ZWrite><ZTest>True</ZTest>${fadeIn > 0 ? `<FadeInType>1</FadeInType><FadeIn><Frame>${num(fadeIn)}</Frame></FadeIn>` : ""}<FadeOutType>1</FadeOutType><FadeOut><Frame>${num(fadeOut)}</Frame></FadeOut></RendererCommonValues>`;
}

function wrapProject(node) {
  return `<?xml version="1.0" encoding="utf-8"?>
<EffekseerProject>
  <Root>
    <Name>Root</Name>
    <Children>
${node}
    </Children>
  </Root>
  <ToolVersion>1.80.6</ToolVersion>
  <Version>3</Version>
  <StartFrame>0</StartFrame>
  <EndFrame>180</EndFrame>
  <IsLoop>True</IsLoop>
</EffekseerProject>
`;
}

function triplet(min, center, max) {
  return centerMinMax(center, min, max);
}

function centerMinMax(center, min, max) {
  return `<Center>${num(center)}</Center><Max>${num(max)}</Max><Min>${num(min)}</Min>`;
}

function minMax(min, max) {
  return `<Max>${num(max)}</Max><Min>${num(min)}</Min>`;
}

function color(alpha) {
  return `<R>255</R><G>255</G><B>255</B><A>${Math.round(255 * Math.max(0, Math.min(1, alpha)))}</A>`;
}

function factor(value, at) {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0.25 || value > 4) {
    fail(at, "must be a finite number between 0.25 and 4");
  }
  return value;
}

function unit(value, at) {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0.1 || value > 1) {
    fail(at, "must be a finite number between 0.1 and 1");
  }
  return value;
}

function relativeResource(value, at, extension) {
  if (typeof value !== "string" || value.length === 0 || path.isAbsolute(value) || value.includes("..") || path.extname(value).toLowerCase() !== extension) {
    fail(at, `must be a relative ${extension} resource path without parent traversal`);
  }
  return value.replaceAll("\\", "/");
}

function assertObject(value, at) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) fail(at, "must be an object");
}

function assertKeys(value, keys, at) {
  const expected = new Set(keys);
  for (const key of Object.keys(value)) if (!expected.has(key)) fail(`${at}.${key}`, "unknown property");
  for (const key of keys) if (!Object.hasOwn(value, key)) fail(`${at}.${key}`, "required property is missing");
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch (error) {
    throw new Error(`Failed to parse JSON ${file}: ${error.message}`, { cause: error });
  }
}

function num(value) {
  return Number(value.toFixed(6)).toString();
}

function xmlText(value) {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&apos;");
}

function fail(at, message) {
  throw new Error(`${at}: ${message}`);
}
