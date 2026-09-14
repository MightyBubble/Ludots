using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.MassNavigation.Runtime;

/// <summary>
/// MassNavigation 作者面：一切键可选。文件缺席 = 全默认 + 板推导世界尺寸；
/// 在场 = overlay（mapId 可选作用域，空 mapId 匹配任意聚焦地图）。
/// 死节（scenario/scenarioRuntime/agentProfiles/presentation/teamRelationships）经
/// StrictJsonOptions 的 UnmappedMemberHandling.Disallow 拒写。
/// </summary>
public sealed class MassNavigationConfig
{
    public string MapId { get; set; } = string.Empty;
    public MassNavigationWorldConfig? World { get; set; }
    public MassNavigationFlowSolverConfig Solver { get; set; } = new();
    public MassNavigationCadenceConfig Cadence { get; set; } = new();
    public MassNavigationRuntimeCapacityConfig RuntimeCapacity { get; set; } = new();
    public MassNavigationRelationshipPolicyConfig RelationshipPolicy { get; set; } = new();
    public MassNavigationFlowTuning Flow { get; set; } = new();
    public MassNavigationFlowArrivalTuning Arrival { get; set; } = new();
    public MassNavigationFlowAvoidanceTuning Avoidance { get; set; } = new();
    public MassNavigationCrowdSemantics Semantics { get; set; } = new();
    public MassNavigationStreamingConfig Streaming { get; set; } = new();

    public static MassNavigationConfig Load(JsonObject configObject)
    {
        if (configObject == null)
        {
            throw new ArgumentNullException(nameof(configObject));
        }

        using var document = JsonDocument.Parse(configObject.ToJsonString());
        return Load(document.RootElement);
    }

    public static MassNavigationConfig Load(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var document = JsonDocument.Parse(stream);
        return Load(document.RootElement);
    }

    private static MassNavigationConfig Load(JsonElement root)
    {
        var options = StrictJsonOptions.CreateCamelCase();

        MassNavigationConfig? config = root.Deserialize<MassNavigationConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize mass-navigation config.");
        }

