using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// CrowdSimulation 配置装载入口。
/// 管线侧负责 config_catalog 合并（DeepObject）；<see cref="Load"/> 按
/// LU-20 的契约把合并结果、地图、代理体型与固定时钟组装成只读运行时配置——
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

    private const string MassNavigationRelativePath = "MassNavigationConfig.json";

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

        var config = CrowdSimulationConfig.Load(merged);

        // 一张地图只能启用一种求解器：两份配置指向同一 mapId 才算冲突，磁盘上并存默认值不算。
        if (catalog != null && catalog.TryGet(MassNavigationRelativePath, out var massEntry))
        {
            JsonObject? massMerged = _pipeline.MergeDeepObjectFromCatalog(in massEntry);
            EnsureSolverExclusive(massMerged?["mapId"]?.GetValue<string>(), config.MapId);
        }

        return config;
    }

    /// <summary>求解器互斥（LU-19）：两份配置 mapId 相同才冲突。</summary>
    public static void EnsureSolverExclusive(string? massNavigationMapId, string crowdSimulationMapId)
    {
        if (string.Equals(massNavigationMapId, crowdSimulationMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: 地图 \"{crowdSimulationMapId}\" 同时启用了 CrowdSimulation 与 MassNavigation 求解器；一张地图只能使用其中一个。");
        }
    }

    /// <summary>
    /// LU-20：校验并组装只读运行时配置。输入全部来自 Ludots 已加载的数据。
    /// </summary>
    /// <param name="config">catalog 合并后的 CrowdSimulationConfig.json。</param>
    /// <param name="map">地图（Boards / Teams / Players / Entities）。</param>
    /// <param name="profiles">Navigation/agent_profiles.json 注册表。</param>
    /// <param name="fixedHz">Engine/clock.json 的 FixedHz——仿真频率的唯一来源。</param>
    /// <param name="pairRules">已经投影好的玩家对矩阵。装载只按下标挂到地图玩家上，不解释关系类型。</param>
    public static CrowdSimulationRuntimeConfig Load(
        CrowdSimulationConfig config,
        MapConfig map,
        AgentProfileRegistry profiles,
        int fixedHz,
        CrowdPairRules pairRules)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(pairRules);
        if (fixedHz < 1)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fixedHz = {fixedHz}，需为 ≥ 1 的整数（步进频率只由 Engine/clock.json 规定）。");
        }

        CrowdSimulationConfigValidator.Validate(config);

        string mapFile = $"Maps/{config.MapId}.json";
        if (!string.Equals(map.Id, config.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: mapId = \"{config.MapId}\"，与当前地图 \"{map.Id}\" 不一致（CrowdSimulationConfig 按 mapId 绑定地图）。");
        }

        if (map.Boards.Count == 0)
        {
            throw new InvalidOperationException($"{mapFile}: Boards 不能为空（CrowdSimulation 世界需要一块正方形板）。");
        }

        var board = map.Boards[0];
        int navCellSizeCm = config.World.NavCellSizeCm;
        if (navCellSizeCm <= 0)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: world.navCellSizeCm = {navCellSizeCm}，需为 > 0 的整数");
        }

        // 迷雾格长由作者写成厘米,必须整除导航格;派生倍数是栅格算法量,范围仍是 1..32。
        int fogCellSizeCm = config.Fog.CellSizeCm;
        if (fogCellSizeCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fog.cellSizeCm（{fogCellSizeCm}）必须能被 world.navCellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellsPerFogCell = fogCellSizeCm / navCellSizeCm;
        if (navCellsPerFogCell < 1 || navCellsPerFogCell > 32)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: fog.cellSizeCm（{fogCellSizeCm}）/ world.navCellSizeCm（{navCellSizeCm}）= {navCellsPerFogCell}，需为 ≥ 1、≤ 32 的整数");
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
            throw new InvalidOperationException($"{mapFile}: Boards[0].WidthCm（{board.WidthCm}）必须能被 world.navCellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellCount = board.WidthCm / navCellSizeCm;
        if (navCellCount < 16 || navCellCount > 1024)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 每边导航格数 = {navCellCount}（Boards[0].WidthCm / world.navCellSizeCm），需为 ≥ 16、≤ 1024 的整数");
        }

        int navTileSizeCm = config.Navtile.SizeCm;
        if (navTileSizeCm % navCellSizeCm != 0)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: navtile.sizeCm（{navTileSizeCm}）必须能被 world.navCellSizeCm（{navCellSizeCm}）整除。");
        }

        int navCellsPerTile = navTileSizeCm / navCellSizeCm;
        if (navCellsPerTile < 4 || navCellsPerTile > 128)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: navtile.sizeCm（{navTileSizeCm}）/ world.navCellSizeCm（{navCellSizeCm}）= {navCellsPerTile}，需为 ≥ 4、≤ 128 的整数");
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

        var agentTypeByLayer = new Dictionary<int, int>();
        for (int i = 0; i < config.AgentTypes.Length; i++)
        {
            agentTypeByLayer.Add(config.AgentTypes[i].Layer, i);
        }

        var avoidanceRadiusScale = Fix64.FromDouble(config.Agents.AvoidanceRadiusScale);
        // 原型推挤优先级(参考 core/archetypes.js)= agentTypes[].pushPriority + 半径级份额(authored 数据,
        // 参考端 radiusClasses[].pushPriority);份额按半径厘米查表,每个派生半径级必须有条目。
        var radiusShareByRadiusCm = new Dictionary<int, double>();
        foreach (var (key, rc) in config.Agents.RadiusClasses)
        {
            if (!TryParseCanonicalRadiusCm(key, out int radiusCm))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: agents.radiusClasses 的键 \"{key}\" 必须是半径厘米的十进制整数。");
            }

            if (!radiusShareByRadiusCm.TryAdd(radiusCm, rc.PushPriority))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: agents.radiusClasses 半径 {radiusCm} 重复");
            }
        }

        var runtimeProfiles = new List<RuntimeAgentProfile>(profiles.Count);
        for (int i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            if (!agentTypeByLayer.TryGetValue(p.Layer, out int agentTypeIndex))
            {
                throw new InvalidOperationException(
                    $"Navigation/agent_profiles.json: AgentProfile \"{p.Id}\" 的 layer = {p.Layer} 找不到对应 agentTypes[].layer");
            }

            var radiusCm = Fix64.FromDouble(p.RadiusCm);
            int clearance = CrowdSimulationSpace.ClearanceOf(radiusCm, avoidanceRadiusScale, navCellSizeCm);
            if (clearance > 255)
            {
                throw new InvalidOperationException(
                    $"Navigation/agent_profiles.json: AgentProfile \"{p.Id}\" 换算的净空 {clearance} 超过 255 格");
            }

            int radiusKey = (int)radiusCm.ToInt();
            if (!radiusShareByRadiusCm.TryGetValue(radiusKey, out double radiusShare))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: agents.radiusClasses 缺半径 {radiusKey} 的条目(profile \"{p.Id}\")");
            }

            runtimeProfiles.Add(new RuntimeAgentProfile
            {
                Id = p.Id,
                RadiusCm = radiusCm,
                PersonalRadiusCm = CrowdSimulationSpace.PersonalRadiusCm(radiusCm, avoidanceRadiusScale),
                PushPriority = Fix64.FromDouble(config.AgentTypes[agentTypeIndex].PushPriority + radiusShare),
                AgentTypeIndex = agentTypeIndex,
                ClearanceNavCells = clearance,
                NavContextId = CrowdSimulationSpace.NavContextId(p.Layer, clearance),
            });
        }

        if (runtimeProfiles.Count > 255)
        {
            throw new InvalidOperationException(
                $"Navigation/agent_profiles.json: 移动类型 × 半径级组合 {runtimeProfiles.Count} 超过 255 上限");
        }

        // 单位模板 × 半径级组合数：每个单位模板 × 其移动类型下已有的体型数。
        int archetypes = 0;
        foreach (var u in config.UnitTypes)
        {
            int layer = config.AgentTypes[Array.FindIndex(config.AgentTypes, a => a.Id == u.AgentType)].Layer;
            int radiusClasses = 0;
            foreach (var rp in runtimeProfiles)
            {
                if (config.AgentTypes[rp.AgentTypeIndex].Layer == layer) radiusClasses++;
            }

            archetypes += radiusClasses;
        }

        if (archetypes > 255)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: unitTypes × 半径级组合 {archetypes} 超过 255 上限");
        }

        int AreaIndex(string id)
        {
            for (int i = 0; i < config.NavAreas.Length; i++)
            {
                if (config.NavAreas[i].Id == id) return i;
            }

            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 内部错误，导航区域 \"{id}\" 未登记");
        }

        var terrainToArea = new int[config.World.TerrainTypes.Length];
        for (int i = 0; i < terrainToArea.Length; i++)
        {
            terrainToArea[i] = AreaIndex(config.TerrainAreas[config.World.TerrainTypes[i].Id]);
        }

        var runtimeAgentTypes = new List<RuntimeAgentType>(config.AgentTypes.Length);
        foreach (var a in config.AgentTypes)
        {
            var costByArea = new Fix64[config.NavAreas.Length];
            foreach (var cost in a.AreaCost)
            {
                costByArea[AreaIndex(cost.Key)] = Fix64.FromDouble(cost.Value);
            }

            int profileIndex = Array.FindIndex(config.NavProfiles, p => p.Id == a.Profile);
            runtimeAgentTypes.Add(new RuntimeAgentType
            {
                Id = a.Id,
                Layer = a.Layer,
                SpeedCmPerSecond = Fix64.FromDouble(a.SpeedCmPerSecond),
                PushPriority = a.PushPriority,
                NavProfileIndex = profileIndex,
                CostByArea = costByArea,
            });
        }

        var distinctRadiusSet = new SortedSet<int>();
        foreach (var profile in runtimeProfiles) distinctRadiusSet.Add((int)profile.RadiusCm.ToInt());
        var distinctRadiusCm = new int[distinctRadiusSet.Count];
        distinctRadiusSet.CopyTo(distinctRadiusCm);

        return new CrowdSimulationRuntimeConfig
        {
            MapId = config.MapId,
            FixedHz = fixedHz,
            NavCellsPerSide = navCellCount,
            NavCellSizeCm = navCellSizeCm,
            WorldSeed = config.World.Seed,
            SurfaceAsset = config.World.SurfaceAsset,
            TerrainTypeIds = Array.ConvertAll(config.World.TerrainTypes, t => t.Id),
            NavAreas = Array.ConvertAll(config.NavAreas, a => new RuntimeNavArea { Id = a.Id, SlopeFree = a.SlopeFree }),
            TerrainToArea = terrainToArea,
            NavProfiles = Array.ConvertAll(config.NavProfiles, p => new RuntimeNavProfile
            {
                Id = p.Id,
                MaxSlopeDeg = p.MaxSlopeDeg is double slope ? Fix64.FromDouble(slope) : null,
                Jump = p.Jump == null
                    ? null
                    : new RuntimeJumpCapability
                    {
                        UpCm = p.Jump.UpCm,
                        DownCm = p.Jump.DownCm,
                        RangeNavCells = p.Jump.RangeNavCells,
                        Cost = Fix64.FromDouble(p.Jump.Cost),
                    },
            }),
            AgentTypes = runtimeAgentTypes,
            UnitTypes = Array.ConvertAll(config.UnitTypes, u => new RuntimeUnitType
            {
                Id = u.Id,
                AgentTypeIndex = Array.FindIndex(config.AgentTypes, a => a.Id == u.AgentType),
                Special = u.Special,
                TemplatesByRadiusCm = ParseTemplatesByRadiusCm(u),
            }),
            Profiles = runtimeProfiles,
            RadiusClasses = Array.AsReadOnly(distinctRadiusCm),
            AvoidanceRadiusScale = avoidanceRadiusScale,
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
                Templates = BuildStructureTemplates(config),
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

    private static bool TryParseCanonicalRadiusCm(string key, out int radiusCm)
    {
        if (!int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out radiusCm))
        {
            return false;
        }

        return radiusCm > 0 && string.Equals(key, radiusCm.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>结构模板表装载(参考 templates.structures):模板 area id 解析为导航区域下标,
    /// 未知区域在装载即报错。表按 id 排序,place 指令按 id 查本表,不看 JSON 键顺序。</summary>
    private static RuntimeStructureTemplate[] BuildStructureTemplates(CrowdSimulationConfig config)
    {
        var entries = config.Structures.Templates;
        if (entries == null || entries.Count == 0) return Array.Empty<RuntimeStructureTemplate>();
        var areaIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < config.NavAreas.Length; i++) areaIndex.Add(config.NavAreas[i].Id, i);
        return OrderedById(entries, (id, t) => new RuntimeStructureTemplate
        {
            Id = id,
            Name = t.Name ?? id,
            Footprint = t.Footprint,
            Blocker = t.Blocker,
            AreaIndex = t.Area != null ? areaIndex[t.Area] : null,
            Priority = t.Priority ?? 0,
            LifetimeSec = t.LifetimeSec is { } sec ? Fix64.FromDouble(sec) : null,
            LifetimeSecRaw = t.LifetimeSec,
            Layered = t.Layered,
        });
    }

    /// <summary>unitTypes[].templates 的半径级键按整数厘米解析(声明式映射,不是 id 字符串约定)。</summary>
    private static Dictionary<int, string>? ParseTemplatesByRadiusCm(CrowdSimulationConfig.UnitTypeEntry unitType)
    {
        if (unitType.Templates == null) return null;
        var parsed = new Dictionary<int, string>(unitType.Templates.Count);
        foreach (var kvp in unitType.Templates)
        {
            if (!int.TryParse(kvp.Key, System.Globalization.CultureInfo.InvariantCulture, out int radiusCm) || radiusCm <= 0)
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: unitTypes.{unitType.Id}.templates 的键 \"{kvp.Key}\" 需为 > 0 的整数半径(厘米)");
            }

            if (string.IsNullOrWhiteSpace(kvp.Value))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: unitTypes.{unitType.Id}.templates[{kvp.Key}] 需为非空模板 id");
            }

            parsed[radiusCm] = kvp.Value;
        }

        return parsed;
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
