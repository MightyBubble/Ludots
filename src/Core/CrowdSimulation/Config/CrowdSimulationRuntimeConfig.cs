using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 组装完成的只读运行时配置。数学一律走 Ludots 已有定点体系（Fix64 / Fix64Math），
/// 长度单位为厘米（Fix64）；数量、容量、格计数保持 int。内核不读文件、不碰全局单例。
/// </summary>
public sealed class CrowdSimulationRuntimeConfig
{
    public required int Version { get; init; }
    public required string MapId { get; init; }
    public required int FixedHz { get; init; }

    /// <summary>导航网格边长（格）。世界正方形，Boards[0].WidthCm / NavCellSizeCm。</summary>
    public required int NavCellCount { get; init; }
    public required int NavCellSizeCm { get; init; }
    /// <summary>世界种子(生成盐,与参考实现的 world.seed 同值;部署 / 生成的 RNG 流以此为根)。</summary>
    public required int WorldSeed { get; init; }
    public required string SurfaceAsset { get; init; }

    /// <summary>地形类型表；下标 = .navsurface 栅格中的编号。</summary>
    public required IReadOnlyList<string> TerrainTypeIds { get; init; }
    public required IReadOnlyList<RuntimeNavArea> NavAreas { get; init; }
    /// <summary>terrainIdx → navAreaIdx，长度 = TerrainTypeIds.Count。</summary>
    public required IReadOnlyList<int> TerrainToArea { get; init; }
    public required IReadOnlyList<RuntimeNavProfile> NavProfiles { get; init; }
    public required IReadOnlyList<RuntimeAgentType> AgentTypes { get; init; }
    public required IReadOnlyList<RuntimeUnitType> UnitTypes { get; init; }
    /// <summary>由 AgentProfileRegistry 绑定的代理体型（移动类型 × 半径级）。</summary>
    public required IReadOnlyList<RuntimeAgentProfile> Profiles { get; init; }

    public required Fix64 AvoidanceRadiusScale { get; init; }
    public required RuntimeNavmeshSection Navmesh { get; init; }
    public required RuntimeHpaSection Hpa { get; init; }
    public required int NavtileCacheCapacity { get; init; }
    public required RuntimeFlowfieldSection Flowfield { get; init; }
    public required RuntimePlanningSection Planning { get; init; }
    public required RuntimeFogSection Fog { get; init; }
    public required RuntimeFormationSection Formation { get; init; }
    public required IReadOnlyList<RuntimeFormationShape> Formations { get; init; }
    public required RuntimeMovementSection Movement { get; init; }
    public required RuntimeAvoidanceSection Avoidance { get; init; }
    public required RuntimePushSection Push { get; init; }
    /// <summary>关系推挤矩阵(P×P,玩家下标 = 地图 Players 表序):逐对推挤模式,避让热路径查表。</summary>
    public required RuntimeRelations Relations { get; init; }
    public required RuntimeSimSection Sim { get; init; }
    public required RuntimeSpawnSection Spawn { get; init; }
    public required RuntimeStructuresSection Structures { get; init; }
    public required RuntimeDeploySection Deploy { get; init; }
    public required RuntimeTelemetrySection Telemetry { get; init; }
}

public enum CrowdSimulationPushMode
{
    Priority,
    Rigid,
}

public sealed class RuntimeNavArea
{
    public required string Id { get; init; }
    public required bool SlopeFree { get; init; }
}

public sealed class RuntimeNavProfile
{
    public required string Id { get; init; }
    /// <summary>坡度上限（度）；null = 不限坡度。slopeFree 区域不适用。</summary>
    public required Fix64? MaxSlopeDeg { get; init; }
    public required RuntimeJumpCapability? Jump { get; init; }
}

public sealed class RuntimeJumpCapability
{
    public required int UpCm { get; init; }
    public required int DownCm { get; init; }
    public required int RangeCells { get; init; }
    public required Fix64 Cost { get; init; }
}

public sealed class RuntimeAgentType
{
    public required string Id { get; init; }
    public required int Layer { get; init; }
    /// <summary>代价 1 地面上的速度（厘米 / 仿真秒）。</summary>
    public required Fix64 SpeedCmPerSecond { get; init; }
    public required int PushPriority { get; init; }
    public required int NavProfileIndex { get; init; }
    /// <summary>按导航区域下标的通行代价；0 = 不可通行（配置中未写的区域）。</summary>
    public required Fix64[] CostByArea { get; init; }
}

