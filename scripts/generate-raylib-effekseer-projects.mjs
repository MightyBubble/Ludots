import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import {
  emitterNodeType,
  emitterProfile,
  raylibEffekseerProfileContract,
  requiredEmitterProfile,
  validateEmitterProjectParameters
} from "./raylib-effekseer-profile-contracts.mjs";
import { effekseerUpstream } from "./effekseer-runtime-contract.mjs";

const repo = process.cwd();
const defaultAuthoringPath = path.join(
  repo,
  "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/Authoring/raylib-micro-showcases.authoring.json");
const cli = parseCli(process.argv.slice(2));
const authoringPath = path.resolve(repo, cli.authoringPath ?? defaultAuthoringPath);
const assetRoot = path.resolve(path.dirname(authoringPath), "../Effekseer");
const config = readJson(authoringPath);

assertObject(config, "authoring");
if (!Array.isArray(config.emitters) || config.emitters.length === 0) {
  fail("authoring.emitters", "must be a non-empty array");
}
if (!Array.isArray(config.showcases) || config.showcases.length === 0) {
  fail("authoring.showcases", "must be a non-empty array");
}

const profileBuilders = Object.freeze({
  spriteBurst: buildSpriteBurst,
  spriteIcon: buildSpriteIcon,
  ribbonTrail: buildRibbonTrail,
  trackBeam: buildTrackBeam,
  ringPulse: buildRingPulse,
  modelShard: buildModelShard
});

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

  const profile = emitterProfile(requiredProfile);
  const parameters = validateEmitterProjectParameters(emitter.project, `${at}.project`);
  const builder = profileBuilders[profile.generator];
  if (builder === undefined) fail(`${at}.project.profile`, `has no generator for ${JSON.stringify(profile.generator)}`);
  const node = builder(parameters, profile.visual, emitterNodeType(emitter.assetKind));
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
  const semanticShowcases = new Set(config.showcases.filter(showcase => showcase.group === "semantic").map(showcase => showcase.id));
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

