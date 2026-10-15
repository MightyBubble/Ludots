using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Scripting;

namespace CrowdSimulationTests;

/// <summary>
/// 按某个 mod 的依赖闭包装出真实配置管线(不起引擎)。场景 mod 只写差量片段，
/// 单独解析它的文件没有意义，要按运行时同样的加载序合并后再解析。
/// </summary>
internal sealed class ModConfigChain
{
    private readonly ConfigPipeline _pipeline;
    private readonly ConfigCatalog _catalog;

    private ModConfigChain(string modId, ConfigPipeline pipeline, ConfigCatalog catalog)
    {
        ModId = modId;
        _pipeline = pipeline;
        _catalog = catalog;
    }

    public string ModId { get; }

    public static ModConfigChain Open(string modId)
    {
        string repoRoot = RepoRoot();
        IReadOnlyList<ResolvedModLoadEntry> plan = NavMeshBakeConfigLoader.ResolveModLoadPlan(repoRoot, modId);
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", Path.Combine(repoRoot, "assets"));
        var mods = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        foreach (ResolvedModLoadEntry entry in plan)
        {
            vfs.Mount(entry.Id, entry.RootPath);
            mods.LoadedModIds.Add(entry.Id);
        }

        var pipeline = new ConfigPipeline(vfs, mods);
        return new ModConfigChain(modId, pipeline, ConfigCatalogLoader.Load(pipeline));
    }

    /// <summary>repo mods/ 下自带 <paramref name="relativePath"/> 的 mod(按 mod id 排序)。</summary>
    public static IReadOnlyList<string> ModsShipping(string relativePath, string? underModsSubdir = null)
    {
        string root = Path.Combine(RepoRoot(), "mods");
        if (underModsSubdir != null)
            root = Path.Combine(root, underModsSubdir);
        return ModDiscovery.DiscoverMods(new[] { root })
            .Where(mod => File.Exists(Path.Combine(mod.DirectoryPath, "assets", relativePath)))
            .Select(mod => mod.Manifest.Name)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    public JsonObject Merged(string relativePath)
    {
        ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(_catalog, relativePath, ConfigMergePolicy.DeepObject);
        return _pipeline.MergeDeepObjectFromCatalog(in entry, new ConfigConflictReport())
            ?? throw new InvalidOperationException($"{ModId}: {relativePath} 合并为空");
    }

    public string StartupMapId() =>
        _pipeline.MergeGameConfig().StartupMapId
        ?? throw new InvalidOperationException($"{ModId}: 加载序里没有 game.json 的 startupMapId");

    public static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