        config.ApplyEngineDefaults();
        config.Validate();
        return config;
    }

    /// <summary>
    /// 缺省配置：聚焦地图上的全默认实例。streamingChunkSizeCm 与
    /// runtimeCapacity.loadedChunkCapacity 保持 0（未推导），由板绑定补齐。
    /// </summary>
    public static MassNavigationConfig CreateDefaultForMap(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("MassNavigation default config requires a non-empty map id.");
        }

        var config = new MassNavigationConfig { MapId = mapId };
        config.ApplyEngineDefaults();
        config.Validate();
        return config;
    }

    /// <summary>
    /// 是否作用于给定地图：空 mapId = 无作用域（任意地图的 overlay）。
    /// </summary>
    public bool AppliesToMap(string focusedMapId)
    {
        return string.IsNullOrWhiteSpace(MapId) ||
               string.Equals(focusedMapId, MapId, StringComparison.Ordinal);
    }

    /// <summary>
    /// 引擎默认（自现役基座 config 提炼）：未作者的键（数值 0 / 空串 / null 节）
    /// 在校验前填充。板可推导项（streamingChunkSizeCm、loadedChunkCapacity）不在此填，
    /// 留给 BindBoardWorld 按板推导。
    /// </summary>
    public void ApplyEngineDefaults()
    {
        World ??= new MassNavigationWorldConfig();
        if (World.SolverWindowWidthCm <= 0)
        {
            World.SolverWindowWidthCm = MassNavigationEngineDefaults.SolverWindowCm;
        }

        if (World.SolverWindowHeightCm <= 0)
        {
            World.SolverWindowHeightCm = MassNavigationEngineDefaults.SolverWindowCm;
        }

        if (World.CommandFocusHoldTicks <= 0)
        {
            World.CommandFocusHoldTicks = MassNavigationEngineDefaults.CommandFocusHoldTicks;
        }

        if (World.WorkAreaPaddingCm <= 0)
        {
            World.WorkAreaPaddingCm = MassNavigationEngineDefaults.WorkAreaPaddingCm;
        }

        if (World.WorkAreaMaxWidthCm <= 0)
        {
            World.WorkAreaMaxWidthCm = MassNavigationEngineDefaults.WorkAreaMaxCm;
        }

        if (World.WorkAreaMaxHeightCm <= 0)
        {
            World.WorkAreaMaxHeightCm = MassNavigationEngineDefaults.WorkAreaMaxCm;
        }

        if (Solver.FieldWidthCm <= 0)
        {
            Solver.FieldWidthCm = World.SolverWindowWidthCm;
        }

        if (Solver.FieldHeightCm <= 0)
        {
            Solver.FieldHeightCm = World.SolverWindowHeightCm;
        }

        if (Solver.FlowCellSizeCm <= 0)
        {
            Solver.FlowCellSizeCm = MassNavigationEngineDefaults.FlowCellSizeCm;
        }

        if (Solver.MaxObstacleCount <= 0)
        {
            Solver.MaxObstacleCount = MassNavigationEngineDefaults.MaxObstacleCount;
        }

        if (Solver.ParallelWorkerCount <= 0)
        {
            Solver.ParallelWorkerCount = MassNavigationEngineDefaults.ParallelWorkerCount;
        }

        if (Solver.SeparationHashCellSizeCm <= 0)
        {
            Solver.SeparationHashCellSizeCm = MassNavigationEngineDefaults.SeparationHashCellSizeCm;
        }

        if (Solver.SeparationHashMinSearchRadiusCells <= 0)
        {
            Solver.SeparationHashMinSearchRadiusCells = MassNavigationEngineDefaults.SeparationHashMinSearchRadiusCells;
        }

        if (Solver.HardResolveHashCellSizeCm <= 0)
        {
            Solver.HardResolveHashCellSizeCm = MassNavigationEngineDefaults.HardResolveHashCellSizeCm;
        }

        if (Solver.HardResolveHashMinSearchRadiusCells <= 0)
        {
            Solver.HardResolveHashMinSearchRadiusCells = MassNavigationEngineDefaults.HardResolveHashMinSearchRadiusCells;
        }

        if (Solver.PlayAreaMinXCm <= 0f)
        {
            Solver.PlayAreaMinXCm = MassNavigationEngineDefaults.PlayAreaMarginCm;
        }

        if (Solver.PlayAreaMinYCm <= 0f)
        {
            Solver.PlayAreaMinYCm = MassNavigationEngineDefaults.PlayAreaMarginCm;
        }

        if (Solver.PlayAreaMaxXCm <= 0f)
        {
            Solver.PlayAreaMaxXCm = Solver.FieldWidthCm - MassNavigationEngineDefaults.PlayAreaMarginCm;
        }

        if (Solver.PlayAreaMaxYCm <= 0f)
        {
            Solver.PlayAreaMaxYCm = Solver.FieldHeightCm - MassNavigationEngineDefaults.PlayAreaMarginCm;
        }

        RuntimeCapacity.ApplyEngineDefaults();
        Cadence.ApplyEngineDefaults();
        RelationshipPolicy.ApplyEngineDefaults();
        Flow.ApplyEngineDefaults();
        Arrival.ApplyEngineDefaults();
        Avoidance.ApplyEngineDefaults();
        Semantics.ApplyEngineDefaults();
        Streaming.ApplyEngineDefaults();
    }

    private void Validate()
    {
        Solver.Validate();
        RuntimeCapacity.Validate();
        Cadence.Validate();
        Streaming.Validate();
        Flow.Validate();
        Arrival.Validate();
        Avoidance.Validate();
        RelationshipPolicy.Validate();
        Semantics.Validate();
        World ??= new MassNavigationWorldConfig();
        World.Validate(Solver);
    }
}

/// <summary>
/// 引擎默认常数 SSOT（自现役基座 MassNavigationConfig.json 提炼）。
/// 板推导项除外：world.streamingChunkSizeCm（= 板 ChunkSizeCm）与
/// runtimeCapacity.loadedChunkCapacity（= 流送窗口 chunk 数）由板绑定推导。
/// </summary>
public static class MassNavigationEngineDefaults
{
    public const int SolverWindowCm = 10000;
    public const int CommandFocusHoldTicks = 90;
    public const int WorkAreaPaddingCm = 4000;
    public const int WorkAreaMaxCm = 48000;

    public const int FlowCellSizeCm = 100;
    public const int MaxObstacleCount = 64;
    public const int ParallelWorkerCount = 8;
    public const int SeparationHashCellSizeCm = 100;
    public const int SeparationHashMinSearchRadiusCells = 2;
    public const int HardResolveHashCellSizeCm = 50;
    public const int HardResolveHashMinSearchRadiusCells = 1;
    public const float PlayAreaMarginCm = 50f;

