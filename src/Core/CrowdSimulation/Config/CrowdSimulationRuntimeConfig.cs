using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 组装完成的只读运行时配置。数学一律走 Ludots 已有定点体系（Fix64 / Fix64Math），
/// 长度单位为厘米（Fix64）；数量、容量、格计数保持 int。内核不读文件、不碰全局单例。
/// </summary>
public sealed class CrowdSimulationRuntimeConfig
{
    public required string MapId { get; init; }
    public required int FixedHz { get; init; }

    /// <summary>导航网格边长（格）。世界正方形，求解器板的 WidthCm / NavCellSizeCm。</summary>
    public required int NavCellsPerSide { get; init; }
    public required int NavCellSizeCm { get; init; }
    /// <summary>生成盐，部署和点名生成的 RNG 根。</summary>
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
    /// <summary>玩家对功能位(P×P,下标 = 地图 Players 表序)。避让和视野组热路径只查这张表。</summary>
    public required CrowdPairRules PairRules { get; init; }
    public required RuntimeSimSection Sim { get; init; }
    public required RuntimeSpawnSection Spawn { get; init; }
    public required RuntimeStructuresSection Structures { get; init; }
    public required RuntimeDeploySection Deploy { get; init; }
    public required RuntimeTelemetrySection Telemetry { get; init; }

    /// <summary>半径厘米升序去重表。</summary>
    public required IReadOnlyList<int> RadiusClasses { get; init; }
}

/// <summary>有序玩家对上的推挤响应。Yield 按推挤优先级让路；Hold 静止时不让、自己移动时全让。</summary>
public enum CrowdPushResponse
{
    Yield,
    Hold,
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
    public required int RangeNavCells { get; init; }
    public required Fix64 Cost { get; init; }
}

public sealed class RuntimeAgentType
{
    public required string Id { get; init; }
    /// <summary>
    /// 被单位模板引用的 pathing agent type，按合并后的属性顺序编号。
    /// 体型档案的 layer 是 NavTile 层，不参与这个编号。
    /// </summary>
    public required int AgentTypeIndex { get; init; }
    public required int NavProfileIndex { get; init; }
    /// <summary>按下标 = areaId 的通行代价。passable 为 false 时为 0。</summary>
    public required Fix64[] CostByArea { get; init; }
    /// <summary>按下标 = areaId 的速度除数。未写 speedDivisor 时与代价相同；不可通行为 0。</summary>
    public required Fix64[] SpeedDivisorByArea { get; init; }
}

public sealed class RuntimeUnitType
{
    public required string Id { get; init; }
    public required int AgentTypeIndex { get; init; }
    /// <summary>半径级(厘米)→ 单位实体模板 id;生成管线按(兵种 × 半径级)实例化模板。</summary>
    public required IReadOnlyDictionary<int, string>? TemplatesByRadiusCm { get; init; }
}

public sealed class RuntimeAgentProfile
{
    public required string Id { get; init; }
    public required Fix64 RadiusCm { get; init; }
    /// <summary>个人避让半径（厘米）= 半径 × avoidanceRadiusScale。</summary>
    public required Fix64 PersonalRadiusCm { get; init; }
    /// <summary>推挤优先级来自单位模板。</summary>
    public required Fix64 PushPriority { get; init; }
    /// <summary>该模板的速度（厘米 / 仿真秒）。领队步伐取同组最小，子步数取实际绑定模板的最大。</summary>
    public required Fix64 SpeedCmPerSecond { get; init; }
    /// <summary>true 不参与批量部署。同一移动类型的模板不必相同。</summary>
    public required bool Special { get; init; }
    public required int AgentTypeIndex { get; init; }
    /// <summary>通行净空（切比雪夫格数，由几何推导，不是配置项）。</summary>
    public required int ClearanceNavCells { get; init; }
    /// <summary>导航上下文 id = AgentTypeIndex × <see cref="CrowdSimulationLimits.NavContextLayerStride"/> + clearance（数值键，禁止字符串拼接）。</summary>
    public required int NavContextId { get; init; }
}

public sealed class RuntimeNavmeshSection
{
    public required Fix64 MinRegionNavCellArea { get; init; }
    public required Fix64 MaxSimplificationErrorNavCells { get; init; }
    public required Fix64 MaxEdgeLenNavCells { get; init; }
    public required int MaxVertsPerPoly { get; init; }
    public required Fix64 PortalInsetNavCells { get; init; }
}

public sealed class RuntimeHpaSection
{
    public required int NavCellsPerTile { get; init; }
    public required int MaxEntranceWidthNavCells { get; init; }
}

