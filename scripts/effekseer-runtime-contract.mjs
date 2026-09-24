import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const contractPath = fileURLToPath(new URL(
  "../src/Libraries/Effekseer/runtime-contract.json",
  import.meta.url));
const upstreamPath = fileURLToPath(new URL(
  "../src/Libraries/Effekseer/Effekseer.upstream.json",
  import.meta.url));
const msbuildPropsPath = fileURLToPath(new URL(
  "../src/Libraries/Effekseer/runtime-contract.props",
  import.meta.url));

export const effekseerRuntimeContract = readContract(contractPath);
export const effekseerUpstream = readUpstream(upstreamPath);
export const emitterAssetKinds = Object.freeze(
  Object.keys(effekseerRuntimeContract.emitterNodeTypes));

function readContract(file) {
  const value = readJson(file);
  assertExactKeys(value, [
    "schemaVersion",
    "bridgeAbiVersion",
    "runtimeFormatVersion",
    "dynamicInputSlotCount",
    "runtimes",
    "emitterNodeTypes"
  ], "Effekseer runtime contract");
  assertPositiveInteger(value.schemaVersion, "schemaVersion");
  assertPositiveInteger(value.bridgeAbiVersion, "bridgeAbiVersion");
  assertPositiveInteger(value.runtimeFormatVersion, "runtimeFormatVersion");
  assertPositiveInteger(value.dynamicInputSlotCount, "dynamicInputSlotCount");
  if (!Array.isArray(value.runtimes) || value.runtimes.length === 0) {
    fail("runtimes must be a non-empty array");
  }
  const runtimeIds = new Set();
  for (const runtime of value.runtimes) {
    assertExactKeys(runtime, ["runtimeIdentifier", "library"], "runtime entry");
    assertCanonicalString(runtime.runtimeIdentifier, "runtimeIdentifier");
    assertCanonicalString(runtime.library, "library");
    if (runtime.library.includes("\\") || path.posix.isAbsolute(runtime.library) || runtime.library.split("/").includes("..")) {
      fail(`runtime library ${JSON.stringify(runtime.library)} must be a portable relative path without parent traversal`);
    }
    if (runtimeIds.has(runtime.runtimeIdentifier)) {
      fail(`runtimeIdentifier ${JSON.stringify(runtime.runtimeIdentifier)} is duplicated`);
    }
    runtimeIds.add(runtime.runtimeIdentifier);
  }
  if (value.emitterNodeTypes === null || typeof value.emitterNodeTypes !== "object" || Array.isArray(value.emitterNodeTypes)) {
    fail("emitterNodeTypes must be an object");
  }
  const nodeTypes = new Set();
  for (const [assetKind, nodeType] of Object.entries(value.emitterNodeTypes)) {
    assertCanonicalString(assetKind, "emitter AssetKind");
    assertPositiveInteger(nodeType, `${assetKind} node type`);
    if (nodeTypes.has(nodeType)) fail(`node type ${nodeType} is duplicated`);
    nodeTypes.add(nodeType);
  }
  if (nodeTypes.size === 0) fail("emitterNodeTypes must not be empty");
  return deepFreeze(value);
}

function readUpstream(file) {
  const value = readJson(file);
  assertExactKeys(value, [
    "name",
    "version",
    "tag",
    "commit",
    "sourceUrl",
    "sourceSha256",
    "vendoredTreeSha256",
    "vendoredFileCount",
    "license"
  ], "Effekseer upstream provenance");
  assertCanonicalString(value.name, "Effekseer upstream name");
  assertCanonicalString(value.version, "Effekseer upstream version");
  assertCanonicalString(value.tag, "Effekseer upstream tag");
  if (typeof value.commit !== "string" || !/^[a-f0-9]{40}$/.test(value.commit)) {
    fail("Effekseer upstream commit must be a lowercase 40-character Git digest");
  }
  let sourceUrl;
  try {
    sourceUrl = new URL(value.sourceUrl);
  } catch {
    fail("Effekseer upstream sourceUrl must be an absolute URL");
  }
  if (sourceUrl.protocol !== "https:" || sourceUrl.hostname !== "github.com" || !sourceUrl.pathname.endsWith(".zip")) {
    fail("Effekseer upstream sourceUrl must identify the pinned GitHub release ZIP over HTTPS");
  }
  assertSha256(value.sourceSha256, "Effekseer upstream sourceSha256");
  assertSha256(value.vendoredTreeSha256, "Effekseer upstream vendoredTreeSha256");
  assertPositiveInteger(value.vendoredFileCount, "Effekseer upstream vendoredFileCount");
  assertCanonicalString(value.license, "Effekseer upstream license");
  return deepFreeze(value);
}

function assertSha256(value, label) {
  if (typeof value !== "string" || !/^[a-f0-9]{64}$/.test(value)) {
    fail(`${label} must be a lowercase SHA-256 digest`);
  }
}

