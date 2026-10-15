using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Tool;

/// <summary>
/// 地图范围、连续高度图和群体导航格边长。导航格由地图边长除以群体格边长得出，不读板的 Grid.CellSizeCm。
/// </summary>
public readonly record struct CrowdNavSurfaceBakeSources(
    string MapSource,
    int MapWidthCm,
    int MapHeightCm,
    string HeightSource,
    int HeightBoundsWidthCm,
    int HeightBoundsHeightCm,
    int HeightSampleColumns,
    int HeightSampleRows,
    string CrowdSource,
    int CrowdCellSizeCm);

/// <summary>
/// 把逻辑地形的 AreaId 烤成 .navsurface 的地形类型格。
/// 传入 linkCandidateBake 时，用同一份高度场和桥面栅格逐种类生成链接候选，并写入它带的 bakeInputsDigest。
/// 不传时没有链接种类，指纹只含导航格边长。
/// </summary>
public static class CrowdNavSurfaceAuthoring
{
    public static NavSurfaceAsset Bake(
        LogicTerrainField terrain,
        IReadOnlyList<string> terrainTypeIds,
        CrowdNavSurfaceBakeSources sources,
        CrowdLinkCandidateBakeInput? linkCandidateBake)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(terrainTypeIds);
        RequireSourceName(sources.MapSource, nameof(sources.MapSource));
        RequireSourceName(sources.HeightSource, nameof(sources.HeightSource));
        RequireSourceName(sources.CrowdSource, nameof(sources.CrowdSource));

        if (terrainTypeIds.Count < 1 || terrainTypeIds.Count > CrowdSimulationLimits.MaxTerrainTypeIds)
        {
            throw new InvalidOperationException(
                $"{sources.CrowdSource}: terrainTypes 有 {terrainTypeIds.Count} 项，需为 1~{CrowdSimulationLimits.MaxTerrainTypeIds}。");
        }

