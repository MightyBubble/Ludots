using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Tool;

/// <summary>
/// 地图、连续高度图和群体配置里已经写好的尺寸。烘焙只核对它们彼此一致，不另算一套格网。
/// </summary>
public readonly record struct CrowdNavSurfaceBakeSources(
    string MapSource,
    int MapWidthCm,
    int MapHeightCm,
    int MapCellSizeCm,
    string HeightSource,
    int HeightBoundsWidthCm,
    int HeightBoundsHeightCm,
    int HeightSampleColumns,
    int HeightSampleRows,
    string CrowdSource,
    int CrowdCellSizeCm);

/// <summary>
/// 把逻辑地形的 AreaId 烤成 .navsurface 的地形类型格。
/// 跳跃候选由调用方传入，本函数不生成。
/// </summary>
public static class CrowdNavSurfaceAuthoring
{
    public static NavSurfaceAsset Bake(
        LogicTerrainField terrain,
        IReadOnlyList<string> terrainTypeIds,
        CrowdNavSurfaceBakeSources sources,
        IReadOnlyList<NavSurfaceJumpCandidate> jumpCandidates)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(terrainTypeIds);
        ArgumentNullException.ThrowIfNull(jumpCandidates);
        RequireSourceName(sources.MapSource, nameof(sources.MapSource));
        RequireSourceName(sources.HeightSource, nameof(sources.HeightSource));
        RequireSourceName(sources.CrowdSource, nameof(sources.CrowdSource));

        if (terrainTypeIds.Count < 1 || terrainTypeIds.Count > CrowdSimulationLimits.MaxTerrainTypeIds)
        {
            throw new InvalidOperationException(
                $"{sources.CrowdSource}: terrainAreas 有 {terrainTypeIds.Count} 项，需为 1~{CrowdSimulationLimits.MaxTerrainTypeIds}。");
        }