export function computeVendoredSourceFingerprint(root) {
  root = path.resolve(root);
  if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
    fail(`vendored source root does not exist: ${root}`);
  }
  const files = [];
  const pending = [root];
  while (pending.length > 0) {
    const directory = pending.pop();
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const fullPath = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) fail(`vendored source must not contain symbolic links: ${fullPath}`);
      if (entry.isDirectory()) pending.push(fullPath);
      else if (entry.isFile()) files.push(fullPath);
      else fail(`vendored source contains an unsupported filesystem entry: ${fullPath}`);
    }
  }
  files.sort((left, right) => left.localeCompare(right, "en"));
  const hash = crypto.createHash("sha256");
  for (const file of files) {
    const relative = path.relative(root, file).replaceAll("\\", "/");
    const bytes = fs.readFileSync(file);
    hash.update(relative, "utf8");
    hash.update("\0", "utf8");
    hash.update(String(bytes.length), "utf8");
    hash.update("\0", "utf8");
    hash.update(bytes);
  }
  return Object.freeze({ sha256: hash.digest("hex"), fileCount: files.length });
}

export function verifyVendoredSource(root) {
  const actual = computeVendoredSourceFingerprint(root);
  if (actual.sha256 !== effekseerUpstream.vendoredTreeSha256 ||
      actual.fileCount !== effekseerUpstream.vendoredFileCount) {
    fail(
      `vendored source fingerprint mismatch at ${path.resolve(root)}; ` +
      `expected ${effekseerUpstream.vendoredTreeSha256}/${effekseerUpstream.vendoredFileCount}, ` +
      `received ${actual.sha256}/${actual.fileCount}`);
  }
  return actual;
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch (error) {
    fail(`cannot read ${file}: ${error.message}`);
  }
}

function assertExactKeys(value, expected, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    fail(`${label} must be an object`);
  }
  const actual = Object.keys(value).sort();
  const required = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(required)) {
    fail(`${label} fields must be exactly ${required.join(", ")}; received ${actual.join(", ")}`);
  }
}

function assertPositiveInteger(value, label) {
  if (!Number.isSafeInteger(value) || value <= 0) fail(`${label} must be a positive integer`);
}

function assertCanonicalString(value, label) {
  if (typeof value !== "string" || value.length === 0 || value !== value.trim()) {
    fail(`${label} must be a canonical non-empty string`);
  }
}

function deepFreeze(value) {
  if (value && typeof value === "object" && !Object.isFrozen(value)) {
    Object.freeze(value);
    for (const child of Object.values(value)) deepFreeze(child);
  }
  return value;
}

export function renderEffekseerRuntimeMsbuildProps(contract = effekseerRuntimeContract) {
  const supported = contract.runtimes.map(runtime => runtime.runtimeIdentifier).join(";");
  const groups = contract.runtimes.map(runtime => `  <PropertyGroup Condition="'$(EffekseerNativeRid)' == '${xml(runtime.runtimeIdentifier)}'">
    <EffekseerNativeContractLibrary>${xml(runtime.library.replaceAll("/", "\\"))}</EffekseerNativeContractLibrary>
  </PropertyGroup>`).join("\n");
  return `<!-- Generated from runtime-contract.json by scripts/effekseer-runtime-contract.mjs. -->
<Project>
  <PropertyGroup>
    <EffekseerSupportedRuntimeIdentifiers>${xml(supported)}</EffekseerSupportedRuntimeIdentifiers>
  </PropertyGroup>
${groups}
</Project>
`;
}

function xml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&apos;");
}

function fail(message) {
  throw new Error(`Effekseer runtime contract: ${message}`);
}

if (path.resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const expected = renderEffekseerRuntimeMsbuildProps();
  if (args.length === 1 && args[0] === "--write-msbuild-props") {
    fs.writeFileSync(msbuildPropsPath, expected, "utf8");
    process.stdout.write(`${msbuildPropsPath}\n`);
  } else if (args.length === 1 && args[0] === "--check-msbuild-props") {
    if (!fs.existsSync(msbuildPropsPath) || fs.readFileSync(msbuildPropsPath, "utf8") !== expected) {
      fail(`generated MSBuild properties are stale: ${msbuildPropsPath}`);
    }
    process.stdout.write(`${msbuildPropsPath}\n`);
  } else if (args.length === 2 && args[0] === "--print-vendored-source") {
    process.stdout.write(`${JSON.stringify(computeVendoredSourceFingerprint(args[1]))}\n`);
  } else if (args.length === 2 && args[0] === "--verify-vendored-source") {
    const result = verifyVendoredSource(args[1]);
    process.stdout.write(`${result.sha256} ${result.fileCount}\n`);
  } else {
    throw new Error(
      "Usage: node scripts/effekseer-runtime-contract.mjs " +
      "<--write-msbuild-props|--check-msbuild-props|--print-vendored-source <root>|--verify-vendored-source <root>>");
  }
}
