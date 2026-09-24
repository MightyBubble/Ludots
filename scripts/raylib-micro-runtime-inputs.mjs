import crypto from "node:crypto";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { effekseerRuntimeContract } from "./effekseer-runtime-contract.mjs";
import { loadRecordingContract } from "./raylib-micro-recording-evidence.mjs";

const defaultRepo = path.resolve(fileURLToPath(new URL("..", import.meta.url)));
const sharedRelative = "mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod";
const entriesRelative = "mods/showcases/performer_raylib_micro_showcases_entries";
const skippedDirectoryNames = new Set([".git", "bin", "obj", "ref"]);
const runtimeSourceExtensions = new Set([
  ".c",
  ".cmake",
  ".cpp",
  ".cs",
  ".csproj",
  ".fs",
  ".h",
  ".hpp",
  ".json",
  ".mjs",
  ".props",
  ".ps1",
  ".targets",
  ".vs"
]);

export function computeRaylibMicroRuntimeInputSha256(repo = defaultRepo) {
  repo = path.resolve(repo);
  const recording = loadRecordingContract(repo);
  const raylibOutput = path.join(repo, recording.raylibAppOutput);
  const launcherOutput = path.join(repo, recording.launcherCliOutput);
  requireDirectory(raylibOutput, "Raylib Release runtime output");
  requireDirectory(launcherOutput, "Launcher CLI Release runtime output");

  const runtimeManifestPath = path.join(raylibOutput, "runtime-manifest.json");
  requireFile(runtimeManifestPath, "Effekseer runtime manifest");
  const runtimeManifest = readJson(runtimeManifestPath, "Effekseer runtime manifest");
  const runtime = effekseerRuntimeContract.runtimes.find(
    entry => entry.runtimeIdentifier === runtimeManifest.runtimeIdentifier);
  if (runtime === undefined || runtime.library !== runtimeManifest.library) {
    throw new Error("Raylib Release runtime manifest does not match the canonical Effekseer runtime contract.");
  }
  const undeclaredRootLibrary = path.join(raylibOutput, path.basename(runtime.library));
  const declaredLibrary = path.join(raylibOutput, runtime.library);
  if (path.resolve(undeclaredRootLibrary) !== path.resolve(declaredLibrary) && fs.existsSync(undeclaredRootLibrary)) {
    throw new Error(`Raylib Release runtime contains an undeclared root library: ${undeclaredRootLibrary}`);
  }

  for (const relative of [
    recording.raylibAppEntryAssembly,
    "Ludots.Adapter.Raylib.dll",
    "Ludots.Client.Raylib.dll",
    "Ludots.Core.dll",
    "runtime-manifest.json",
    runtime.library
  ]) {
    requireFile(path.join(raylibOutput, relative), `Raylib runtime input ${relative}`);
  }

  const launcherEntryBase = path.basename(recording.launcherCliEntryAssembly, ".dll");
  for (const relative of [
    recording.launcherCliEntryAssembly,
    "Ludots.Launcher.Backend.dll",
    `${launcherEntryBase}.deps.json`,
    `${launcherEntryBase}.runtimeconfig.json`
  ]) {
    requireFile(path.join(launcherOutput, relative), `Launcher runtime input ${relative}`);
  }

  const launcherUserConfig = path.join(repo, recording.launcherUserConfig);
  requireFile(launcherUserConfig, "explicit Launcher user config");
  const userConfig = readJson(launcherUserConfig, "explicit Launcher user config");
  if (userConfig === null || typeof userConfig !== "object" || Array.isArray(userConfig) || Object.keys(userConfig).length !== 0) {
    throw new Error(`Explicit Launcher user config must be an empty JSON object: ${launcherUserConfig}`);
  }

  const declaredFiles = [launcherUserConfig];
  for (const relative of recording.launcherConfigFiles) {
    const file = path.join(repo, relative);
    requireFile(file, `Launcher config input ${relative}`);
    declaredFiles.push(file);
  }
  for (const relative of recording.runtimeAdditionalInputs) {
    const file = path.join(repo, relative);
    requireFile(file, `declared additional runtime input ${relative}`);
    declaredFiles.push(file);
  }

  const nonMsBuildSourceFiles = recording.runtimeNonMsBuildSourceRoots.flatMap(relative => {
    const root = path.join(repo, relative);
    requireDirectory(root, `declared non-MSBuild runtime source root ${relative}`);
    return collectFiles(root, shouldIncludeRuntimeSourceFile);
  });
  const projectRoots = recording.runtimeProjectRoots.flatMap(relative => {
    const root = path.join(repo, relative);
    requireDirectory(root, `declared runtime project root ${relative}`);
    const projects = collectFiles(root, file => file.endsWith(".csproj"));
    if (projects.length === 0) throw new Error(`Declared runtime project root contains no projects: ${root}`);
    return projects;
  });
  const rootProjects = [recording.raylibAppProject, recording.launcherCliProject].map(relative => {
    const project = path.join(repo, relative);
    requireFile(project, `declared runtime project ${relative}`);
    return project;
  });
  const projectGraph = collectMsBuildProjectGraph(repo, [...rootProjects, ...projectRoots]);
  assertProjectTargetPath(
    repo,
    projectGraph,
    recording.raylibAppProject,
    path.join(recording.raylibAppOutput, recording.raylibAppEntryAssembly));
  assertProjectTargetPath(
    repo,
    projectGraph,
    recording.launcherCliProject,
    path.join(recording.launcherCliOutput, recording.launcherCliEntryAssembly));

  const files = [...new Set([
    ...declaredFiles,
    ...nonMsBuildSourceFiles,
    ...projectGraph.files,
    ...collectFiles(path.join(repo, sharedRelative), shouldIncludeSharedFile),
    ...collectFiles(path.join(repo, entriesRelative), shouldIncludeEntryFile),
    ...collectFiles(raylibOutput, file => shouldIncludeDeployedRuntimeFile(file, raylibOutput, runtime.library)),
    ...collectFiles(launcherOutput, file => shouldIncludeDeployedRuntimeFile(file, launcherOutput))
  ])].sort((left, right) => left.localeCompare(right, "en"));
  if (files.length === 0) {
    throw new Error("Raylib micro runtime input set is empty. Generate the showcases before computing evidence.");
  }

  const hash = crypto.createHash("sha256");
  for (const file of files) {
    const relative = path.relative(repo, file).replaceAll("\\", "/");
    const bytes = fs.readFileSync(file);
    hash.update(relative, "utf8");
    hash.update("\0", "utf8");
    hash.update(String(bytes.length), "ascii");
    hash.update("\0", "utf8");
    hash.update(bytes);
    hash.update("\0", "utf8");
  }
  return hash.digest("hex");
}

