import crypto from "node:crypto";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const defaultRepo = path.resolve(fileURLToPath(new URL("..", import.meta.url)));
const sharedRelative = "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod";
const authoringContractRelative = `${sharedRelative}/assets/Presentation/Authoring/raylib-micro-showcases.contract.json`;
const showcaseManifestRelative = `${sharedRelative}/assets/Presentation/showcases.manifest.json`;
const sha256Pattern = /^[a-f0-9]{64}$/;

export function loadRecordingContract(repo = defaultRepo) {
  repo = path.resolve(repo);
  const contract = readJson(path.join(repo, authoringContractRelative));
  requireObject(contract, "authoring contract");
  const recording = contract.recording;
  requireObject(recording, "authoring contract recording");
  requireExactKeys(recording, [
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
  ], "authoring contract recording");
  for (const field of [
    "artifactRoot",
    "reportPath",
    "recordingSummaryPath",
    "contactSheetPath",
    "workRoot",
    "logRoot",
    "raylibAppProject",
    "raylibAppOutput",
    "launcherCliProject",
    "launcherCliOutput",
    "launcherUserConfig"
  ]) {
    requireRepositoryRelativePath(recording[field], `recording.${field}`);
  }
  for (const field of ["recordingSummaryPath", "contactSheetPath"]) {
    if (path.posix.dirname(recording[field]) !== recording.artifactRoot) {
      fail(`recording.${field} must be a direct child of recording.artifactRoot`);
    }
  }
  for (const field of ["raylibAppEntryAssembly", "launcherCliEntryAssembly"]) {
    requireFileName(recording[field], `recording.${field}`);
  }
  if (typeof recording.launcherUserConfigEnvironmentVariable !== "string" ||
      !/^[A-Z][A-Z0-9_]+$/.test(recording.launcherUserConfigEnvironmentVariable)) {
    fail("recording.launcherUserConfigEnvironmentVariable must be an uppercase environment variable name");
  }
  requireObject(recording.captureEnvironmentVariables, "recording.captureEnvironmentVariables");
  requireExactKeys(recording.captureEnvironmentVariables, [
    "screenshotPath",
    "screenshotFrames",
    "screenshotFrame",
    "minimumRuntimeMilliseconds",
    "autoExitFrame",
    "autoOrbitDegreesPerSecond",
    "lightweightDiagnosticHud",
    "fixedFrameDeltaSeconds"
  ], "recording.captureEnvironmentVariables");
  const environmentNames = new Set([recording.launcherUserConfigEnvironmentVariable]);
  for (const [key, value] of Object.entries(recording.captureEnvironmentVariables)) {
    if (typeof value !== "string" || !/^[A-Z][A-Z0-9_]+$/.test(value)) {
      fail(`recording.captureEnvironmentVariables.${key} must be an uppercase environment variable name`);
    }
    if (environmentNames.has(value)) fail(`recording environment variable duplicates ${JSON.stringify(value)}`);
    environmentNames.add(value);
  }
  for (const field of [
    "launcherConfigFiles",
    "runtimeProjectRoots",
    "runtimeNonMsBuildSourceRoots",
    "runtimeAdditionalInputs"
  ]) {
    if (!Array.isArray(recording[field]) || recording[field].length === 0) {
      fail(`recording.${field} must be a non-empty array`);
    }
    const seen = new Set();
    for (let index = 0; index < recording[field].length; index++) {
      const value = recording[field][index];
      requireRepositoryRelativePath(value, `recording.${field}[${index}]`);
      if (seen.has(value)) fail(`recording.${field} duplicates ${JSON.stringify(value)}`);
      seen.add(value);
    }
  }
  for (const field of [
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
  ]) {
    requirePositiveInteger(recording[field], `recording.${field}`);
  }
  const videoFrameCount = recording.framesPerSecond * recording.durationSeconds;
  if (recording.captureFrameCount % videoFrameCount !== 0) {
    fail("recording.captureFrameCount must be evenly divisible by framesPerSecond * durationSeconds");
  }
  if (recording.posterFrame > recording.captureFrameCount) {
    fail("recording.posterFrame must not exceed captureFrameCount");
  }
  return Object.freeze({
    ...recording,
    launcherConfigFiles: Object.freeze([...recording.launcherConfigFiles]),
    runtimeProjectRoots: Object.freeze([...recording.runtimeProjectRoots]),
    runtimeNonMsBuildSourceRoots: Object.freeze([...recording.runtimeNonMsBuildSourceRoots]),
    runtimeAdditionalInputs: Object.freeze([...recording.runtimeAdditionalInputs]),
    videoFrameCount
  });
}

