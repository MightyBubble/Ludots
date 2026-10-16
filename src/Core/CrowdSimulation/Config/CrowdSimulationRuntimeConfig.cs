using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Structures;
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
    /// <summary>链接种类，按 traversal.json linkKinds 合并顺序。内核目前最多消费一种。</summary>
    public required IReadOnlyList<RuntimeLinkKind> LinkKinds { get; init; }
    /// <summary>本图写了 linkCandidateBake 的种类，按 linkKinds 合并顺序；就是 .navsurface 的链接种类表。</summary>
    public required IReadOnlyList<RuntimeLinkCandidateBake> LinkCandidateBakes { get; init; }
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
    public required bool IgnoresSlope { get; init; }
}

public sealed class RuntimeLinkKind
{
    public required string Id { get; init; }
    public required Fix64 TraversalSpeedRatio { get; init; }
}

/// <summary>
/// 一种链接的候选烘焙参数。几何参数来自地图；两张地形掩码按地形表下标，由通行矩阵推导：
/// 端点 = 有这种链接能力的行里 depart 或 arrive 含它的区域，硬缝 = 这些行都不可通行的区域。
/// </summary>
public sealed class RuntimeLinkCandidateBake
{
    public required string KindId { get; init; }
    public required int MaxGapCm { get; init; }
    public required int LinkSpacingCm { get; init; }
    public required double EndpointMaxSlopeDeg { get; init; }
    public required bool[] EndpointTerrain { get; init; }
    public required bool[] HardGapTerrain { get; init; }
}

/// <summary>移动类型的坡度与链接参数，和 <see cref="RuntimeAgentType"/> 一一对应。</summary>
public sealed class RuntimeNavProfile
{
    public required string Id { get; init; }
    /// <summary>坡度上限（度）；null = 不限坡度。ignoresSlope 区域不适用。</summary>
    public required Fix64? MaxSlopeDeg { get; init; }
    public required RuntimeLinkCapability? Link { get; init; }
}

public sealed class RuntimeLinkCapability
{
    public required int UpCm { get; init; }
    public required int DownCm { get; init; }
    public required int RangeNavCells { get; init; }
    public required Fix64 Cost { get; init; }
    /// <summary>按区域下标：这一行能从这种区域出发走链接。</summary>
    public required bool[] DepartByArea { get; init; }
    /// <summary>按区域下标：这一行能经链接落到这种区域。</summary>
    public required bool[] ArriveByArea { get; init; }
}

public sealed class RuntimeAgentType
{
    public required string Id { get; init; }
    /// <summary>
    /// 被单位模板引用的 traversal.json 移动类型，按合并后的属性顺序编号。
    /// 体型档案的 layer 是 NavTile 层，不参与这个编号。
    /// </summary>
    public required int AgentTypeIndex { get; init; }
    public required int NavProfileIndex { get; init; }
    /// <summary>按区域下标的通行代价。不可通行为 0。</summary>
    public required Fix64[] CostByArea { get; init; }
    /// <summary>按区域下标的速度除数。不可通行为 0。</summary>
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
    public required Fix64 SpeedCmPerSec { get; init; }
    /// <summary>true 参与批量部署和随机测试。同一移动类型的模板不必相同。</summary>
    public required bool AutoDeploy { get; init; }
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
    public required Fix64 LeaderSpeedRatio { get; init; }
    public required Fix64 LeaderTurnRatePerSec { get; init; }
    public required Fix64 FaceTurnScale { get; init; }
    public required Fix64 CatchUpSpeedRatio { get; init; }
    public required Fix64 SlotSightNavCells { get; init; }
    public required Fix64 SettleTimeSec { get; init; }
    public required Fix64 LeaderLookAheadNavCells { get; init; }
    public required Fix64 MagicBoxMaxSpreadCm { get; init; }
    public required Fix64 MagicBoxPadCm { get; init; }
    public required Fix64 SlotArriveNavCells { get; init; }
    public required Fix64 GoalArriveNavCells { get; init; }
    public required Fix64 SettleRadiusNavCells { get; init; }
    public required Fix64 ClumpSlackScale { get; init; }
    public required Fix64 MinClumpNavCells { get; init; }
    public required Fix64 HeadingInheritDot { get; init; }
    public required Fix64 MirrorFlipDot { get; init; }
}

public sealed class RuntimeFormationShape
{
    public required string Id { get; init; }
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
    public required Fix64 BlendCommitRatio { get; init; }
    public required int SlotCheckIntervalSubSteps { get; init; }
    public required Fix64 SightHysteresisScale { get; init; }
    public required Fix64 LaneSpreadSpeedRatio { get; init; }
    public required Fix64 WakeDistanceNavCells { get; init; }
    public required Fix64 StallSpeedRatio { get; init; }
    public required Fix64 StallDecayPerSec { get; init; }
    public required Fix64 UnitTurnRateRadPerSec { get; init; }
    public required Fix64 StopSpeedRatio { get; init; }
    public required Fix64 SpeedCapRatio { get; init; }
    public required Fix64 RestAccelScale { get; init; }
    public required Fix64 RestSeparationScale { get; init; }
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
    public required Fix64 SmoothingRatio { get; init; }
}

public sealed class RuntimePushSection
{
    public required Fix64 MovingPriorityBonus { get; init; }
    public required Fix64 DominantShare { get; init; }
}

