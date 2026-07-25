using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Vehicle3D;

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
    public int ChainLinkCount { get; set; }
    public int QueryHitCapacity { get; set; }
    public int ReplaySteps { get; set; }
    public int ReplayGridSize { get; set; }
    public int ReplayBodySpacingCm { get; set; }
    public int ReplayCenterXCm { get; set; }
    public int ReplayBaseHeightCm { get; set; }
    public int ReplayLaneOffsetCm { get; set; }
    public int ReplayDifferenceStep { get; set; }
    public int ReplayDifferenceBodyIndex { get; set; }
    public float ReplayDifferenceVelocityXCmPerSecond { get; set; }
    public float ReplayDifferenceVelocityYCmPerSecond { get; set; }
    public float ReplayDifferenceVelocityZCmPerSecond { get; set; }
    public int BenchmarkDefaultBodies { get; set; }
    public int[] BenchmarkPresets { get; set; } = Array.Empty<int>();
    public int BenchmarkLaneColumns { get; set; }
    public int BenchmarkLaneDecks { get; set; }
    public int BenchmarkLaneSpacingCm { get; set; }
    public int BenchmarkDeckSpacingCm { get; set; }
    public int BenchmarkCycleSteps { get; set; }
    public int BenchmarkWaveCount { get; set; }
    public int BenchmarkBaseHeightCm { get; set; }
    public int BenchmarkArcHeightCm { get; set; }
    public int BenchmarkTravelHalfWidthCm { get; set; }
    public int BenchmarkSpeedCmPerSecond { get; set; }
    public float BenchmarkSpinRadiansPerSecond { get; set; }
    public float BenchmarkRealTimeBudgetMilliseconds { get; set; }
    public int ImpactSpeedCmPerSecond { get; set; }
    public float FrictionCoefficient { get; set; }
    public float MaximumRecoveryVelocityCmPerSecond { get; set; }
    public float SpringAngularFrequency { get; set; }
    public float SpringTwiceDampingRatio { get; set; }
    public Physics3DScannerRangeShowcaseConfig ScannerRange { get; set; } = new();
    public Physics3DMaterialHillShowcaseConfig MaterialHill { get; set; } = new();
    public Physics3DWindTunnelShowcaseConfig WindTunnel { get; set; } = new();
    public Physics3DScaleCityShowcaseConfig ScaleCity { get; set; } = new();
    public Physics3DCharacterTraversalShowcaseConfig CharacterTraversal { get; set; } = new();
    public Physics3DWheelLabShowcaseConfig WheelLab { get; set; } = new();
    public Physics3DRagdollLabShowcaseConfig RagdollLab { get; set; } = new();
    public Physics3DConstraintForgeShowcaseConfig ConstraintForge { get; set; } = new();

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
        RequirePositive(ChainLinkCount, nameof(ChainLinkCount));
        RequirePositive(QueryHitCapacity, nameof(QueryHitCapacity));
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
        long replayBodies = (long)ReplayGridSize * ReplayGridSize;
        if (ReplayDifferenceStep <= 0 || ReplayDifferenceStep > ReplaySteps)
        {
            throw new InvalidOperationException("replayDifferenceStep must be in [1, replaySteps].");
        }
        if (ReplayDifferenceBodyIndex < 0 || ReplayDifferenceBodyIndex >= replayBodies)
        {
            throw new InvalidOperationException("replayDifferenceBodyIndex must select one authored replay body.");
        }
        RequireFinite(ReplayDifferenceVelocityXCmPerSecond, nameof(ReplayDifferenceVelocityXCmPerSecond));
        RequireFinite(ReplayDifferenceVelocityYCmPerSecond, nameof(ReplayDifferenceVelocityYCmPerSecond));
        RequireFinite(ReplayDifferenceVelocityZCmPerSecond, nameof(ReplayDifferenceVelocityZCmPerSecond));
        if (ReplayDifferenceVelocityXCmPerSecond == 0f &&
            ReplayDifferenceVelocityYCmPerSecond == 0f &&
            ReplayDifferenceVelocityZCmPerSecond == 0f)
        {
            throw new InvalidOperationException("The configured replay difference velocity must be non-zero.");
        }
        RequirePositive(BenchmarkDefaultBodies, nameof(BenchmarkDefaultBodies));
        RequirePositive(BenchmarkLaneColumns, nameof(BenchmarkLaneColumns));
        RequirePositive(BenchmarkLaneDecks, nameof(BenchmarkLaneDecks));
        RequirePositive(BenchmarkLaneSpacingCm, nameof(BenchmarkLaneSpacingCm));
        RequirePositive(BenchmarkDeckSpacingCm, nameof(BenchmarkDeckSpacingCm));
        RequirePositive(BenchmarkCycleSteps, nameof(BenchmarkCycleSteps));
        RequirePositive(BenchmarkWaveCount, nameof(BenchmarkWaveCount));
        RequirePositive(BenchmarkBaseHeightCm, nameof(BenchmarkBaseHeightCm));
        RequirePositive(BenchmarkArcHeightCm, nameof(BenchmarkArcHeightCm));
        RequirePositive(BenchmarkTravelHalfWidthCm, nameof(BenchmarkTravelHalfWidthCm));
        RequirePositive(BenchmarkSpeedCmPerSecond, nameof(BenchmarkSpeedCmPerSecond));
        RequireFinitePositive(BenchmarkSpinRadiansPerSecond, nameof(BenchmarkSpinRadiansPerSecond));
        RequireFinitePositive(BenchmarkRealTimeBudgetMilliseconds, nameof(BenchmarkRealTimeBudgetMilliseconds));
        if (BenchmarkCycleSteps < 2)
        {
            throw new InvalidOperationException("benchmarkCycleSteps must contain at least two fixed steps.");
        }
        if (BenchmarkWaveCount > BenchmarkCycleSteps)
        {
            throw new InvalidOperationException("benchmarkWaveCount cannot exceed benchmarkCycleSteps.");
        }
        float rotatingBodyClearanceCm = BodySizeCm * MathF.Sqrt(3f);
        if (BenchmarkLaneSpacingCm <= rotatingBodyClearanceCm)
        {
            throw new InvalidOperationException(
                $"benchmarkLaneSpacingCm must exceed the rotating body clearance {rotatingBodyClearanceCm:0.###}cm.");
        }
        if (BenchmarkDeckSpacingCm <= rotatingBodyClearanceCm)
        {
            throw new InvalidOperationException(
                $"benchmarkDeckSpacingCm must exceed the rotating body clearance {rotatingBodyClearanceCm:0.###}cm.");
        }
        float maximumAdjacentWaveHeightDeltaCm = 0f;
        for (int wave = 0; wave < BenchmarkWaveCount; wave++)
        {
            int nextWave = (wave + 1) % BenchmarkWaveCount;
            int ageSteps = checked((wave * BenchmarkCycleSteps) / BenchmarkWaveCount);
            int nextAgeSteps = checked((nextWave * BenchmarkCycleSteps) / BenchmarkWaveCount);
            float heightCm = BenchmarkArcHeightAtAge(ageSteps, BenchmarkCycleSteps, BenchmarkArcHeightCm);
            float nextHeightCm = BenchmarkArcHeightAtAge(nextAgeSteps, BenchmarkCycleSteps, BenchmarkArcHeightCm);
            maximumAdjacentWaveHeightDeltaCm = MathF.Max(
                maximumAdjacentWaveHeightDeltaCm,
                MathF.Abs(nextHeightCm - heightCm));
        }
        float minimumDeckSpacingCm = rotatingBodyClearanceCm + maximumAdjacentWaveHeightDeltaCm;
        if (BenchmarkDeckSpacingCm <= minimumDeckSpacingCm)
        {
            throw new InvalidOperationException(
                $"benchmarkDeckSpacingCm must exceed {minimumDeckSpacingCm:0.###}cm so adjacent launch waves remain contact-free.");
        }
        RequirePositive(ImpactSpeedCmPerSecond, nameof(ImpactSpeedCmPerSecond));
        RequireFiniteNonNegative(FrictionCoefficient, nameof(FrictionCoefficient));
        RequireFiniteNonNegative(MaximumRecoveryVelocityCmPerSecond, nameof(MaximumRecoveryVelocityCmPerSecond));
        RequireFinitePositive(SpringAngularFrequency, nameof(SpringAngularFrequency));
        RequireFiniteNonNegative(SpringTwiceDampingRatio, nameof(SpringTwiceDampingRatio));
        ScannerRange.Validate(nameof(ScannerRange));
        MaterialHill.Validate(nameof(MaterialHill));
        WindTunnel.Validate(nameof(WindTunnel));
        ScaleCity.Validate(nameof(ScaleCity), BodySizeCm, BenchmarkPresets[0]);
        CharacterTraversal.Validate(nameof(CharacterTraversal));
        WheelLab.Validate(nameof(WheelLab));
        RagdollLab.Validate(nameof(RagdollLab));
        ConstraintForge.Validate(nameof(ConstraintForge));
        if (MaximumBodies < 2) throw new InvalidOperationException("maximumBodies must leave capacity for a floor and at least one mobile body.");
        if (1L + replayBodies > MaximumBodies)
        {
            throw new InvalidOperationException("The configured replay grid exceeds maximumBodies.");
        }
        if (1L + (7L * ScannerRange.TargetCount) > MaximumBodies)
        {
            throw new InvalidOperationException("The configured Scanner Range targets exceed maximumBodies.");
        }
        if (1L + (2L * MaterialHill.Lanes.Length) > MaximumBodies)
        {
            throw new InvalidOperationException("The configured Material Hill exhibits exceed maximumBodies.");
        }
        if ((long)ReplaySteps * replayBodies > int.MaxValue)
        {
            throw new InvalidOperationException("The configured replay state buffer exceeds the supported array length.");
        }
        int maximumBenchmarkBodies = MaximumBodies - 1;
        if (BenchmarkDefaultBodies > maximumBenchmarkBodies) throw new InvalidOperationException("benchmarkDefaultBodies must leave one body slot for the floor.");
        if (BenchmarkPresets.Length == 0) throw new InvalidOperationException("Physics3D showcase requires benchmarkPresets.");
        bool containsDefault = false;
        for (int i = 0; i < BenchmarkPresets.Length; i++)
        {
            if (BenchmarkPresets[i] <= 0 || BenchmarkPresets[i] > maximumBenchmarkBodies)
            {
                throw new InvalidOperationException($"benchmarkPresets[{i}] must leave one body slot for the floor.");
            }

            if (i > 0 && BenchmarkPresets[i] <= BenchmarkPresets[i - 1])
            {
                throw new InvalidOperationException("benchmarkPresets must be strictly increasing.");
            }

            containsDefault |= BenchmarkPresets[i] == BenchmarkDefaultBodies;
        }

        if (!containsDefault)
        {
            throw new InvalidOperationException("benchmarkDefaultBodies must be one of benchmarkPresets.");
        }

        long benchmarkPathCapacity = (long)BenchmarkLaneColumns * BenchmarkLaneDecks;
        int maximumSparseBodies = BenchmarkPresets[^1] - ScaleCity.InteractiveBodyLimit;
        if (benchmarkPathCapacity < maximumSparseBodies)
        {
            throw new InvalidOperationException(
                $"Scale City sparse path capacity {benchmarkPathCapacity} must cover {maximumSparseBodies} sparse bodies without path reuse.");
        }
    }

    private static float BenchmarkArcHeightAtAge(int ageSteps, int cycleSteps, int arcHeightCm)
    {
        float normalizedAge = ageSteps / (float)cycleSteps;
        return 4f * arcHeightCm * normalizedAge * (1f - normalizedAge);
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

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException($"Physics3D showcase requires finite {name}.");
    }
}