export function verifyRecordingEvidence({
  repo = defaultRepo,
  runtimeInputSha256,
  requireComplete = false,
  replacingIds = new Set()
}) {
  repo = path.resolve(repo);
  requireSha256(runtimeInputSha256, "current runtime input SHA-256");
  if (!(replacingIds instanceof Set)) fail("replacingIds must be a Set");
  const recording = loadRecordingContract(repo);
  const manifest = readManifest(repo);
  const manifestById = new Map(manifest.map(item => [item.id, item]));
  verifyArtifactLayout(repo, recording, manifestById, requireComplete);

  const summaryPath = path.join(repo, recording.recordingSummaryPath);
  const summary = fs.existsSync(summaryPath) ? readJson(summaryPath) : [];
  if (!Array.isArray(summary)) fail("recording summary must be an array");
  const seen = new Set();
  const currentById = new Map();
  for (let index = 0; index < summary.length; index++) {
    const entry = summary[index];
    const label = `recording-summary[${index}]`;
    verifyEntryShape(entry, label);
    if (seen.has(entry.id)) fail(`${label}.id duplicates ${JSON.stringify(entry.id)}`);
    seen.add(entry.id);
    const expected = manifestById.get(entry.id);
    if (expected === undefined) fail(`${label}.id is not declared by showcases.manifest.json: ${JSON.stringify(entry.id)}`);
    if (entry.binding !== expected.binding) fail(`${label}.binding must be ${JSON.stringify(expected.binding)}`);

    if (replacingIds.has(entry.id)) continue;

    const videoPath = resolveEvidencePath(repo, entry.video.path, expected.recording, `${label}.video.path`);
    const posterPath = resolveEvidencePath(repo, entry.poster.path, expected.poster, `${label}.poster.path`);
    verifyFileHash(videoPath, entry.video.sha256, `${label}.video.sha256`);
    verifyFileHash(posterPath, entry.poster.sha256, `${label}.poster.sha256`);

    const video = probeVideo(videoPath);
    const poster = probePoster(posterPath);
    verifyVideoMetadata(entry.video, video, recording, label);
    verifyPosterMetadata(entry.poster, poster, recording, label);
    if (entry.runtimeInputSha256 === runtimeInputSha256) currentById.set(entry.id, entry);
  }

  if (requireComplete) {
    const missing = manifest.filter(item => !currentById.has(item.id)).map(item => item.id);
    if (missing.length > 0) {
      fail(`current recording evidence is incomplete: ${missing.join(", ")}`);
    }
  }
  return { recording, manifest, summary, currentById, summaryPath };
}