public sealed class RuntimeFlowfieldSection
{
    public required int CacheCapacity { get; init; }
    public required int PoolCapacity { get; init; }
    public required int CorridorPaddingNavTiles { get; init; }
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
    public required int FogCellSizeCm { get; init; }
    public required int NavCellsPerFogCell { get; init; }
    public required Fix64 VisionCm { get; init; }
    public required int RevealTicks { get; init; }
    public required int SlotCacheCapacity { get; init; }
    public required int VariantCapacity { get; init; }
    /// <summary>视线遮挡的初始开关。fogSight 可在运行时改 CrowdFog.Los。高度表在迷雾装配时注入。</summary>
    public required bool LineOfSight { get; init; }
    /// <summary>观察者眼高(厘米)。</summary>
    public required int EyeCm { get; init; }
}

public sealed class RuntimeFormationSection
{
    public required Fix64 SpacingScale { get; init; }
    public required Fix64 LeaderSpeed { get; init; }
    public required Fix64 LeaderTurnRatePerSec { get; init; }
    public required Fix64 FaceTurnScale { get; init; }
    public required Fix64 CatchUp { get; init; }
    public required Fix64 SlotSightNavCells { get; init; }
    public required Fix64 SettleTimeSec { get; init; }
    public required Fix64 LeaderLookAheadNavCells { get; init; }
    public required Fix64 MagicBoxMaxSpreadCm { get; init; }
    public required Fix64 MagicBoxPadCm { get; init; }
    public required Fix64 SlotArriveNavCells { get; init; }
    public required Fix64 GoalArriveNavCells { get; init; }
    public required Fix64 SettleRadiusNavCells { get; init; }
    public required Fix64 ClumpSlack { get; init; }
    public required Fix64 MinClumpNavCells { get; init; }
    public required Fix64 HeadingInheritDot { get; init; }
    public required Fix64 MirrorFlipDot { get; init; }
}

public sealed class RuntimeFormationShape
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required Fix64 SpacingDiameterScale { get; init; }
    public required Fix64 Aspect { get; init; }
    public required bool Wedge { get; init; }
}

public sealed class RuntimeMovementSection
{
    public required Fix64 MaxStepNavCells { get; init; }
    public required int MaxSubSteps { get; init; }
    public required Fix64 SlotTimeConstantSec { get; init; }
    public required Fix64 BlendRatePerSec { get; init; }
    public required Fix64 BlendCommit { get; init; }
    public required int SlotCheckIntervalSubSteps { get; init; }
    public required Fix64 SightHysteresis { get; init; }
    public required Fix64 LaneSpreadSpeedRatio { get; init; }
    public required Fix64 WakeDistanceNavCells { get; init; }
    public required Fix64 StallSpeedRatio { get; init; }
    public required Fix64 StallDecayPerSec { get; init; }
    public required Fix64 UnitTurnRateRadPerSec { get; init; }
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
    public required Fix64 AccelerationPerSec { get; init; }
    public required Fix64 RestDeadband { get; init; }
    public required Fix64 MaxPush { get; init; }
    public required Fix64 Smoothing { get; init; }
}

public sealed class RuntimePushSection
{
    public required Fix64 MovingBonus { get; init; }
    public required Fix64 DominantShare { get; init; }
}

/// <summary>
/// 玩家对功能位。ResponseByPair[a * P + b] 是表序 a 碰到表序 b 时的推挤响应。
/// SharesVisionByPair 同下标；两个方向都为真才并视野组。
/// IndexByPlayerId 把玩家号换成表序：玩家号必须恰为 1..P 的排列，数组按下标直查，长度 = P + 1。
/// </summary>
public sealed class CrowdPairRules
{
    public required int PlayerCount { get; init; }
    public required CrowdPushResponse[] ResponseByPair { get; init; }
    public required bool[] SharesVisionByPair { get; init; }
    public required int[] IndexByPlayerId { get; init; }

    public static CrowdPairRules Matrices(int playerCount, CrowdPushResponse[] responseByPair, bool[] sharesVisionByPair)
    {
        if (playerCount < 1)
        {
            throw new InvalidOperationException($"CrowdPairRules: 玩家数 {playerCount}，需为 ≥ 1。");
        }

        int cells = playerCount * playerCount;
        if (responseByPair.Length != cells || sharesVisionByPair.Length != cells)
        {
            throw new InvalidOperationException(
                $"CrowdPairRules: 矩阵长度需为 {cells}（玩家数 {playerCount}），推挤 {responseByPair.Length}，视野 {sharesVisionByPair.Length}。");
        }

        return new CrowdPairRules
        {
            PlayerCount = playerCount,
            ResponseByPair = responseByPair,
            SharesVisionByPair = sharesVisionByPair,
            IndexByPlayerId = new int[playerCount + 1],
        };
    }

