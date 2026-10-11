using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// CrowdSimulationConfig.json 的反序列化形状（键与 JSON 一一对应，camelCase）。
/// 显式必填：缺键 / 未知键直接拒绝（反序列化层），数值范围与跨文件引用由
/// <see cref="CrowdSimulationConfigLoader"/> 校验。世界距离用厘米（Cm）。
/// 阵型距离、唤醒和部署散布装载时换成导航格 Fix64；magicBox 的外扩和最大跨度保持厘米。
/// 栅格算法量用 NavCells / NavCellArea / NavTiles / Fraction。内核坐标保持厘米。
/// </summary>
public sealed class CrowdSimulationConfig
{
    public required string MapId { get; init; }
    public required WorldSection World { get; init; }
    public required NavAreaEntry[] NavAreas { get; init; }
    public required Dictionary<string, string> TerrainAreas { get; init; }
    public required NavProfileEntry[] NavProfiles { get; init; }
    public required AgentTypeEntry[] AgentTypes { get; init; }
    public required UnitTypeEntry[] UnitTypes { get; init; }
    public required AgentsSection Agents { get; init; }
    public required NavmeshSection Navmesh { get; init; }
    public required HpaSection Hpa { get; init; }
    public required NavtileSection Navtile { get; init; }
    public required FlowfieldSection Flowfield { get; init; }
    public required PlanningSection Planning { get; init; }
    public required FogSection Fog { get; init; }
    public required FormationSection Formation { get; init; }
    public required Dictionary<string, FormationShapeEntry> Formations { get; init; }
    public required MovementSection Movement { get; init; }
    public required AvoidanceSection Avoidance { get; init; }
    public required PushSection Push { get; init; }
    public required RelationsSection Relations { get; init; }
    public required SimSection Sim { get; init; }
    public required SpawnSection Spawn { get; init; }
    public required StructuresSection Structures { get; init; }
    public required DeploySection Deploy { get; init; }
    public required TelemetrySection Telemetry { get; init; }

    public static CrowdSimulationConfig Load(JsonObject configObject)
    {
        ArgumentNullException.ThrowIfNull(configObject);
        RejectRetiredKeys(configObject);
        RejectIdKeyedNulls(configObject);
        try
        {
            return configObject.Deserialize<CrowdSimulationConfig>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException("CrowdSimulationConfig.json: 反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"CrowdSimulationConfig.json: {ex.Message}", ex);
        }
    }

    public static CrowdSimulationConfig Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var node = JsonNode.Parse(stream) ?? throw new InvalidOperationException("CrowdSimulationConfig.json: JSON 根为空。");
        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException("CrowdSimulationConfig.json: 根必须是 JSON 对象。");
        }

