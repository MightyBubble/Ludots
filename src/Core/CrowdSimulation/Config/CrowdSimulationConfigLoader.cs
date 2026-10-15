using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using static Ludots.Core.CrowdSimulation.Config.CrowdSimulationConfig;

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
        CrowdSimulationConfig config = ConfigMergeProvenance.Wrap(RelativePath, report, tracked =>
        {
            JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, tracked);
            if (merged == null)
            {
                throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 没有任何来源提供该文件（标准参数在 CrowdSimulationStandardMod，地图事实在地图 Mod）。");
            }

            return CrowdSimulationConfig.Load(merged);
        });
        return config;
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
    /// <param name="map">地图（Boards / Teams / Players / Entities）。聚焦的地图 id 就是会话地图。求解器板取 navmesh 里声明的那一块，没声明则取 RootBoard。</param>
    /// <param name="profiles">Navigation/agent_profiles.json 注册表。运行时体型行只来自带 CrowdSimulationAgent 的模板。</param>
    /// <param name="navmesh">Navigation/navmesh.json。只读 maps.&lt;mapId&gt;：求解器、板、导航格边长、地形顺序。</param>
    /// <param name="traversal">CrowdSimulation/traversal.json。区域、地形到区域、链接种类、通行矩阵。</param>
    /// <param name="templates">带 CrowdSimulationAgent 的单位模板。</param>
    /// <param name="fixedHz">Engine/clock.json 的 FixedHz——仿真频率的唯一来源。</param>
    /// <param name="pairRules">已经投影好的玩家对矩阵。装载只按下标挂到地图玩家上，不解释关系类型。</param>
    public static CrowdSimulationRuntimeConfig Load(
        CrowdSimulationConfig config,
        MapConfig map,
        AgentProfileRegistry profiles,
        NavMeshBakeConfig navmesh,
        CrowdTraversalConfig traversal,
        IReadOnlyList<CrowdUnitTemplateFacts> templates,
        int fixedHz,
        CrowdPairRules pairRules)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(navmesh);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(pairRules);
        if (fixedHz < 1)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fixedHz = {fixedHz}，需为 ≥ 1 的整数（步进频率只由 Engine/clock.json 规定）。");
        }

        CrowdSimulationConfigValidator.Validate(config);
        string mapFile = $"Maps/{map.Id}.json";
        NavMapNavBoardsConfig mapNav = NavSolverDeclaration.RequireMap(navmesh, map.Id);
        if (!string.Equals(mapNav.Solver, NavSolverDeclaration.CrowdSimulation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"地图 \"{map.Id}\" 的 solver 是 \"{mapNav.Solver}\"，群体会话只接受 {NavSolverDeclaration.CrowdSimulation}。");
        }

        var bound = BindNavigation(config, map, profiles, mapNav, traversal, templates);

        BoardConfig board = NavSolverDeclaration.RequireBoard(map, mapNav);
        int navCellSizeCm = bound.NavCellSizeCm;

        // 迷雾格长由作者写成厘米,必须整除导航格;派生倍数是栅格算法量,范围仍是 1..32。
        int fogCellSizeCm = config.Fog.CellSizeCm;
        if (fogCellSizeCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fog.cellSizeCm（{fogCellSizeCm}）必须能被 maps.{map.Id}.navCellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellsPerFogCell = fogCellSizeCm / navCellSizeCm;
        if (navCellsPerFogCell < 1 || navCellsPerFogCell > 32)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fog.cellSizeCm（{fogCellSizeCm}）/ maps.{map.Id}.navCellSizeCm（{navCellSizeCm}）= {navCellsPerFogCell}，需为 ≥ 1、≤ 32 的整数");
        }

        if (board.WidthCm != board.HeightCm)
        {
            throw new InvalidOperationException($"{mapFile}: 求解器板 \"{board.Name}\" 必须是正方形（WidthCm {board.WidthCm} ≠ HeightCm {board.HeightCm}）。");
        }

        if (board.Anchor == null)
        {
            throw new InvalidOperationException(
                $"{mapFile}: 求解器板 \"{board.Name}\" 的 Anchor 必须给出，且 LocalXCm、LocalYCm、WorldXCm、WorldYCm 均为 0。");
        }

        if (board.Anchor.LocalXCm != 0 || board.Anchor.LocalYCm != 0
            || board.Anchor.WorldXCm != 0 || board.Anchor.WorldYCm != 0)
        {
            throw new InvalidOperationException(
                $"{mapFile}: 求解器板 \"{board.Name}\" 的 Anchor 的 LocalXCm、LocalYCm、WorldXCm、WorldYCm 必须为 0（当前 {board.Anchor.LocalXCm}, {board.Anchor.LocalYCm}, {board.Anchor.WorldXCm}, {board.Anchor.WorldYCm}）。");
        }

        if (board.WidthCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException($"{mapFile}: 求解器板 \"{board.Name}\" 的 WidthCm（{board.WidthCm}）必须能被 maps.{map.Id}.navCellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellCount = board.WidthCm / navCellSizeCm;
        if (navCellCount < 16 || navCellCount > 1024)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 每边导航格数 = {navCellCount}（求解器板 \"{board.Name}\" 的 WidthCm / maps.{map.Id}.navCellSizeCm），需为 ≥ 16、≤ 1024 的整数");
        }

        int navTileSizeCm = config.Navtile.SizeCm;
        if (navTileSizeCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: navtile.sizeCm（{navTileSizeCm}）必须能被 maps.{map.Id}.navCellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellsPerTile = navTileSizeCm / navCellSizeCm;
        if (navCellsPerTile < 4 || navCellsPerTile > 128)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: navtile.sizeCm（{navTileSizeCm}）/ maps.{map.Id}.navCellSizeCm（{navCellSizeCm}）= {navCellsPerTile}，需为 ≥ 4、≤ 128 的整数");
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

        int coloredBases = 0;
        int blankColorIndex = -1;
        for (int i = 0; i < config.Deploy.Bases.Length; i++)
        {
            var basis = config.Deploy.Bases[i];
            if (!playerIds.Contains(basis.PlayerId))
            {
                throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: deploy.bases[].playerId = {basis.PlayerId}，必须存在于地图 Players[]");
            }

            if (string.IsNullOrWhiteSpace(basis.Color))
            {
                if (blankColorIndex < 0) blankColorIndex = i;
            }
            else
            {
                coloredBases++;
            }
        }

        if (coloredBases > 0 && blankColorIndex >= 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: deploy.bases[{blankColorIndex}].color 未填。基地颜色必须全部填写，或全部不填。");
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
            LinkKinds = bound.LinkKinds,
            LinkCandidateBakes = bound.LinkCandidateBakes,
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

    /// <summary>结构模板表装载：区域 id 解析为 traversal 区域下标，未知区域在装载即报错。表按 id 排序。</summary>
    private static RuntimeStructureTemplate[] BuildStructureTemplates(
        CrowdSimulationConfig config,
        IReadOnlyDictionary<string, int> areaIndex)
    {
        var entries = config.Structures.Templates;
        if (entries.Count == 0) return Array.Empty<RuntimeStructureTemplate>();
        return OrderedById(entries, (id, t) =>
        {
            string path = $"structures.templates.{id}";
            Fix64 coverage = Fix64.Zero;
            int? area = null;
            int priority = 0;
            int portalReach = 0;
            CrowdStructureEffectKind effect;
            switch (t.Effect)
            {
                case StructureBlockEffect block:
                    effect = CrowdStructureEffectKind.Block;
                    coverage = Fix64.FromDouble(block.Coverage);
                    break;
                case StructureSetAreaEffect setArea:
                    effect = CrowdStructureEffectKind.SetArea;
                    area = ResolveArea(areaIndex, setArea.Area, $"{path}.effect.area");
                    priority = setArea.Priority;
                    break;
                case StructureSurfaceEffect surface:
                    effect = CrowdStructureEffectKind.Surface;
                    area = ResolveArea(areaIndex, surface.Area, $"{path}.effect.area");
                    portalReach = surface.PortalReachNavCells;
                    break;
                default:
                    throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: {path}.effect 类型未知。");
            }

            bool[] endpointAreas = Array.Empty<bool>();
            bool[] interiorAreas = Array.Empty<bool>();
            var placement = CrowdStructurePlacementKind.Unrestricted;
            if (t.Placement is StructureSpanEndsPlacement spanEnds)
            {
                placement = CrowdStructurePlacementKind.SpanEnds;
                endpointAreas = ResolveAreaMask(areaIndex, spanEnds.EndpointAreas, $"{path}.placement.endpointAreas");
                interiorAreas = ResolveAreaMask(areaIndex, spanEnds.InteriorAreas, $"{path}.placement.interiorAreas");
            }

            return new RuntimeStructureTemplate
            {
                Id = id,
                Layer = t.Layer == "upper" ? CrowdStructureLayer.Upper : CrowdStructureLayer.Base,
                Footprint = t.Footprint switch
                {
                    "square" => CrowdStructureShape.Square,
                    "disc" => CrowdStructureShape.Disc,
                    _ => CrowdStructureShape.Path,
                },
                Effect = effect,
                BlockCoverage = coverage,
                AreaIndex = area,
                Priority = priority,
                PortalReachNavCells = portalReach,
                Placement = placement,
                EndpointAreas = endpointAreas,
                InteriorAreas = interiorAreas,
                LifetimeSec = t.LifetimeSec is { } sec ? Fix64.FromDouble(sec) : null,
                LifetimeSecRaw = t.LifetimeSec,
            };
        });
    }

    private static int ResolveArea(IReadOnlyDictionary<string, int> areaIndex, string areaId, string path)
    {
        if (!areaIndex.TryGetValue(areaId, out int index))
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: {path} 未知导航区域 \"{areaId}\"（区域在 {CrowdTraversalConfig.RelativePath} 的 areas）。");
        }

        return index;
    }

    private static bool[] ResolveAreaMask(IReadOnlyDictionary<string, int> areaIndex, string[] areaIds, string path)
    {
        var mask = new bool[areaIndex.Count];
        for (int i = 0; i < areaIds.Length; i++)
        {
            mask[ResolveArea(areaIndex, areaIds[i], $"{path}[{i}]")] = true;
        }

        return mask;
    }

    private static NavigationBinding BindNavigation(
        CrowdSimulationConfig config,
        MapConfig map,
        AgentProfileRegistry profiles,
        NavMapNavBoardsConfig mapNav,
        CrowdTraversalConfig traversal,
        IReadOnlyList<CrowdUnitTemplateFacts> templates)
    {
        string mapPath = $"Navigation/navmesh.json: maps.{map.Id}";
        if (mapNav.TerrainTypes == null || mapNav.TerrainTypes.Count == 0)
        {
            throw new InvalidOperationException($"{mapPath}.terrainTypes 不能为空。数组下标就是 .navsurface 地形编号。");
        }

        if (mapNav.NavCellSizeCm is not int navCellSizeCm || navCellSizeCm <= 0)
        {
            throw new InvalidOperationException($"{mapPath}.navCellSizeCm 需为 > 0 的整数。");
        }

        if (traversal.LinkKinds.Count > 1)
        {
            throw new InvalidOperationException(
                $"{CrowdTraversalConfig.RelativePath}: linkKinds 有 {traversal.LinkKinds.Count} 种，群体内核目前只消费一种。");
        }

        string surfaceAsset = RequireNavSurfaceAsset(map);
        if (templates.Count == 0)
        {
            throw new InvalidOperationException("Entities/templates.json: 没有带 CrowdSimulationAgent 的单位模板。");
        }

        int areaCount = traversal.Areas.Count;
        var navAreas = new RuntimeNavArea[areaCount];
        var areaIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < areaCount; i++)
        {
            navAreas[i] = new RuntimeNavArea { Id = traversal.Areas[i].Id, IgnoresSlope = traversal.Areas[i].IgnoresSlope };
            areaIndexById.Add(traversal.Areas[i].Id, i);
        }

        var terrainIds = new string[mapNav.TerrainTypes.Count];
        var terrainToArea = new int[mapNav.TerrainTypes.Count];
        for (int i = 0; i < terrainIds.Length; i++)
        {
            string terrainId = mapNav.TerrainTypes[i];
            if (!traversal.TerrainAreas.TryGetValue(terrainId, out int areaIndex))
            {
                throw new InvalidOperationException(
                    $"{mapPath}.terrainTypes[{i}] = \"{terrainId}\" 不在 {CrowdTraversalConfig.RelativePath} 的 terrainAreas 里。");
            }

            terrainIds[i] = terrainId;
            terrainToArea[i] = areaIndex;
        }

        ValidateUpperLayerAreas(config, traversal, areaIndexById);
        RuntimeLinkCandidateBake[] linkCandidateBakes = DeriveLinkCandidateBakes(mapPath, mapNav, traversal, terrainToArea);

        var profileById = new Dictionary<string, AgentProfileConfig>(StringComparer.Ordinal);
        for (int i = 0; i < profiles.Count; i++)
        {
            profileById[profiles[i].Id] = profiles[i];
        }

        var seenTemplates = new HashSet<string>(StringComparer.Ordinal);
        var referencedAgentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            if (!seenTemplates.Add(template.TemplateId))
            {
                throw new InvalidOperationException($"Entities/templates.json: 模板 \"{template.TemplateId}\" 重复。");
            }

            if (!traversal.TryGetRow(template.AgentTypeId, out _))
            {
                throw new InvalidOperationException(
                    $"Entities/templates.json: 模板 \"{template.TemplateId}\" 的 agentTypeId \"{template.AgentTypeId}\" 不在 {CrowdTraversalConfig.RelativePath} 的 agentTypes 里。");
            }

            if (!profileById.TryGetValue(template.ProfileId, out var body))
            {
                throw new InvalidOperationException(
                    $"Entities/templates.json: 模板 \"{template.TemplateId}\" 的 profileId \"{template.ProfileId}\" 不在 Navigation/agent_profiles.json。");
            }

            if (!string.Equals(body.NavProfileId, template.AgentTypeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Entities/templates.json: 模板 \"{template.TemplateId}\" 的档案 \"{body.Id}\" 的 navProfileId 是 \"{body.NavProfileId}\"，群体要求它等于移动类型 \"{template.AgentTypeId}\"。");
            }

            referencedAgentIds.Add(template.AgentTypeId);
        }

        var selectedRows = new List<TraversalRow>();
        var membersByAgent = new List<List<CrowdUnitTemplateFacts>>();
        var agentIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TraversalRow row in traversal.AgentTypes)
        {
            if (!referencedAgentIds.Contains(row.Id))
            {
                continue;
            }

            agentIndexById.Add(row.Id, selectedRows.Count);
            selectedRows.Add(row);
            membersByAgent.Add(new List<CrowdUnitTemplateFacts>());
        }

        foreach (var template in templates)
        {
            membersByAgent[agentIndexById[template.AgentTypeId]].Add(template);
        }

        var avoidance = Fix64.FromDouble(config.Agents.AvoidanceRadiusScale);
        var runtimeProfiles = new List<RuntimeAgentProfile>(templates.Count);
        var runtimeAgentTypes = new List<RuntimeAgentType>(selectedRows.Count);
        var unitTypes = new List<RuntimeUnitType>(selectedRows.Count);
        var runtimeNavProfiles = new List<RuntimeNavProfile>(selectedRows.Count);

        for (int agentIndex = 0; agentIndex < selectedRows.Count; agentIndex++)
        {
            TraversalRow row = selectedRows[agentIndex];
            var members = membersByAgent[agentIndex];
            runtimeNavProfiles.Add(ToRuntimeNavProfile(row));

            var templatesByRadius = new Dictionary<int, string>();
            foreach (var member in members)
            {
                var body = profileById[member.ProfileId];
                var radiusCm = Fix64.FromDouble(body.RadiusCm);
                int radiusKey = radiusCm.ToInt();
                if (!templatesByRadius.TryAdd(radiusKey, member.TemplateId))
                {
                    throw new InvalidOperationException(
                        $"Entities/templates.json: 移动类型 \"{row.Id}\" 的半径 {radiusKey} 有多份模板。");
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

            var costByArea = new Fix64[areaCount];
            var speedByArea = new Fix64[areaCount];
            for (int i = 0; i < areaCount; i++)
            {
                if (row.Cells[i] is { } cell)
                {
                    costByArea[i] = Fix64.FromDouble(cell.Cost);
                    speedByArea[i] = Fix64.FromDouble(cell.SpeedDivisor);
                }
            }

            runtimeAgentTypes.Add(new RuntimeAgentType
            {
                Id = row.Id,
                AgentTypeIndex = agentIndex,
                NavProfileIndex = agentIndex,
                CostByArea = costByArea,
                SpeedDivisorByArea = speedByArea,
            });
            unitTypes.Add(new RuntimeUnitType
            {
                Id = row.Id,
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

        var linkKinds = new RuntimeLinkKind[traversal.LinkKinds.Count];
        for (int k = 0; k < linkKinds.Length; k++)
        {
            linkKinds[k] = new RuntimeLinkKind
            {
                Id = traversal.LinkKinds[k].Id,
                TraversalSpeedRatio = Fix64.FromDouble(traversal.LinkKinds[k].TraversalSpeedRatio),
            };
        }

        return new NavigationBinding(
            navCellSizeCm,
            surfaceAsset,
            terrainIds,
            terrainToArea,
            navAreas,
            linkKinds,
            linkCandidateBakes,
            runtimeNavProfiles,
            runtimeAgentTypes,
            unitTypes,
            runtimeProfiles,
            Array.AsReadOnly(distinctRadiusCm),
            avoidance,
            areaIndexById);
    }

    /// <summary>
    /// 上层区域 = surface 效果引用的区域。上层格不在底层栅格里，所以它不能是链接端点、不能是地形对应的目标，
    /// 不能被 setArea 引用，也不能出现在 spanEnds 的区域表里。
    /// </summary>
    private static void ValidateUpperLayerAreas(
        CrowdSimulationConfig config,
        CrowdTraversalConfig traversal,
        IReadOnlyDictionary<string, int> areaIndexById)
    {
        var entries = config.Structures.Templates;
        var upper = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, t) in entries)
        {
            if (t.Effect is not StructureSurfaceEffect surface) continue;
            ResolveArea(areaIndexById, surface.Area, $"structures.templates.{id}.effect.area");
            upper.Add(surface.Area);
        }

        foreach (var (id, t) in entries)
        {
            if (t.Effect is StructureSetAreaEffect setArea && upper.Contains(setArea.Area))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: structures.templates.{id}.effect.area \"{setArea.Area}\" 是上层区域，只能被 surface 效果引用。");
            }

            if (t.Placement is StructureSpanEndsPlacement spanEnds)
            {
                RejectUpperIn(upper, spanEnds.EndpointAreas, $"structures.templates.{id}.placement.endpointAreas");
                RejectUpperIn(upper, spanEnds.InteriorAreas, $"structures.templates.{id}.placement.interiorAreas");
            }
        }

        foreach (var (terrainId, areaIndex) in traversal.TerrainAreas)
        {
            if (upper.Contains(traversal.Areas[areaIndex].Id))
            {
                throw new InvalidOperationException(
                    $"{CrowdTraversalConfig.RelativePath}: terrainAreas.{terrainId} = \"{traversal.Areas[areaIndex].Id}\" 是上层区域，不能做底层地形的区域。");
            }
        }

        foreach (TraversalRow row in traversal.AgentTypes)
        {
            foreach (string areaId in upper)
            {
                if (row.Cells[areaIndexById[areaId]] is not { } cell) continue;
                for (int k = 0; k < cell.Depart.Count; k++)
                {
                    if (cell.Depart[k] || cell.Arrive[k])
                    {
                        throw new InvalidOperationException(
                            $"{CrowdTraversalConfig.RelativePath}: agentTypes.{row.Id}.cells.{areaId} 是上层区域，depart / arrive 必须为空。");
                    }
                }
            }
        }
    }

    private static void RejectUpperIn(HashSet<string> upper, string[] areaIds, string path)
    {
        for (int i = 0; i < areaIds.Length; i++)
        {
            if (upper.Contains(areaIds[i]))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: {path}[{i}] = \"{areaIds[i]}\" 是上层区域，底层格的静态地形区域不会是它。");
            }
        }
    }

    private static RuntimeLinkCandidateBake[] DeriveLinkCandidateBakes(
        string mapPath,
        NavMapNavBoardsConfig mapNav,
        CrowdTraversalConfig traversal,
        int[] terrainToArea)
    {
        if (mapNav.LinkCandidateBake == null)
        {
            return [];
        }

        var kindIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TraversalLinkKind kind in traversal.LinkKinds)
        {
            kindIds.Add(kind.Id);
        }

        foreach (string kindId in mapNav.LinkCandidateBake.Keys)
        {
            if (!kindIds.Contains(kindId))
            {
                throw new InvalidOperationException(
                    $"{mapPath}.linkCandidateBake.{kindId} 不在 {CrowdTraversalConfig.RelativePath} 的 linkKinds 里。");
            }
        }

        int areaCount = traversal.Areas.Count;
        var bakes = new List<RuntimeLinkCandidateBake>(mapNav.LinkCandidateBake.Count);
        for (int k = 0; k < traversal.LinkKinds.Count; k++)
        {
            string kindId = traversal.LinkKinds[k].Id;
            if (!mapNav.LinkCandidateBake.TryGetValue(kindId, out NavLinkCandidateBakeConfig? geometry))
            {
                continue;
            }

            var endpointArea = new bool[areaCount];
            var hardGapArea = new bool[areaCount];
            Array.Fill(hardGapArea, true);
            bool anyRow = false;
            foreach (TraversalRow row in traversal.AgentTypes)
            {
                if (row.Links[k] == null) continue;
                anyRow = true;
                for (int a = 0; a < areaCount; a++)
                {
                    if (row.Cells[a] is not { } cell) continue;
                    hardGapArea[a] = false;
                    endpointArea[a] |= cell.Depart[k] || cell.Arrive[k];
                }
            }

            if (!anyRow)
            {
                throw new InvalidOperationException(
                    $"{mapPath}.linkCandidateBake.{kindId}：{CrowdTraversalConfig.RelativePath} 里没有任何移动类型有这种链接能力，候选不会被用到。");
            }

            var endpointTerrain = new bool[terrainToArea.Length];
            var hardGapTerrain = new bool[terrainToArea.Length];
            for (int t = 0; t < terrainToArea.Length; t++)
            {
                endpointTerrain[t] = endpointArea[terrainToArea[t]];
                hardGapTerrain[t] = hardGapArea[terrainToArea[t]];
            }

            bakes.Add(new RuntimeLinkCandidateBake
            {
                KindId = kindId,
                MaxGapCm = geometry.MaxGapCm,
                LinkSpacingCm = geometry.LinkSpacingCm,
                EndpointMaxSlopeDeg = geometry.EndpointMaxSlopeDeg,
                EndpointTerrain = endpointTerrain,
                HardGapTerrain = hardGapTerrain,
            });
        }

        return bakes.ToArray();
    }

    private static RuntimeNavProfile ToRuntimeNavProfile(TraversalRow row)
    {
        RuntimeLinkCapability? link = null;
        if (row.Links.Count > 0 && row.Links[0] is { } rowLink)
        {
            var departByArea = new bool[row.Cells.Count];
            var arriveByArea = new bool[row.Cells.Count];
            for (int a = 0; a < row.Cells.Count; a++)
            {
                if (row.Cells[a] is not { } cell) continue;
                departByArea[a] = cell.Depart[0];
                arriveByArea[a] = cell.Arrive[0];
            }

            link = new RuntimeLinkCapability
            {
                UpCm = rowLink.UpCm,
                DownCm = rowLink.DownCm,
                RangeNavCells = rowLink.RangeNavCells,
                Cost = Fix64.FromDouble(rowLink.CostPerNavCell),
                DepartByArea = departByArea,
                ArriveByArea = arriveByArea,
            };
        }

        return new RuntimeNavProfile
        {
            Id = row.Id,
            MaxSlopeDeg = row.MaxSlopeDeg is double deg ? Fix64.FromDouble(deg) : null,
            Link = link,
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
            IReadOnlyList<RuntimeLinkKind> linkKinds,
            IReadOnlyList<RuntimeLinkCandidateBake> linkCandidateBakes,
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
            LinkKinds = linkKinds;
            LinkCandidateBakes = linkCandidateBakes;
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
        public IReadOnlyList<RuntimeLinkKind> LinkKinds { get; }
        public IReadOnlyList<RuntimeLinkCandidateBake> LinkCandidateBakes { get; }
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