export function updateRecordingEvidence({ repo = defaultRepo, runtimeInputSha256, ids }) {
  repo = path.resolve(repo);
  requireSha256(runtimeInputSha256, "current runtime input SHA-256");
  if (!Array.isArray(ids) || ids.length === 0) fail("update ids must be a non-empty array");
  const manifest = readManifest(repo);
  const manifestById = new Map(manifest.map(item => [item.id, item]));
  const updateIds = new Set();
  for (const id of ids) {
    if (typeof id !== "string" || !manifestById.has(id)) fail(`unknown update id ${JSON.stringify(id)}`);
    if (updateIds.has(id)) fail(`duplicate update id ${JSON.stringify(id)}`);
    updateIds.add(id);
  }
  const state = verifyRecordingEvidence({
    repo,
    runtimeInputSha256,
    requireComplete: false,
    replacingIds: updateIds
  });

  const byId = new Map(state.summary.map(entry => [entry.id, entry]));
  for (const id of updateIds) {
    const item = manifestById.get(id);
    const videoPath = resolveEvidencePath(repo, item.recording, item.recording, `${id} video`);
    const posterPath = resolveEvidencePath(repo, item.poster, item.poster, `${id} poster`);
    const video = probeVideo(videoPath);
    const poster = probePoster(posterPath);
    verifyVideoAgainstContract(video, state.recording, id);
    verifyPosterAgainstContract(poster, state.recording, id);
    byId.set(id, {
      id,
      binding: item.binding,
      runtimeInputSha256,
      video: {
        path: item.recording,
        sha256: hashFile(videoPath),
        width: video.width,
        height: video.height,
        frameCount: video.frameCount,
        durationSeconds: video.durationSeconds
      },
      poster: {
        path: item.poster,
        sha256: hashFile(posterPath),
        width: poster.width,
        height: poster.height
      }
    });
  }

  const ordered = state.manifest.flatMap(item => byId.has(item.id) ? [byId.get(item.id)] : []);
  fs.writeFileSync(state.summaryPath, `${JSON.stringify(ordered, null, 2)}\n`, "utf8");
  return verifyRecordingEvidence({ repo, runtimeInputSha256, requireComplete: false });
}

function readManifest(repo) {
  const value = readJson(path.join(repo, showcaseManifestRelative));
  if (!Array.isArray(value) || value.length === 0) fail("showcases.manifest.json must be a non-empty array");
  const ids = new Set();
  return value.map((item, index) => {
    const label = `showcases.manifest[${index}]`;
    requireObject(item, label);
    for (const field of ["id", "binding", "artifactDir", "recording", "poster"]) {
      if (typeof item[field] !== "string" || item[field].length === 0 || item[field] !== item[field].trim()) {
        fail(`${label}.${field} must be a canonical non-empty string`);
      }
    }
    if (ids.has(item.id)) fail(`${label}.id duplicates ${JSON.stringify(item.id)}`);
    ids.add(item.id);
    requireRepositoryRelativePath(item.artifactDir, `${label}.artifactDir`);
    requireRepositoryRelativePath(item.recording, `${label}.recording`);
    requireRepositoryRelativePath(item.poster, `${label}.poster`);
    return item;
  });
}

function verifyEntryShape(entry, label) {
  requireObject(entry, label);
  requireExactKeys(entry, ["id", "binding", "runtimeInputSha256", "video", "poster"], label);
  for (const field of ["id", "binding"]) {
    if (typeof entry[field] !== "string" || entry[field].length === 0 || entry[field] !== entry[field].trim()) {
      fail(`${label}.${field} must be a canonical non-empty string`);
    }
  }
  requireSha256(entry.runtimeInputSha256, `${label}.runtimeInputSha256`);
  requireObject(entry.video, `${label}.video`);
  requireExactKeys(entry.video, ["path", "sha256", "width", "height", "frameCount", "durationSeconds"], `${label}.video`);
  requireObject(entry.poster, `${label}.poster`);
  requireExactKeys(entry.poster, ["path", "sha256", "width", "height"], `${label}.poster`);
  requireRepositoryRelativePath(entry.video.path, `${label}.video.path`);
  requireRepositoryRelativePath(entry.poster.path, `${label}.poster.path`);
  requireSha256(entry.video.sha256, `${label}.video.sha256`);
  requireSha256(entry.poster.sha256, `${label}.poster.sha256`);
  for (const field of ["width", "height", "frameCount"]) requirePositiveInteger(entry.video[field], `${label}.video.${field}`);
  requirePositiveNumber(entry.video.durationSeconds, `${label}.video.durationSeconds`);
  for (const field of ["width", "height"]) requirePositiveInteger(entry.poster[field], `${label}.poster.${field}`);
}

function verifyVideoMetadata(entry, actual, recording, label) {
  for (const field of ["width", "height", "frameCount"]) {
    if (entry[field] !== actual[field]) fail(`${label}.video.${field} does not match the MP4: expected ${actual[field]}, received ${entry[field]}`);
  }
  if (Math.abs(entry.durationSeconds - actual.durationSeconds) > 1e-6) {
    fail(`${label}.video.durationSeconds does not match the MP4: expected ${actual.durationSeconds}, received ${entry.durationSeconds}`);
  }
  verifyVideoAgainstContract(actual, recording, label);
}