        return Load(obj);
    }

    /// <summary>旧作者键只用于报错。命中即拒绝，不读取值。</summary>
    internal static readonly (string OldPath, string NewKey)[] RetiredSpatialKeys =
    [
        ("deploy.baseJitterCells", "deploy.baseJitterCm"),
        ("deploy.spreadCells", "deploy.spreadCm"),
        ("flowfield.corridorPadding", "flowfield.corridorPaddingNavTiles"),
        ("fog.cellCells", "fog.cellSizeCm"),
        ("formation.goalArriveCells", "formation.goalArriveCm"),
        ("formation.leaderLookAhead", "formation.leaderLookAheadCm"),
        ("formation.magicBoxPadCells", "formation.magicBoxPadCm"),
        ("formation.magicBoxPadNavCells", "formation.magicBoxPadCm"),
        ("formation.minClumpCells", "formation.minClumpCm"),
        ("formation.settleRadiusCells", "formation.settleRadiusCm"),
        ("formation.slotArriveCells", "formation.slotArriveCm"),
        ("formation.slotSightCells", "formation.slotSightCm"),
        ("formations[].spacing", "formations.<id>.spacingDiameterScale"),
        ("hpa.clusterSize", "navtile.sizeCm"),
        ("hpa.maxEntranceWidth", "hpa.maxEntranceWidthNavCells"),
        ("movement.laneSpread", "movement.laneSpreadSpeedRatio"),
        ("movement.maxStepCells", "movement.maxStepNavCells"),
        ("movement.wakeDistanceCells", "movement.wakeDistanceCm"),
        ("navProfiles[].jump.rangeCells", "navProfiles[].jump.rangeNavCells"),
        ("navmesh.maxEdgeLen", "navmesh.maxEdgeLenNavCells"),
        ("navmesh.maxSimplificationError", "navmesh.maxSimplificationErrorNavCells"),
        ("navmesh.minRegionArea", "navmesh.minRegionNavCellArea"),
        ("navmesh.portalInsetCells", "navmesh.portalInsetNavCells"),
        ("spawn.cellInset", "spawn.navCellInsetFraction"),
        ("spawn.clusterSpacing", "spawn.groupSpacingScale"),
        ("structures.portalCells", "structures.portalReachNavCells"),
        ("formation.settleTime", "formation.settleTimeSec"),
        ("movement.slotTimeConstant", "movement.slotTimeConstantSec"),
        ("sim.timeScale", "sim.simSecondsPerTickSecond"),
    ];

    private static readonly string[] IdKeyedObjects =
    [
        "formations",
        "agents.radiusClasses",
        "structures.templates",
    ];

    private static void RejectRetiredKeys(JsonObject root)
    {
        if (root.ContainsKey("version"))
        {
            throw new InvalidOperationException(
                "CrowdSimulationConfig.json: 已删除 version。配置不再带版本号，由严格 schema 与 mod.json semver 管理。");
        }

        foreach (var (oldPath, newKey) in RetiredSpatialKeys)
        {
            RejectRetiredSpatialKey(root, oldPath.Split('.'), 0, "", newKey);
        }
    }

    private static void RejectIdKeyedNulls(JsonObject root)
    {
        foreach (string path in IdKeyedObjects)
        {
            RejectIdKeyedNull(root, path.Split('.'), 0, "");
        }
    }

    private static void RejectIdKeyedNull(JsonNode? node, string[] segments, int index, string built)
    {
        if (node is not JsonObject obj || index >= segments.Length)
        {
            return;
        }

        string name = segments[index];
        if (!obj.ContainsKey(name))
        {
            return;
        }

        if (obj[name] is null)
        {
            if (index == segments.Length - 1)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationConfig.json: {built}{name} 不接受 null。以 id 为键的对象不接受 null。");
            }

            return;
        }

        if (index < segments.Length - 1)
        {
            RejectIdKeyedNull(obj[name], segments, index + 1, built + name + ".");
            return;
        }

        if (obj[name] is JsonArray)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: {built}{name} 以 id 为键的对象，不再使用数组。");
        }

        if (obj[name] is not JsonObject table)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: {built}{name} 必须是以 id 为键的对象。");
        }

        foreach (var (key, value) in table)
        {
            if (value is null)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationConfig.json: {built}{name}.{key} 为 null。以 id 为键的对象不接受 null。");
            }
        }
    }

    private static void RejectRetiredSpatialKey(JsonNode? node, string[] segments, int index, string built, string newKey)
    {
        if (node is not JsonObject obj || index >= segments.Length)
        {
            return;
        }

        string segment = segments[index];
        bool array = segment.EndsWith("[]", StringComparison.Ordinal);
        string name = array ? segment[..^2] : segment;
        if (!obj.ContainsKey(name))
        {
            return;
        }

        if (array)
        {
            if (obj[name] is JsonArray items)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    RejectRetiredSpatialKey(items[i], segments, index + 1, $"{built}{name}[{i}].", newKey);
                }

                return;
            }

            if (obj[name] is JsonObject children)
            {
                foreach (var (key, child) in children)
                {
                    RejectRetiredSpatialKey(child, segments, index + 1, $"{built}{name}.{key}.", newKey);
                }
            }

            return;
        }

        if (index == segments.Length - 1)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: 已删除 {built}{name}，改用 {newKey}。");
        }

        RejectRetiredSpatialKey(obj[name], segments, index + 1, built + name + ".", newKey);
    }

    public sealed class WorldSection
    {
        public required int NavCellSizeCm { get; init; }
        public required TerrainTypeEntry[] TerrainTypes { get; init; }
        public required string SurfaceAsset { get; init; }
        /// <summary>世界种子(生成盐;每张地图一个,与参考实现的 world.seed 同值)。</summary>
        public int Seed { get; init; }
    }

    public sealed class TerrainTypeEntry
    {
        public required string Id { get; init; }
    }

    /// <summary>导航区域。slopeFree = 平面区域，几何 profile 的坡度上限不适用。</summary>
    public sealed class NavAreaEntry
    {
        public required string Id { get; init; }
        public required bool SlopeFree { get; init; }
    }

    public sealed class NavProfileEntry
    {
        public required string Id { get; init; }
        public double? MaxSlopeDeg { get; init; }
        public JumpSection? Jump { get; init; }
    }

    public sealed class JumpSection
    {
        public required int UpCm { get; init; }
        public required int DownCm { get; init; }
        public required int RangeNavCells { get; init; }
        public required double Cost { get; init; }
    }

    /// <summary>移动类型：areaCost 的键必须是 navAreas 的 id；未写的区域 = 不可通行。</summary>
    public sealed class AgentTypeEntry
    {
        public required string Id { get; init; }
        public required int Layer { get; init; }
        public required double SpeedCmPerSecond { get; init; }
        public required int PushPriority { get; init; }
        public required string Profile { get; init; }
        public required Dictionary<string, double> AreaCost { get; init; }
    }

    public sealed class UnitTypeEntry
    {
        public required string Id { get; init; }
        public required string AgentType { get; init; }
        /// <summary>特殊单位(不参与批量部署,只能由生成工具点名;参考实现的 unitTypes[].special)。</summary>
        public bool Special { get; init; }
        /// <summary>半径级(厘米)→ 单位实体模板 id:该兵种出生时实例化的模板,模板自身声明 profile。
        /// 键是 JSON 字符串,装载时按整数半径厘米解析。</summary>
        public Dictionary<string, string>? Templates { get; init; }
    }

    public sealed class AgentsSection
    {
        public required double AvoidanceRadiusScale { get; init; }
        /// <summary>本会话的体型档案来源(mod URI,如 "CrowdSimulationMod:assets/Navigation/agent_profiles.json")。
        /// 缺失 = 用引擎全局档案注册表(与别的特性共享,半径级会并集——只对明确接受这一点的场景)。</summary>
        public string? ProfilesUri { get; init; }
        /// <summary>半径级表(参考实现 templates.radiusClasses):键是半径厘米，值是该级的推挤优先级份额,
        /// 与 agentTypes[].pushPriority 合成原型优先级(参考 core/archetypes.js)。键按数值升序消费，不看对象键顺序。</summary>
        public required Dictionary<string, RadiusClassEntry> RadiusClasses { get; init; }
    }

    /// <summary>半径级(参考实现 templates.radiusClasses 条目):推挤份额。半径厘米是对象键。</summary>
    public sealed class RadiusClassEntry
    {
        public required double PushPriority { get; init; }
    }

    public sealed class NavmeshSection
    {
        public required double MinRegionNavCellArea { get; init; }
        public required double MaxSimplificationErrorNavCells { get; init; }
        public required double MaxEdgeLenNavCells { get; init; }
        public required int MaxVertsPerPoly { get; init; }
        public required double PortalInsetNavCells { get; init; }
    }

    public sealed class HpaSection
    {
        public required int MaxEntranceWidthNavCells { get; init; }
    }

    public sealed class NavtileSection
    {
        public required int SizeCm { get; init; }
        public required int CacheCapacity { get; init; }
    }

    public sealed class FlowfieldSection
    {
        public required int CacheCapacity { get; init; }
        public required int PoolCapacity { get; init; }
        public required int CorridorPaddingNavTiles { get; init; }
        public required int MaxRebuildsPerTick { get; init; }
    }

    public sealed class PlanningSection
    {
        public required int LatencyTicks { get; init; }
        public required int RefreshLatencyTicks { get; init; }
        public required int MaxRefreshesPerTick { get; init; }
        public required int RebakeSlices { get; init; }
    }

    public sealed class FogSection
    {
        public required double RateHz { get; init; }
        public required int CellSizeCm { get; init; }
        public required int VisionCm { get; init; }
        public required int RevealTicks { get; init; }
        public required int SlotCacheCapacity { get; init; }
        public required int VariantCapacity { get; init; }
        /// <summary>视线遮挡(lineOfSight 同名):建筑与更高地形挡视线;运行时 fogSight 命令可切。</summary>
        public required bool LineOfSight { get; init; }
        /// <summary>观察者眼高(参考端 eyeMeters,厘米域)。</summary>
        public required int EyeCm { get; init; }
    }

    public sealed class FormationSection
    {
        public required double SpacingScale { get; init; }
        public required double LeaderSpeed { get; init; }
        public required double LeaderTurnRate { get; init; }
        public required double FaceTurnScale { get; init; }
        public required double CatchUp { get; init; }
        public required double SlotSightCm { get; init; }
        public required double SettleTimeSec { get; init; }
        public required double LeaderLookAheadCm { get; init; }
        public required double MagicBoxMaxSpreadCm { get; init; }
        public required double MagicBoxPadCm { get; init; }
        public required double SlotArriveCm { get; init; }
        public required double GoalArriveCm { get; init; }
        public required double SettleRadiusCm { get; init; }
        public required double ClumpSlack { get; init; }
        public required double MinClumpCm { get; init; }
        /// <summary>与上一任领队朝向的点积上限继承阈值(参考实现 planner.js 内联 0.94)。</summary>
        public required double HeadingInheritDot { get; init; }
        /// <summary>与上一任领队朝向的点积反转阈值(参考实现 planner.js 内联 -0.5)。</summary>
        public required double MirrorFlipDot { get; init; }
    }

    public sealed class FormationShapeEntry
    {
        public required string Label { get; init; }
        public required double SpacingDiameterScale { get; init; }
        public required double Aspect { get; init; }
        public required bool Wedge { get; init; }
    }

    public sealed class MovementSection
    {
        public required double MaxStepNavCells { get; init; }
        public required int MaxSubSteps { get; init; }
        public required double SlotTimeConstantSec { get; init; }
        public required double BlendRate { get; init; }
        public required double BlendCommit { get; init; }
        public required int SlotCheckInterval { get; init; }
        public required double SightHysteresis { get; init; }
        public required double LaneSpreadSpeedRatio { get; init; }
        public required double WakeDistanceCm { get; init; }
        public required double StallSpeedRatio { get; init; }
        public required double StallDecay { get; init; }
        public required double UnitTurnRate { get; init; }
        public required double StopSpeedRatio { get; init; }
        public required double SpeedCapRatio { get; init; }
        public required double RestAccelScale { get; init; }
        public required double RestSeparationScale { get; init; }
        public required double JumpSpeedRatio { get; init; }
    }

    public sealed class AvoidanceSection
    {
        public required double RateHz { get; init; }
        public required int HashRings { get; init; }
        public required int MaxNeighbors { get; init; }
        public required int MaxScan { get; init; }
        public required double SeparationWeight { get; init; }
        public required double Acceleration { get; init; }
        public required double RestDeadband { get; init; }
        public required double MaxPush { get; init; }
        public required double Smoothing { get; init; }
    }

    /// <summary>推挤：modes 按态度选择模式（priority = 让路 / rigid = 刚性），键固定为 samePlayer / Friendly / Neutral / Hostile。</summary>
    public sealed class PushSection
    {
        public required double MovingBonus { get; init; }
        public required double DominantShare { get; init; }
        public required PushModesSection Modes { get; init; }
    }

    /// <summary>
    /// 关系接口(参考实现 globals.relations):(玩家 a, 玩家 b) → 关系种类,一次解析进 P×P 矩阵。
    /// 解析序 overrides > 同玩家(self) > 同队(sameTeam) > default;每种关系带推挤模式
    /// (priority = 高优先级推低 / rigid = 刚性,休息单位挡住移动单位)。
    /// </summary>
    public sealed class RelationsSection
    {
        public required Dictionary<string, RelationsKindEntry> Kinds { get; init; }
        public required string Self { get; init; }
        public required string SameTeam { get; init; }
        public required string Default { get; init; }
        public RelationsOverrideEntry[] Overrides { get; init; } = Array.Empty<RelationsOverrideEntry>();
    }

    public sealed class RelationsKindEntry
    {
        public required string Push { get; init; }
        /// <summary>该关系是否共享视野(shareVision):双向都真 → 两玩家成一个视野组(迷雾与认知共享)。</summary>
        public required bool ShareVision { get; init; }
    }

    public sealed class RelationsOverrideEntry
    {
        public required int PlayerA { get; init; }
        public required int PlayerB { get; init; }
        public required string Kind { get; init; }
    }

    public sealed class PushModesSection
    {
        [JsonPropertyName("samePlayer")]
        public required string SamePlayer { get; init; }
        [JsonPropertyName("Friendly")]
        public required string Friendly { get; init; }
        [JsonPropertyName("Neutral")]
        public required string Neutral { get; init; }
        [JsonPropertyName("Hostile")]
        public required string Hostile { get; init; }
    }

    public sealed class SimSection
    {
        public required int MaxUnits { get; init; }
        /// <summary>每个 tick 秒推进的仿真秒数。全局变速走 TimeFlow，不走这个字段。</summary>
        public required double SimSecondsPerTickSecond { get; init; }
        /// <summary>运动内核按单位/占格下标划分的工人数。1 = 单线程。结果与该数无关。</summary>
        public required int ParallelWorkerCount { get; init; }
    }

    public sealed class SpawnSection
    {
        public required int PlacementTries { get; init; }
        public required double GroupSpacingScale { get; init; }
        public required int TargetSearchTries { get; init; }
        public required double NavCellInsetFraction { get; init; }
    }

    public sealed class StructuresSection
    {
        public required double BlockCoverage { get; init; }
        public required int PortalReachNavCells { get; init; }
        /// <summary>结构实体模板(参考实现 templates.structures):键是模板 id，place/remove 按 id 引用。运行时按 id 排序，不看对象键顺序。</summary>
        public Dictionary<string, StructureTemplateEntry>? Templates { get; init; }
    }

    /// <summary>结构实体模板条目(参考 config.js validateConfig 的结构规则):
    /// footprint 必填;blocker 只支持 rect;area 与 priority 同进同出;lifetimeSec &gt; 0。</summary>
    public sealed class StructureTemplateEntry
    {
        public string? Name { get; init; }
        public required string Footprint { get; init; }
        public bool Blocker { get; init; }
        public string? Area { get; init; }
        public int? Priority { get; init; }
        public double? LifetimeSec { get; init; }
        public bool Layered { get; init; }
    }

    public sealed class DeploySection
    {
        public required int InitialUnits { get; init; }
        public required double SpreadCm { get; init; }
        public required double BaseJitterCm { get; init; }
        public required int CentersPerGroup { get; init; }
        public required DeployBaseEntry[] Bases { get; init; }
    }

    public sealed class DeployBaseEntry
    {
        public required int PlayerId { get; init; }
        public required int XCm { get; init; }
        public required int YCm { get; init; }
        /// <summary>该玩家的单位配色(#rrggbb;呈现层调色板数据,仿真空口)。</summary>
        public string? Color { get; init; }
    }

    public sealed class TelemetrySection
    {
        public required double TurnEpsRad { get; init; }
        public required double Ema { get; init; }
    }
}