internal sealed class Physics3DWheelLabShowcaseConfig
{
    public int VehicleCapacity { get; set; }
    public int WheelCapacity { get; set; }
    public int QueryBatchCapacity { get; set; }
    public int ComparisonResultCapacity { get; set; }
    public int TrialTimeLimitTicks { get; set; }
    public int TrialRecommendedThrottleTicks { get; set; }
    public int TrialRecommendedBrakeTicks { get; set; }
    public Vehicle3DWheelKind InitialWheelKind { get; set; }
    public Vehicle3DWheelQueryKind ScanningQueryKind { get; set; }
    public float TrialInputDeadZone { get; set; }
    public float TrialMaximumLateralOffsetCm { get; set; }
    public float TrialCompletionMinimumZCm { get; set; }
    public float TrialStopSpeedKph { get; set; }
    public float TrialBrakeInputThreshold { get; set; }
    public float TrialMinimumBrakeStartSpeedKph { get; set; }
    public float RoadWidthCm { get; set; }
    public float RoadThicknessCm { get; set; }
    public float RoadStartZCm { get; set; }
    public float PotholeStartZCm { get; set; }
    public float PotholeEndZCm { get; set; }
    public float PotholeDepthCm { get; set; }
    public float PotholeTransitionLengthCm { get; set; }
    public float BankStartZCm { get; set; }
    public float BankEndZCm { get; set; }
    public float BankAngleDegrees { get; set; }
    public float PlatformGapStartZCm { get; set; }
    public float PlatformGapEndZCm { get; set; }
    public float RampStartZCm { get; set; }
    public float RampEndZCm { get; set; }
    public float RampAngleDegrees { get; set; }
    public float BrakeStartZCm { get; set; }
    public float BrakeEndZCm { get; set; }
    public float RoadEndZCm { get; set; }
    public int BumpCount { get; set; }
    public float BumpWidthCm { get; set; }
    public float BumpHeightCm { get; set; }
    public float BumpDepthCm { get; set; }
    public float FirstBumpZCm { get; set; }
    public float BumpSpacingCm { get; set; }
    public float MovingPlatformWidthCm { get; set; }
    public float MovingPlatformThicknessCm { get; set; }
    public float MovingPlatformTravelCm { get; set; }
    public float MovingPlatformRadiansPerStep { get; set; }
    public float MovingPlatformMaximumYawRadians { get; set; }
    public float StopWallHeightCm { get; set; }
    public float StopWallThicknessCm { get; set; }
    public float ChassisWidthCm { get; set; }
    public float ChassisHeightCm { get; set; }
    public float ChassisLengthCm { get; set; }
    public float ChassisMass { get; set; }
    public float SpawnXCm { get; set; }
    public float SpawnYCm { get; set; }
    public float SpawnZCm { get; set; }
    public float WheelRadiusCm { get; set; }
    public float WheelWidthCm { get; set; }
    public float WheelMass { get; set; }
    public float WheelTrackCm { get; set; }
    public float WheelBaseCm { get; set; }
    public float WheelMountYCm { get; set; }
    public float SuspensionMinimumLengthCm { get; set; }
    public float SuspensionRestLengthCm { get; set; }
    public float SuspensionMaximumLengthCm { get; set; }
    public float MaximumSteeringAngleDegrees { get; set; }
    public float SuspensionStiffness { get; set; }
    public float SuspensionDamping { get; set; }
    public float MaximumSuspensionForce { get; set; }
    public float LongitudinalGrip { get; set; }
    public float LateralGrip { get; set; }
    public float MaximumDriveForce { get; set; }
    public float MaximumBrakeForce { get; set; }
    public float BoxWheelForceScale { get; set; }
    public float MaximumLateralForce { get; set; }
    public float MaximumWheelAngularSpeedRadiansPerSecond { get; set; }
    public float AlignmentSpringAngularFrequency { get; set; }
    public float AlignmentSpringTwiceDampingRatio { get; set; }
    public float JointSuspensionSpringAngularFrequency { get; set; }
    public float JointSuspensionSpringTwiceDampingRatio { get; set; }
    public float LimitSpringAngularFrequency { get; set; }
    public float LimitSpringTwiceDampingRatio { get; set; }
    public float LineServoMaximumSpeed { get; set; }
    public float LineServoBaseSpeed { get; set; }
    public float LineServoMaximumForce { get; set; }
    public float AxleMotorMaximumForce { get; set; }
    public float AxleMotorSoftness { get; set; }
    public float ResetBelowYCm { get; set; }
    public float DebugLineThicknessCm { get; set; }
    public float DebugContactMarkerDiameterCm { get; set; }
    public float DebugNormalLengthCm { get; set; }
    public float DebugSlipScaleSeconds { get; set; }
    public float DebugMaximumSlipLengthCm { get; set; }
    public float DebugScanningWheelAlpha { get; set; }

