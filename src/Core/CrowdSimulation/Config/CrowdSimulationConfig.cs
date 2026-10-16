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
    public required WorldSection World { get; init; }
    public required WorkerSection Worker { get; init; }
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
    public required PairRulesSection PairRules { get; init; }
    public required SimSection Sim { get; init; }
    public required SpawnSection Spawn { get; init; }
    public required StructuresSection Structures { get; init; }
    public required DeploySection Deploy { get; init; }
    public required TelemetrySection Telemetry { get; init; }

    public static CrowdSimulationConfig Load(JsonObject configObject)
    {
        ArgumentNullException.ThrowIfNull(configObject);
        RejectIdKeyedNulls(configObject);
        RequireStructureKinds(configObject);
        var options = StrictJsonOptions.CreateCamelCase();
        options.AllowOutOfOrderMetadataProperties = true;
        CrowdSimulationConfig cfg;
        try
        {
            cfg = configObject.Deserialize<CrowdSimulationConfig>(options)
                ?? throw new InvalidOperationException("CrowdSimulationConfig.json: 反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"CrowdSimulationConfig.json: {ex.Message}", ex);
        }

        if (cfg.Worker.PlanTimeoutMs <= 0)
        {
            throw new InvalidOperationException(
                "CrowdSimulationConfig.json: worker.planTimeoutMs 需为 > 0 的整数毫秒。");
        }

        return cfg;
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

    private static readonly string[] IdKeyedObjects =
    [
        "formations",
        "structures.templates",
    ];

    private static readonly string[] StructureEffectKinds = ["block", "setArea", "surface"];
    private static readonly string[] StructurePlacementKinds = ["unrestricted", "spanEnds"];

    private static void RequireStructureKinds(JsonObject root)
    {
        if (root["structures"] is not JsonObject structures || structures["templates"] is not JsonObject templates)
        {
            return;
        }

        foreach (var (id, node) in templates)
        {
            if (node is not JsonObject template) continue;
            RequireKind(template, $"structures.templates.{id}", "effect", StructureEffectKinds);
            RequireKind(template, $"structures.templates.{id}", "placement", StructurePlacementKinds);
        }
    }

    private static void RequireKind(JsonObject template, string path, string key, string[] kinds)
    {
        if (template[key] is not JsonObject obj) return;
        string? kind = obj["kind"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        if (kind == null || Array.IndexOf(kinds, kind) < 0)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: {path}.{key}.kind 需为 {string.Join(" / ", kinds)}。");
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

    public sealed class WorldSection
    {
        /// <summary>生成盐。地图覆盖写 world.seed；合并结果缺这个键则装载失败。</summary>
        public required int Seed { get; init; }
    }

    public sealed class WorkerSection
    {
        public required int PlanTimeoutMs { get; init; }
    }

    public sealed class AgentsSection
    {
        public required double AvoidanceRadiusScale { get; init; }
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
        /// <summary>视线遮挡的初始开关。结构挡住的 fog 格不透明，更高地形挡住视线。fogSight 可在运行时改 CrowdFog.Los。高度表在迷雾装配时注入。</summary>
        public required bool LineOfSight { get; init; }
        /// <summary>观察者眼高，厘米。</summary>
        public required int EyeCm { get; init; }
    }

    public sealed class FormationSection
    {
        public required double SpacingScale { get; init; }
        public required double LeaderSpeedRatio { get; init; }
        public required double LeaderTurnRatePerSec { get; init; }
        public required double FaceTurnScale { get; init; }
        public required double CatchUpSpeedRatio { get; init; }
        public required double SlotSightCm { get; init; }
        public required double SettleTimeSec { get; init; }
        public required double LeaderLookAheadCm { get; init; }
        public required double MagicBoxMaxSpreadCm { get; init; }
        public required double MagicBoxPadCm { get; init; }
        public required double SlotArriveCm { get; init; }
        public required double GoalArriveCm { get; init; }
        public required double SettleRadiusCm { get; init; }
        public required double ClumpSlackScale { get; init; }
        public required double MinClumpCm { get; init; }
        /// <summary>与上一任领队朝向的点积：不低于该值则继承朝向；不高于 mirrorFlipDot 则反转。</summary>
        public required double HeadingInheritDot { get; init; }
        /// <summary>与上一任领队朝向的点积：不高于该值则反转。</summary>
        public required double MirrorFlipDot { get; init; }
    }

    public sealed class FormationShapeEntry
    {
        public required double SpacingDiameterScale { get; init; }
        public required double Aspect { get; init; }
        public required bool Wedge { get; init; }
    }

    public sealed class MovementSection
    {
        public required double MaxStepNavCells { get; init; }
        public required int MaxSubSteps { get; init; }
        public required double SlotTimeConstantSec { get; init; }
        public required double BlendRatePerSec { get; init; }
        public required double BlendCommitRatio { get; init; }
        public required int SlotCheckIntervalSubSteps { get; init; }
        public required double SightHysteresisScale { get; init; }
        public required double LaneSpreadSpeedRatio { get; init; }
        public required double WakeDistanceCm { get; init; }
        public required double StallSpeedRatio { get; init; }
        public required double StallDecayPerSec { get; init; }
        public required double UnitTurnRateRadPerSec { get; init; }
        public required double StopSpeedRatio { get; init; }
        public required double SpeedCapRatio { get; init; }
        public required double RestAccelScale { get; init; }
        public required double RestSeparationScale { get; init; }
    }

    public sealed class AvoidanceSection
    {
        public required double RateHz { get; init; }
        public required int HashRings { get; init; }
        public required int MaxNeighbors { get; init; }
        public required int MaxScan { get; init; }
        public required double SeparationWeight { get; init; }
        public required double AccelerationPerSec { get; init; }
        public required double RestDeadband { get; init; }
        public required double MaxPush { get; init; }
        public required double SmoothingRatio { get; init; }
    }

    /// <summary>Yield 分支的内核参数。让不让路由 pairRules.yieldsTo 投影，不在这里选。</summary>
    public sealed class PushSection
    {
        public required double MovingPriorityBonus { get; init; }
        public required double DominantShare { get; init; }
    }

    /// <summary>
    /// 功能位各点名一个关系类型 id。群体模拟不解释这个 id。
    /// 有边 / 没边在会话激活时投影成玩家对矩阵。
    /// </summary>
    public sealed class PairRulesSection
    {
        public required PairRuleSlot YieldsTo { get; init; }
        public required PairRuleSlot SharesVisionWith { get; init; }
    }

    public sealed class PairRuleSlot
    {
        public required string Relation { get; init; }
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
        public required double NavCellInsetFraction { get; init; }
    }

    public sealed class StructuresSection
    {
        /// <summary>键是模板 id。placeStructure / removeStructureAt 按 id 引用。运行时按 id 的序数排序，不看对象键顺序。</summary>
        public required Dictionary<string, StructureTemplateEntry> Templates { get; init; }
    }

    /// <summary>layer、footprint、effect、placement 只允许 <see cref="CrowdSimulationConfigValidator"/> 列出的四种组合。</summary>
    public sealed class StructureTemplateEntry
    {
        public required string Layer { get; init; }
        public required string Footprint { get; init; }
        public required StructureEffectEntry Effect { get; init; }
        public required StructurePlacementEntry Placement { get; init; }
        /// <summary>必写；null 表示永久。</summary>
        public required double? LifetimeSec { get; init; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(StructureBlockEffect), "block")]
    [JsonDerivedType(typeof(StructureSetAreaEffect), "setArea")]
    [JsonDerivedType(typeof(StructureSurfaceEffect), "surface")]
    public abstract class StructureEffectEntry
    {
    }

    /// <summary>格被足迹盖住的面积份额（X、Y 两向之积）≥ coverage 时成为阻挡格。</summary>
    public sealed class StructureBlockEffect : StructureEffectEntry
    {
        public required double Coverage { get; init; }
    }

    /// <summary>格心落在足迹里的底层格改成 area；重叠时 priority 高者胜，同级后放置者胜。</summary>
    public sealed class StructureSetAreaEffect : StructureEffectEntry
    {
        public required string Area { get; init; }
        public required int Priority { get; init; }
    }

    /// <summary>铺一条上层条带，格的通行参数取 area；离两端不到 portalReachNavCells 格的条带格是入口。</summary>
    public sealed class StructureSurfaceEffect : StructureEffectEntry
    {
        public required string Area { get; init; }
        public required int PortalReachNavCells { get; init; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(StructureUnrestrictedPlacement), "unrestricted")]
    [JsonDerivedType(typeof(StructureSpanEndsPlacement), "spanEnds")]
    public abstract class StructurePlacementEntry
    {
    }

    public sealed class StructureUnrestrictedPlacement : StructurePlacementEntry
    {
    }

    /// <summary>按格的静态地形区域判：两个端点格在 endpointAreas 里，其余盖住的格在 interiorAreas 里。</summary>
    public sealed class StructureSpanEndsPlacement : StructurePlacementEntry
    {
        public required string[] EndpointAreas { get; init; }
        public required string[] InteriorAreas { get; init; }
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