        var typeIds = new string[terrainTypeIds.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < terrainTypeIds.Count; i++)
        {
            string id = terrainTypeIds[i];
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"{sources.CrowdSource}: terrainTypes[{i}] 为空。");
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException($"{sources.CrowdSource}: terrainTypes 的 \"{id}\" 重复。");
            }

            typeIds[i] = id;
        }

        if (sources.MapWidthCm != sources.MapHeightCm)
        {
            throw new InvalidOperationException(
                $"{sources.MapSource}: 板必须是正方形（WidthCm {sources.MapWidthCm} ≠ HeightCm {sources.MapHeightCm}）。");
        }

        if (sources.CrowdCellSizeCm <= 0)
        {
            throw new InvalidOperationException(
                $"{sources.CrowdSource}: navCellSizeCm = {sources.CrowdCellSizeCm}，需为 > 0。");
        }

        if (sources.MapWidthCm % sources.CrowdCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{sources.MapSource}: WidthCm（{sources.MapWidthCm}）不能被 {sources.CrowdSource} 的 navCellSizeCm {sources.CrowdCellSizeCm} cm 整除。");
        }

        int navCells = sources.MapWidthCm / sources.CrowdCellSizeCm;
        if (terrain.WidthCells != navCells ||
            terrain.HeightCells != navCells ||
            terrain.HorizontalStepCm != sources.CrowdCellSizeCm ||
            terrain.VerticalStepCm != sources.CrowdCellSizeCm)
        {
            throw new InvalidOperationException(
                $"{sources.MapSource}: 逻辑地形是 {terrain.WidthCells}×{terrain.HeightCells} 格、步长 {terrain.HorizontalStepCm}×{terrain.VerticalStepCm} cm，" +
                $"地图 {sources.MapWidthCm}×{sources.MapHeightCm} cm / {sources.CrowdSource} 的 navCellSizeCm {sources.CrowdCellSizeCm} cm 推出 {navCells}×{navCells} 格。");
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

        IReadOnlyList<RuntimeLinkCandidateBake> kinds =
            linkCandidateBake?.Kinds ?? Array.Empty<RuntimeLinkCandidateBake>();
        var kindIds = new string[kinds.Count];
        var candidates = new List<NavSurfaceJumpCandidate>();
        for (int k = 0; k < kinds.Count; k++)
        {
            kindIds[k] = kinds[k].KindId;
            candidates.AddRange(CrowdLinkCandidateBake.Bake(
                linkCandidateBake!.MapId,
                navCells,
                cells,
                checked((byte)k),
                kinds[k],
                linkCandidateBake.Heights,
                linkCandidateBake.Bridge,
                sources.CrowdCellSizeCm));
        }

        return new NavSurfaceAsset
        {
            NavCellsX = navCells,
            NavCellsY = navCells,
            NavCellSizeCm = sources.CrowdCellSizeCm,
            TerrainTypeIds = typeIds,
            LinkKindIds = kindIds,
            BakeInputsDigest = linkCandidateBake?.BakeInputsDigest ?? NavSurfaceContract.BakeInputsDigestWithoutLinks(sources.CrowdCellSizeCm),
            TerrainCells = cells,
            JumpCandidates = candidates.ToArray(),
        };
    }

    public static NavSurfaceAsset BakeAuthored(
        string repoRoot,
        string modId,
        string mapId,
        LogicTerrainField terrain)
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
            heightAsset.Replace('\\', '/'),
            height.Bounds.Width,
            height.Bounds.Height,
            height.SampleColumns,
            height.SampleRows,
            "Navigation/navmesh.json",
            binding.Runtime.NavCellSizeCm);
        CrowdLinkCandidateBakeInput? linkCandidateBake = null;
        if (binding.Runtime.LinkCandidateBakes.Count > 0)
        {
            NavHeightField heights = NavHeightField.FromHeightmap(height, binding.Runtime.NavCellsPerSide, binding.Runtime.NavCellSizeCm);
            byte[] bridge = RasterizeBridgeMask(binding.Bridges, binding.Runtime);
            linkCandidateBake = new CrowdLinkCandidateBakeInput
            {
                MapId = mapId,
                Kinds = binding.Runtime.LinkCandidateBakes,
                Heights = heights,
                Bridge = bridge,
                BakeInputsDigest = NavSurfaceContract.BakeInputsDigest(binding.Runtime, binding.Bridges, heights),
            };
        }

        return Bake(terrain, binding.Runtime.TerrainTypeIds, sources, linkCandidateBake);
    }

    /// <summary>
    /// 桥面掩码。端点已经含岸上延伸（模板 effect.portalReachNavCells − 1），栅格化不再外扩。
    /// </summary>
    public static byte[] RasterizeBridgeMask(IReadOnlyList<LayeredPathRecord> bridges, CrowdSimulationRuntimeConfig config)
    {
        UpperLayerSurface deck = UpperLayerBake.RasterizeLayeredPaths(bridges, config);
        var mask = new byte[deck.AreaIndex.Length];
        for (int i = 0; i < mask.Length; i++)
        {
            if (deck.AreaIndex[i] != 0)
            {
                mask[i] = 1;
            }
        }

        return mask;
    }

    public readonly record struct CrowdAuthoringTerrainFile(string RelativePath, string FullPath, int CellSizeCm);

    public static CrowdAuthoringTerrainFile RequireAuthoringTerrain(string repoRoot, string modId, string mapId)
    {
        CrowdAuthoringStart start = ResolveAuthoringStart(repoRoot, modId, mapId);
        if (!File.Exists(start.TerrainFullPath))
        {
            throw new InvalidOperationException(
                $"地编资产不存在：{start.TerrainRelativePath}（{start.TerrainFullPath}）。新地图先跑 nav init-crowd-terrain。");
        }

        return new CrowdAuthoringTerrainFile(start.TerrainRelativePath, start.TerrainFullPath, start.NavCellSizeCm);
    }

    /// <summary>
    /// 新地图起步要写的两份文件：逻辑地形和连续高度图。路径和尺寸都从地图与 navmesh.json 推出，文件本身可以还不存在。
    /// </summary>
    public readonly record struct CrowdAuthoringStart(
        string TerrainRelativePath,
        string TerrainFullPath,
        string HeightRelativePath,
        string HeightFullPath,
        int BoardSizeCm,
        int NavCellsPerSide,
        int NavCellSizeCm,
        IReadOnlyList<string> TerrainTypeIds);

    public static CrowdAuthoringStart ResolveAuthoringStart(string repoRoot, string modId, string mapId)
    {
        MapConfig map = ToolMapConfigResolver.LoadMap(repoRoot, mapId, modId);
        CrowdAuthoringBinding binding = LoadRuntime(repoRoot, modId, map);
        string? terrainAsset = binding.MapNav.LogicTerrainAsset;
        if (string.IsNullOrWhiteSpace(terrainAsset))
        {
            throw new InvalidOperationException(
                $"Navigation/navmesh.json: maps.{mapId}.logicTerrainAsset 为空。地编资产不写在地图 Board.DataFile 上。");
        }

        string? heightAsset = map.ContinuousHeightmap?.Asset;
        if (string.IsNullOrWhiteSpace(heightAsset))
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 缺少 ContinuousHeightmap.asset。");
        }

        BoardConfig board = NavSolverDeclaration.RequireBoard(map, binding.MapNav);
        string modRoot = ToolMapConfigResolver.ResolveModRoot(repoRoot, modId);
        return new CrowdAuthoringStart(
            terrainAsset.Replace('\\', '/'),
            ResolveModAssetPath(modRoot, terrainAsset),
            heightAsset.Replace('\\', '/'),
            ResolveModAssetPath(modRoot, heightAsset),
            board.WidthCm,
            binding.Runtime.NavCellsPerSide,
            binding.Runtime.NavCellSizeCm,
            binding.Runtime.TerrainTypeIds);
    }

    /// <summary>
    /// 地形类型矩形，导航格坐标，两端都含。文本形式 <c>terrainId:minCol,minRow,maxCol,maxRow</c>。
    /// </summary>
    public readonly record struct CrowdTerrainRect(string TerrainId, int MinCol, int MinRow, int MaxCol, int MaxRow)
    {
        public static CrowdTerrainRect Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("--rect 为空。");
            }

            int colon = text.IndexOf(':', StringComparison.Ordinal);
            string[] parts = colon > 0 ? text[(colon + 1)..].Split(',') : Array.Empty<string>();
            if (parts.Length != 4)
            {
                throw new InvalidOperationException($"--rect \"{text}\" 须写成 terrainId:minCol,minRow,maxCol,maxRow。");
            }

            var values = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out values[i]))
                {
                    throw new InvalidOperationException($"--rect \"{text}\" 第 {i + 1} 个坐标不是整数。");
                }
            }

            return new CrowdTerrainRect(text[..colon].Trim(), values[0], values[1], values[2], values[3]);
        }
    }

    /// <summary>
    /// 先整张填 <paramref name="fillTerrainId"/>，再按顺序盖矩形，后写的覆盖先写的。返回逐格 AreaId（terrainTypes 下标）。
    /// </summary>
    public static byte[] PaintTerrainAreas(
        int navCellsPerSide,
        IReadOnlyList<string> terrainTypeIds,
        string fillTerrainId,
        IReadOnlyList<CrowdTerrainRect> rects)
    {
        ArgumentNullException.ThrowIfNull(terrainTypeIds);
        ArgumentNullException.ThrowIfNull(rects);
        if (navCellsPerSide <= 0)
        {
            throw new InvalidOperationException($"导航格数 {navCellsPerSide} 无效。");
        }

        var cells = new byte[checked(navCellsPerSide * navCellsPerSide)];
        Array.Fill(cells, TerrainIndex(terrainTypeIds, fillTerrainId));
        for (int r = 0; r < rects.Count; r++)
        {
            CrowdTerrainRect rect = rects[r];
            if (rect.MinCol < 0 || rect.MinRow < 0 ||
                rect.MaxCol >= navCellsPerSide || rect.MaxRow >= navCellsPerSide ||
                rect.MinCol > rect.MaxCol || rect.MinRow > rect.MaxRow)
            {
                throw new InvalidOperationException(
                    $"矩形 {rect.TerrainId}:{rect.MinCol},{rect.MinRow},{rect.MaxCol},{rect.MaxRow} 超出 0~{navCellsPerSide - 1} 或首尾颠倒。");
            }

            byte area = TerrainIndex(terrainTypeIds, rect.TerrainId);
            for (int row = rect.MinRow; row <= rect.MaxRow; row++)
            {
                cells.AsSpan(row * navCellsPerSide + rect.MinCol, rect.MaxCol - rect.MinCol + 1).Fill(area);
            }
        }

        return cells;
    }

    /// <summary>
    /// 平地高度图：uint16 缩放布局（群体会话只读这种布局），每边一个导航格一个采样，所有采样都是 <paramref name="heightCm"/>。
    /// </summary>
    public static ContinuousHeightmapAsset CreateFlatHeightmap(int boardSizeCm, int samplesPerSide, int heightCm)
    {
        if (boardSizeCm <= 0 || samplesPerSide <= 0)
        {
            throw new InvalidOperationException($"高度图尺寸无效：板 {boardSizeCm} cm，每边 {samplesPerSide} 个采样。");
        }

        int samples = checked(samplesPerSide * samplesPerSide);
        return new ContinuousHeightmapAsset(
            new WorldAabbCm(0, 0, boardSizeCm, boardSizeCm),
            samplesPerSide,
            samplesPerSide,
            new ushort[samples],
            new[] { new ContinuousHeightmapLayerDefinition(0, "base", 0, samples) },
            new ContinuousHeightSampleScale(heightCm, 1, 1));
    }

    /// <summary>nav init-crowd-terrain：写 logicTerrainAsset 指向的逻辑地形。</summary>
    public static CrowdAuthoringStart WriteInitialTerrain(
        string repoRoot,
        string modId,
        string mapId,
        string fillTerrainId,
        IReadOnlyList<CrowdTerrainRect> rects,
        bool overwrite)
    {
        CrowdAuthoringStart start = ResolveAuthoringStart(repoRoot, modId, mapId);
        if (File.Exists(start.TerrainFullPath) && !overwrite)
        {
            throw new IOException($"File exists: {start.TerrainFullPath}");
        }

        byte[] cells = PaintTerrainAreas(start.NavCellsPerSide, start.TerrainTypeIds, fillTerrainId, rects);
        Directory.CreateDirectory(Path.GetDirectoryName(start.TerrainFullPath)!);
        using FileStream output = File.Create(start.TerrainFullPath);
        ReactGridTerrainBinary.WriteAreaGrid(output, start.NavCellsPerSide, start.NavCellsPerSide, cells);
        return start;
    }

    /// <summary>nav init-crowd-heightmap：写 ContinuousHeightmap.Asset 指向的平地高度图。</summary>
    public static CrowdAuthoringStart WriteFlatHeightmap(string repoRoot, string modId, string mapId, int heightCm, bool overwrite)
    {
        CrowdAuthoringStart start = ResolveAuthoringStart(repoRoot, modId, mapId);
        if (File.Exists(start.HeightFullPath) && !overwrite)
        {
            throw new IOException($"File exists: {start.HeightFullPath}");
        }

        ContinuousHeightmapAsset height = CreateFlatHeightmap(start.BoardSizeCm, start.NavCellsPerSide, heightCm);
        Directory.CreateDirectory(Path.GetDirectoryName(start.HeightFullPath)!);
        using FileStream output = File.Create(start.HeightFullPath);
        ContinuousHeightmapBinary.Write(output, height);
        return start;
    }

    /// <summary>nav bake-crowd-navsurface：读入库的逻辑地形和高度图，烤出 .navsurface 写到 <paramref name="outFile"/>。</summary>
    public static NavSurfaceAsset BakeAuthoredFile(string repoRoot, string modId, string mapId, string outFile, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(outFile))
        {
            throw new InvalidOperationException("--out is required.");
        }

        if (File.Exists(outFile) && !overwrite)
        {
            throw new IOException($"File exists: {outFile}");
        }

        CrowdAuthoringTerrainFile authored = RequireAuthoringTerrain(repoRoot, modId, mapId);
        LogicTerrainField terrain = ReactGridTerrainBinary.Read(authored.FullPath, authored.CellSizeCm);
        NavSurfaceAsset asset = BakeAuthored(repoRoot, modId, mapId, terrain);
        WriteNavSurface(Path.GetFullPath(outFile), asset);
        return asset;
    }

    private static byte TerrainIndex(IReadOnlyList<string> terrainTypeIds, string terrainId)
    {
        for (int i = 0; i < terrainTypeIds.Count; i++)
        {
            if (string.Equals(terrainTypeIds[i], terrainId, StringComparison.Ordinal))
            {
                return (byte)i;
            }
        }

        throw new InvalidOperationException(
            $"地形类型 \"{terrainId}\" 不在 maps.<mapId>.terrainTypes（{string.Join(", ", terrainTypeIds)}）里。");
    }

    public static void WriteNavSurface(string targetPath, NavSurfaceAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("输出路径为空。");
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream output = File.Create(targetPath);
        NavSurfaceAsset.Write(asset, output);
    }

    private readonly record struct CrowdAuthoringBinding(
        CrowdSimulationRuntimeConfig Runtime,
        NavMapNavBoardsConfig MapNav,
        ConfigPipeline Pipeline,
        ConfigCatalog Catalog,
        ConfigConflictReport Report,
        IReadOnlyList<LayeredPathRecord> Bridges);

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

        CrowdTraversalConfig traversal = CrowdTraversalConfig.Load(pipeline, catalog, report);
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
            config, map, profiles, navmesh, traversal, facts, fixedHz, rules);
        IReadOnlyList<LayeredPathRecord> bridges = Array.Empty<LayeredPathRecord>();
        if (runtime.LinkCandidateBakes.Count > 0)
        {
            var templatesById = new Dictionary<string, EntityTemplate>(templates.Count, StringComparer.Ordinal);
            for (int i = 0; i < templates.Count; i++)
            {
                EntityTemplate template = templates[i];
                templatesById.Add(template.Id, template);
            }

            bridges = CrowdSimulationMapSurfaceSource.Extract(map, templatesById).LayeredPaths;
        }

        return new CrowdAuthoringBinding(runtime, mapNav, pipeline, catalog, report, bridges);
    }

    public readonly record struct CrowdTerrainTypeColorRow(
        string TerrainId,
        int Index,
        int Red,
        int Green,
        int Blue,
        int Alpha);

    public static IReadOnlyList<CrowdTerrainTypeColorRow> LoadMapTerrainColors(string repoRoot, string modId, string mapId)
    {
        MapConfig map = ToolMapConfigResolver.LoadMap(repoRoot, mapId, modId);
        CrowdAuthoringBinding binding = LoadRuntime(repoRoot, modId, map);
        IReadOnlyList<string> terrainIds = binding.Runtime.TerrainTypeIds;
        if (terrainIds.Count == 0)
        {
            throw new InvalidOperationException($"Navigation/navmesh.json: maps.{mapId}.terrainTypes 为空。");
        }

        CrowdTerrainTypeColorTable table = CrowdTerrainTypeColors.LoadMerged(
            binding.Pipeline, binding.Catalog, binding.Report);
        table.RequireCovers(terrainIds, mapId);
        var rows = new CrowdTerrainTypeColorRow[terrainIds.Count];
        for (int i = 0; i < terrainIds.Count; i++)
        {
            CrowdTerrainTypeColor color = table.Colors[terrainIds[i]];
            rows[i] = new CrowdTerrainTypeColorRow(terrainIds[i], i, color.Red, color.Green, color.Blue, color.Alpha);
        }

        return rows;
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
                $"格（{col},{row}）的 AreaId = {cell.AreaId}，超出 terrainTypes（{terrainCount} 项）。");
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