    public const int SimulationHz = 15;
    public const int TargetUpdateHz = 15;
    public const int FlowStepHz = 5;
    public const int FlowCrowdStampHz = 5;
    public const int FlowObstacleStampHz = 2;
    public const int HardResolveHz = 10;
    public const int EntitySyncHz = 15;
    public const int MaxStepsPerFixedTick = 1;
    public const int HardResolveCandidateThresholdAgents = 1;

    public const int NavigationGroupCapacity = 256;
    public const int GroupMembershipAgentCapacity = 16384;
    public const int MovePlanExecutionGroupCapacity = 256;
    public const int RouteStateCapacity = 4096;
    public const int RouteMaxExpandedPerRequest = 2048;
    public const int RouteWaypointCapacityPerAgent = 64;
    public const int RelationshipDomainCapacity = 16;
    public const int DisplacedAgentCapacity = 64;

    public const string CooperativeStance = "Friendly";

    public const bool FlowEnabled = false;
    public const int FlowIterationsPerStep = 4096;

    public const bool ArrivalEnabled = true;
    public const int ArrivalTimeoutMs = 1500;
    public const int ArrivalProgressDistanceCm = 60;
    public const int ArrivalWakePushDistanceCm = 80;
    public const int ArrivalMaxRetryCount = 2;

    public const string AvoidanceMode = "Separation";

    public const float StreamingRetainSeconds = 6f;
    public const int StreamingRadiusCm = 16000;
}

public sealed class MassNavigationRelationshipPolicyConfig
{
    public string CooperativeStance { get; set; } = string.Empty;

    public void ApplyEngineDefaults()
    {
        if (string.IsNullOrWhiteSpace(CooperativeStance))
        {
            CooperativeStance = MassNavigationEngineDefaults.CooperativeStance;
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CooperativeStance))
        {
            throw new InvalidOperationException("MassNavigation relationshipPolicy.cooperativeStance must be explicit.");
        }
    }
}

public sealed class MassNavigationConfigLoader
{
    public const string DefaultRelativePath = "MassNavigationConfig.json";

    private readonly ConfigPipeline _pipeline;

    public MassNavigationConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public MassNavigationConfig Load(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        string relativePath = DefaultRelativePath)
    {
        if (TryLoad(catalog, report, out MassNavigationConfig? config, relativePath))
        {
            return config;
        }

        throw new InvalidOperationException($"MassNavigation runtime config '{relativePath}' must be registered in config_catalog.json.");
    }

    public bool TryLoad(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        [NotNullWhen(true)] out MassNavigationConfig? config,
        string relativePath = DefaultRelativePath)
    {
        config = null;
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (!catalog.TryGet(relativePath, out ConfigCatalogEntry entry))
        {
            return false;
        }

        if (entry.MergePolicy != ConfigMergePolicy.DeepObject)
        {
            throw new InvalidOperationException($"MassNavigation runtime config '{relativePath}' must use DeepObject merge policy.");
        }

        JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged == null)
        {
            throw new InvalidOperationException($"MassNavigation runtime requires config '{relativePath}' through ConfigPipeline.");
        }

        config = MassNavigationConfig.Load(merged);
        return true;
    }
}

public sealed class MassNavigationStreamingConfig
{
    public float RetainSeconds { get; set; }
    public int RadiusCm { get; set; }

    public void ApplyEngineDefaults()
    {
        if (RetainSeconds <= 0f)
        {
            RetainSeconds = MassNavigationEngineDefaults.StreamingRetainSeconds;
        }

        if (RadiusCm <= 0)
        {
            RadiusCm = MassNavigationEngineDefaults.StreamingRadiusCm;
        }
    }

    public void Validate()
    {
        if (RetainSeconds < 0f)
        {
            throw new InvalidOperationException("MassNavigation streaming.retainSeconds must be >= 0.");
        }

        if (RadiusCm <= 0)
        {
            throw new InvalidOperationException("MassNavigation streaming.radiusCm must be > 0.");
        }
    }
}