public sealed class RuntimeUnitType
{
    public required string Id { get; init; }
    public required int AgentTypeIndex { get; init; }
    /// <summary>特殊单位(不参与批量部署,只能由生成工具点名)。</summary>
    public bool Special { get; init; }
    /// <summary>半径级(厘米)→ 单位实体模板 id;生成管线按(兵种 × 半径级)实例化模板。</summary>
    public required IReadOnlyDictionary<int, string>? TemplatesByRadiusCm { get; init; }
}

public sealed class RuntimeAgentProfile
{
    public required string Id { get; init; }
    public required Fix64 RadiusCm { get; init; }
    /// <summary>个人避让半径（厘米）= 半径 × avoidanceRadiusScale。</summary>
    public required Fix64 PersonalRadiusCm { get; init; }
    /// <summary>推挤优先级（移动类型份额 + 半径级份额）。</summary>
    public required Fix64 PushPriority { get; init; }
    public required int AgentTypeIndex { get; init; }
    /// <summary>通行净空（切比雪夫格数，由几何推导，不是配置项）。</summary>
    public required int ClearanceCells { get; init; }
    /// <summary>导航上下文 id = layer × 256 + clearance（数值键，禁止字符串拼接）。</summary>
    public required int NavContextId { get; init; }
}

public sealed class RuntimeNavmeshSection
{
    public required Fix64 MinRegionArea { get; init; }
    public required Fix64 MaxSimplificationError { get; init; }
    public required Fix64 MaxEdgeLen { get; init; }
    public required int MaxVertsPerPoly { get; init; }
    public required Fix64 PortalInsetCells { get; init; }
}

public sealed class RuntimeHpaSection
{
    public required int ClusterSize { get; init; }
    public required int MaxEntranceWidth { get; init; }
}

public sealed class RuntimeFlowfieldSection
{
    public required int CacheCapacity { get; init; }
    public required int PoolCapacity { get; init; }
    public required int CorridorPadding { get; init; }
    public required int MaxRebuildsPerTick { get; init; }
}

public sealed class RuntimePlanningSection
{
    public required int LatencyTicks { get; init; }
    public required int RefreshLatencyTicks { get; init; }
    public required int MaxRefreshesPerTick { get; init; }
    public required int RebakeSlices { get; init; }
}

public sealed class RuntimeFogSection
{
    public required Fix64 RateHz { get; init; }
    public required int CellCells { get; init; }
    public required Fix64 VisionCm { get; init; }
    public required int RevealTicks { get; init; }
    public required int SlotCacheCapacity { get; init; }
    public required int VariantCapacity { get; init; }
}

public sealed class RuntimeFormationSection
{
    public required Fix64 SpacingScale { get; init; }
    public required Fix64 LeaderSpeed { get; init; }
    public required Fix64 LeaderTurnRate { get; init; }
    public required Fix64 FaceTurnScale { get; init; }
    public required Fix64 CatchUp { get; init; }
    public required Fix64 SlotSightCells { get; init; }
    public required Fix64 SettleTime { get; init; }
    public required Fix64 LeaderLookAhead { get; init; }
    public required Fix64 MagicBoxMaxSpreadCm { get; init; }
    public required Fix64 MagicBoxPadCells { get; init; }
    public required Fix64 SlotArriveCells { get; init; }
    public required Fix64 GoalArriveCells { get; init; }
    public required Fix64 SettleRadiusCells { get; init; }
    public required Fix64 ClumpSlack { get; init; }
    public required Fix64 MinClumpCells { get; init; }
    public required Fix64 HeadingInheritDot { get; init; }
    public required Fix64 MirrorFlipDot { get; init; }
}

public sealed class RuntimeFormationShape
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required Fix64 Spacing { get; init; }
    public required Fix64 Aspect { get; init; }
    public required bool Wedge { get; init; }
}

public sealed class RuntimeMovementSection
{
    public required Fix64 MaxStepCells { get; init; }
    public required int MaxSubSteps { get; init; }
    public required Fix64 SlotTimeConstant { get; init; }
    public required Fix64 BlendRate { get; init; }
    public required Fix64 BlendCommit { get; init; }
    public required int SlotCheckInterval { get; init; }
    public required Fix64 SightHysteresis { get; init; }
    public required Fix64 LaneSpread { get; init; }
    public required Fix64 WakeDistanceCells { get; init; }
    public required Fix64 StallSpeedRatio { get; init; }
    public required Fix64 StallDecay { get; init; }
    public required Fix64 UnitTurnRate { get; init; }
    public required Fix64 StopSpeedRatio { get; init; }
    public required Fix64 SpeedCapRatio { get; init; }
    public required Fix64 RestAccelScale { get; init; }
    public required Fix64 RestSeparationScale { get; init; }
    public required Fix64 JumpSpeedRatio { get; init; }
}