function verifyPosterMetadata(entry, actual, recording, label) {
  for (const field of ["width", "height"]) {
    if (entry[field] !== actual[field]) fail(`${label}.poster.${field} does not match the PNG: expected ${actual[field]}, received ${entry[field]}`);
  }
  verifyPosterAgainstContract(actual, recording, label);
}

function verifyVideoAgainstContract(video, recording, label) {
  if (video.width !== recording.width || video.height !== recording.height) {
    fail(`${label} video dimensions are ${video.width}x${video.height}; expected ${recording.width}x${recording.height}`);
  }
  if (video.frameCount !== recording.videoFrameCount) {
    fail(`${label} video has ${video.frameCount} frames; expected ${recording.videoFrameCount}`);
  }
  if (Math.abs(video.durationSeconds - recording.durationSeconds) > (1 / recording.framesPerSecond)) {
    fail(`${label} video duration is ${video.durationSeconds}; expected ${recording.durationSeconds}`);
  }
}

function verifyPosterAgainstContract(poster, recording, label) {
  if (poster.width !== recording.width || poster.height !== recording.height) {
    fail(`${label} poster dimensions are ${poster.width}x${poster.height}; expected ${recording.width}x${recording.height}`);
  }
}

function probeVideo(file) {
  const stream = probe(file, ["-count_frames", "-show_entries", "stream=width,height,nb_read_frames,duration"]);
  const frameCount = Number(stream.nb_read_frames);
  const durationSeconds = Number(stream.duration);
  requirePositiveInteger(frameCount, `${file} frame count`);
  requirePositiveNumber(durationSeconds, `${file} duration`);
  return { width: Number(stream.width), height: Number(stream.height), frameCount, durationSeconds };
}

function probePoster(file) {
  const stream = probe(file, ["-show_entries", "stream=width,height"]);
  return { width: Number(stream.width), height: Number(stream.height) };
}

function probe(file, extraArgs) {
  const result = spawnSync("ffprobe", [
    "-v", "error",
    ...extraArgs,
    "-select_streams", "v:0",
    "-of", "json",
    file
  ], { encoding: "utf8", windowsHide: true });
  if (result.error) fail(`ffprobe could not inspect ${file}: ${result.error.message}`);
  if (result.status !== 0) fail(`ffprobe failed for ${file}: ${result.stderr.trim()}`);
  let value;
  try {
    value = JSON.parse(result.stdout);
  } catch (error) {
    fail(`ffprobe returned invalid JSON for ${file}: ${error.message}`);
  }
  const stream = value?.streams?.[0];
  requireObject(stream, `ffprobe stream for ${file}`);
  requirePositiveInteger(Number(stream.width), `${file} width`);
  requirePositiveInteger(Number(stream.height), `${file} height`);
  return stream;
}

function verifyArtifactLayout(repo, recording, manifestById, requireComplete) {
  const root = path.join(repo, recording.artifactRoot);
  if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) return;
  const expectedRootFiles = new Set([
    path.basename(recording.recordingSummaryPath),
    path.basename(recording.contactSheetPath)
  ]);
  const extras = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!manifestById.has(entry.name)) extras.push(`${entry.name}/`);
    } else if (entry.isFile()) {
      if (!expectedRootFiles.has(entry.name)) extras.push(entry.name);
    } else {
      extras.push(entry.name);
    }
  }
  if (extras.length > 0) fail(`artifact root contains contract-external entries: ${extras.sort().join(", ")}`);

  for (const item of manifestById.values()) {
    const directory = path.join(repo, item.artifactDir);
    if (!fs.existsSync(directory)) continue;
    requireDirectory(directory, `${item.id} artifact directory`);
    const expectedFiles = new Set([path.basename(item.recording), path.basename(item.poster)]);
    const entryExtras = fs.readdirSync(directory, { withFileTypes: true })
      .filter(entry => !entry.isFile() || !expectedFiles.has(entry.name))
      .map(entry => entry.isDirectory() ? `${entry.name}/` : entry.name)
      .sort();
    if (entryExtras.length > 0) fail(`${item.id} artifact directory contains contract-external entries: ${entryExtras.join(", ")}`);
  }

  if (requireComplete) {
    for (const relative of [recording.recordingSummaryPath, recording.contactSheetPath]) {
      requireFile(path.join(repo, relative), `required recording artifact ${relative}`);
    }
  }
}

