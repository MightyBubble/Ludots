// Ludots.ModCompiler：无 .NET SDK 的 mod 编译器（epic #1190 族E 根治项）。
// 用内嵌 Roslyn 进程内编译 BuildableSource mod，玩家/作者机不需要安装 dotnet SDK。
// 引用解析顺序：
//   1) external/ref/net9.0/*.dll   —— 框架引用程序集（随仓库分发）
//   2) assets/ModSdk/ref/*.dll     —— Ludots/Arch SDK 引用（launcher 导出）
//   3) 各依赖 mod 的 bin/<tfm>/*.dll
//   4) 显式 -r <dll> 参数
// 用法：Ludots.ModCompiler <modDir> [-r <ref.dll> ...] [-o <out.dll>]
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Ludots.ModCompiler <modDir> [-r <ref.dll> ...] [-o <out.dll>]");
    return 2;
}

var modDir = Path.GetFullPath(args[0]);
var manifestPath = Path.Combine(modDir, "mod.json");
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"mod.json not found: {manifestPath}");
    return 2;
}

string? explicitOut = null;
var extraRefs = new List<string>();
for (var i = 1; i < args.Length; i++)
{
    if (args[i] == "-r" && i + 1 < args.Length) { extraRefs.Add(args[++i]); }
    else if (args[i] == "-o" && i + 1 < args.Length) { explicitOut = args[++i]; }
}

using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
var manifest = manifestDoc.RootElement;
var modName = manifest.GetProperty("name").GetString()!;
var mainEntry = manifest.TryGetProperty("main", out var mainProp) ? mainProp.GetString() : null;
var tfm = "net9.0";

// 1) 源码（剔除 bin/obj）
var sources = Directory.EnumerateFiles(modDir, "*.cs", SearchOption.AllDirectories)
    .Where(p => !IsUnderBuildDir(p))
    .ToList();
if (sources.Count == 0)
{
    Console.Error.WriteLine($"no source files under {modDir}");
    return 2;
}

// 2) 引用集
var repoRoot = FindRepoRoot(modDir);
var referencePaths = new List<string>();
if (repoRoot == null)
{
    Console.Error.WriteLine("cannot locate repo root (assets/ + external/ markers) from " + modDir);
    return 2;
}

var frameworkRefDir = Path.Combine(repoRoot, "external", "ref", tfm);
if (!Directory.Exists(frameworkRefDir))
{
    Console.Error.WriteLine($"framework ref assemblies missing: {frameworkRefDir}");
    return 2;
}

referencePaths.AddRange(Directory.EnumerateFiles(frameworkRefDir, "*.dll"));

var modSdkRefDir = Path.Combine(repoRoot, "assets", "ModSdk", "ref");
if (Directory.Exists(modSdkRefDir))
{
    referencePaths.AddRange(Directory.EnumerateFiles(modSdkRefDir, "*.dll"));
}
else
{
    Console.Error.WriteLine($"warning: ModSdk ref dir missing (run a launcher build once to export): {modSdkRefDir}");
}

// 依赖 mod 的输出 bin（依赖闭包由调用方保证已编译）。
// 依赖按 mod.json 的 name 引用，目录深度不定（mods/ 根与 mods/showcases/**/ 嵌套并存），
// 因此用全仓 mod.json 名字→目录映射解析，而不是假定兄弟目录。
if (manifest.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object && repoRoot != null)
{
    var modsRoot = Path.Combine(repoRoot, "mods");
    var modDirByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var candidateManifest in Directory.EnumerateFiles(modsRoot, "mod.json", SearchOption.AllDirectories))
    {
        var normalized = candidateManifest.Replace('\\', '/');
        if (normalized.Contains("/fixtures/") || normalized.Contains("/bin/") || normalized.Contains("/obj/"))
        {
            continue;
        }

        using var depDoc = JsonDocument.Parse(File.ReadAllText(candidateManifest));
        if (depDoc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { Length: > 0 } depName)
        {
            modDirByName[depName] = Path.GetDirectoryName(candidateManifest)!;
        }
    }

    foreach (var dep in deps.EnumerateObject())
    {
        if (!modDirByName.TryGetValue(dep.Name, out var depDir))
        {
            Console.Error.WriteLine($"warning: dependency mod not found in repo: {dep.Name}");
            continue;
        }

        var depBin = Path.Combine(depDir, "bin", tfm);
        if (Directory.Exists(depBin))
        {
            referencePaths.AddRange(Directory.EnumerateFiles(depBin, "*.dll"));
        }
        else
        {
            Console.Error.WriteLine($"warning: dependency bin missing (compile it first): {depBin}");
        }
    }
}

referencePaths.AddRange(extraRefs);

// 3) 编译（对齐 mods csproj 默认：ImplicitUsings + Nullable + AllowUnsafe）
var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
var globalUsings = """
    global using System;
    global using System.Collections.Generic;
    global using System.IO;
    global using System.Linq;
    global using System.Net.Http;
    global using System.Threading;
    global using System.Threading.Tasks;
    """;
var trees = new List<SyntaxTree>
{
    CSharpSyntaxTree.ParseText(globalUsings, parseOptions, path: "GlobalUsings.g.cs")
};
trees.AddRange(sources.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), parseOptions, path: p)));

var compilation = CSharpCompilation.Create(
    modName,
    trees,
    referencePaths.Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(p => MetadataReference.CreateFromFile(p)),
    new CSharpCompilationOptions(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable,
        allowUnsafe: true,
        deterministic: true,
        optimizationLevel: OptimizationLevel.Release));

var outPath = explicitOut ?? (mainEntry is { Length: > 0 }
    ? Path.Combine(modDir, mainEntry.Replace('/', Path.DirectorySeparatorChar))
    : Path.Combine(modDir, "bin", tfm, $"{modName}.dll"));
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

var emitResult = compilation.Emit(outPath);
if (!emitResult.Success)
{
    foreach (var diagnostic in emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
    {
        Console.Error.WriteLine(diagnostic.ToString());
    }

    return 1;
}

Console.WriteLine($"compiled: {outPath} (sources={sources.Count}, refs={referencePaths.Count})");
return 0;

static bool IsUnderBuildDir(string path)
{
    var normalized = path.Replace('\\', '/');
    return normalized.Contains("/bin/") || normalized.Contains("/obj/");
}

static string? FindRepoRoot(string startDir)
{
    // 双标记定位：仓库根同时有 assets/ 与 external/；mod 自身的 assets/ 子目录不会同时命中两者
    var current = new DirectoryInfo(startDir);
    while (current != null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "assets")) &&
            Directory.Exists(Path.Combine(current.FullName, "external")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}