public sealed class MassNavigationFlowSolverConfig
{
    public int FieldWidthCm { get; set; }
    public int FieldHeightCm { get; set; }
    public int FlowCellSizeCm { get; set; }
    public int MaxObstacleCount { get; set; }
    public int ParallelWorkerCount { get; set; }
    public int SeparationHashCellSizeCm { get; set; }
    public int SeparationHashMinSearchRadiusCells { get; set; }
    public int HardResolveHashCellSizeCm { get; set; }
    public int HardResolveHashMinSearchRadiusCells { get; set; }
    public float PlayAreaMinXCm { get; set; }
    public float PlayAreaMaxXCm { get; set; }
    public float PlayAreaMinYCm { get; set; }
    public float PlayAreaMaxYCm { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int FlowGridWidth => FieldWidthCm / FlowCellSizeCm;

    [System.Text.Json.Serialization.JsonIgnore]
    public int FlowGridHeight => FieldHeightCm / FlowCellSizeCm;

    [System.Text.Json.Serialization.JsonIgnore]
    public int SeparationHashWidth => FieldWidthCm / SeparationHashCellSizeCm;

    [System.Text.Json.Serialization.JsonIgnore]
    public int SeparationHashHeight => FieldHeightCm / SeparationHashCellSizeCm;

    [System.Text.Json.Serialization.JsonIgnore]
    public int HardResolveHashWidth => FieldWidthCm / HardResolveHashCellSizeCm;

    [System.Text.Json.Serialization.JsonIgnore]
    public int HardResolveHashHeight => FieldHeightCm / HardResolveHashCellSizeCm;

    public void Validate()
    {
        RequirePositive(FieldWidthCm, nameof(FieldWidthCm));
        RequirePositive(FieldHeightCm, nameof(FieldHeightCm));
        RequirePositive(FlowCellSizeCm, nameof(FlowCellSizeCm));
        RequirePositive(MaxObstacleCount, nameof(MaxObstacleCount));
        RequirePositive(ParallelWorkerCount, nameof(ParallelWorkerCount));
        RequirePositive(SeparationHashCellSizeCm, nameof(SeparationHashCellSizeCm));
        RequireNonNegative(SeparationHashMinSearchRadiusCells, nameof(SeparationHashMinSearchRadiusCells));
        RequirePositive(HardResolveHashCellSizeCm, nameof(HardResolveHashCellSizeCm));
        RequireNonNegative(HardResolveHashMinSearchRadiusCells, nameof(HardResolveHashMinSearchRadiusCells));
        RequireDivisible(FieldWidthCm, FlowCellSizeCm, nameof(FieldWidthCm), nameof(FlowCellSizeCm));
        RequireDivisible(FieldHeightCm, FlowCellSizeCm, nameof(FieldHeightCm), nameof(FlowCellSizeCm));
        RequireDivisible(FieldWidthCm, SeparationHashCellSizeCm, nameof(FieldWidthCm), nameof(SeparationHashCellSizeCm));
        RequireDivisible(FieldHeightCm, SeparationHashCellSizeCm, nameof(FieldHeightCm), nameof(SeparationHashCellSizeCm));
        RequireDivisible(FieldWidthCm, HardResolveHashCellSizeCm, nameof(FieldWidthCm), nameof(HardResolveHashCellSizeCm));
        RequireDivisible(FieldHeightCm, HardResolveHashCellSizeCm, nameof(FieldHeightCm), nameof(HardResolveHashCellSizeCm));
        RequireGridCapacity(FlowGridWidth, FlowGridHeight, "flow grid");
        RequireGridCapacity(SeparationHashWidth, SeparationHashHeight, "separation hash");
        RequireGridCapacity(HardResolveHashWidth, HardResolveHashHeight, "hard-resolve hash");
        RequireOrderedPlayArea(PlayAreaMinXCm, PlayAreaMaxXCm, FieldWidthCm, nameof(PlayAreaMinXCm), nameof(PlayAreaMaxXCm));
        RequireOrderedPlayArea(PlayAreaMinYCm, PlayAreaMaxYCm, FieldHeightCm, nameof(PlayAreaMinYCm), nameof(PlayAreaMaxYCm));
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"MassNavigation solver requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new InvalidOperationException($"MassNavigation solver requires {name} >= 0.");
        }
    }

    private static void RequireDivisible(int value, int divisor, string valueName, string divisorName)
    {
        if (value % divisor != 0)
        {
            throw new InvalidOperationException($"MassNavigation solver requires {valueName} to be divisible by {divisorName}.");
        }
    }

    private static void RequireGridCapacity(int width, int height, string label)
    {
        if ((long)width * height > int.MaxValue)
        {
            throw new InvalidOperationException($"MassNavigation solver {label} is too large for managed SoA arrays.");
        }
    }

    private static void RequireOrderedPlayArea(float min, float max, int fieldSize, string minName, string maxName)
    {
        if (!(min >= 0f) || !(max <= fieldSize) || !(min <= max))
        {
            throw new InvalidOperationException(
                $"MassNavigation solver requires ordered {minName}/{maxName} inside the configured field.");
        }
    }
}