    public void Validate(string parameterName)
    {
        RequireAtLeast(VehicleCapacity, 1, nameof(VehicleCapacity));
        RequireAtLeast(WheelCapacity, 4, nameof(WheelCapacity));
        RequireAtLeast(QueryBatchCapacity, 4, nameof(QueryBatchCapacity));
        RequireAtLeast(ComparisonResultCapacity, 3, nameof(ComparisonResultCapacity));
        RequireAtMost(ComparisonResultCapacity, 64, nameof(ComparisonResultCapacity));
        RequireAtLeast(TrialTimeLimitTicks, 1, nameof(TrialTimeLimitTicks));
        RequireAtMost(TrialTimeLimitTicks, 1_000_000, nameof(TrialTimeLimitTicks));
        RequireAtLeast(TrialRecommendedThrottleTicks, 1, nameof(TrialRecommendedThrottleTicks));
        RequireAtLeast(TrialRecommendedBrakeTicks, 1, nameof(TrialRecommendedBrakeTicks));
        if ((long)TrialRecommendedThrottleTicks + TrialRecommendedBrakeTicks > TrialTimeLimitTicks)
        {
            throw new InvalidOperationException(
                $"{parameterName} recommended throttle and brake ticks must fit inside trialTimeLimitTicks.");
        }
        if (QueryBatchCapacity < WheelCapacity)
        {
            throw new InvalidOperationException($"{parameterName}.queryBatchCapacity must cover wheelCapacity.");
        }

        if (!Enum.IsDefined(InitialWheelKind))
        {
            throw new InvalidOperationException($"{parameterName}.initialWheelKind is invalid.");
        }

        if (!Enum.IsDefined(ScanningQueryKind))
        {
            throw new InvalidOperationException($"{parameterName}.scanningQueryKind is invalid.");
        }

        RequireUnitIntervalExclusiveUpper(TrialInputDeadZone, nameof(TrialInputDeadZone));
        RequirePositive(TrialMaximumLateralOffsetCm, nameof(TrialMaximumLateralOffsetCm));
        RequirePositive(TrialStopSpeedKph, nameof(TrialStopSpeedKph));
        RequireUnitIntervalExclusiveLower(TrialBrakeInputThreshold, nameof(TrialBrakeInputThreshold));
        RequireNonNegative(TrialMinimumBrakeStartSpeedKph, nameof(TrialMinimumBrakeStartSpeedKph));

        RequirePositive(RoadWidthCm, nameof(RoadWidthCm));
        RequirePositive(RoadThicknessCm, nameof(RoadThicknessCm));
        RequirePositive(PotholeDepthCm, nameof(PotholeDepthCm));
        RequirePositive(PotholeTransitionLengthCm, nameof(PotholeTransitionLengthCm));
        if ((PotholeTransitionLengthCm * 2f) >= PotholeEndZCm - PotholeStartZCm)
        {
            throw new InvalidOperationException(
                $"{parameterName}.potholeTransitionLengthCm must leave a non-empty recessed floor.");
        }
        RequireAngle(BankAngleDegrees, nameof(BankAngleDegrees));
        RequireAngle(RampAngleDegrees, nameof(RampAngleDegrees));
        if (!(RoadStartZCm < PotholeStartZCm &&
              PotholeStartZCm < PotholeEndZCm &&
              PotholeEndZCm < BankStartZCm &&
              BankStartZCm < BankEndZCm &&
              BankEndZCm < PlatformGapStartZCm &&
              PlatformGapStartZCm < PlatformGapEndZCm &&
              PlatformGapEndZCm < RampStartZCm &&
              RampStartZCm < RampEndZCm &&
              RampEndZCm < BrakeStartZCm &&
              BrakeStartZCm < BrakeEndZCm &&
              BrakeEndZCm < RoadEndZCm))
        {
            throw new InvalidOperationException($"{parameterName} course Z coordinates must be strictly ordered from start to finish.");
        }

        RequireFinite(TrialCompletionMinimumZCm, nameof(TrialCompletionMinimumZCm));
        if (TrialCompletionMinimumZCm < BrakeStartZCm || TrialCompletionMinimumZCm > BrakeEndZCm)
        {
            throw new InvalidOperationException(
                $"{parameterName}.trialCompletionMinimumZCm must remain inside the braking zone.");
        }

        RequireAtLeast(BumpCount, 1, nameof(BumpCount));
        RequirePositive(BumpWidthCm, nameof(BumpWidthCm));
        RequirePositive(BumpHeightCm, nameof(BumpHeightCm));
        RequirePositive(BumpDepthCm, nameof(BumpDepthCm));
        RequirePositive(BumpSpacingCm, nameof(BumpSpacingCm));
        if (BumpWidthCm > RoadWidthCm)
        {
            throw new InvalidOperationException($"{parameterName}.bumpWidthCm cannot exceed roadWidthCm.");
        }

        float lastBumpZ = FirstBumpZCm + ((BumpCount - 1) * BumpSpacingCm);
        if (FirstBumpZCm <= RoadStartZCm || lastBumpZ >= PotholeStartZCm)
        {
            throw new InvalidOperationException($"{parameterName} speed bumps must remain between the start and pothole.");
        }

        RequirePositive(MovingPlatformWidthCm, nameof(MovingPlatformWidthCm));
        RequirePositive(MovingPlatformThicknessCm, nameof(MovingPlatformThicknessCm));
        RequirePositive(MovingPlatformTravelCm, nameof(MovingPlatformTravelCm));
        RequirePositive(MovingPlatformRadiansPerStep, nameof(MovingPlatformRadiansPerStep));
        RequirePositive(MovingPlatformMaximumYawRadians, nameof(MovingPlatformMaximumYawRadians));
        RequirePositive(StopWallHeightCm, nameof(StopWallHeightCm));
        RequirePositive(StopWallThicknessCm, nameof(StopWallThicknessCm));
        RequirePositive(ChassisWidthCm, nameof(ChassisWidthCm));
        RequirePositive(ChassisHeightCm, nameof(ChassisHeightCm));
        RequirePositive(ChassisLengthCm, nameof(ChassisLengthCm));
        RequirePositive(ChassisMass, nameof(ChassisMass));
        RequireFinite(SpawnXCm, nameof(SpawnXCm));
        RequireFinite(SpawnYCm, nameof(SpawnYCm));
        RequireFinite(SpawnZCm, nameof(SpawnZCm));
        if (SpawnZCm <= RoadStartZCm || SpawnZCm >= FirstBumpZCm)
        {
            throw new InvalidOperationException($"{parameterName}.spawnZCm must place the vehicle before the first speed bump.");
        }

        if ((MovingPlatformWidthCm - (2f * MovingPlatformTravelCm)) <= ChassisWidthCm)
        {
            throw new InvalidOperationException($"{parameterName} moving platform must retain a full chassis-width crossing at maximum travel.");
        }

        RequirePositive(WheelRadiusCm, nameof(WheelRadiusCm));
        RequirePositive(WheelWidthCm, nameof(WheelWidthCm));
        RequirePositive(WheelMass, nameof(WheelMass));
        RequirePositive(WheelTrackCm, nameof(WheelTrackCm));
        RequirePositive(WheelBaseCm, nameof(WheelBaseCm));
        RequireFinite(WheelMountYCm, nameof(WheelMountYCm));
        if (WheelTrackCm >= RoadWidthCm || WheelBaseCm >= ChassisLengthCm)
        {
            throw new InvalidOperationException($"{parameterName} wheel track/base must fit the authored chassis and road.");
        }

        RequireNonNegative(SuspensionMinimumLengthCm, nameof(SuspensionMinimumLengthCm));
        RequirePositive(SuspensionRestLengthCm, nameof(SuspensionRestLengthCm));
        RequirePositive(SuspensionMaximumLengthCm, nameof(SuspensionMaximumLengthCm));
        if (SuspensionMinimumLengthCm > SuspensionRestLengthCm ||
            SuspensionRestLengthCm > SuspensionMaximumLengthCm)
        {
            throw new InvalidOperationException($"{parameterName} suspension lengths must satisfy minimum <= rest <= maximum.");
        }

        RequireAngle(MaximumSteeringAngleDegrees, nameof(MaximumSteeringAngleDegrees));
        RequireNonNegative(SuspensionStiffness, nameof(SuspensionStiffness));
        RequireNonNegative(SuspensionDamping, nameof(SuspensionDamping));
        RequireNonNegative(MaximumSuspensionForce, nameof(MaximumSuspensionForce));
        RequireNonNegative(LongitudinalGrip, nameof(LongitudinalGrip));
        RequireNonNegative(LateralGrip, nameof(LateralGrip));
        RequireNonNegative(MaximumDriveForce, nameof(MaximumDriveForce));
        RequireNonNegative(MaximumBrakeForce, nameof(MaximumBrakeForce));
        RequirePositive(BoxWheelForceScale, nameof(BoxWheelForceScale));
        RequireNonNegative(MaximumLateralForce, nameof(MaximumLateralForce));
        RequireNonNegative(MaximumWheelAngularSpeedRadiansPerSecond, nameof(MaximumWheelAngularSpeedRadiansPerSecond));

        RequireSpring(AlignmentSpringAngularFrequency, AlignmentSpringTwiceDampingRatio, "alignmentSpring");
        RequireSpring(JointSuspensionSpringAngularFrequency, JointSuspensionSpringTwiceDampingRatio, "jointSuspensionSpring");
        RequireSpring(LimitSpringAngularFrequency, LimitSpringTwiceDampingRatio, "limitSpring");
        RequirePositive(LineServoMaximumSpeed, nameof(LineServoMaximumSpeed));
        RequireNonNegative(LineServoBaseSpeed, nameof(LineServoBaseSpeed));
        RequirePositive(LineServoMaximumForce, nameof(LineServoMaximumForce));
        RequirePositive(AxleMotorMaximumForce, nameof(AxleMotorMaximumForce));
        RequirePositive(AxleMotorSoftness, nameof(AxleMotorSoftness));
        RequireFinite(ResetBelowYCm, nameof(ResetBelowYCm));
        if (ResetBelowYCm >= -PotholeDepthCm)
        {
            throw new InvalidOperationException($"{parameterName}.resetBelowYCm must remain below the pothole floor.");
        }

        RequirePositive(DebugLineThicknessCm, nameof(DebugLineThicknessCm));
        RequirePositive(DebugContactMarkerDiameterCm, nameof(DebugContactMarkerDiameterCm));
        RequirePositive(DebugNormalLengthCm, nameof(DebugNormalLengthCm));
        RequirePositive(DebugSlipScaleSeconds, nameof(DebugSlipScaleSeconds));
        RequirePositive(DebugMaximumSlipLengthCm, nameof(DebugMaximumSlipLengthCm));
        if (!float.IsFinite(DebugScanningWheelAlpha) || DebugScanningWheelAlpha <= 0f || DebugScanningWheelAlpha > 1f)
        {
            throw new InvalidOperationException($"{parameterName}.debugScanningWheelAlpha must be in (0, 1].");
        }
    }

