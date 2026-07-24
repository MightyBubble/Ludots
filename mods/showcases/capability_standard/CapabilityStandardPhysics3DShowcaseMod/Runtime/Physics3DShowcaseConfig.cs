using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public Physics3DShowcaseScene InitialScene { get; set; }
    public int MaximumBodies { get; set; }
    public int VisibleBodyLimit { get; set; }
    public int PanelRefreshHz { get; set; }
    public int FloorSizeCm { get; set; }
    public int FloorThicknessCm { get; set; }
    public int BodySizeCm { get; set; }
    public int PyramidRows { get; set; }
    public int PyramidCenterXCm { get; set; }
    public int PyramidCenterZCm { get; set; }
    public int PyramidGapCm { get; set; }
    public int SpherePyramidRows { get; set; }
    public int SpherePyramidCenterXCm { get; set; }
    public int SpherePyramidCenterZCm { get; set; }
    public int SpherePyramidSpacingCm { get; set; }
    public int CapsulePyramidRows { get; set; }
    public int CapsulePyramidBaseColumns { get; set; }
    public int CapsulePyramidCenterXCm { get; set; }
    public int CapsulePyramidCenterZCm { get; set; }
    public int CapsulePyramidSpacingCm { get; set; }
    public int StackingRailThicknessCm { get; set; }
    public int StackingRailHeightCm { get; set; }
    public int StackingRailClearanceCm { get; set; }
    public int ChainLinkCount { get; set; }
    public int CcdSpeedCmPerSecond { get; set; }
    public int QueryDistanceCm { get; set; }
    public int QueryHitCapacity { get; set; }
    public int ContactEventCapacity { get; set; }
    public int ReplaySteps { get; set; }
    public int ReplayGridSize { get; set; }
    public int ReplayBodySpacingCm { get; set; }
    public int ReplayCenterXCm { get; set; }
    public int ReplayBaseHeightCm { get; set; }
    public int ReplayLaneOffsetCm { get; set; }
    public int BenchmarkDefaultBodies { get; set; }
    public int[] BenchmarkPresets { get; set; } = Array.Empty<int>();
    public int BenchmarkColumns { get; set; }
    public int BenchmarkDepth { get; set; }
    public int BenchmarkSpacingCm { get; set; }
    public int BenchmarkBaseHeightCm { get; set; }
    public int BenchmarkRecycleHeightCm { get; set; }
    public int BenchmarkTravelHalfWidthCm { get; set; }
    public int BenchmarkSpeedCmPerSecond { get; set; }
    public int ImpactSpeedCmPerSecond { get; set; }
    public float FrictionCoefficient { get; set; }
    public float MaximumRecoveryVelocityCmPerSecond { get; set; }
    public float SpringAngularFrequency { get; set; }
    public float SpringTwiceDampingRatio { get; set; }

    public static Physics3DShowcaseConfig Load(JsonObject configObject)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        Physics3DShowcaseConfig config = configObject.Deserialize<Physics3DShowcaseConfig>(options)
            ?? throw new InvalidOperationException("Physics3D showcase config deserialized to null.");
        config.Validate();
        return config;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(MapId)) throw new InvalidOperationException("Physics3D showcase requires a mapId.");
        if (!Enum.IsDefined(InitialScene)) throw new InvalidOperationException("Physics3D showcase initialScene is invalid.");
        RequirePositive(MaximumBodies, nameof(MaximumBodies));
        RequirePositive(VisibleBodyLimit, nameof(VisibleBodyLimit));
        if (VisibleBodyLimit > MaximumBodies) throw new InvalidOperationException("visibleBodyLimit cannot exceed maximumBodies.");
        RequirePositive(PanelRefreshHz, nameof(PanelRefreshHz));
        RequirePositive(FloorSizeCm, nameof(FloorSizeCm));
        RequirePositive(FloorThicknessCm, nameof(FloorThicknessCm));
        RequirePositive(BodySizeCm, nameof(BodySizeCm));
        RequirePositive(PyramidRows, nameof(PyramidRows));
        RequireNonNegative(PyramidGapCm, nameof(PyramidGapCm));
        RequirePositive(SpherePyramidRows, nameof(SpherePyramidRows));
        RequirePositive(SpherePyramidSpacingCm, nameof(SpherePyramidSpacingCm));
        if ((long)SpherePyramidSpacingCm * SpherePyramidSpacingCm >= 2L * BodySizeCm * BodySizeCm)
        {
            throw new InvalidOperationException("spherePyramidSpacingCm must let every upper sphere rest on four spheres below it.");
        }
        RequirePositive(CapsulePyramidRows, nameof(CapsulePyramidRows));
        RequirePositive(CapsulePyramidBaseColumns, nameof(CapsulePyramidBaseColumns));
        if (CapsulePyramidBaseColumns < CapsulePyramidRows)
        {
            throw new InvalidOperationException("capsulePyramidBaseColumns must be at least capsulePyramidRows.");
        }
        RequirePositive(CapsulePyramidSpacingCm, nameof(CapsulePyramidSpacingCm));
        if (CapsulePyramidSpacingCm >= BodySizeCm * 2L)
        {
            throw new InvalidOperationException("capsulePyramidSpacingCm must let every upper capsule rest on two capsules below it.");
        }
        RequirePositive(StackingRailThicknessCm, nameof(StackingRailThicknessCm));
        RequirePositive(StackingRailHeightCm, nameof(StackingRailHeightCm));
        RequireNonNegative(StackingRailClearanceCm, nameof(StackingRailClearanceCm));
        RequirePositive(ChainLinkCount, nameof(ChainLinkCount));
        RequirePositive(CcdSpeedCmPerSecond, nameof(CcdSpeedCmPerSecond));
        RequirePositive(QueryDistanceCm, nameof(QueryDistanceCm));
        RequirePositive(QueryHitCapacity, nameof(QueryHitCapacity));
        RequirePositive(ContactEventCapacity, nameof(ContactEventCapacity));
        RequirePositive(ReplaySteps, nameof(ReplaySteps));
        RequirePositive(ReplayGridSize, nameof(ReplayGridSize));
        RequirePositive(ReplayBodySpacingCm, nameof(ReplayBodySpacingCm));
        RequireNonNegative(ReplayBaseHeightCm, nameof(ReplayBaseHeightCm));
        RequirePositive(ReplayLaneOffsetCm, nameof(ReplayLaneOffsetCm));
        if (ReplayBodySpacingCm <= BodySizeCm)
        {
            throw new InvalidOperationException("replayBodySpacingCm must exceed bodySizeCm so the two recorded runs begin contact-free.");
        }
        long replaySpanCm = (long)(ReplayGridSize - 1) * ReplayBodySpacingCm;
        if ((2L * ReplayLaneOffsetCm) <= replaySpanCm + BodySizeCm)
        {
            throw new InvalidOperationException("replayLaneOffsetCm must leave a visible gap between recorded and replayed lanes.");
        }
        RequirePositive(BenchmarkDefaultBodies, nameof(BenchmarkDefaultBodies));
        RequirePositive(BenchmarkColumns, nameof(BenchmarkColumns));
        RequirePositive(BenchmarkDepth, nameof(BenchmarkDepth));
        RequirePositive(BenchmarkSpacingCm, nameof(BenchmarkSpacingCm));
        RequirePositive(BenchmarkBaseHeightCm, nameof(BenchmarkBaseHeightCm));
        RequireNonNegative(BenchmarkRecycleHeightCm, nameof(BenchmarkRecycleHeightCm));
        RequirePositive(BenchmarkTravelHalfWidthCm, nameof(BenchmarkTravelHalfWidthCm));
        RequirePositive(BenchmarkSpeedCmPerSecond, nameof(BenchmarkSpeedCmPerSecond));
        if (BenchmarkRecycleHeightCm >= BenchmarkBaseHeightCm)
        {
            throw new InvalidOperationException("benchmarkRecycleHeightCm must be below benchmarkBaseHeightCm.");
        }
        if (BenchmarkSpacingCm <= BodySizeCm)
        {
            throw new InvalidOperationException("benchmarkSpacingCm must exceed bodySizeCm so the throughput scene does not become an implicit contact pile.");
        }
        long benchmarkHalfSpanCm = ((long)(BenchmarkColumns - 1) * BenchmarkSpacingCm) / 2L;
        if (BenchmarkTravelHalfWidthCm <= benchmarkHalfSpanCm + (BodySizeCm / 2L))
        {
            throw new InvalidOperationException("benchmarkTravelHalfWidthCm must contain the complete authored body stream.");
        }
        RequirePositive(ImpactSpeedCmPerSecond, nameof(ImpactSpeedCmPerSecond));
        RequireFiniteNonNegative(FrictionCoefficient, nameof(FrictionCoefficient));
        RequireFiniteNonNegative(MaximumRecoveryVelocityCmPerSecond, nameof(MaximumRecoveryVelocityCmPerSecond));
        RequireFinitePositive(SpringAngularFrequency, nameof(SpringAngularFrequency));
        RequireFiniteNonNegative(SpringTwiceDampingRatio, nameof(SpringTwiceDampingRatio));
        if (MaximumBodies < 2) throw new InvalidOperationException("maximumBodies must leave capacity for a floor and at least one mobile body.");
        long pyramidBodies = (long)PyramidRows * (PyramidRows + 1) * ((2L * PyramidRows) + 1) / 6;
        long sphereBodies = (long)SpherePyramidRows * (SpherePyramidRows + 1) * ((2L * SpherePyramidRows) + 1) / 6;
        long capsuleBodies = ((long)CapsulePyramidRows * CapsulePyramidBaseColumns) -
                             ((long)CapsulePyramidRows * (CapsulePyramidRows - 1) / 2);
        const int stackingRailBodies = 6;
        if (1L + pyramidBodies + sphereBodies + capsuleBodies + stackingRailBodies > MaximumBodies)
        {
            throw new InvalidOperationException("The configured stacking exhibits exceed maximumBodies.");
        }
        long replayBodies = (long)ReplayGridSize * ReplayGridSize;
        if (1L + replayBodies > MaximumBodies)
        {
            throw new InvalidOperationException("The configured replay grid exceeds maximumBodies.");
        }
        if ((long)ReplaySteps * replayBodies > int.MaxValue)
        {
            throw new InvalidOperationException("The configured replay state buffer exceeds the supported array length.");
        }
        int maximumBenchmarkBodies = MaximumBodies - 1;
        if (BenchmarkDefaultBodies > maximumBenchmarkBodies) throw new InvalidOperationException("benchmarkDefaultBodies must leave one body slot for the floor.");
        if (BenchmarkPresets.Length == 0) throw new InvalidOperationException("Physics3D showcase requires benchmarkPresets.");
        for (int i = 0; i < BenchmarkPresets.Length; i++)
        {
            if (BenchmarkPresets[i] <= 0 || BenchmarkPresets[i] > maximumBenchmarkBodies)
            {
                throw new InvalidOperationException($"benchmarkPresets[{i}] must leave one body slot for the floor.");
            }
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0) throw new InvalidOperationException($"Physics3D showcase requires {name} > 0.");
    }

    private static void RequireNonNegative(int value, string name)
    {
        if (value < 0) throw new InvalidOperationException($"Physics3D showcase requires {name} >= 0.");
    }

    private static void RequireFinitePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f) throw new InvalidOperationException($"Physics3D showcase requires finite {name} > 0.");
    }

    private static void RequireFiniteNonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f) throw new InvalidOperationException($"Physics3D showcase requires finite {name} >= 0.");
    }
}

internal sealed class Physics3DShowcaseConfigLoader
{
    public const string RelativePath = "CapabilityStandardPhysics3DShowcaseConfig.json";
    private readonly ConfigPipeline _pipeline;

    public Physics3DShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Physics3DShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"Physics3D showcase config '{RelativePath}' is not registered.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Physics3D showcase config '{RelativePath}' must use Replace policy.");
        }

        JsonObject merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject
            ?? throw new InvalidOperationException($"Physics3D showcase config '{RelativePath}' is missing.");
        return Physics3DShowcaseConfig.Load(merged);
    }
}
