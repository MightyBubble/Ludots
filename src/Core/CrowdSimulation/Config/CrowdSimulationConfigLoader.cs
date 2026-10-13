using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// CrowdSimulation 配置装载入口。
/// 管线侧负责 config_catalog 合并（DeepObject）；<see cref="Load"/> 把
/// 合并结果、地图、代理体型与固定时钟组装成只读运行时配置——
/// 内核不读文件、不读全局单例。世界距离在组装层用 double 做厘米 ÷ 导航格长，再
/// <c>Fix64.FromDouble</c> 成交格数；其余长度仍是厘米 Fix64。内核坐标保持厘米。
/// </summary>
public sealed class CrowdSimulationConfigLoader
{
    public const string RelativePath = "CrowdSimulationConfig.json";

    /// <summary>迷雾格长下限(厘米),即 fog.cellSizeCm。
    /// 形状米坐标先被 CrowdFogArea 的 ±46340 m 守卫卡住,再除以格长(米)。圆扫描线只走包围盒,
    /// |py−圆心| ≤ 半径 + 1/2;圆心在负半轴、包围盒被夹到 0 格时 py 最小是 0.5,同样只多出半格。
    /// 格长 ≥ 1 米时被平方的量 ≤ 46340.5,平方仍小于 2^31。</summary>
    public const int MinFogCellSizeCm = CrowdReferenceMeters.CentimetersPerMeter;

    private readonly ConfigPipeline _pipeline;