    private static void RequireAtLeast(int value, int minimum, string name)
    {
        if (value < minimum)
        {
            throw new InvalidOperationException($"WheelLab showcase requires {name} >= {minimum}.");
        }
    }

    private static void RequireAtMost(int value, int maximum, string name)
    {
        if (value > maximum)
        {
            throw new InvalidOperationException($"WheelLab showcase requires {name} <= {maximum}.");
        }
    }

    private static void RequirePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidOperationException($"WheelLab showcase requires finite {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new InvalidOperationException($"WheelLab showcase requires finite {name} >= 0.");
        }
    }

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidOperationException($"WheelLab showcase requires finite {name}.");
        }
    }

    private static void RequireAngle(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f || value >= 45f)
        {
            throw new InvalidOperationException($"WheelLab showcase requires {name} in (0, 45) degrees.");
        }
    }

    private static void RequireUnitIntervalExclusiveUpper(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f || value >= 1f)
        {
            throw new InvalidOperationException($"WheelLab showcase requires {name} in [0, 1).");
        }
    }

    private static void RequireUnitIntervalExclusiveLower(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f || value > 1f)
        {
            throw new InvalidOperationException($"WheelLab showcase requires {name} in (0, 1].");
        }
    }

    private static void RequireSpring(float angularFrequency, float twiceDampingRatio, string name)
    {
        RequirePositive(angularFrequency, $"{name}AngularFrequency");
        RequireNonNegative(twiceDampingRatio, $"{name}TwiceDampingRatio");
    }
}