public sealed class MassNavigationWorldConfig
{
    public int SolverWindowWidthCm { get; set; }
    public int SolverWindowHeightCm { get; set; }

    /// <summary>0 = 未作者，由 BindBoardWorld 按板 ChunkSizeCm 推导。</summary>
    public int StreamingChunkSizeCm { get; set; }
    public int CommandFocusHoldTicks { get; set; }
    public int WorkAreaPaddingCm { get; set; }
    public int WorkAreaMaxWidthCm { get; set; }
    public int WorkAreaMaxHeightCm { get; set; }
    public string ActiveHotZoneId { get; set; } = string.Empty;
    public MassNavigationHotZoneConfig[] HotZones { get; set; } = Array.Empty<MassNavigationHotZoneConfig>();

    public MassNavigationHotZoneConfig GetRequiredHotZone(string hotZoneId)
    {
        if (!TryGetHotZone(hotZoneId, out MassNavigationHotZoneConfig hotZone))
        {
            throw new InvalidOperationException($"MassNavigation world hot zone '{hotZoneId}' is not configured.");
        }

        return hotZone;
    }

    public bool TryGetHotZone(string hotZoneId, out MassNavigationHotZoneConfig hotZone)
    {
        int index = FindHotZoneIndex(hotZoneId);
        if (index < 0)
        {
            hotZone = null!;
            return false;
        }

        hotZone = HotZones[index];
        return true;
    }

    public void Validate(MassNavigationFlowSolverConfig solver)
    {
        if (solver == null)
        {
            throw new InvalidOperationException("MassNavigation world validation requires an explicit solver section.");
        }

        if (SolverWindowWidthCm != solver.FieldWidthCm ||
            SolverWindowHeightCm != solver.FieldHeightCm)
        {
            throw new InvalidOperationException(
                $"MassNavigation world solver window must match solver field size ({solver.FieldWidthCm}x{solver.FieldHeightCm} cm).");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < HotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = HotZones[i];
            zone.Validate();
            if (!ids.Add(zone.Id))
            {
                throw new InvalidOperationException($"MassNavigation world contains duplicate hot zone id '{zone.Id}'.");
            }

        }

        if (HotZones.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(ActiveHotZoneId))
            {
                throw new InvalidOperationException("MassNavigation world requires ActiveHotZoneId when hotspot debug landmarks are configured.");
            }

            GetRequiredHotZone(ActiveHotZoneId);
        }
        else if (!string.IsNullOrWhiteSpace(ActiveHotZoneId))
        {
            throw new InvalidOperationException("MassNavigation world ActiveHotZoneId must not be set without configured hot zones.");
        }

        if (StreamingChunkSizeCm < 0)
        {
            throw new InvalidOperationException("MassNavigation world requires StreamingChunkSizeCm >= 0 (0 = derive from board).");
        }

        if (CommandFocusHoldTicks < 0)
        {
            throw new InvalidOperationException("MassNavigation world requires CommandFocusHoldTicks >= 0.");
        }

        if (WorkAreaPaddingCm < 0)
        {
            throw new InvalidOperationException("MassNavigation world requires WorkAreaPaddingCm >= 0.");
        }

        if (WorkAreaMaxWidthCm <= 0 || WorkAreaMaxHeightCm <= 0)
        {
            throw new InvalidOperationException("MassNavigation world requires positive WorkAreaMaxWidthCm and WorkAreaMaxHeightCm.");
        }

        if (WorkAreaMaxWidthCm < SolverWindowWidthCm || WorkAreaMaxHeightCm < SolverWindowHeightCm)
        {
            throw new InvalidOperationException("MassNavigation world work area max must be at least the solver cache size.");
        }
    }

    private int FindHotZoneIndex(string hotZoneId)
    {
        for (int i = 0; i < HotZones.Length; i++)
        {
            if (string.Equals(HotZones[i].Id, hotZoneId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed class MassNavigationHotZoneConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public int WidthCm { get; set; }
    public int HeightCm { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("MassNavigation hot zone requires a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            throw new InvalidOperationException($"MassNavigation hot zone '{Id}' requires a non-empty label.");
        }

        if (WidthCm <= 0 || HeightCm <= 0)
        {
            throw new InvalidOperationException($"MassNavigation hot zone '{Id}' requires positive width and height.");
        }
    }
}