function buildSpriteBurst(project, visual, nodeType) {
  const p = common(project);
  const life = tupleTriplet(visual.lifeFrames, 1 / p.tempo);
  const generation = tupleTriplet(visual.generationIntervalFrames, 1 / p.density);
  const locationXz = visual.locationHorizontalRadius * project.spread;
  const velocityXz = visual.horizontalVelocity * project.spread;
  const scale = visual.baseScale * p.size;
  return `      <Node>
        ${commonValues(life, generation, visual.generationOffsetFrames / p.tempo)}
        <LocationValues><Type>1</Type><PVA>
          <Location><X>${minMax(-locationXz, locationXz)}</X><Y>${minMax(-visual.locationVerticalRadius * project.spread, visual.locationVerticalRadius * project.spread)}</Y><Z>${minMax(-locationXz, locationXz)}</Z></Location>
          <Velocity><X>${minMax(-velocityXz, velocityXz)}</X><Y>${scaledCenterMinMax(visual.verticalVelocity, project.rise)}</Y><Z>${minMax(-velocityXz, velocityXz)}</Z></Velocity>
        </PVA></LocationValues>
        <RotationValues><Type>1</Type><PVA><Rotation><Z>${minMax(visual.rotationDegrees[0], visual.rotationDegrees[1])}</Z></Rotation><Velocity><Z>${scaledCenterMinMax(visual.spinVelocity, project.spin)}</Z></Velocity></PVA></RotationValues>
        <ScalingValues><Type>1</Type><PVA><Scale><X>${centerMinMax(scale, scale * visual.scaleRange[0], scale * visual.scaleRange[1])}</X><Y>${centerMinMax(scale, scale * visual.scaleRange[0], scale * visual.scaleRange[1])}</Y><Z>${centerMinMax(scale, scale * visual.scaleRange[0], scale * visual.scaleRange[1])}</Z></Scale><Velocity><X>${scaledCenterMinMax(visual.scaleVelocity, project.growth)}</X><Y>${scaledCenterMinMax(visual.scaleVelocity, project.growth)}</Y><Z>${scaledCenterMinMax(visual.scaleVelocity, project.growth)}</Z></Velocity></PVA></ScalingValues>
        ${rendererCommon(project.texture, visual.renderer, p.tempo)}
        <DrawingValues><Type>${nodeType}</Type><ColorAll><Type>0</Type><Fixed>${color(p.alpha)}</Fixed></ColorAll><Sprite><Billboard>${visual.billboard}</Billboard></Sprite></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildSpriteIcon(project, visual, nodeType) {
  return `      <Node>
        <CommonValues><MaxGeneration><Value>${visual.maxGeneration}</Value><Infinite>False</Infinite></MaxGeneration><RemoveWhenLifeIsExtinct>${booleanXml(visual.removeWhenLifeIsExtinct)}</RemoveWhenLifeIsExtinct><Life>${tupleTriplet(visual.lifeFrames)}</Life><GenerationTime>${tupleTriplet(visual.generationIntervalFrames)}</GenerationTime><GenerationTimeOffset>${tupleTriplet(visual.generationOffsetFrames)}</GenerationTimeOffset></CommonValues>
        <ScalingValues><Fixed><Scale><X>${num(project.size)}</X><Y>${num(project.size)}</Y><Z>${num(project.size)}</Z></Scale></Fixed></ScalingValues>
        ${rendererCommon(project.texture, visual.renderer, 1)}
        <DrawingValues><Type>${nodeType}</Type><ColorAll><Type>0</Type><Fixed>${color(visual.alpha)}</Fixed></ColorAll><Sprite><Billboard>${visual.billboard}</Billboard></Sprite></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildRibbonTrail(project, visual, nodeType) {
  const p = common(project);
  return `      <Node>
        ${commonValues(repeatedTriplet(visual.lifeFrames * project.length / p.tempo), repeatedTriplet(visual.generationIntervalFrames / p.density), visual.generationOffsetFrames / p.tempo)}
        <LocationValues><Type>1</Type><PVA><Velocity><X>${repeatedCenterMinMax(visual.forwardVelocity * p.tempo)}</X></Velocity></PVA></LocationValues>
        ${rendererCommon(visual.renderer.texture, visual.renderer, p.tempo)}
        <DrawingValues><Type>${nodeType}</Type><Ribbon><ViewpointDependent>${booleanXml(visual.viewpointDependent)}</ViewpointDependent><ColorAll_Fixed>${color(p.alpha)}</ColorAll_Fixed><Position>${visual.positionMode}</Position><Position_Fixed_L>${num(-visual.halfWidth * project.width * p.size)}</Position_Fixed_L><Position_Fixed_R>${num(visual.halfWidth * project.width * p.size)}</Position_Fixed_R><SplineDivision>${visual.splineDivision}</SplineDivision></Ribbon></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildTrackBeam(project, visual, nodeType) {
  const p = common(project);
  const edgeAlpha = Math.max(visual.minimumLayerAlpha, p.alpha * visual.edgeAlphaMultiplier);
  const middleAlpha = Math.max(visual.minimumLayerAlpha, p.alpha * visual.middleAlphaMultiplier);
  return `      <Node>
        ${commonValues(repeatedTriplet(visual.lifeFrames * project.length / p.tempo), repeatedTriplet(visual.generationIntervalFrames / p.density), visual.generationOffsetFrames / p.tempo)}
        <LocationValues><Type>1</Type><PVA><Velocity><X>${repeatedCenterMinMax(visual.forwardVelocity * p.tempo)}</X></Velocity></PVA></LocationValues>
        ${rendererCommon(visual.renderer.texture, visual.renderer, p.tempo)}
        <DrawingValues><Type>${nodeType}</Type>
          <TrailColorLeft><Type>0</Type><Fixed>${color(edgeAlpha)}</Fixed></TrailColorLeft><TrailColorLeftMiddle><Type>0</Type><Fixed>${color(middleAlpha)}</Fixed></TrailColorLeftMiddle>
          <TrailColorCenter><Type>0</Type><Fixed>${color(p.alpha)}</Fixed></TrailColorCenter><TrailColorCenterMiddle><Type>0</Type><Fixed>${color(p.alpha)}</Fixed></TrailColorCenterMiddle>
          <TrailColorRight><Type>0</Type><Fixed>${color(edgeAlpha)}</Fixed></TrailColorRight><TrailColorRightMiddle><Type>0</Type><Fixed>${color(middleAlpha)}</Fixed></TrailColorRightMiddle>
          <Track><TrackSizeFor_Fixed>${num(visual.widths[0] * project.width * p.size)}</TrackSizeFor_Fixed><TrackSizeMiddle_Fixed>${num(visual.widths[1] * project.width * p.size)}</TrackSizeMiddle_Fixed><TrackSizeBack_Fixed>${num(visual.widths[2] * project.width * p.size)}</TrackSizeBack_Fixed><SplineDivision>${visual.splineDivision}</SplineDivision></Track>
        </DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildRingPulse(project, visual, nodeType) {
  const p = common(project);
  const inner = Math.max(
    visual.minimumInnerRadius,
    visual.outerRadius - visual.innerWidthMultiplier * project.width);
  return `      <Node>
        ${commonValues(repeatedTriplet(visual.lifeFrames / p.tempo), repeatedTriplet(visual.generationIntervalFrames / p.density), visual.generationOffsetFrames / p.tempo)}
        <ScalingValues><Type>1</Type><PVA><Scale><X>${repeatedCenterMinMax(visual.initialScale * p.size)}</X><Y>${repeatedCenterMinMax(visual.initialScale * p.size)}</Y><Z>${repeatedCenterMinMax(visual.initialScale * p.size)}</Z></Scale><Velocity><X>${repeatedCenterMinMax(visual.scaleVelocity * project.growth)}</X><Y>${repeatedCenterMinMax(visual.scaleVelocity * project.growth)}</Y><Z>${repeatedCenterMinMax(visual.scaleVelocity * project.growth)}</Z></Velocity></PVA></ScalingValues>
        ${rendererCommon(visual.renderer.texture, visual.renderer, p.tempo)}
        <DrawingValues><Type>${nodeType}</Type><Ring><Billboard>${visual.billboard}</Billboard><VertexCount>${visual.vertexCount}</VertexCount><Outer_Fixed><Location><X>${num(visual.outerRadius)}</X></Location></Outer_Fixed><Inner_Fixed><Location><X>${num(inner)}</X></Location></Inner_Fixed><CenterRatio_Fixed>${num(visual.centerRatio)}</CenterRatio_Fixed><OuterColor_Fixed>${color(p.alpha * visual.edgeAlphaMultiplier)}</OuterColor_Fixed><CenterColor_Fixed>${color(p.alpha)}</CenterColor_Fixed><InnerColor_Fixed>${color(p.alpha * visual.edgeAlphaMultiplier)}</InnerColor_Fixed></Ring></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function buildModelShard(project, visual, nodeType) {
  const p = common(project);
  return `      <Node>
        ${commonValues(tupleTriplet(visual.lifeFrames, 1 / p.tempo), tupleTriplet(visual.generationIntervalFrames, 1 / p.density), visual.generationOffsetFrames / p.tempo)}
        <LocationValues><Type>1</Type><PVA><Location><X>${minMax(-visual.locationHorizontalRadius * project.spread, visual.locationHorizontalRadius * project.spread)}</X><Y>${minMax(0, visual.locationVerticalRadius * project.spread)}</Y><Z>${minMax(-visual.locationHorizontalRadius * project.spread, visual.locationHorizontalRadius * project.spread)}</Z></Location><Velocity><Y>${scaledCenterMinMax(visual.verticalVelocity, project.rise)}</Y></Velocity></PVA></LocationValues>
        <RotationValues><Type>1</Type><PVA><Rotation><Y>${minMax(visual.rotationDegrees[0], visual.rotationDegrees[1])}</Y></Rotation><Velocity><Y>${scaledCenterMinMax(visual.rotationVelocityY, project.spin)}</Y><Z>${scaledCenterMinMax(visual.rotationVelocityZ, project.spin)}</Z></Velocity></PVA></RotationValues>
        <ScalingValues><Fixed><Scale><X>${num(visual.baseScale * p.size)}</X><Y>${num(visual.baseScale * p.size)}</Y><Z>${num(visual.baseScale * p.size)}</Z></Scale></Fixed></ScalingValues>
        ${rendererCommon(visual.renderer.texture, visual.renderer, p.tempo)}
        <DrawingValues><Type>${nodeType}</Type><Model><Model>${xmlText(visual.model)}</Model><Color>${visual.colorMode}</Color><Color_Fixed>${color(p.alpha)}</Color_Fixed></Model></DrawingValues>
        <Name>__NODE_NAME__</Name><Children />
      </Node>`;
}

function common(project) {
  return {
    tempo: project.tempo,
    density: project.density,
    size: project.size,
    alpha: project.alpha
  };
}

function commonValues(life, generation, offset) {
  return `<CommonValues><MaxGeneration><Infinite>True</Infinite></MaxGeneration><Life>${life}</Life><GenerationTime>${generation}</GenerationTime><GenerationTimeOffset>${centerMinMax(offset, offset, offset)}</GenerationTimeOffset></CommonValues>`;
}

function rendererCommon(texture, renderer, tempo) {
  const defaults = raylibEffekseerProfileContract.rendererDefaults;
  const fadeIn = renderer.fadeInFrames / tempo;
  const fadeOut = renderer.fadeOutFrames / tempo;
  return `<RendererCommonValues>${texture !== null ? `<ColorTexture>${xmlText(texture)}</ColorTexture>` : ""}<AlphaBlend>${renderer.alphaBlend}</AlphaBlend><ZWrite>${booleanXml(defaults.zWrite)}</ZWrite><ZTest>${booleanXml(defaults.zTest)}</ZTest>${fadeIn > 0 ? `<FadeInType>1</FadeInType><FadeIn><Frame>${num(fadeIn)}</Frame></FadeIn>` : ""}<FadeOutType>1</FadeOutType><FadeOut><Frame>${num(fadeOut)}</Frame></FadeOut></RendererCommonValues>`;
}

function wrapProject(node) {
  const project = raylibEffekseerProfileContract.project;
  return `<?xml version="1.0" encoding="utf-8"?>
<EffekseerProject>
  <Root>
    <Name>Root</Name>
    <Children>
${node}
    </Children>
  </Root>
  <ToolVersion>${xmlText(effekseerUpstream.version)}</ToolVersion>
  <Version>${project.version}</Version>
  <StartFrame>${project.startFrame}</StartFrame>
  <EndFrame>${project.endFrame}</EndFrame>
  <IsLoop>${booleanXml(project.isLoop)}</IsLoop>
</EffekseerProject>
`;
}

function tupleTriplet(values, scale = 1) {
  return triplet(values[0] * scale, values[1] * scale, values[2] * scale);
}

function scaledCenterMinMax(values, scale) {
  return centerMinMax(values[1] * scale, values[0] * scale, values[2] * scale);
}

function repeatedTriplet(value) {
  return triplet(value, value, value);
}

function repeatedCenterMinMax(value) {
  return centerMinMax(value, value, value);
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
  const contract = raylibEffekseerProfileContract.color;
  const alphaChannel = Math.round(contract.channelMax * Math.max(0, Math.min(1, alpha)));
  return `<R>${contract.red}</R><G>${contract.green}</G><B>${contract.blue}</B><A>${alphaChannel}</A>`;
}

function assertObject(value, at) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) fail(at, "must be an object");
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

function booleanXml(value) {
  return value ? "True" : "False";
}

function fail(at, message) {
  throw new Error(`${at}: ${message}`);
}