internal sealed class Physics3DCharacterTraversalShowcaseConfig
{
    public string CharacterCameraId { get; set; } = string.Empty;
    public float CharacterCameraTargetHeightOffsetCm { get; set; }
    public int PlatformRouteTimeLimitTicks { get; set; }
    public int TraversalRouteTimeLimitTicks { get; set; }
    public float RouteFailureMinimumYCm { get; set; }
    public float RouteMaximumLateralOffsetCm { get; set; }
    public float RouteCompletionHeightToleranceCm { get; set; }
    public int ControllerCapacity { get; set; }
    public int BodySlotCapacity { get; set; }
    public int OverlapHitCapacity { get; set; }
    public float CharacterRadiusCm { get; set; }
    public float CharacterCylinderLengthCm { get; set; }
    public float CharacterMass { get; set; }
    public float MaximumGroundSpeedCmPerSecond { get; set; }
    public float MaximumGroundAccelerationCmPerSecondSquared { get; set; }
    public float MaximumAirSpeedCmPerSecond { get; set; }
    public float MaximumAirAccelerationCmPerSecondSquared { get; set; }
    public float JumpSpeedCmPerSecond { get; set; }
    public float MaximumSlopeDegrees { get; set; }
    public float SupportProbeDistanceCm { get; set; }
    public float SkinWidthCm { get; set; }
    public float MaximumStepHeightCm { get; set; }
    public float StepForwardProbeDistanceCm { get; set; }
    public float StepAssistSpeedCmPerSecond { get; set; }
    public int CoyoteTicks { get; set; }
    public float UprightMaximumSpeed { get; set; }
    public float UprightMaximumForce { get; set; }
    public float UprightAnchorRadiusCm { get; set; }
    public float UprightAnchorParkingYCm { get; set; }
    public float AttachProbeDistanceCm { get; set; }
    public float AttachSpeedCmPerSecond { get; set; }
    public float ClimbSpeedCmPerSecond { get; set; }
    public float LateralClimbSpeedCmPerSecond { get; set; }
    public float TraversalMaximumAccelerationCmPerSecondSquared { get; set; }
    public float LedgeProbeHeightCm { get; set; }
    public float LedgeProbeForwardCm { get; set; }
    public float LedgeProbeDownCm { get; set; }
    public float MinimumLedgeHeightCm { get; set; }
    public float HandClearanceRadiusCm { get; set; }
    public float MantleForwardCm { get; set; }
    public float MantleSpeedCmPerSecond { get; set; }
    public float MantleCompletionDistanceCm { get; set; }
    public float MinimumTopNormalY { get; set; }
    public float DetachUpSpeedCmPerSecond { get; set; }
    public float DetachOutSpeedCmPerSecond { get; set; }
    public float CourseStartXCm { get; set; }
    public float CourseStartZCm { get; set; }
    public float PlatformStationStartXCm { get; set; }
    public float PlatformStationStartZCm { get; set; }
    public float PlatformStationStartDeckSizeXCm { get; set; }
    public float PlatformStationStartDeckSizeZCm { get; set; }
    public float PlatformStationConveyorOffsetXCm { get; set; }
    public float PlatformStationConveyorCenterYCm { get; set; }
    public float PlatformStationConveyorSizeXCm { get; set; }
    public float PlatformStationConveyorSizeZCm { get; set; }
    public float PlatformStationConveyorSpeedCmPerSecond { get; set; }
    public float PlatformStationOneWayCenterXCm { get; set; }
    public float PlatformStationOneWayCenterYCm { get; set; }
    public float PlatformStationOneWaySizeXCm { get; set; }
    public float PlatformStationOneWaySizeZCm { get; set; }
    public float PlatformStationOneWayMinimumNormalAlignment { get; set; }
    public float PlatformStationOneWayBackfaceToleranceCm { get; set; }
    public float PlatformStationOneWayMaximumPassThroughRelativeSpeedCmPerSecond { get; set; }
    public float RampCenterXCm { get; set; }
    public float RampCenterYCm { get; set; }
    public float RampLengthCm { get; set; }
    public float RampHeightCm { get; set; }
    public float RampWidthCm { get; set; }
    public float RampAngleDegrees { get; set; }
    public float StepStartXCm { get; set; }
    public int StepCount { get; set; }
    public float StepDepthCm { get; set; }
    public float StepHeightCm { get; set; }
    public float StepWidthCm { get; set; }
    public float MovingPlatformCenterXCm { get; set; }
    public float MovingPlatformCenterYCm { get; set; }
    public float MovingPlatformTravelCm { get; set; }
    public float MovingPlatformSpeedRadiansPerStep { get; set; }
    public float PlatformSizeXCm { get; set; }
    public float PlatformSizeYCm { get; set; }
    public float PlatformSizeZCm { get; set; }
    public float RotatingPlatformCenterXCm { get; set; }
    public float RotatingPlatformCenterYCm { get; set; }
    public float RotatingPlatformRadiusCm { get; set; }
    public float RotatingPlatformRadiansPerStep { get; set; }
    public float LadderCenterXCm { get; set; }
    public float LadderCenterYCm { get; set; }
    public float LadderHeightCm { get; set; }
    public float LadderWidthCm { get; set; }
    public float LadderThicknessCm { get; set; }
    public float LadderDeckCenterXCm { get; set; }
    public float LadderDeckCenterYCm { get; set; }
    public float LadderDeckLengthCm { get; set; }
    public float WallCenterXCm { get; set; }
    public float WallCenterYCm { get; set; }
    public float WallHeightCm { get; set; }
    public float WallWidthCm { get; set; }
    public float WallThicknessCm { get; set; }
    public float WallDeckCenterXCm { get; set; }
    public float WallDeckCenterYCm { get; set; }
    public float WallDeckLengthCm { get; set; }
    public float DeckThicknessCm { get; set; }