        var typeIds = new string[terrainTypeIds.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < terrainTypeIds.Count; i++)
        {
            string id = terrainTypeIds[i];
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"{sources.CrowdSource}: terrainAreas[{i}].terrainId 为空。");
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException($"{sources.CrowdSource}: terrainAreas 的 terrainId \"{id}\" 重复。");
            }

            typeIds[i] = id;
        }

        if (sources.MapWidthCm != sources.MapHeightCm)
        {
            throw new InvalidOperationException(
                $"{sources.MapSource}: 板必须是正方形（WidthCm {sources.MapWidthCm} ≠ HeightCm {sources.MapHeightCm}）。");
        }

        if (sources.MapCellSizeCm <= 0)
        {
            throw new InvalidOperationException($"{sources.MapSource}: 格边长 {sources.MapCellSizeCm} cm 无效。");
        }

        if (sources.CrowdCellSizeCm <= 0)
        {
            throw new InvalidOperationException(
                $"{sources.CrowdSource}: profiles.cellSizeCm = {sources.CrowdCellSizeCm}，需为 > 0。");
        }

        if (sources.MapCellSizeCm != sources.CrowdCellSizeCm)
        {
            throw new InvalidOperationException(
                $"{sources.MapSource}: 格边长 {sources.MapCellSizeCm} cm 与 {sources.CrowdSource} 的 profiles.cellSizeCm {sources.CrowdCellSizeCm} cm 不一致。");
        }

        if (sources.MapWidthCm % sources.MapCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{sources.MapSource}: WidthCm（{sources.MapWidthCm}）不能被格边长 {sources.MapCellSizeCm} cm 整除。");
        }

        int navCells = sources.MapWidthCm / sources.MapCellSizeCm;
        if (terrain.WidthCells != navCells ||
            terrain.HeightCells != navCells ||
            terrain.HorizontalStepCm != sources.MapCellSizeCm ||
            terrain.VerticalStepCm != sources.MapCellSizeCm)
        {
            throw new InvalidOperationException(
                $"{sources.MapSource}: 逻辑地形是 {terrain.WidthCells}×{terrain.HeightCells} 格、步长 {terrain.HorizontalStepCm}×{terrain.VerticalStepCm} cm，" +
                $"地图 {sources.MapWidthCm}×{sources.MapHeightCm} cm / 格边长 {sources.MapCellSizeCm} cm 推出 {navCells}×{navCells} 格。");
        }

        if (sources.HeightBoundsWidthCm != sources.MapWidthCm || sources.HeightBoundsHeightCm != sources.MapHeightCm)
        {
            throw new InvalidOperationException(
                $"{sources.HeightSource}: 包围盒 {sources.HeightBoundsWidthCm}×{sources.HeightBoundsHeightCm} cm 与地图 {sources.MapWidthCm}×{sources.MapHeightCm} cm 不一致。");
        }

        int samplesX = sources.HeightSampleColumns;
        int samplesY = sources.HeightSampleRows;
        if (samplesX != samplesY || samplesX < navCells || samplesX % navCells != 0)
        {
            throw new InvalidOperationException(
                $"{sources.HeightSource}: 采样 {samplesX}×{samplesY} 与导航格 {navCells}×{navCells} 不一致（采样须为正方形，且每边不小于并整除导航格数）。");
        }

        var cells = new byte[checked(navCells * navCells)];
        for (int row = 0; row < navCells; row++)
        {
            for (int col = 0; col < navCells; col++)
            {
                LogicTerrainCell cell = terrain.GetCell(col, row);
                RejectAuthoredLogicChannel(col, row, cell, typeIds.Length);
                cells[row * navCells + col] = cell.AreaId;
            }
        }

        var jumps = new NavSurfaceJumpCandidate[jumpCandidates.Count];
        for (int i = 0; i < jumpCandidates.Count; i++)
        {
            NavSurfaceJumpCandidate jump = jumpCandidates[i];
            if (jump.FromX >= navCells || jump.FromY >= navCells || jump.ToX >= navCells || jump.ToY >= navCells)
            {
                throw new InvalidOperationException(
                    $"跳跃候选 {i} 端点（{jump.FromX},{jump.FromY}）→（{jump.ToX},{jump.ToY}）越出导航格 {navCells}×{navCells}。");
            }

            jumps[i] = jump;
        }

        return new NavSurfaceAsset
        {
            NavCellsX = navCells,
            NavCellsY = navCells,
            NavCellSizeCm = sources.MapCellSizeCm,
            TerrainTypeIds = typeIds,
            TerrainCells = cells,
            JumpCandidates = jumps,
        };
    }

    public static NavSurfaceAsset BakeAuthored(
        string repoRoot,
        string modId,
        string mapId,
        LogicTerrainField terrain,
        IReadOnlyList<NavSurfaceJumpCandidate> jumpCandidates)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("烘焙需要仓库根目录。");
        }

        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new InvalidOperationException("烘焙需要 modId。");
        }

        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("烘焙需要 mapId。");
        }

        MapConfig map = ToolMapConfigResolver.LoadMap(repoRoot, mapId, modId);
        CrowdAuthoringBinding binding = LoadRuntime(repoRoot, modId, map);
        BoardConfig board = NavSolverDeclaration.RequireBoard(map, binding.MapNav);
        string? heightAsset = map.ContinuousHeightmap?.Asset;
        if (string.IsNullOrWhiteSpace(heightAsset))
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 缺少 ContinuousHeightmap.asset。");
        }

        string modRoot = ToolMapConfigResolver.ResolveModRoot(repoRoot, modId);
        string heightPath = ResolveModAssetPath(modRoot, heightAsset);
        if (!File.Exists(heightPath))
        {
            throw new InvalidOperationException($"{heightAsset}: 高度图不存在（{heightPath}）。");
        }

        ContinuousHeightmapAsset height;
        using (FileStream stream = File.OpenRead(heightPath))
        {
            height = ContinuousHeightmapBinary.Read(stream);
        }

        var sources = new CrowdNavSurfaceBakeSources(
            $"Maps/{mapId}.json",
            board.WidthCm,
            board.HeightCm,
            board.GridCellSizeCm,
            heightAsset.Replace('\\', '/'),
            height.Bounds.Width,
            height.Bounds.Height,
            height.SampleColumns,
            height.SampleRows,
            "Navigation/navmesh.json",
            binding.Runtime.NavCellSizeCm);
        return Bake(terrain, binding.Runtime.TerrainTypeIds, sources, jumpCandidates);
    }

    private readonly record struct CrowdAuthoringBinding(
        CrowdSimulationRuntimeConfig Runtime,
        NavMapNavBoardsConfig MapNav);

    private static CrowdAuthoringBinding LoadRuntime(string repoRoot, string modId, MapConfig map)
    {
        string assetsRoot = Path.Combine(Path.GetFullPath(repoRoot), "assets");
        if (!Directory.Exists(assetsRoot))
        {
            throw new DirectoryNotFoundException($"Repo root is missing assets/: {repoRoot}");
        }

        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", assetsRoot);
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        modLoader.LoadResolvedPlan(NavMeshBakeConfigLoader.ResolveModLoadPlan(repoRoot, modId));
        var pipeline = new ConfigPipeline(vfs, modLoader);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        var report = new ConfigConflictReport();
        AgentProfileRegistry profiles = new AgentProfileConfigLoader(pipeline).Load(catalog, report);
        NavMeshBakeConfig navmesh = new NavMeshBakeConfigLoader(pipeline, profiles).Load(catalog, report);
        NavMapNavBoardsConfig mapNav = NavSolverDeclaration.RequireMap(navmesh, map.Id);
        if (!string.Equals(mapNav.Solver, NavSolverDeclaration.CrowdSimulation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"地图 \"{map.Id}\" 的 solver 是 \"{mapNav.Solver}\"，烘焙 CrowdSimulation 地表只接受 {NavSolverDeclaration.CrowdSimulation}。");
        }

        PathingConfig pathing = new PathingConfigLoader(pipeline).Load(catalog, report);
        CrowdSimulationConfig config = new CrowdSimulationConfigLoader(pipeline).LoadConfig(catalog, report);
        int fixedHz = new EngineClockConfigLoader(pipeline).Load(catalog, report).FixedHz;
        List<EntityTemplate> templates = LoadTemplates(pipeline, catalog, report);
        List<CrowdUnitTemplateFacts> facts = CrowdUnitTemplateFacts.FromTemplates(templates);
        int players = map.Players.Count;
        var response = new CrowdPushResponse[players * players];
        Array.Fill(response, CrowdPushResponse.Hold);
        var shares = new bool[players * players];
        CrowdPairRules rules = CrowdPairRules.Matrices(players, response, shares);
        CrowdSimulationRuntimeConfig runtime = CrowdSimulationConfigLoader.Load(
            config, map, profiles, navmesh, pathing, facts, fixedHz, rules);
        return new CrowdAuthoringBinding(runtime, mapNav);
    }

    private static readonly JsonSerializerOptions TemplateJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        IncludeFields = true,
    };

    private static List<EntityTemplate> LoadTemplates(ConfigPipeline pipeline, ConfigCatalog catalog, ConfigConflictReport report)
    {
        const string relative = ConfigMergeProvenance.TemplatesRelativePath;
        if (!catalog.TryGet(relative, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"config_catalog.json 没有登记 {relative}。");
        }

        IReadOnlyList<MergedConfigEntry> merged = pipeline.MergeArrayByIdFromCatalog(in entry, report);
        var byId = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        for (int i = 0; i < merged.Count; i++)
        {
            EntityTemplate? template = merged[i].Node.Deserialize<EntityTemplate>(TemplateJsonOptions);
            if (template == null || string.IsNullOrWhiteSpace(template.Id))
            {
                throw new InvalidOperationException($"{relative}: 条目 '{merged[i].Id}' 没有 id。");
            }

            byId[template.Id] = template;
        }

        EntityTemplateInheritance.ExpandAll(byId, report);
        return new List<EntityTemplate>(byId.Values);
    }

    public static void WriteReactAreaGrid(NavSurfaceAsset asset, Stream output)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ReactGridTerrainBinary.WriteAreaGrid(output, asset.NavCellsX, asset.NavCellsY, asset.TerrainCells);
    }

    public static string ResolveModAssetPath(string modRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(modRoot))
        {
            throw new InvalidOperationException("Mod 根目录为空。");
        }

        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new InvalidOperationException("资产路径为空。");
        }

        string trimmed = relative.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(relative) || trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"资产路径必须是 Mod 内的相对路径：{relative}");
        }

        string[] segments = trimmed.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == "..")
            {
                throw new InvalidOperationException($"资产路径不能包含 ..：{relative}");
            }
        }

        const string assetsPrefix = "assets/";
        if (trimmed.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[assetsPrefix.Length..];
        }

        string full = Path.GetFullPath(Path.Combine(modRoot, "assets", trimmed.Replace('/', Path.DirectorySeparatorChar)));
        string assetsRoot = Path.GetFullPath(Path.Combine(modRoot, "assets"));
        string prefix = assetsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? assetsRoot
            : assetsRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"资产路径越出 Mod：{relative}");
        }

        return full;
    }

    public static string ResolveNavSurfaceOutputPath(string modRoot, string relative)
    {
        string full = ResolveModAssetPath(modRoot, relative);
        if (!full.EndsWith(".navsurface", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"输出路径必须以 .navsurface 结尾：{relative}");
        }

        return full;
    }

    private static void RejectAuthoredLogicChannel(int col, int row, LogicTerrainCell cell, int terrainCount)
    {
        if (cell.HeightLevel != 0)
        {
            throw new InvalidOperationException(
                $"格（{col},{row}）的 HeightLevel = {cell.HeightLevel}。CrowdSimulation 地表不使用逻辑地形的高度。");
        }

        if (cell.WaterHeightLevel != 0)
        {
            throw new InvalidOperationException(
                $"格（{col},{row}）的 WaterHeightLevel = {cell.WaterHeightLevel}。CrowdSimulation 地表不使用逻辑地形的水位。");
        }

        if ((cell.SurfaceFlags & LogicTerrainSurfaceFlags.Water) != 0)
        {
            throw new InvalidOperationException(
                $"格（{col},{row}）的 Water 已设置。CrowdSimulation 地表不使用逻辑地形的水面标记。");
        }

        if ((cell.SurfaceFlags & LogicTerrainSurfaceFlags.Ramp) != 0)
        {
            throw new InvalidOperationException(
                $"格（{col},{row}）的 Ramp 已设置。CrowdSimulation 地表不使用逻辑地形的坡道标记。");
        }

        if ((cell.SurfaceFlags & LogicTerrainSurfaceFlags.Blocked) != 0)
        {
            throw new InvalidOperationException(
                $"格（{col},{row}）的 Blocked 已设置。阻挡物仍是地图实体，不写在逻辑地形格上。");
        }

        if (cell.AreaId >= terrainCount)
        {
            throw new InvalidOperationException(
                $"格（{col},{row}）的 AreaId = {cell.AreaId}，超出 terrainAreas（{terrainCount} 项）。");
        }
    }

    private static void RequireSourceName(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("来源名不能为空。", name);
        }
    }
}