    public CrowdSimulationConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>经 catalog 合并并解析 CrowdSimulationConfig.json（DeepObject）。</summary>
    public CrowdSimulationConfig LoadConfig(ConfigCatalog catalog, ConfigConflictReport? report = null)
    {
        var entry = ConfigPipeline.RequireEntry(catalog, RelativePath, ConfigMergePolicy.DeepObject);
        JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged == null)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 没有任何来源提供该文件（标准参数在 CrowdSimulationStandardMod，地图事实在地图 Mod）。");
        }

        return CrowdSimulationConfig.Load(merged);
    }

    /// <summary>求解器互斥：两份配置 mapId 相同才冲突。</summary>
    public static void EnsureSolverExclusive(string? massNavigationMapId, string crowdSimulationMapId)
    {
        if (string.Equals(massNavigationMapId, crowdSimulationMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: 地图 \"{crowdSimulationMapId}\" 同时启用了 CrowdSimulation 与 MassNavigation 求解器；一张地图只能使用其中一个。");
        }
    }

    /// <summary>
    /// 校验并组装只读运行时配置。输入全部来自 Ludots 已加载的数据。
    /// </summary>
    /// <param name="config">catalog 合并后的 CrowdSimulationConfig.json。</param>
    /// <param name="map">地图（Boards / Teams / Players / Entities）。聚焦的地图 id 就是会话地图。</param>
    /// <param name="profiles">Navigation/agent_profiles.json 注册表。运行时体型行只来自带 CrowdSimulationAgent 的模板。校验集另外纳入 pathing agentTypes[].profileId 和地图绑定档案。</param>
    /// <param name="navmesh">Navigation/navmesh.json。区域、能力档、maps.&lt;mapId&gt; 的地形事实。</param>
    /// <param name="pathing">Navigation/pathing.json。移动类型与区域代价。</param>
    /// <param name="templates">带 CrowdSimulationAgent 的单位模板。</param>
    /// <param name="fixedHz">Engine/clock.json 的 FixedHz——仿真频率的唯一来源。</param>
    /// <param name="pairRules">已经投影好的玩家对矩阵。装载只按下标挂到地图玩家上，不解释关系类型。</param>
    public static CrowdSimulationRuntimeConfig Load(
        CrowdSimulationConfig config,
        MapConfig map,
        AgentProfileRegistry profiles,
        NavMeshBakeConfig navmesh,
        PathingConfig pathing,
        IReadOnlyList<CrowdUnitTemplateFacts> templates,
        int fixedHz,
        CrowdPairRules pairRules)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(navmesh);
        ArgumentNullException.ThrowIfNull(pathing);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(pairRules);
        if (fixedHz < 1)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fixedHz = {fixedHz}，需为 ≥ 1 的整数（步进频率只由 Engine/clock.json 规定）。");
        }

        CrowdSimulationConfigValidator.Validate(config);
        var bound = BindNavigation(config, map, profiles, navmesh, pathing, templates);

        string mapFile = $"Maps/{map.Id}.json";

        if (map.Boards.Count == 0)
        {
            throw new InvalidOperationException($"{mapFile}: Boards 不能为空（CrowdSimulation 世界需要一块正方形板）。");
        }

        var board = map.Boards[0];
        int navCellSizeCm = bound.NavCellSizeCm;

        // 迷雾格长由作者写成厘米,必须整除导航格;派生倍数是栅格算法量,范围仍是 1..32。
        int fogCellSizeCm = config.Fog.CellSizeCm;
        if (fogCellSizeCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fog.cellSizeCm（{fogCellSizeCm}）必须能被 profiles.cellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellsPerFogCell = fogCellSizeCm / navCellSizeCm;
        if (navCellsPerFogCell < 1 || navCellsPerFogCell > 32)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fog.cellSizeCm（{fogCellSizeCm}）/ profiles.cellSizeCm（{navCellSizeCm}）= {navCellsPerFogCell}，需为 ≥ 1、≤ 32 的整数");
        }

        if (board.WidthCm != board.HeightCm)
        {
            throw new InvalidOperationException($"{mapFile}: Boards[0] 必须是正方形（WidthCm {board.WidthCm} ≠ HeightCm {board.HeightCm}）。");
        }

        if (board.Anchor == null)
        {
            throw new InvalidOperationException(
                $"{mapFile}: Boards[0].Anchor 必须给出，且 LocalXCm、LocalYCm、WorldXCm、WorldYCm 均为 0。");
        }

        if (board.Anchor.LocalXCm != 0 || board.Anchor.LocalYCm != 0
            || board.Anchor.WorldXCm != 0 || board.Anchor.WorldYCm != 0)
        {
            throw new InvalidOperationException(
                $"{mapFile}: Boards[0].Anchor 的 LocalXCm、LocalYCm、WorldXCm、WorldYCm 必须为 0（当前 {board.Anchor.LocalXCm}, {board.Anchor.LocalYCm}, {board.Anchor.WorldXCm}, {board.Anchor.WorldYCm}）。");
        }

        if (board.WidthCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException($"{mapFile}: Boards[0].WidthCm（{board.WidthCm}）必须能被 profiles.cellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellCount = board.WidthCm / navCellSizeCm;
        if (navCellCount < 16 || navCellCount > 1024)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 每边导航格数 = {navCellCount}（Boards[0].WidthCm / profiles.cellSizeCm），需为 ≥ 16、≤ 1024 的整数");
        }

        int navTileSizeCm = config.Navtile.SizeCm;
        if (navTileSizeCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: navtile.sizeCm（{navTileSizeCm}）必须能被 profiles.cellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellsPerTile = navTileSizeCm / navCellSizeCm;
        if (navCellsPerTile < 4 || navCellsPerTile > 128)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: navtile.sizeCm（{navTileSizeCm}）/ profiles.cellSizeCm（{navCellSizeCm}）= {navCellsPerTile}，需为 ≥ 4、≤ 128 的整数");
        }

        if (navCellCount % navCellsPerTile != 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: 每边导航格数（{navCellCount}）必须能被 navtile.sizeCm 派生的导航格数（{navCellsPerTile}）整除");
        }

        int fogEdge = (navCellCount + navCellsPerFogCell - 1) / navCellsPerFogCell;
        int radiusCells = (int)Math.Ceiling(config.Fog.VisionCm / (double)fogCellSizeCm);
        int maxRadiusCells = MaxFogVisionRadiusCells(fogEdge);
        if (radiusCells > maxRadiusCells)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fog.visionCm（{config.Fog.VisionCm}）/ fog.cellSizeCm（{fogCellSizeCm}）向上取整 = {radiusCells}，不得超过每边迷雾格数（{fogEdge}）对角格距的向上取整 {maxRadiusCells}");
        }

        Fix64 NavCellsFromCm(double centimeters) => Fix64.FromDouble(centimeters / (double)navCellSizeCm);

        if (map.Players.Count < 1 || map.Players.Count > 16)
        {
            throw new InvalidOperationException($"{mapFile}: Players 需要 1~16 个（当前 {map.Players.Count} 个）。");
        }

        if (pairRules.PlayerCount != map.Players.Count)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: 功能位矩阵有 {pairRules.PlayerCount} 个玩家，地图 Players 有 {map.Players.Count} 个。");
        }

        pairRules.BindPlayerIndex(map);
        var playerIds = new HashSet<int>();
        foreach (var p in map.Players)
        {
            playerIds.Add(p.PlayerId);
        }

        foreach (var b in config.Deploy.Bases)
        {
            if (!playerIds.Contains(b.PlayerId))
            {
                throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: deploy.bases[].playerId = {b.PlayerId}，必须存在于地图 Players[]");
            }
        }

        return new CrowdSimulationRuntimeConfig
        {
            MapId = map.Id,
            FixedHz = fixedHz,
            NavCellsPerSide = navCellCount,
            NavCellSizeCm = navCellSizeCm,
            WorldSeed = config.World.Seed,
            SurfaceAsset = bound.SurfaceAsset,
            TerrainTypeIds = bound.TerrainTypeIds,
            NavAreas = bound.NavAreas,
            TerrainToArea = bound.TerrainToArea,
            NavProfiles = bound.NavProfiles,
            AgentTypes = bound.AgentTypes,
            UnitTypes = bound.UnitTypes,
            Profiles = bound.Profiles,
            RadiusClasses = bound.RadiusClasses,
            AvoidanceRadiusScale = bound.AvoidanceRadiusScale,
            Navmesh = new RuntimeNavmeshSection
            {
                MinRegionNavCellArea = Fix64.FromDouble(config.Navmesh.MinRegionNavCellArea),
                MaxSimplificationErrorNavCells = Fix64.FromDouble(config.Navmesh.MaxSimplificationErrorNavCells),
                MaxEdgeLenNavCells = Fix64.FromDouble(config.Navmesh.MaxEdgeLenNavCells),
                MaxVertsPerPoly = config.Navmesh.MaxVertsPerPoly,
                PortalInsetNavCells = Fix64.FromDouble(config.Navmesh.PortalInsetNavCells),
            },
            Hpa = new RuntimeHpaSection { NavCellsPerTile = navCellsPerTile, MaxEntranceWidthNavCells = config.Hpa.MaxEntranceWidthNavCells },
            NavtileCacheCapacity = config.Navtile.CacheCapacity,
            Flowfield = new RuntimeFlowfieldSection
            {
                CacheCapacity = config.Flowfield.CacheCapacity,
                PoolCapacity = config.Flowfield.PoolCapacity,
                CorridorPaddingNavTiles = config.Flowfield.CorridorPaddingNavTiles,
                MaxRebuildsPerTick = config.Flowfield.MaxRebuildsPerTick,
            },
            Planning = new RuntimePlanningSection
            {
                LatencyTicks = config.Planning.LatencyTicks,
                RefreshLatencyTicks = config.Planning.RefreshLatencyTicks,
                MaxRefreshesPerTick = config.Planning.MaxRefreshesPerTick,
                RebakeSlices = config.Planning.RebakeSlices,
            },
            Fog = new RuntimeFogSection
            {
                RateHz = Fix64.FromDouble(config.Fog.RateHz),
                FogCellSizeCm = fogCellSizeCm,
                NavCellsPerFogCell = navCellsPerFogCell,
                VisionCm = Fix64.FromInt(config.Fog.VisionCm),
                RevealTicks = config.Fog.RevealTicks,
                SlotCacheCapacity = config.Fog.SlotCacheCapacity,
                VariantCapacity = config.Fog.VariantCapacity,
                LineOfSight = config.Fog.LineOfSight,
                EyeCm = config.Fog.EyeCm,
            },
            Formation = new RuntimeFormationSection
            {
                SpacingScale = Fix64.FromDouble(config.Formation.SpacingScale),
                LeaderSpeed = Fix64.FromDouble(config.Formation.LeaderSpeed),
                LeaderTurnRatePerSec = Fix64.FromDouble(config.Formation.LeaderTurnRatePerSec),
                FaceTurnScale = Fix64.FromDouble(config.Formation.FaceTurnScale),
                CatchUp = Fix64.FromDouble(config.Formation.CatchUp),
                SlotSightNavCells = NavCellsFromCm(config.Formation.SlotSightCm),
                SettleTimeSec = Fix64.FromDouble(config.Formation.SettleTimeSec),
                LeaderLookAheadNavCells = NavCellsFromCm(config.Formation.LeaderLookAheadCm),
                MagicBoxMaxSpreadCm = Fix64.FromDouble(config.Formation.MagicBoxMaxSpreadCm),
                MagicBoxPadCm = Fix64.FromDouble(config.Formation.MagicBoxPadCm),
                SlotArriveNavCells = NavCellsFromCm(config.Formation.SlotArriveCm),
                GoalArriveNavCells = NavCellsFromCm(config.Formation.GoalArriveCm),
                SettleRadiusNavCells = NavCellsFromCm(config.Formation.SettleRadiusCm),
                ClumpSlack = Fix64.FromDouble(config.Formation.ClumpSlack),
                MinClumpNavCells = NavCellsFromCm(config.Formation.MinClumpCm),
                HeadingInheritDot = Fix64.FromDouble(config.Formation.HeadingInheritDot),
                MirrorFlipDot = Fix64.FromDouble(config.Formation.MirrorFlipDot),
            },
            Formations = OrderedById(config.Formations, (id, f) => new RuntimeFormationShape
            {
                Id = id,
                Label = f.Label,
                SpacingDiameterScale = Fix64.FromDouble(f.SpacingDiameterScale),
                Aspect = Fix64.FromDouble(f.Aspect),
                Wedge = f.Wedge,
            }),
            Movement = new RuntimeMovementSection
            {
                MaxStepNavCells = Fix64.FromDouble(config.Movement.MaxStepNavCells),
                MaxSubSteps = config.Movement.MaxSubSteps,
                SlotTimeConstantSec = Fix64.FromDouble(config.Movement.SlotTimeConstantSec),
                BlendRatePerSec = Fix64.FromDouble(config.Movement.BlendRatePerSec),
                BlendCommit = Fix64.FromDouble(config.Movement.BlendCommit),
                SlotCheckIntervalSubSteps = config.Movement.SlotCheckIntervalSubSteps,
                SightHysteresis = Fix64.FromDouble(config.Movement.SightHysteresis),
                LaneSpreadSpeedRatio = Fix64.FromDouble(config.Movement.LaneSpreadSpeedRatio),
                WakeDistanceNavCells = NavCellsFromCm(config.Movement.WakeDistanceCm),
                StallSpeedRatio = Fix64.FromDouble(config.Movement.StallSpeedRatio),
                StallDecayPerSec = Fix64.FromDouble(config.Movement.StallDecayPerSec),
                UnitTurnRateRadPerSec = Fix64.FromDouble(config.Movement.UnitTurnRateRadPerSec),
                StopSpeedRatio = Fix64.FromDouble(config.Movement.StopSpeedRatio),
                SpeedCapRatio = Fix64.FromDouble(config.Movement.SpeedCapRatio),
                RestAccelScale = Fix64.FromDouble(config.Movement.RestAccelScale),
                RestSeparationScale = Fix64.FromDouble(config.Movement.RestSeparationScale),
                JumpSpeedRatio = Fix64.FromDouble(config.Movement.JumpSpeedRatio),
            },
            Avoidance = new RuntimeAvoidanceSection
            {
                RateHz = Fix64.FromDouble(config.Avoidance.RateHz),
                HashRings = config.Avoidance.HashRings,
                MaxNeighbors = config.Avoidance.MaxNeighbors,
                MaxScan = config.Avoidance.MaxScan,
                SeparationWeight = Fix64.FromDouble(config.Avoidance.SeparationWeight),
                AccelerationPerSec = Fix64.FromDouble(config.Avoidance.AccelerationPerSec),
                RestDeadband = Fix64.FromDouble(config.Avoidance.RestDeadband),
                MaxPush = Fix64.FromDouble(config.Avoidance.MaxPush),
                Smoothing = Fix64.FromDouble(config.Avoidance.Smoothing),
            },
            Push = new RuntimePushSection
            {
                MovingBonus = Fix64.FromDouble(config.Push.MovingBonus),
                DominantShare = Fix64.FromDouble(config.Push.DominantShare),
            },
            PairRules = pairRules,
            Sim = new RuntimeSimSection
            {
                MaxUnits = config.Sim.MaxUnits,
                SimSecondsPerTickSecond = Fix64.FromDouble(config.Sim.SimSecondsPerTickSecond),
                SimSecondsPerTickSecondRaw = config.Sim.SimSecondsPerTickSecond,
                ParallelWorkerCount = config.Sim.ParallelWorkerCount,
            },
            Spawn = new RuntimeSpawnSection
            {
                PlacementTries = config.Spawn.PlacementTries,
                GroupSpacingScale = Fix64.FromDouble(config.Spawn.GroupSpacingScale),
                TargetSearchTries = config.Spawn.TargetSearchTries,
                NavCellInsetFraction = Fix64.FromDouble(config.Spawn.NavCellInsetFraction),
            },
            Structures = new RuntimeStructuresSection
            {
                BlockCoverage = Fix64.FromDouble(config.Structures.BlockCoverage),
                PortalReachNavCells = config.Structures.PortalReachNavCells,
                Templates = BuildStructureTemplates(config, bound.AreaIndexById),
            },
            Deploy = new RuntimeDeploySection
            {
                InitialUnits = config.Deploy.InitialUnits,
                SpreadNavCells = NavCellsFromCm(config.Deploy.SpreadCm),
                BaseJitterNavCells = NavCellsFromCm(config.Deploy.BaseJitterCm),
                CentersPerGroup = config.Deploy.CentersPerGroup,
                Bases = Array.ConvertAll(config.Deploy.Bases, b => new RuntimeDeployBase
                {
                    PlayerId = b.PlayerId,
                    XCm = b.XCm,
                    YCm = b.YCm,
                    Color = b.Color,
                }),
            },
            Telemetry = new RuntimeTelemetrySection
            {
                TurnEpsRad = Fix64.FromDouble(config.Telemetry.TurnEpsRad),
                Ema = Fix64.FromDouble(config.Telemetry.Ema),
            },
        };
    }

    private static TOut[] OrderedById<TIn, TOut>(Dictionary<string, TIn> table, Func<string, TIn, TOut> project)
    {
        var ids = new string[table.Count];
        table.Keys.CopyTo(ids, 0);
        Array.Sort(ids, StringComparer.Ordinal);
        var result = new TOut[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            result[i] = project(ids[i], table[ids[i]]);
        }

        return result;
    }

    /// <summary>结构模板表装载:模板 area id 解析为 navmesh areas 下标。未知区域在装载即报错。表按 id 排序。</summary>
    private static RuntimeStructureTemplate[] BuildStructureTemplates(
        CrowdSimulationConfig config,
        IReadOnlyDictionary<string, int> areaIndex)
    {
        var entries = config.Structures.Templates;
        if (entries == null || entries.Count == 0) return Array.Empty<RuntimeStructureTemplate>();
        return OrderedById(entries, (id, t) =>
        {
            int? area = null;
            if (t.Area != null)
            {
                if (!areaIndex.TryGetValue(t.Area, out int index))
                {
                    throw new InvalidOperationException(
                        $"{CrowdSimulationConfigValidator.FileName}: structures.templates.{id}.area 未知导航区域 \"{t.Area}\"");
                }

                area = index;
            }

            return new RuntimeStructureTemplate
            {
                Id = id,
                Name = t.Name ?? id,
                Footprint = t.Footprint,
                Blocker = t.Blocker,
                AreaIndex = area,
                Priority = t.Priority ?? 0,
                LifetimeSec = t.LifetimeSec is { } sec ? Fix64.FromDouble(sec) : null,
                LifetimeSecRaw = t.LifetimeSec,
                Layered = t.Layered,
            };
        });
    }

    private static NavigationBinding BindNavigation(
        CrowdSimulationConfig config,
        MapConfig map,
        AgentProfileRegistry profiles,
        NavMeshBakeConfig navmesh,
        PathingConfig pathing,
        IReadOnlyList<CrowdUnitTemplateFacts> templates)
    {
        if (navmesh.Maps == null || !navmesh.Maps.TryGetValue(map.Id, out var mapNav) || mapNav == null)
        {
            throw new InvalidOperationException(
                $"Navigation/navmesh.json: maps.{map.Id} 缺失。群体模拟从这里读地形区域和 .navsurface。");
        }

        if (mapNav.TerrainAreas == null || mapNav.TerrainAreas.Count == 0)
        {
            throw new InvalidOperationException(
                $"Navigation/navmesh.json: maps.{map.Id}.terrainAreas 不能为空。数组下标就是 .navsurface 地形编号。");
        }

        string surfaceAsset = RequireNavSurfaceAsset(map);

        if (navmesh.Areas == null || navmesh.Areas.Count == 0)
        {
            throw new InvalidOperationException("Navigation/navmesh.json: areas 不能为空。");
        }

        if (pathing.AgentTypes == null || pathing.AgentTypes.Count == 0)
        {
            throw new InvalidOperationException("Navigation/pathing.json: agentTypes 不能为空。");
        }

        if (templates.Count == 0)
        {
            throw new InvalidOperationException("Entities/templates.json: 没有带 CrowdSimulationAgent 的单位模板。");
        }

        var areaIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        int maxAreaId = -1;
        for (int i = 0; i < navmesh.Areas.Count; i++)
        {
            var area = navmesh.Areas[i] ?? throw new InvalidOperationException($"Navigation/navmesh.json: areas[{i}] 为空。");
            if (area.AreaId > maxAreaId)
            {
                maxAreaId = area.AreaId;
            }
        }

        if (maxAreaId < 0)
        {
            throw new InvalidOperationException("Navigation/navmesh.json: areas 不能为空。");
        }

        var navAreas = new RuntimeNavArea[maxAreaId + 1];
        for (int i = 0; i < navmesh.Areas.Count; i++)
        {
            var area = navmesh.Areas[i];
            if (string.IsNullOrWhiteSpace(area.Id) || !areaIndexById.TryAdd(area.Id, area.AreaId))
            {
                throw new InvalidOperationException($"Navigation/navmesh.json: areas id \"{area.Id}\" 为空或重复。");
            }

            if (navAreas[area.AreaId] != null)
            {
                throw new InvalidOperationException($"Navigation/navmesh.json: areas areaId {area.AreaId} 重复。");
            }

            navAreas[area.AreaId] = new RuntimeNavArea { Id = area.Id, SlopeFree = area.SlopeFree };
        }

        var terrainIds = new string[mapNav.TerrainAreas.Count];
        var terrainToArea = new int[mapNav.TerrainAreas.Count];
        for (int i = 0; i < mapNav.TerrainAreas.Count; i++)
        {
            NavTerrainAreaBinding binding = mapNav.TerrainAreas[i]
                ?? throw new InvalidOperationException($"Navigation/navmesh.json: maps.{map.Id}.terrainAreas[{i}] 为空。");
            if (string.IsNullOrWhiteSpace(binding.TerrainId) ||
                string.IsNullOrWhiteSpace(binding.AreaId) ||
                !areaIndexById.TryGetValue(binding.AreaId, out int areaIndex))
            {
                throw new InvalidOperationException(
                    $"Navigation/navmesh.json: maps.{map.Id}.terrainAreas[{i}]（{binding.TerrainId}）需映射到已定义的导航区域（当前 \"{binding.AreaId}\"）。");
            }

            terrainIds[i] = binding.TerrainId;
            terrainToArea[i] = areaIndex;
        }

        var profileById = new Dictionary<string, AgentProfileConfig>(StringComparer.Ordinal);
        for (int i = 0; i < profiles.Count; i++)
        {
            profileById[profiles[i].Id] = profiles[i];
        }

        var navProfileIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < navmesh.Profiles.Count; i++)
        {
            var cap = navmesh.Profiles[i] ?? throw new InvalidOperationException($"Navigation/navmesh.json: profiles[{i}] 为空。");
            if (!navProfileIndexById.TryAdd(cap.Id, i))
            {
                throw new InvalidOperationException($"Navigation/navmesh.json: profiles id \"{cap.Id}\" 重复。");
            }
        }

        var pathingById = new Dictionary<string, PathingAgentTypeConfig>(StringComparer.Ordinal);
        for (int i = 0; i < pathing.AgentTypes.Count; i++)
        {
            var agent = pathing.AgentTypes[i] ?? throw new InvalidOperationException($"Navigation/pathing.json: agentTypes[{i}] 为空。");
            if (string.IsNullOrWhiteSpace(agent.Id) || !pathingById.TryAdd(agent.Id, agent))
            {
                throw new InvalidOperationException($"Navigation/pathing.json: agentTypes id \"{agent.Id}\" 为空或重复。");
            }
        }

        var seenTemplates = new HashSet<string>(StringComparer.Ordinal);
        var referencedAgentIds = new HashSet<string>(StringComparer.Ordinal);
        var referencedProfileIds = new List<string>();
        foreach (var template in templates)
        {
            if (!seenTemplates.Add(template.TemplateId))
            {
                throw new InvalidOperationException($"Entities/templates.json: 模板 \"{template.TemplateId}\" 重复。");
            }

            if (!pathingById.ContainsKey(template.AgentTypeId))
            {
                throw new InvalidOperationException(
                    $"Entities/templates.json: 模板 \"{template.TemplateId}\" 的 agentTypeId \"{template.AgentTypeId}\" 不在 Navigation/pathing.json 的 agentTypes 里。");
            }

            if (!profileById.ContainsKey(template.ProfileId))
            {
                throw new InvalidOperationException(
                    $"Entities/templates.json: 模板 \"{template.TemplateId}\" 的 profileId \"{template.ProfileId}\" 不在 Navigation/agent_profiles.json。");
            }

            referencedAgentIds.Add(template.AgentTypeId);
            referencedProfileIds.Add(template.ProfileId);
        }

        var selectedAgents = new List<PathingAgentTypeConfig>();
        var membersByAgent = new List<List<CrowdUnitTemplateFacts>>();
        var agentIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < pathing.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig agent = pathing.AgentTypes[i];
            if (!referencedAgentIds.Contains(agent.Id))
            {
                continue;
            }

            agentIndexById.Add(agent.Id, selectedAgents.Count);
            selectedAgents.Add(agent);
            membersByAgent.Add(new List<CrowdUnitTemplateFacts>());
            referencedProfileIds.Add(agent.ProfileId);
        }

        foreach (var template in templates)
        {
            membersByAgent[agentIndexById[template.AgentTypeId]].Add(template);
        }

        NavMapAgentBinding mapBinding = NavMapAgentBinding.Create(
            map.Id,
            navTagged: false,
            navmesh,
            pathing,
            profiles,
            referencedAgentIds);
        for (int i = 0; i < mapBinding.BoundAgents.Count; i++)
        {
            referencedProfileIds.Add(mapBinding.BoundAgents[i].ProfileId);
            PathingAreaCosts.RequireComplete(mapBinding.BoundAgents[i], mapBinding.NavAreas);
        }

        NavMeshBakeConfigLoader.ValidateReferencedAgentNavProfiles(navmesh, profiles, referencedProfileIds);
        if (config.Structures?.Templates != null)
        {
            foreach (var entry in config.Structures.Templates)
            {
                string area = entry.Value?.Area;
                if (string.IsNullOrEmpty(area) || !areaIndexById.ContainsKey(area) || mapBinding.ContainsNavArea(area))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: structures.templates.{entry.Key}.area \"{area}\" 不在 maps.{map.Id}.navAreas 里。");
            }
        }

        var avoidance = Fix64.FromDouble(config.Agents.AvoidanceRadiusScale);
        var runtimeProfiles = new List<RuntimeAgentProfile>(templates.Count);
        var runtimeAgentTypes = new List<RuntimeAgentType>(selectedAgents.Count);
        var unitTypes = new List<RuntimeUnitType>(selectedAgents.Count);
        var runtimeNavProfiles = new List<RuntimeNavProfile>();
        var runtimeNavIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        int navCellSizeCm = 0;

        for (int agentIndex = 0; agentIndex < selectedAgents.Count; agentIndex++)
        {
            var agent = selectedAgents[agentIndex];
            var members = membersByAgent[agentIndex];
            if (members.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Navigation/pathing.json: agentTypes \"{agent.Id}\" 没有 CrowdSimulationAgent 模板。");
            }

            if (!profileById.TryGetValue(agent.ProfileId, out var representative))
            {
                throw new InvalidOperationException(
                    $"Navigation/pathing.json: agentTypes[{agent.Id}].profileId \"{agent.ProfileId}\" 不在 Navigation/agent_profiles.json。");
            }

            if (!navProfileIndexById.TryGetValue(representative.NavProfileId, out int capFileIndex))
            {
                throw new InvalidOperationException(
                    $"Navigation/agent_profiles.json: \"{representative.Id}\" 的 navProfileId \"{representative.NavProfileId}\" 不在 Navigation/navmesh.json 的 profiles 里。");
            }

            var capability = navmesh.Profiles[capFileIndex];
            if (capability.CellSizeCm <= 0)
            {
                throw new InvalidOperationException(
                    $"Navigation/navmesh.json: profiles.{capability.Id}.cellSizeCm 需为 > 0。群体模拟用它做导航格边长，本会话的能力档必须相同。");
            }

            if (navCellSizeCm == 0)
            {
                navCellSizeCm = capability.CellSizeCm;
            }
            else if (navCellSizeCm != capability.CellSizeCm)
            {
                throw new InvalidOperationException(
                    $"Navigation/navmesh.json: profiles.{capability.Id}.cellSizeCm = {capability.CellSizeCm}，与本会话已采用的 {navCellSizeCm} 不一致。");
            }

            if (!runtimeNavIndex.TryGetValue(capability.Id, out int navProfileIndex))
            {
                navProfileIndex = runtimeNavProfiles.Count;
                runtimeNavIndex.Add(capability.Id, navProfileIndex);
                runtimeNavProfiles.Add(ToRuntimeProfile(capability));
            }

            var templatesByRadius = new Dictionary<int, string>();
            foreach (var member in members)
            {
                var body = profileById[member.ProfileId];
                if (!string.Equals(body.NavProfileId, capability.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Entities/templates.json: 模板 \"{member.TemplateId}\" 的档案 \"{body.Id}\" 指向能力档 \"{body.NavProfileId}\"，与移动类型 \"{agent.Id}\" 的 \"{capability.Id}\" 不一致。");
                }

                var radiusCm = Fix64.FromDouble(body.RadiusCm);
                int radiusKey = radiusCm.ToInt();
                if (!templatesByRadius.TryAdd(radiusKey, member.TemplateId))
                {
                    throw new InvalidOperationException(
                        $"Entities/templates.json: 移动类型 \"{agent.Id}\" 的半径 {radiusKey} 有多份模板。");
                }

                var expectedPersonal = CrowdSimulationSpace.PersonalRadiusCm(radiusCm, avoidance);
                if (member.PersonalRadiusCm != expectedPersonal)
                {
                    throw new InvalidOperationException(
                        $"Entities/templates.json: 模板 \"{member.TemplateId}\" 的 personalRadiusCm 与半径 × agents.avoidanceRadiusScale 不一致。");
                }

                int clearance = CrowdSimulationSpace.ClearanceOf(radiusCm, avoidance, navCellSizeCm);
                if (clearance > 255)
                {
                    throw new InvalidOperationException(
                        $"Navigation/agent_profiles.json: AgentProfile \"{body.Id}\" 换算的净空 {clearance} 超过 255 格");
                }

                runtimeProfiles.Add(new RuntimeAgentProfile
                {
                    Id = body.Id,
                    RadiusCm = radiusCm,
                    PersonalRadiusCm = member.PersonalRadiusCm,
                    PushPriority = member.PushPriority,
                    SpeedCmPerSecond = member.SpeedCmPerSecond,
                    Special = member.Special,
                    AgentTypeIndex = agentIndex,
                    ClearanceNavCells = clearance,
                    NavContextId = CrowdSimulationSpace.NavContextId(agentIndex, clearance),
                });
            }

            var costByArea = new Fix64[navAreas.Length];
            var speedByArea = new Fix64[navAreas.Length];
            for (int i = 0; i < navAreas.Length; i++)
            {
                costByArea[i] = Fix64.OneValue;
                speedByArea[i] = Fix64.OneValue;
            }

            var seenAreaCosts = new HashSet<int>();
            var areaCosts = agent.NavMesh?.AreaCosts;
            if (areaCosts != null)
            {
                for (int i = 0; i < areaCosts.Count; i++)
                {
                    var entry = areaCosts[i] ?? throw new InvalidOperationException(
                        $"Navigation/pathing.json: agentTypes[{agent.Id}].navMesh.areaCosts[{i}] 为空。");
                    if (!seenAreaCosts.Add(entry.AreaId))
                    {
                        throw new InvalidOperationException(
                            $"Navigation/pathing.json: agentTypes[{agent.Id}].navMesh.areaCosts areaId {entry.AreaId} 重复。");
                    }

                    if (!areaIndexById.ContainsValue(entry.AreaId) || (uint)entry.AreaId >= (uint)navAreas.Length || navAreas[entry.AreaId] == null)
                    {
                        throw new InvalidOperationException(
                            $"Navigation/pathing.json: agentTypes.{agent.Id}.areaCosts areaId {entry.AreaId} 未知导航区域");
                    }

                    ResolvedPathingAreaCost resolved = PathingAreaCosts.Resolve(entry);
                    int idx = entry.AreaId;
                    if (!resolved.Passable)
                    {
                        costByArea[idx] = Fix64.Zero;
                        speedByArea[idx] = Fix64.Zero;
                        continue;
                    }

                    costByArea[idx] = Fix64.FromDouble(resolved.Cost);
                    speedByArea[idx] = Fix64.FromDouble(resolved.SpeedDivisor);
                }
            }

            if (capability.Jump != null)
            {
                double minCost = double.PositiveInfinity;
                for (int i = 0; i < costByArea.Length; i++)
                {
                    if (costByArea[i] > Fix64.Zero)
                    {
                        minCost = Math.Min(minCost, costByArea[i].ToDouble());
                    }
                }

                if (minCost < double.PositiveInfinity && capability.Jump.Cost < minCost)
                {
                    throw new InvalidOperationException(
                        $"Navigation/navmesh.json: profiles.{capability.Id}.jump.cost 不能低于该类型最低区域代价（寻路启发式需可采纳）");
                }
            }

            runtimeAgentTypes.Add(new RuntimeAgentType
            {
                Id = agent.Id,
                AgentTypeIndex = agentIndex,
                NavProfileIndex = navProfileIndex,
                CostByArea = costByArea,
                SpeedDivisorByArea = speedByArea,
            });
            unitTypes.Add(new RuntimeUnitType
            {
                Id = agent.Id,
                AgentTypeIndex = agentIndex,
                TemplatesByRadiusCm = templatesByRadius,
            });
        }

        if (runtimeProfiles.Count > 255)
        {
            throw new InvalidOperationException(
                $"Navigation/agent_profiles.json: 移动类型 × 半径级组合 {runtimeProfiles.Count} 超过 255 上限");
        }

        var distinctRadiusSet = new SortedSet<int>();
        foreach (var profile in runtimeProfiles)
        {
            distinctRadiusSet.Add(profile.RadiusCm.ToInt());
        }

        var distinctRadiusCm = new int[distinctRadiusSet.Count];
        distinctRadiusSet.CopyTo(distinctRadiusCm);

        return new NavigationBinding(
            navCellSizeCm,
            surfaceAsset,
            terrainIds,
            terrainToArea,
            navAreas,
            runtimeNavProfiles,
            runtimeAgentTypes,
            unitTypes,
            runtimeProfiles,
            Array.AsReadOnly(distinctRadiusCm),
            avoidance,
            areaIndexById);
    }

    private static RuntimeNavProfile ToRuntimeProfile(NavMeshAgentProfileConfig capability)
    {
        Fix64? slope = capability.MaxSlopeDeg is float deg ? Fix64.FromDouble(deg) : null;
        RuntimeJumpCapability? jump = null;
        if (capability.Jump != null)
        {
            var authored = capability.Jump;
            if (authored.DownCm < authored.UpCm || authored.RangeNavCells <= 0 || authored.Cost <= 0f)
            {
                throw new InvalidOperationException(
                    $"Navigation/navmesh.json: profiles.{capability.Id}.jump 参数无效（downCm ≥ upCm，rangeNavCells / cost > 0）。");
            }

            jump = new RuntimeJumpCapability
            {
                UpCm = authored.UpCm,
                DownCm = authored.DownCm,
                RangeNavCells = authored.RangeNavCells,
                Cost = Fix64.FromDouble(authored.Cost),
            };
        }

        return new RuntimeNavProfile
        {
            Id = capability.Id,
            MaxSlopeDeg = slope,
            Jump = jump,
        };
    }

    private static string RequireNavSurfaceAsset(MapConfig map)
    {
        string asset = map.ContinuousHeightmap?.Asset;
        if (string.IsNullOrWhiteSpace(asset))
        {
            throw new InvalidOperationException(
                $"地图 \"{map.Id}\" 缺少 ContinuousHeightmap.asset，无法推导 .navsurface。");
        }

        string relative = asset.Replace('\\', '/');
        const string assetsPrefix = "assets/";
        if (relative.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[assetsPrefix.Length..];
        }

        const string heightExtension = ".height";
        if (!relative.EndsWith(heightExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"地图 \"{map.Id}\" 的 ContinuousHeightmap.asset \"{asset}\" 须以 .height 结尾。.navsurface 与它同主干。");
        }

        return string.Concat(relative.AsSpan(0, relative.Length - heightExtension.Length), ".navsurface");
    }

    private sealed class NavigationBinding
    {
        public NavigationBinding(
            int navCellSizeCm,
            string surfaceAsset,
            IReadOnlyList<string> terrainTypeIds,
            IReadOnlyList<int> terrainToArea,
            IReadOnlyList<RuntimeNavArea> navAreas,
            IReadOnlyList<RuntimeNavProfile> navProfiles,
            IReadOnlyList<RuntimeAgentType> agentTypes,
            IReadOnlyList<RuntimeUnitType> unitTypes,
            IReadOnlyList<RuntimeAgentProfile> agentProfiles,
            IReadOnlyList<int> radiusClasses,
            Fix64 avoidanceRadiusScale,
            IReadOnlyDictionary<string, int> areaIndexById)
        {
            NavCellSizeCm = navCellSizeCm;
            SurfaceAsset = surfaceAsset;
            TerrainTypeIds = terrainTypeIds;
            TerrainToArea = terrainToArea;
            NavAreas = navAreas;
            NavProfiles = navProfiles;
            AgentTypes = agentTypes;
            UnitTypes = unitTypes;
            Profiles = agentProfiles;
            RadiusClasses = radiusClasses;
            AvoidanceRadiusScale = avoidanceRadiusScale;
            AreaIndexById = areaIndexById;
        }

        public int NavCellSizeCm { get; }
        public string SurfaceAsset { get; }
        public IReadOnlyList<string> TerrainTypeIds { get; }
        public IReadOnlyList<int> TerrainToArea { get; }
        public IReadOnlyList<RuntimeNavArea> NavAreas { get; }
        public IReadOnlyList<RuntimeNavProfile> NavProfiles { get; }
        public IReadOnlyList<RuntimeAgentType> AgentTypes { get; }
        public IReadOnlyList<RuntimeUnitType> UnitTypes { get; }
        public IReadOnlyList<RuntimeAgentProfile> Profiles { get; }
        public IReadOnlyList<int> RadiusClasses { get; }
        public Fix64 AvoidanceRadiusScale { get; }
        public IReadOnlyDictionary<string, int> AreaIndexById { get; }
    }

    /// <summary>
    /// 视盘格半径上限。视盘按偏移平铺,使用处只丢掉出图的格子,不把半径夹到 F。
    /// |dx|≥F 或 |dy|≥F 加到任何格心都出图;半径小于对角 (F-1)*√2 时角上的单位仍看不见对角格,超过 F 仍会改变可见集。
    /// 可见集在半径达到对角之后不再变,上限取满足 k² ≥ 2(F-1)² 的最小 k(F=1 时只有本格,上限为 1)。
    /// F≤1024 时该值≤1447,循环角点的 dx*dx+dy*dy 仍小于 2^31。
    /// </summary>
    private static int MaxFogVisionRadiusCells(int fogEdge)
    {
        if (fogEdge <= 1) return 1;
        int n = fogEdge - 1;
        long need = 2L * n * n;
        int k = n + 1;
        while ((long)k * k < need) k++;
        return k;
    }
}