    public void Validate(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(CharacterCameraId))
        {
            throw new InvalidOperationException("CharacterTraversal showcase requires an explicit CharacterCameraId.");
        }

        RequireFinite(CharacterCameraTargetHeightOffsetCm, nameof(CharacterCameraTargetHeightOffsetCm));
        RequirePositive(PlatformRouteTimeLimitTicks, nameof(PlatformRouteTimeLimitTicks));
        RequirePositive(TraversalRouteTimeLimitTicks, nameof(TraversalRouteTimeLimitTicks));
        RequireFinite(RouteFailureMinimumYCm, nameof(RouteFailureMinimumYCm));
        RequireFinitePositive(RouteMaximumLateralOffsetCm, nameof(RouteMaximumLateralOffsetCm));
        RequireFiniteNonNegative(RouteCompletionHeightToleranceCm, nameof(RouteCompletionHeightToleranceCm));
        RequirePositive(ControllerCapacity, nameof(ControllerCapacity));
        RequirePositive(BodySlotCapacity, nameof(BodySlotCapacity));
        RequirePositive(OverlapHitCapacity, nameof(OverlapHitCapacity));
        RequirePositive(StepCount, nameof(StepCount));
        RequireNonNegative(CoyoteTicks, nameof(CoyoteTicks));
        RequireFiniteRange(MaximumSlopeDegrees, 0f, 90f, nameof(MaximumSlopeDegrees));
        RequireFiniteRange(MinimumTopNormalY, 0f, 1.0001f, nameof(MinimumTopNormalY));