function collectMsBuildProjectGraph(repo, rootProjects) {
  const repoPrefix = `${path.resolve(repo)}${path.sep}`.toLowerCase();
  const projects = new Map();
  const files = new Set();
  const pending = [...new Set(rootProjects.map(project => path.resolve(project)))];
  while (pending.length > 0) {
    const project = pending.pop();
    const key = project.toLowerCase();
    if (projects.has(key)) continue;
    requireFile(project, "MSBuild runtime project");
    let evaluation = inspectMsBuildProject(repo, project);
    requireMsBuildEvaluation(evaluation, project);
    if (String(evaluation.Properties.TargetPath).length === 0) {
      const frameworks = String(evaluation.Properties.TargetFrameworks)
        .split(";")
        .map(value => value.trim())
        .filter(Boolean);
      if (frameworks.length === 0) throw new Error(`MSBuild project has no TargetPath or TargetFrameworks: ${project}`);
      evaluation = undefined;
      for (const targetFramework of frameworks) {
        const candidate = inspectMsBuildProject(repo, project, targetFramework);
        requireMsBuildEvaluation(candidate, `${project} (${targetFramework})`);
        const candidateTargetPath = String(candidate.Properties.TargetPath);
        if (candidateTargetPath.length > 0 && fs.existsSync(candidateTargetPath)) {
          evaluation = candidate;
          break;
        }
      }
      if (evaluation === undefined) {
        throw new Error(`MSBuild project has no built Release TargetPath for evaluated frameworks ${frameworks.join(", ")}: ${project}`);
      }
    }

    const targetPath = path.resolve(evaluation.Properties.TargetPath);
    requireRepositoryFile(targetPath, repoPrefix, `MSBuild TargetPath for ${project}`);
    projects.set(key, { project, targetPath });
    files.add(project);
    files.add(targetPath);

    for (const imported of String(evaluation.Properties.MSBuildAllProjects).split(";")) {
      if (imported.trim().length === 0) continue;
      addRepositoryFileIfPresent(files, imported, repoPrefix);
    }
    addDirectoryBuildInputs(files, repo, path.dirname(project));

    for (const reference of evaluation.Items.ProjectReference) {
      const referencePath = path.resolve(requireEvaluatedItemFullPath(reference, "ProjectReference", project));
      requireRepositoryFile(referencePath, repoPrefix, `ProjectReference from ${project}`);
      pending.push(referencePath);
    }
    for (const itemType of ["Compile", "Content", "EmbeddedResource", "AdditionalFiles"]) {
      for (const item of evaluation.Items[itemType]) {
        addEvaluatedProjectInput(files, item, repoPrefix);
      }
    }
    for (const item of evaluation.Items.None) {
      if (item.CopyToOutputDirectory === undefined || item.CopyToOutputDirectory === "") continue;
      if (typeof item.CopyToOutputDirectory !== "string") {
        throw new Error(`MSBuild None.CopyToOutputDirectory must be a string for ${project}.`);
      }
      const name = path.basename(requireEvaluatedItemFullPath(item, "None", project)).toLowerCase();
      if (name === "launcher.runtime.json" || name.endsWith(".launch.graph.json")) continue;
      addEvaluatedProjectInput(files, item, repoPrefix);
    }
  }
  return { projects, files: [...files] };
}