public sealed class RuntimeAvoidanceSection
{
    public required Fix64 RateHz { get; init; }
    public required int HashRings { get; init; }
    public required int MaxNeighbors { get; init; }
    public required int MaxScan { get; init; }
    public required Fix64 SeparationWeight { get; init; }
    public required Fix64 Acceleration { get; init; }
    public required Fix64 RestDeadband { get; init; }
    public required Fix64 MaxPush { get; init; }
    public required Fix64 Smoothing { get; init; }
}

public sealed class RuntimePushSection
{
    public required Fix64 MovingBonus { get; init; }
    public required Fix64 DominantShare { get; init; }
    public required CrowdSimulationPushMode SamePlayer { get; init; }
    public required CrowdSimulationPushMode Friendly { get; init; }
    public required CrowdSimulationPushMode Neutral { get; init; }
    public required CrowdSimulationPushMode Hostile { get; init; }
}

/// <summary>关系推挤矩阵(core/relations.js buildRelations 产物):PushModeByPair[a * P + b] 为
/// 玩家下标 a 对 b 的推挤模式(下标 = 地图 Players 表序)。IndexByPlayerId 把 PlayerOwner 的
/// 玩家号换算成表序下标;门禁保证玩家号恰为 1..P 的排列,数组按玩家号直查,长度 = P + 1。</summary>
public sealed class RuntimeRelations
{
    public required int PlayerCount { get; init; }
    public required CrowdSimulationPushMode[] PushModeByPair { get; init; }
    /// <summary>长度 = P + 1(下标 0 弃用);IndexByPlayerId[ playerId ] = 表序下标,
    /// 仅对 1..P 的玩家号有定义(装载门禁拒绝表外 id)。</summary>
    public required int[] IndexByPlayerId { get; init; }
}

public sealed class RuntimeSimSection
{
    public required int MaxUnits { get; init; }
    public required Fix64 TimeScale { get; init; }
    /// <summary>timeScale 的配置原始值(到期 tick 等整数契约按参考端 f64 公式换算,不走 Fix64)。</summary>
    public required double TimeScaleRaw { get; init; }
}

public sealed class RuntimeSpawnSection
{
    public required int PlacementTries { get; init; }
    public required Fix64 ClusterSpacing { get; init; }
    public required int TargetSearchTries { get; init; }
    public required Fix64 CellInset { get; init; }
}

public sealed class RuntimeStructuresSection
{
    public required Fix64 BlockCoverage { get; init; }
    public required int PortalCells { get; init; }
    /// <summary>结构实体模板(下标即 place 指令的模板号;areaId 已解析为导航区域下标)。</summary>
    public required IReadOnlyList<RuntimeStructureTemplate> Templates { get; init; }
}

public sealed class RuntimeStructureTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>足迹形状:rect / disc / path(厘米域,尺寸由 place 指令给出)。</summary>
    public required string Footprint { get; init; }
    public required bool Blocker { get; init; }
    /// <summary>导航区域下标;null = 不改区域。</summary>
    public required int? AreaIndex { get; init; }
    /// <summary>区域覆盖优先级(area 非空时必有);同级后放置者胜。</summary>
    public required int Priority { get; init; }
    /// <summary>寿命(仿真秒);null = 永久。到期 tick 换算用参考端原始 double 公式(整数 tick 不容骑线)。</summary>
    public required Fix64? LifetimeSec { get; init; }
    /// <summary>寿命的配置原始值(到期 tick = placeTick + ceil(lifetime / (timeScale/fixedHz)),与参考端同式同值)。</summary>
    public double? LifetimeSecRaw { get; init; }
    public required bool Layered { get; init; }
}

public sealed class RuntimeDeploySection
{
    public required int InitialUnits { get; init; }
    public required Fix64 SpreadCells { get; init; }
    public required Fix64 BaseJitterCells { get; init; }
    public required int CentersPerGroup { get; init; }
    public required IReadOnlyList<RuntimeDeployBase> Bases { get; init; }
}

public sealed class RuntimeDeployBase
{
    public required int PlayerId { get; init; }
    public required int XCm { get; init; }
    public required int YCm { get; init; }
    /// <summary>该玩家的单位配色(#rrggbb;null = 未配置,呈现层回退默认)。</summary>
    public string? Color { get; init; }
}

public sealed class RuntimeTelemetrySection
{
    public required Fix64 TurnEpsRad { get; init; }
    public required Fix64 Ema { get; init; }
}