        RequireFinitePositive(CharacterRadiusCm, nameof(CharacterRadiusCm));
        RequireFinitePositive(CharacterCylinderLengthCm, nameof(CharacterCylinderLengthCm));
        RequireFinitePositive(CharacterMass, nameof(CharacterMass));
        RequireFinitePositive(MaximumGroundSpeedCmPerSecond, nameof(MaximumGroundSpeedCmPerSecond));
        RequireFinitePositive(MaximumGroundAccelerationCmPerSecondSquared, nameof(MaximumGroundAccelerationCmPerSecondSquared));
        RequireFinitePositive(MaximumAirSpeedCmPerSecond, nameof(MaximumAirSpeedCmPerSecond));
        RequireFinitePositive(MaximumAirAccelerationCmPerSecondSquared, nameof(MaximumAirAccelerationCmPerSecondSquared));
        RequireFinitePositive(JumpSpeedCmPerSecond, nameof(JumpSpeedCmPerSecond));
        RequireFinitePositive(SupportProbeDistanceCm, nameof(SupportProbeDistanceCm));
        RequireFinitePositive(SkinWidthCm, nameof(SkinWidthCm));
        RequireFinitePositive(MaximumStepHeightCm, nameof(MaximumStepHeightCm));
        RequireFinitePositive(StepForwardProbeDistanceCm, nameof(StepForwardProbeDistanceCm));
        RequireFinitePositive(StepAssistSpeedCmPerSecond, nameof(StepAssistSpeedCmPerSecond));
        RequireFinitePositive(UprightMaximumSpeed, nameof(UprightMaximumSpeed));
        RequireFinitePositive(UprightMaximumForce, nameof(UprightMaximumForce));
        RequireFinitePositive(UprightAnchorRadiusCm, nameof(UprightAnchorRadiusCm));
        if (!float.IsFinite(UprightAnchorParkingYCm))
        {
            throw new InvalidOperationException($"CharacterTraversal showcase requires finite {nameof(UprightAnchorParkingYCm)}.");
        }
        RequireFinitePositive(AttachProbeDistanceCm, nameof(AttachProbeDistanceCm));
        RequireFinitePositive(AttachSpeedCmPerSecond, nameof(AttachSpeedCmPerSecond));
        RequireFinitePositive(ClimbSpeedCmPerSecond, nameof(ClimbSpeedCmPerSecond));
        RequireFinitePositive(LateralClimbSpeedCmPerSecond, nameof(LateralClimbSpeedCmPerSecond));
        RequireFinitePositive(TraversalMaximumAccelerationCmPerSecondSquared, nameof(TraversalMaximumAccelerationCmPerSecondSquared));
        RequireFinitePositive(LedgeProbeHeightCm, nameof(LedgeProbeHeightCm));
        RequireFinitePositive(LedgeProbeForwardCm, nameof(LedgeProbeForwardCm));
        RequireFinitePositive(LedgeProbeDownCm, nameof(LedgeProbeDownCm));
        RequireFinitePositive(MinimumLedgeHeightCm, nameof(MinimumLedgeHeightCm));
        RequireFinitePositive(HandClearanceRadiusCm, nameof(HandClearanceRadiusCm));
        RequireFinitePositive(MantleForwardCm, nameof(MantleForwardCm));
        RequireFinitePositive(MantleSpeedCmPerSecond, nameof(MantleSpeedCmPerSecond));
        RequireFinitePositive(MantleCompletionDistanceCm, nameof(MantleCompletionDistanceCm));
        RequireFinitePositive(DetachUpSpeedCmPerSecond, nameof(DetachUpSpeedCmPerSecond));
        RequireFinitePositive(DetachOutSpeedCmPerSecond, nameof(DetachOutSpeedCmPerSecond));
        RequireFinitePositive(PlatformStationStartDeckSizeXCm, nameof(PlatformStationStartDeckSizeXCm));
        RequireFinitePositive(PlatformStationStartDeckSizeZCm, nameof(PlatformStationStartDeckSizeZCm));
        RequireFinitePositive(PlatformStationConveyorOffsetXCm, nameof(PlatformStationConveyorOffsetXCm));
        RequireFinitePositive(PlatformStationConveyorCenterYCm, nameof(PlatformStationConveyorCenterYCm));
        RequireFinitePositive(PlatformStationConveyorSizeXCm, nameof(PlatformStationConveyorSizeXCm));
        RequireFinitePositive(PlatformStationConveyorSizeZCm, nameof(PlatformStationConveyorSizeZCm));
        RequireFinitePositive(PlatformStationConveyorSpeedCmPerSecond, nameof(PlatformStationConveyorSpeedCmPerSecond));
        RequireFinite(PlatformStationOneWayCenterXCm, nameof(PlatformStationOneWayCenterXCm));
        RequireFinitePositive(PlatformStationOneWayCenterYCm, nameof(PlatformStationOneWayCenterYCm));
        RequireFinitePositive(PlatformStationOneWaySizeXCm, nameof(PlatformStationOneWaySizeXCm));
        RequireFinitePositive(PlatformStationOneWaySizeZCm, nameof(PlatformStationOneWaySizeZCm));
        RequireFiniteRange(
            PlatformStationOneWayMinimumNormalAlignment,
            -0.0001f,
            1.0001f,
            nameof(PlatformStationOneWayMinimumNormalAlignment));
        RequireFiniteNonNegative(
            PlatformStationOneWayBackfaceToleranceCm,
            nameof(PlatformStationOneWayBackfaceToleranceCm));
        RequireFiniteNonNegative(
            PlatformStationOneWayMaximumPassThroughRelativeSpeedCmPerSecond,
            nameof(PlatformStationOneWayMaximumPassThroughRelativeSpeedCmPerSecond));
        float conveyorMaximumX = RotatingPlatformCenterXCm +
                                 PlatformStationConveyorOffsetXCm +
                                 (PlatformStationConveyorSizeXCm * 0.5f);
        float oneWayMinimumX = PlatformStationOneWayCenterXCm -
                               (PlatformStationOneWaySizeXCm * 0.5f);
        if (oneWayMinimumX <= conveyorMaximumX)
        {
            throw new InvalidOperationException(
                $"{parameterName} one-way platform must begin after the conveyor without overlap.");
        }
        RequireFinitePositive(RampLengthCm, nameof(RampLengthCm));
        RequireFinitePositive(RampHeightCm, nameof(RampHeightCm));
        RequireFinitePositive(RampWidthCm, nameof(RampWidthCm));
        RequireFinitePositive(StepDepthCm, nameof(StepDepthCm));
        RequireFinitePositive(StepHeightCm, nameof(StepHeightCm));
        RequireFinitePositive(StepWidthCm, nameof(StepWidthCm));
        RequireFinitePositive(MovingPlatformTravelCm, nameof(MovingPlatformTravelCm));
        RequireFinitePositive(MovingPlatformSpeedRadiansPerStep, nameof(MovingPlatformSpeedRadiansPerStep));
        RequireFinitePositive(PlatformSizeXCm, nameof(PlatformSizeXCm));
        RequireFinitePositive(PlatformSizeYCm, nameof(PlatformSizeYCm));
        RequireFinitePositive(PlatformSizeZCm, nameof(PlatformSizeZCm));
        RequireFinitePositive(RotatingPlatformRadiusCm, nameof(RotatingPlatformRadiusCm));
        RequireFinitePositive(RotatingPlatformRadiansPerStep, nameof(RotatingPlatformRadiansPerStep));
        RequireFinitePositive(LadderHeightCm, nameof(LadderHeightCm));
        RequireFinitePositive(LadderWidthCm, nameof(LadderWidthCm));
        RequireFinitePositive(LadderThicknessCm, nameof(LadderThicknessCm));
        RequireFinitePositive(LadderDeckLengthCm, nameof(LadderDeckLengthCm));
        RequireFinitePositive(WallHeightCm, nameof(WallHeightCm));
        RequireFinitePositive(WallWidthCm, nameof(WallWidthCm));
        RequireFinitePositive(WallThicknessCm, nameof(WallThicknessCm));
        RequireFinitePositive(WallDeckLengthCm, nameof(WallDeckLengthCm));
        RequireFinitePositive(DeckThicknessCm, nameof(DeckThicknessCm));

