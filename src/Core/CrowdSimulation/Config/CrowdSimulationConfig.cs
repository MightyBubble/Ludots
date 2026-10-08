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
/// <see cref="CrowdSimulationConfigLoader"/> 校验。长度字段一律厘米（Cm 后缀），
/// 以格为单位的字段带 Cells 后缀；内核在组装时统一换算成米（÷100）。
/// </summary>
public sealed class CrowdSimulationConfig
{
    public required int Version { get; init; }
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
    public required FormationShapeEntry[] Formations { get; init; }
    public required MovementSection Movement { get; init; }
    public required AvoidanceSection Avoidance { get; init; }
    public required PushSection Push { get; init; }
    public required SimSection Sim { get; init; }
    public required SpawnSection Spawn { get; init; }
    public required StructuresSection Structures { get; init; }
    public required DeploySection Deploy { get; init; }
    public required TelemetrySection Telemetry { get; init; }

    public static CrowdSimulationConfig Load(JsonObject configObject)
    {
        ArgumentNullException.ThrowIfNull(configObject);
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
        public required int RangeCells { get; init; }
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
    }

    public sealed class AgentsSection
    {
        public required double AvoidanceRadiusScale { get; init; }
        /// <summary>本会话的体型档案来源(mod URI,如 "CrowdSimulationMod:assets/Navigation/agent_profiles.json")。
        /// 缺失 = 用引擎全局档案注册表(与别的特性共享,半径级会并集——只对明确接受这一点的场景)。</summary>
        public string? ProfilesUri { get; init; }
    }

    public sealed class NavmeshSection
    {
        public required double MinRegionArea { get; init; }
        public required double MaxSimplificationError { get; init; }
        public required double MaxEdgeLen { get; init; }
        public required int MaxVertsPerPoly { get; init; }
        public required double PortalInsetCells { get; init; }
    }

    public sealed class HpaSection
    {
        public required int ClusterSize { get; init; }
        public required int MaxEntranceWidth { get; init; }
    }

    public sealed class NavtileSection
    {
        public required int CacheCapacity { get; init; }
    }

    public sealed class FlowfieldSection
    {
        public required int CacheCapacity { get; init; }
        public required int PoolCapacity { get; init; }
        public required int CorridorPadding { get; init; }
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
        public required int CellCells { get; init; }
        public required int VisionCm { get; init; }
        public required int RevealTicks { get; init; }
        public required int SlotCacheCapacity { get; init; }
        public required int VariantCapacity { get; init; }
    }

    public sealed class FormationSection
    {
        public required double SpacingScale { get; init; }
        public required double LeaderSpeed { get; init; }
        public required double LeaderTurnRate { get; init; }
        public required double FaceTurnScale { get; init; }
        public required double CatchUp { get; init; }
        public required double SlotSightCells { get; init; }
        public required double SettleTime { get; init; }
        public required double LeaderLookAhead { get; init; }
        public required double MagicBoxMaxSpreadCm { get; init; }
        public required double MagicBoxPadCells { get; init; }
        public required double SlotArriveCells { get; init; }
        public required double GoalArriveCells { get; init; }
        public required double SettleRadiusCells { get; init; }
        public required double ClumpSlack { get; init; }
        public required double MinClumpCells { get; init; }
        /// <summary>与上一任领队朝向的点积上限继承阈值(参考实现 planner.js 内联 0.94)。</summary>
        public required double HeadingInheritDot { get; init; }
        /// <summary>与上一任领队朝向的点积反转阈值(参考实现 planner.js 内联 -0.5)。</summary>
        public required double MirrorFlipDot { get; init; }
    }

    public sealed class FormationShapeEntry
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public required double Spacing { get; init; }
        public required double Aspect { get; init; }
        public required bool Wedge { get; init; }
    }

    public sealed class MovementSection
    {
        public required double MaxStepCells { get; init; }
        public required int MaxSubSteps { get; init; }
        public required double SlotTimeConstant { get; init; }
        public required double BlendRate { get; init; }
        public required double BlendCommit { get; init; }
        public required int SlotCheckInterval { get; init; }
        public required double SightHysteresis { get; init; }
        public required double LaneSpread { get; init; }
        public required double WakeDistanceCells { get; init; }
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
        public required double TimeScale { get; init; }
    }

    public sealed class SpawnSection
    {
        public required int PlacementTries { get; init; }
        public required double ClusterSpacing { get; init; }
        public required int TargetSearchTries { get; init; }
        public required double CellInset { get; init; }
    }

    public sealed class StructuresSection
    {
        public required double BlockCoverage { get; init; }
        public required int PortalCells { get; init; }
    }

    public sealed class DeploySection
    {
        public required int InitialUnits { get; init; }
        public required double SpreadCells { get; init; }
        public required double BaseJitterCells { get; init; }
        public required int CentersPerGroup { get; init; }
        public required string[] Templates { get; init; }
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