/// <summary>
/// 玩家对功能位。ResponseByPair[a * P + b] 是表序 a 碰到表序 b 时的推挤响应。
/// SharesVisionByPair 同下标；两个方向都为真才并视野组。
/// IndexByPlayerId 把玩家号换成表序：玩家号必须恰为 1..P 的排列，数组按下标直查，长度 = P + 1。
/// PlayerIds 是反过来的那张表：下标是表序，值是地图 Players 声明的玩家号。
/// </summary>
public sealed class CrowdPairRules
{
    public required int PlayerCount { get; init; }
    public required CrowdPushResponse[] ResponseByPair { get; init; }
    public required bool[] SharesVisionByPair { get; init; }
    public required int[] IndexByPlayerId { get; init; }
    internal int[] PlayerIds { get; private set; } = Array.Empty<int>();

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
            PlayerIds = new int[playerCount],
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

        if (PlayerIds.Length != count)
        {
            throw new InvalidOperationException(
                $"Maps/{map.Id}.json: 玩家号表长度 {PlayerIds.Length}，需为 {count}。");
        }

        Array.Clear(IndexByPlayerId);
        for (int i = 0; i < count; i++)
        {
            int id = map.Players[i].PlayerId;
            IndexByPlayerId[id] = i;
            PlayerIds[i] = id;
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
    public required Fix64 NavCellInsetFraction { get; init; }
}

public sealed class RuntimeStructuresSection
{
    /// <summary>运行时表按下标排列，placeStructure 写的是模板 id 字符串，仓再用 TemplateIndexOf 换成下标。区域在装载时收成区域下标。</summary>
    public required IReadOnlyList<RuntimeStructureTemplate> Templates { get; init; }

    public int TemplateIndexOf(string templateId)
    {
        for (int i = 0; i < Templates.Count; i++)
        {
            if (Templates[i].Id == templateId) return i;
        }

        return -1;
    }

    /// <summary>
    /// 地图实体点名的模板。阻挡只接受 square + block，条带只接受 path + surface，两者都必须永久。
    /// </summary>
    public int RequireMapTemplate(string templateId, CrowdStructureEffectKind effect)
    {
        string role = effect == CrowdStructureEffectKind.Block ? "阻挡" : "条带";
        int index = TemplateIndexOf(templateId);
        if (index < 0)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 地图{role}实体点名的结构模板 \"{templateId}\" 不存在。");
        }

        var tpl = Templates[index];
        bool fits = effect == CrowdStructureEffectKind.Block
            ? tpl.Effect == CrowdStructureEffectKind.Block && tpl.Footprint == CrowdStructureShape.Square
            : tpl.Effect == CrowdStructureEffectKind.Surface && tpl.Footprint == CrowdStructureShape.Path;
        if (!fits)
        {
            throw new InvalidOperationException(effect == CrowdStructureEffectKind.Block
                ? $"{CrowdSimulationConfigValidator.FileName}: 地图阻挡实体点名的结构模板 \"{templateId}\" 必须是 square 足迹的 block 模板。"
                : $"{CrowdSimulationConfigValidator.FileName}: 地图条带实体点名的结构模板 \"{templateId}\" 必须是 path 足迹的 surface 模板。");
        }

        if (tpl.LifetimeSec != null)
        {
            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 地图{role}实体点名的结构模板 \"{templateId}\" 带寿命；地图上的结构必须是永久的。");
        }

        return index;
    }
}

public enum CrowdStructureLayer : byte
{
    Base,
    Upper,
}

public enum CrowdStructureEffectKind : byte
{
    Block,
    SetArea,
    Surface,
}

public enum CrowdStructurePlacementKind : byte
{
    Unrestricted,
    SpanEnds,
}

public sealed class RuntimeStructureTemplate
{
    public required string Id { get; init; }
    public required CrowdStructureLayer Layer { get; init; }
    /// <summary>尺寸由 place 指令给出：square 边长、disc 半径、path 宽，都是厘米。</summary>
    public required CrowdStructureShape Footprint { get; init; }
    public required CrowdStructureEffectKind Effect { get; init; }
    /// <summary>block 的覆盖份额；其他效果为 0。</summary>
    public required Fix64 BlockCoverage { get; init; }
    /// <summary>setArea / surface 的区域下标；block 为 null。</summary>
    public required int? AreaIndex { get; init; }
    /// <summary>setArea 的覆盖优先级，同级后放置者胜；其他效果为 0。</summary>
    public required int Priority { get; init; }
    /// <summary>surface 的入口格数；其他效果为 0。</summary>
    public required int PortalReachNavCells { get; init; }
    public required CrowdStructurePlacementKind Placement { get; init; }
    /// <summary>spanEnds 时按区域下标，真表示端点格的静态地形区域可以是它；unrestricted 时为空数组。</summary>
    public required bool[] EndpointAreas { get; init; }
    /// <summary>spanEnds 时按区域下标，真表示端点以外的盖住格的静态地形区域可以是它；unrestricted 时为空数组。</summary>
    public required bool[] InteriorAreas { get; init; }
    /// <summary>寿命(仿真秒);null = 永久。到期 tick 用配置原始 double 做有理数 ceil，整数 tick 不容骑线。</summary>
    public required Fix64? LifetimeSec { get; init; }
    /// <summary>寿命的配置原始值。到期 tick = placeTick + ceil(lifetimeSec × FixedHz / simSecondsPerTickSecond)。</summary>
    public double? LifetimeSecRaw { get; init; }

    public bool Blocks => Effect == CrowdStructureEffectKind.Block;
    public bool OnUpperLayer => Layer == CrowdStructureLayer.Upper;
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
}

public sealed class RuntimeTelemetrySection
{
    public required Fix64 TurnEpsRad { get; init; }
    public required Fix64 Ema { get; init; }
}