function requireMsBuildEvaluation(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`MSBuild project inspection must return an object for ${label}.`);
  }
  const properties = value.Properties;
  if (properties === null || typeof properties !== "object" || Array.isArray(properties)) {
    throw new Error(`MSBuild project inspection is missing Properties for ${label}.`);
  }
  for (const property of ["TargetPath", "TargetFrameworks", "MSBuildAllProjects"]) {
    if (typeof properties[property] !== "string") {
      throw new Error(`MSBuild project inspection property ${property} must be a string for ${label}.`);
    }
  }
  const items = value.Items;
  if (items === null || typeof items !== "object" || Array.isArray(items)) {
    throw new Error(`MSBuild project inspection is missing Items for ${label}.`);
  }
  for (const itemType of ["ProjectReference", "Compile", "Content", "None", "EmbeddedResource", "AdditionalFiles"]) {
    if (!Array.isArray(items[itemType])) {
      throw new Error(`MSBuild project inspection item ${itemType} must be an array for ${label}.`);
    }
  }
}

function inspectMsBuildProject(repo, project, targetFramework) {
  const projectArgument = path.relative(repo, project);
  if (projectArgument.length === 0 || path.isAbsolute(projectArgument) || projectArgument.split(path.sep).includes("..")) {
    throw new Error(`MSBuild project inspection must stay inside the repository: ${project}`);
  }
  const args = [
    "msbuild",
    projectArgument,
    "-nologo",
    "-property:Configuration=Release",
    "-getProperty:TargetPath,TargetFramework,TargetFrameworks,MSBuildAllProjects",
    "-getItem:ProjectReference,Compile,Content,None,EmbeddedResource,AdditionalFiles"
  ];
  if (targetFramework !== undefined) args.splice(4, 0, `-property:TargetFramework=${targetFramework}`);
  const result = spawnSync("dotnet", args, {
    cwd: repo,
    encoding: "utf8",
    windowsHide: true,
    maxBuffer: 64 * 1024 * 1024
  });
  if (result.error) throw new Error(`Could not inspect MSBuild project ${project}: ${result.error.message}`, { cause: result.error });
  if (result.status !== 0) {
    const diagnostics = [result.stderr.trim(), result.stdout.trim()].filter(Boolean).join("\n");
    throw new Error(
      `MSBuild project inspection failed for ${project} with exit code ${result.status}` +
      `${diagnostics.length === 0 ? "." : `:\n${diagnostics}`}`);
  }
  try {
    return JSON.parse(result.stdout.replace(/^\uFEFF/, ""));
  } catch (error) {
    throw new Error(`MSBuild returned invalid project inspection JSON for ${project}: ${error.message}`, { cause: error });
  }
}

function addEvaluatedProjectInput(files, item, repoPrefix) {
  const value = path.resolve(requireEvaluatedItemFullPath(item, "project input", "evaluated project"));
  if (!value.toLowerCase().startsWith(repoPrefix)) return;
  if (containsSkippedDirectory(value)) return;
  requireFile(value, "evaluated MSBuild project input");
  files.add(value);
}

function requireEvaluatedItemFullPath(item, itemType, owner) {
  if (item === null || typeof item !== "object" || Array.isArray(item) ||
      typeof item.FullPath !== "string" || item.FullPath.length === 0) {
    throw new Error(`MSBuild ${itemType} must declare a non-empty FullPath for ${owner}.`);
  }
  return item.FullPath;
}