    /// <summary>
    /// 按地图 Players 表序写下标。热路径用玩家号当数组下标，所以玩家号必须恰为 1..P，不能跳号。
    /// </summary>
    public void BindPlayerIndex(MapConfig map)
    {
        ArgumentNullException.ThrowIfNull(map);
        int count = map.Players.Count;
        if (count != PlayerCount)
        {
            throw new InvalidOperationException(
                $"Maps/{map.Id}.json: Players 有 {count} 个，功能位矩阵按 {PlayerCount} 个玩家。");
        }

        var playerIds = new HashSet<int>();
        foreach (var player in map.Players)
        {
            playerIds.Add(player.PlayerId);
        }

        if (playerIds.Count != count)
        {
            throw new InvalidOperationException($"Maps/{map.Id}.json: Players[].playerId 重复(需恰为 1..{count} 的一个排列,当前 {count} 个条目只有 {playerIds.Count} 个不同 id)。");
        }

        for (int expected = 1; expected <= count; expected++)
        {
            if (!playerIds.Contains(expected))
            {
                throw new InvalidOperationException($"Maps/{map.Id}.json: Players[].playerId 必须恰为 1..{count} 的一个排列(缺失 {expected};求解器按玩家号直查功能位矩阵,表外 id 不允许)。");
            }
        }

        if (IndexByPlayerId.Length != count + 1)
        {
            throw new InvalidOperationException(
                $"Maps/{map.Id}.json: 玩家下标表长度 {IndexByPlayerId.Length}，需为 {count + 1}。");
        }

        Array.Clear(IndexByPlayerId);
        for (int i = 0; i < count; i++)
        {
            IndexByPlayerId[map.Players[i].PlayerId] = i;
        }
    }
}

public sealed class RuntimeSimSection
{
    public required int MaxUnits { get; init; }
    public required Fix64 SimSecondsPerTickSecond { get; init; }
    /// <summary>simSecondsPerTickSecond 的配置原始值。到期 tick 用有理数 ceil,不走 Fix64。</summary>
    public required double SimSecondsPerTickSecondRaw { get; init; }
    /// <summary>运动内核工人数(1 = 单线程)。步进时读取,调用方改它必须仍落在配置校验范围内。</summary>
    public required int ParallelWorkerCount { get; set; }
}

public sealed class RuntimeSpawnSection
{
    public required int PlacementTries { get; init; }
    public required Fix64 GroupSpacingScale { get; init; }
    public required int TargetSearchTries { get; init; }
    public required Fix64 NavCellInsetFraction { get; init; }
}

public sealed class RuntimeStructuresSection
{
    public required Fix64 BlockCoverage { get; init; }
    public required int PortalReachNavCells { get; init; }
    /// <summary>与 TerrainTypeIds 等长。真表示该地形可以落桥端点。</summary>
    public required bool[] AnchorTerrain { get; init; }
    /// <summary>与 TerrainTypeIds 等长。真表示该地形可以被桥跨越。</summary>
    public required bool[] SpanTerrain { get; init; }
    /// <summary>运行时表按下标排列，placeStructure 写的是模板 id 字符串，仓再用 TemplateIndexOf 换成下标。area 在装载时收成区域下标。</summary>
    public required IReadOnlyList<RuntimeStructureTemplate> Templates { get; init; }

    public int TemplateIndexOf(string templateId)
    {
        for (int i = 0; i < Templates.Count; i++)
        {
            if (Templates[i].Id == templateId) return i;
        }

        return -1;
    }
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
    /// <summary>寿命(仿真秒);null = 永久。到期 tick 用配置原始 double 做有理数 ceil，整数 tick 不容骑线。</summary>
    public required Fix64? LifetimeSec { get; init; }
    /// <summary>寿命的配置原始值。到期 tick = placeTick + ceil(lifetimeSec × FixedHz / simSecondsPerTickSecond)。</summary>
    public double? LifetimeSecRaw { get; init; }
    public required bool Layered { get; init; }
}

public sealed class RuntimeDeploySection
{
    public required int InitialUnits { get; init; }
    public required Fix64 SpreadNavCells { get; init; }
    public required Fix64 BaseJitterNavCells { get; init; }
    public required int CentersPerGroup { get; init; }
    public required IReadOnlyList<RuntimeDeployBase> Bases { get; init; }
}

public sealed class RuntimeDeployBase
{
    public required int PlayerId { get; init; }
    public required int XCm { get; init; }
    public required int YCm { get; init; }
    /// <summary>该玩家的单位配色(#rrggbb)。装载要求基地要么全部写颜色，要么全部不写。全部不写则不注册调色板。</summary>
    public string? Color { get; init; }
}

public sealed class RuntimeTelemetrySection
{
    public required Fix64 TurnEpsRad { get; init; }
    public required Fix64 Ema { get; init; }
}