function requireDirectory(value, label) {
  if (!fs.existsSync(value) || !fs.statSync(value).isDirectory()) fail(`${label} does not exist: ${value}`);
}

function requireFile(value, label) {
  if (!fs.existsSync(value) || !fs.statSync(value).isFile()) fail(`${label} does not exist: ${value}`);
}

function resolveEvidencePath(repo, relative, expected, label) {
  if (relative !== expected) fail(`${label} must be ${JSON.stringify(expected)}, received ${JSON.stringify(relative)}`);
  const full = path.resolve(repo, relative);
  const prefix = `${path.resolve(repo)}${path.sep}`;
  if (!full.toLowerCase().startsWith(prefix.toLowerCase())) fail(`${label} escapes the repository root`);
  if (!fs.existsSync(full) || !fs.statSync(full).isFile()) fail(`${label} does not exist: ${full}`);
  return full;
}

function verifyFileHash(file, expected, label) {
  const actual = hashFile(file);
  if (actual !== expected) fail(`${label} mismatch for ${file}: expected ${expected}, actual ${actual}`);
}

function hashFile(file) {
  return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

function requireRepositoryRelativePath(value, label) {
  if (typeof value !== "string" || value.length === 0 || value !== value.trim() || value.includes("\\") || path.posix.isAbsolute(value) || value.split("/").includes("..")) {
    fail(`${label} must be a portable repository-relative path without parent traversal`);
  }
}

function requireFileName(value, label) {
  if (typeof value !== "string" || value.length === 0 || value !== value.trim() || path.basename(value) !== value) {
    fail(`${label} must be a canonical file name`);
  }
}

function requireExactKeys(value, expected, label) {
  const actual = Object.keys(value).sort();
  const required = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(required)) {
    fail(`${label} fields must be exactly ${required.join(", ")}; received ${actual.join(", ")}`);
  }
}

function requireObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) fail(`${label} must be an object`);
}

function requireSha256(value, label) {
  if (typeof value !== "string" || !sha256Pattern.test(value)) fail(`${label} must be a lowercase SHA-256 digest`);
}

function requirePositiveInteger(value, label) {
  if (!Number.isSafeInteger(value) || value <= 0) fail(`${label} must be a positive integer`);
}

function requirePositiveNumber(value, label) {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) fail(`${label} must be a positive finite number`);
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
  } catch (error) {
    fail(`cannot read JSON ${file}: ${error.message}`);
  }
}

function fail(message) {
  throw new Error(`Raylib micro recording evidence: ${message}`);
}

if (path.resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  let repo = defaultRepo;
  let runtimeInputSha256;
  let updateIds;
  let validate = false;
  let requireComplete = false;
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    if (arg === "--repo") repo = args[++index];
    else if (arg === "--runtime-input-sha256") runtimeInputSha256 = args[++index];
    else if (arg === "--update") updateIds = args[++index]?.split(",").filter(Boolean);
    else if (arg === "--validate") validate = true;
    else if (arg === "--require-complete") requireComplete = true;
    else throw new Error(`Unknown argument ${JSON.stringify(arg)}`);
  }
  if (!runtimeInputSha256 || (updateIds === undefined) === !validate) {
    throw new Error("Usage: node scripts/raylib-micro-recording-evidence.mjs --repo <path> --runtime-input-sha256 <sha256> <--update <id,...>|--validate> [--require-complete]");
  }
  const result = updateIds === undefined
    ? verifyRecordingEvidence({ repo, runtimeInputSha256, requireComplete })
    : updateRecordingEvidence({ repo, runtimeInputSha256, ids: updateIds });
  process.stdout.write(`${result.currentById.size}/${result.manifest.length} current recording evidence entries\n`);
}