function addRepositoryFileIfPresent(files, value, repoPrefix) {
  const fullPath = path.resolve(value);
  if (!fullPath.toLowerCase().startsWith(repoPrefix) || !fs.existsSync(fullPath)) return;
  requireFile(fullPath, "imported MSBuild project input");
  files.add(fullPath);
}

function addDirectoryBuildInputs(files, repo, startDirectory) {
  let directory = path.resolve(startDirectory);
  const root = path.resolve(repo);
  while (directory.toLowerCase().startsWith(root.toLowerCase())) {
    for (const name of ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json"] ) {
      const candidate = path.join(directory, name);
      if (fs.existsSync(candidate)) {
        requireFile(candidate, "MSBuild ancestor input");
        files.add(candidate);
      }
    }
    if (directory.toLowerCase() === root.toLowerCase()) break;
    directory = path.dirname(directory);
  }
}

function requireRepositoryFile(value, repoPrefix, label) {
  if (!value.toLowerCase().startsWith(repoPrefix)) throw new Error(`${label} escapes the repository: ${value}`);
  requireFile(value, label);
}

function containsSkippedDirectory(value) {
  return value.split(/[\\/]/).some(part => skippedDirectoryNames.has(part.toLowerCase()));
}

function assertProjectTargetPath(repo, graph, projectRelative, targetRelative) {
  const project = path.resolve(repo, projectRelative);
  const inspection = graph.projects.get(project.toLowerCase());
  if (inspection === undefined) throw new Error(`Runtime project is missing from the evaluated MSBuild graph: ${projectRelative}`);
  const expected = path.resolve(repo, targetRelative);
  if (inspection.targetPath.toLowerCase() !== expected.toLowerCase()) {
    throw new Error(`Runtime output contract drift for ${projectRelative}: MSBuild TargetPath is ${inspection.targetPath}, contract expects ${expected}`);
  }
}

function collectFiles(root, include) {
  requireDirectory(root, "Raylib micro runtime input root");
  const result = [];
  const pending = [root];
  while (pending.length > 0) {
    const directory = pending.pop();
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const fullPath = path.join(directory, entry.name);
      if (entry.isDirectory() && !skippedDirectoryNames.has(entry.name.toLowerCase())) {
        pending.push(fullPath);
      } else if (entry.isFile() && include(fullPath)) {
        result.push(fullPath);
      }
    }
  }
  return result;
}

function requireDirectory(value, label) {
  if (!fs.existsSync(value) || !fs.statSync(value).isDirectory()) {
    throw new Error(`${label} does not exist: ${value}`);
  }
}

function requireFile(value, label) {
  if (!fs.existsSync(value) || !fs.statSync(value).isFile()) {
    throw new Error(`${label} does not exist: ${value}`);
  }
}

function shouldIncludeSharedFile(file) {
  const normalized = file.replaceAll("\\", "/");
  return normalized.endsWith("/mod.json") || normalized.includes("/assets/") || shouldIncludeRuntimeSourceFile(file);
}

function shouldIncludeEntryFile(file) {
  const normalized = file.replaceAll("\\", "/");
  return normalized.endsWith("/mod.json") || normalized.includes("/assets/") || shouldIncludeRuntimeSourceFile(file);
}

function shouldIncludeRuntimeSourceFile(file) {
  const name = path.basename(file);
  return name === "CMakeLists.txt" || runtimeSourceExtensions.has(path.extname(name).toLowerCase());
}

function shouldIncludeDeployedRuntimeFile(file, outputRoot, declaredNativeLibrary) {
  const relative = path.relative(outputRoot, file).replaceAll("\\", "/");
  if (declaredNativeLibrary !== undefined && relative === declaredNativeLibrary) return true;
  const name = path.basename(file).toLowerCase();
  return name.endsWith(".dll") ||
    name.endsWith(".exe") ||
    name.endsWith(".fs") ||
    name.endsWith(".vs") ||
    name.endsWith(".deps.json") ||
    name.endsWith(".runtimeconfig.json") ||
    name === "runtime-manifest.json";
}

function readJson(file, label) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
  } catch (error) {
    throw new Error(`${label} is not valid JSON (${file}): ${error.message}`, { cause: error });
  }
}

if (path.resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length !== 0 && (args.length !== 2 || args[0] !== "--repo")) {
    throw new Error("Usage: node scripts/raylib-micro-runtime-inputs.mjs [--repo <path>]");
  }
  const repo = args.length === 2 ? args[1] : defaultRepo;
  process.stdout.write(`${computeRaylibMicroRuntimeInputSha256(repo)}\n`);
}
