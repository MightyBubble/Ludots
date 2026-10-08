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
/// 管线侧负责 config_catalog 合并（DeepObject）与版本门禁；<see cref="Load"/> 按
/// LU-20 的契约把合并结果、地图、代理体型与固定时钟组装成只读运行时配置——
/// 内核不读文件、不读全局单例。文件值（厘米 / double）在组装层一次性转 Fix64 厘米，
/// 之后内核只走 Ludots 定点体系。
/// </summary>
public sealed class CrowdSimulationConfigLoader
{
    /// <summary>结构版本与 CrowdSimulation 参考实现配置版本同步；不等于当前版本一律拒绝，不做迁移。</summary>
    public const int CurrentConfigVersion = 7;

    public const string RelativePath = "CrowdSimulationConfig.json";

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
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 没有任何来源提供该文件（CrowdSimulationMod 应提供全量默认）。");
        }

        var config = CrowdSimulationConfig.Load(merged);
        if (config.Version != CurrentConfigVersion)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: version = {config.Version}，不是当前版本 v{CurrentConfigVersion}（无迁移，需按当前 Schema 重写）。");
        }

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
    /// <param name="map">地图（Boards / Teams / Players / ParticipantRelationships / Entities）。</param>
    /// <param name="profiles">Navigation/agent_profiles.json 注册表。</param>
    /// <param name="fixedHz">Engine/clock.json 的 FixedHz——仿真频率的唯一来源。</param>
    public static CrowdSimulationRuntimeConfig Load(
        CrowdSimulationConfig config,
        MapConfig map,
        AgentProfileRegistry profiles,
        int fixedHz)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profiles);

        if (config.Version != CurrentConfigVersion)
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: version = {config.Version}，不是当前版本 v{CurrentConfigVersion}（无迁移）。");
        }

        CrowdSimulationConfigValidator.Validate(config);

        string mapFile = $"Maps/{config.MapId}.json";
        if (!string.Equals(map.Id, config.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CrowdSimulationConfigValidator.FileName}: mapId = \"{config.MapId}\"，与当前地图 \"{map.Id}\" 不一致（CrowdSimulationConfig 按 mapId 绑定地图）。");
        }

        if (fixedHz < 10 || fixedHz > 240)
        {
            throw new InvalidOperationException($"Engine/clock.json: FixedHz = {fixedHz}，需为 ≥ 10、≤ 240 的数值");
        }

        if (map.Boards.Count == 0)
        {
            throw new InvalidOperationException($"{mapFile}: Boards 不能为空（CrowdSimulation 世界需要一块正方形板）。");
        }

        var board = map.Boards[0];
        int cellSizeCm = config.World.NavCellSizeCm;
        if (cellSizeCm <= 0)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: world.navCellSizeCm = {cellSizeCm}，需为 > 0 的整数");
        }

        if (board.WidthCm != board.HeightCm)
        {
            throw new InvalidOperationException($"{mapFile}: Boards[0] 必须是正方形（WidthCm {board.WidthCm} ≠ HeightCm {board.HeightCm}）。");
        }

        if (board.WidthCm % cellSizeCm != 0)
        {
            throw new InvalidOperationException($"{mapFile}: Boards[0].WidthCm（{board.WidthCm}）必须能被 world.navCellSizeCm（{cellSizeCm}）整除。");
        }

        int navCellCount = board.WidthCm / cellSizeCm;
        if (navCellCount < 16 || navCellCount > 1024)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: world.navResolution = {navCellCount}（= Boards[0].WidthCm / world.navCellSizeCm），需为整数、≥ 16、≤ 1024");
        }

        if (navCellCount % config.Hpa.ClusterSize != 0)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: world.navResolution（{navCellCount}）必须能被 hpa.clusterSize（{config.Hpa.ClusterSize}，tile 尺寸）整除");
        }

        if (map.Players.Count < 1 || map.Players.Count > 16)
        {
            throw new InvalidOperationException($"{mapFile}: Players 需要 1~16 个（当前 {map.Players.Count} 个）。");
        }

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
        foreach (var rc in config.Agents.RadiusClasses)
        {
            if (!radiusShareByRadiusCm.TryAdd(rc.RadiusCm, rc.PushPriority))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: agents.radiusClasses 半径 {rc.RadiusCm} 重复");
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
            int clearance = CrowdSimulationSpace.ClearanceOf(radiusCm, avoidanceRadiusScale, cellSizeCm);
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
                ClearanceCells = clearance,
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

        return new CrowdSimulationRuntimeConfig
        {
            Version = config.Version,
            MapId = config.MapId,
            FixedHz = fixedHz,
            NavCellCount = navCellCount,
            NavCellSizeCm = cellSizeCm,
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
                        RangeCells = p.Jump.RangeCells,
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
            AvoidanceRadiusScale = avoidanceRadiusScale,
            Navmesh = new RuntimeNavmeshSection
            {
                MinRegionArea = Fix64.FromDouble(config.Navmesh.MinRegionArea),
                MaxSimplificationError = Fix64.FromDouble(config.Navmesh.MaxSimplificationError),
                MaxEdgeLen = Fix64.FromDouble(config.Navmesh.MaxEdgeLen),
                MaxVertsPerPoly = config.Navmesh.MaxVertsPerPoly,
                PortalInsetCells = Fix64.FromDouble(config.Navmesh.PortalInsetCells),
            },
            Hpa = new RuntimeHpaSection { ClusterSize = config.Hpa.ClusterSize, MaxEntranceWidth = config.Hpa.MaxEntranceWidth },
            NavtileCacheCapacity = config.Navtile.CacheCapacity,
            Flowfield = new RuntimeFlowfieldSection
            {
                CacheCapacity = config.Flowfield.CacheCapacity,
                PoolCapacity = config.Flowfield.PoolCapacity,
                CorridorPadding = config.Flowfield.CorridorPadding,
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
                CellCells = config.Fog.CellCells,
                VisionCm = Fix64.FromInt(config.Fog.VisionCm),
                RevealTicks = config.Fog.RevealTicks,
                SlotCacheCapacity = config.Fog.SlotCacheCapacity,
                VariantCapacity = config.Fog.VariantCapacity,
            },
            Formation = new RuntimeFormationSection
            {
                SpacingScale = Fix64.FromDouble(config.Formation.SpacingScale),
                LeaderSpeed = Fix64.FromDouble(config.Formation.LeaderSpeed),
                LeaderTurnRate = Fix64.FromDouble(config.Formation.LeaderTurnRate),
                FaceTurnScale = Fix64.FromDouble(config.Formation.FaceTurnScale),
                CatchUp = Fix64.FromDouble(config.Formation.CatchUp),
                SlotSightCells = Fix64.FromDouble(config.Formation.SlotSightCells),
                SettleTime = Fix64.FromDouble(config.Formation.SettleTime),
                LeaderLookAhead = Fix64.FromDouble(config.Formation.LeaderLookAhead),
                MagicBoxMaxSpreadCm = Fix64.FromDouble(config.Formation.MagicBoxMaxSpreadCm),
                MagicBoxPadCells = Fix64.FromDouble(config.Formation.MagicBoxPadCells),
                SlotArriveCells = Fix64.FromDouble(config.Formation.SlotArriveCells),
                GoalArriveCells = Fix64.FromDouble(config.Formation.GoalArriveCells),
                SettleRadiusCells = Fix64.FromDouble(config.Formation.SettleRadiusCells),
                ClumpSlack = Fix64.FromDouble(config.Formation.ClumpSlack),
                MinClumpCells = Fix64.FromDouble(config.Formation.MinClumpCells),
                HeadingInheritDot = Fix64.FromDouble(config.Formation.HeadingInheritDot),
                MirrorFlipDot = Fix64.FromDouble(config.Formation.MirrorFlipDot),
            },
            Formations = Array.ConvertAll(config.Formations, f => new RuntimeFormationShape
            {
                Id = f.Id,
                Label = f.Label,
                Spacing = Fix64.FromDouble(f.Spacing),
                Aspect = Fix64.FromDouble(f.Aspect),
                Wedge = f.Wedge,
            }),
            Movement = new RuntimeMovementSection
            {
                MaxStepCells = Fix64.FromDouble(config.Movement.MaxStepCells),
                MaxSubSteps = config.Movement.MaxSubSteps,
                SlotTimeConstant = Fix64.FromDouble(config.Movement.SlotTimeConstant),
                BlendRate = Fix64.FromDouble(config.Movement.BlendRate),
                BlendCommit = Fix64.FromDouble(config.Movement.BlendCommit),
                SlotCheckInterval = config.Movement.SlotCheckInterval,
                SightHysteresis = Fix64.FromDouble(config.Movement.SightHysteresis),
                LaneSpread = Fix64.FromDouble(config.Movement.LaneSpread),
                WakeDistanceCells = Fix64.FromDouble(config.Movement.WakeDistanceCells),
                StallSpeedRatio = Fix64.FromDouble(config.Movement.StallSpeedRatio),
                StallDecay = Fix64.FromDouble(config.Movement.StallDecay),
                UnitTurnRate = Fix64.FromDouble(config.Movement.UnitTurnRate),
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
                Acceleration = Fix64.FromDouble(config.Avoidance.Acceleration),
                RestDeadband = Fix64.FromDouble(config.Avoidance.RestDeadband),
                MaxPush = Fix64.FromDouble(config.Avoidance.MaxPush),
                Smoothing = Fix64.FromDouble(config.Avoidance.Smoothing),
            },
            Push = new RuntimePushSection
            {
                MovingBonus = Fix64.FromDouble(config.Push.MovingBonus),
                DominantShare = Fix64.FromDouble(config.Push.DominantShare),
                SamePlayer = ParsePushMode(config.Push.Modes.SamePlayer),
                Friendly = ParsePushMode(config.Push.Modes.Friendly),
                Neutral = ParsePushMode(config.Push.Modes.Neutral),
                Hostile = ParsePushMode(config.Push.Modes.Hostile),
            },
            Relations = BuildRelations(config, map),
            Sim = new RuntimeSimSection { MaxUnits = config.Sim.MaxUnits, TimeScale = Fix64.FromDouble(config.Sim.TimeScale) },
            Spawn = new RuntimeSpawnSection
            {
                PlacementTries = config.Spawn.PlacementTries,
                ClusterSpacing = Fix64.FromDouble(config.Spawn.ClusterSpacing),
                TargetSearchTries = config.Spawn.TargetSearchTries,
                CellInset = Fix64.FromDouble(config.Spawn.CellInset),
            },
            Structures = new RuntimeStructuresSection
            {
                BlockCoverage = Fix64.FromDouble(config.Structures.BlockCoverage),
                PortalCells = config.Structures.PortalCells,
            },
            Deploy = new RuntimeDeploySection
            {
                InitialUnits = config.Deploy.InitialUnits,
                SpreadCells = Fix64.FromDouble(config.Deploy.SpreadCells),
                BaseJitterCells = Fix64.FromDouble(config.Deploy.BaseJitterCells),
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

    private static CrowdSimulationPushMode ParsePushMode(string value) => value switch
    {
        "priority" => CrowdSimulationPushMode.Priority,
        "rigid" => CrowdSimulationPushMode.Rigid,
        _ => throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: push.modes 值 \"{value}\" 需为 priority / rigid"),
    };

    /// <summary>关系推挤矩阵(core/relations.js buildRelations 移植):解析序 overrides > 同玩家 > 同队 > default;
    /// overrides 的玩家号必须存在于地图 Players。矩阵下标 = Players 表序,另存玩家号 → 下标表。</summary>
    private static RuntimeRelations BuildRelations(CrowdSimulationConfig config, MapConfig map)
    {
        var kinds = config.Relations.Kinds;
        var kindIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in kinds.Keys)
        {
            if (!kindIndexByName.TryAdd(name, kindIndexByName.Count))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: relations.kinds 关系 \"{name}\" 重复");
            }
        }

        var kindPush = new CrowdSimulationPushMode[kinds.Count];
        foreach (var (name, entry) in kinds)
        {
            if (entry.Push is not ("priority" or "rigid"))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: relations.kinds.{name}.push 值 \"{entry.Push}\" 需为 priority / rigid");
            }

            kindPush[kindIndexByName[name]] = ParsePushMode(entry.Push);
        }

        int P = map.Players.Count;
        var indexByPlayerId = new Dictionary<int, int>(P);
        var teamByIndex = new int?[P];
        for (int i = 0; i < P; i++)
        {
            indexByPlayerId[map.Players[i].PlayerId] = i;
            teamByIndex[i] = map.Players[i].TeamId > 0 ? map.Players[i].TeamId : null;
        }

        string KindOf(int a, int b) =>
            a == b ? config.Relations.Self
            : teamByIndex[a] != null && teamByIndex[a] == teamByIndex[b] ? config.Relations.SameTeam
            : config.Relations.Default;

        var matrix = new CrowdSimulationPushMode[P * P];
        for (int a = 0; a < P; a++)
        {
            for (int b = 0; b < P; b++)
            {
                string kindName = KindOf(a, b);
                if (!kindIndexByName.TryGetValue(kindName, out int kindIndex))
                {
                    throw new InvalidOperationException(
                        $"{CrowdSimulationConfigValidator.FileName}: relations.self / relations.sameTeam / relations.default 引用未定义的关系 \"{kindName}\"");
                }

                matrix[a * P + b] = kindPush[kindIndex];
            }
        }

        foreach (var o in config.Relations.Overrides)
        {
            if (!indexByPlayerId.TryGetValue(o.A, out int a) || !indexByPlayerId.TryGetValue(o.B, out int b))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: relations.overrides 引用未知玩家 {o.A} / {o.B}");
            }

            if (!kindIndexByName.TryGetValue(o.Kind, out int kindIndex))
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigValidator.FileName}: relations.overrides 引用未定义的关系 \"{o.Kind}\"");
            }

            matrix[a * P + b] = matrix[b * P + a] = kindPush[kindIndex];
        }

        return new RuntimeRelations { PlayerCount = P, PushModeByPair = matrix };
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
}