        if (SkinWidthCm >= CharacterRadiusCm)
        {
            throw new InvalidOperationException($"{parameterName}.skinWidthCm must be smaller than characterRadiusCm.");
        }

        if (MaximumStepHeightCm > StepHeightCm * 2f)
        {
            throw new InvalidOperationException($"{parameterName}.maximumStepHeightCm must match the authored step course scale.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0) throw new InvalidOperationException($"CharacterTraversal showcase requires {name} > 0.");
    }

    private static void RequireNonNegative(int value, string name)
    {
        if (value < 0) throw new InvalidOperationException($"CharacterTraversal showcase requires {name} >= 0.");
    }

    private static void RequireFinitePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidOperationException($"CharacterTraversal showcase requires finite {name} > 0.");
        }
    }

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidOperationException($"CharacterTraversal showcase requires finite {name}.");
        }
    }

    private static void RequireFiniteNonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new InvalidOperationException($"CharacterTraversal showcase requires finite {name} >= 0.");
        }
    }

    private static void RequireFiniteRange(float value, float minimumExclusive, float maximumExclusive, string name)
    {
        if (!float.IsFinite(value) || value <= minimumExclusive || value >= maximumExclusive)
        {
            throw new InvalidOperationException(
                $"CharacterTraversal showcase requires {name} in ({minimumExclusive}, {maximumExclusive}).");
        }
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
