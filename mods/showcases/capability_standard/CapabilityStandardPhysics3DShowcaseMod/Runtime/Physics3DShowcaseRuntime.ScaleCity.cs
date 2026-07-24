using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private int _scaleCityFirstInteractiveBodyIndex;
    private int _scaleCityInteractiveBodyCount;
    private int _scaleCityFirstSparseBodyIndex;
    private int _scaleCitySparseBodyCount;
    private int _scaleCityLastLauncherWaveIndex = -1;
    private int _scaleCityInteractiveRelaunchedBodiesLastStep;
    private int _scaleCitySparseRecycledBodiesLastStep;
    private float _scaleCityWindAccelerationXCmPerSecondSquared;
    private double[] _scaleCityPerformanceSamples = Array.Empty<double>();
    private double[] _scaleCityPerformanceSortWorkspace = Array.Empty<double>();
    private int _scaleCityPerformanceSampleCount;
    private int _scaleCityPerformanceNextSampleIndex;
    private double _scaleCityStepP50Milliseconds;
    private double _scaleCityStepP95Milliseconds;
    private double _scaleCityStepP99Milliseconds;

    internal Physics3DScaleCityShowcaseState ScaleCityState
    {
        get
        {
            if (!_isActive || _scene != Physics3DShowcaseScene.ScaleCity)
            {
                return Physics3DScaleCityShowcaseState.Empty;
            }

            double budgetMilliseconds = ActiveConfig.BenchmarkRealTimeBudgetMilliseconds;
            Physics3DScaleCityPerformanceStatus performanceStatus =
                _scaleCityPerformanceSampleCount < _scaleCityPerformanceSamples.Length
                    ? Physics3DScaleCityPerformanceStatus.Warming
                    : _scaleCityStepP95Milliseconds < budgetMilliseconds &&
                      _scaleCityStepP99Milliseconds < budgetMilliseconds
                        ? Physics3DScaleCityPerformanceStatus.Pass
                        : Physics3DScaleCityPerformanceStatus.OverBudget;
            return new Physics3DScaleCityShowcaseState(
                InteractiveBodies: _scaleCityInteractiveBodyCount,
                SparseBodies: _scaleCitySparseBodyCount,
                ContactPairs: RequirePhysicsWorld().ContactPairCount,
                WindAccelerationXCmPerSecondSquared: _scaleCityWindAccelerationXCmPerSecondSquared,
                LastLauncherWaveIndex: _scaleCityLastLauncherWaveIndex,
                InteractiveRelaunchedBodiesLastStep: _scaleCityInteractiveRelaunchedBodiesLastStep,
                SparseRecycledBodiesLastStep: _scaleCitySparseRecycledBodiesLastStep,
                PerformanceSampleCount: _scaleCityPerformanceSampleCount,
                PerformanceWindowCapacity: _scaleCityPerformanceSamples.Length,
                StepP50Milliseconds: _scaleCityStepP50Milliseconds,
                StepP95Milliseconds: _scaleCityStepP95Milliseconds,
                StepP99Milliseconds: _scaleCityStepP99Milliseconds,
                PerformanceBudgetMilliseconds: budgetMilliseconds,
                PerformanceStatus: performanceStatus);
        }
    }

    internal void RecordScaleCityPerformanceSampleForTests(double elapsedMilliseconds)
    {
        if (!_isActive || _scene != Physics3DShowcaseScene.ScaleCity)
        {
            throw new InvalidOperationException("Scale City performance samples require an active Scale City scene.");
        }

        RecordScaleCityPerformanceSample(elapsedMilliseconds);
    }

    internal bool IsScaleCitySparseBody(Physics3DBodyId body)
    {
        if (!_isActive || _scene != Physics3DShowcaseScene.ScaleCity)
        {
            return false;
        }

        int end = _scaleCityFirstSparseBodyIndex + _scaleCitySparseBodyCount;
        for (int bodyIndex = _scaleCityFirstSparseBodyIndex; bodyIndex < end; bodyIndex++)
        {
            if (_bodyIds[bodyIndex] == body)
            {
                return true;
            }
        }

        return false;
    }

    private void BuildScaleCityScene(int bodyCount)
    {
        ValidateBenchmarkBodyCount(bodyCount);
        Physics3DShowcaseConfig config = ActiveConfig;
        Physics3DScaleCityShowcaseConfig scaleCity = config.ScaleCity;
        EnsureScaleCityPerformanceStorage(scaleCity.PerformanceWindowSampleCount);
        ResetScaleCityPerformanceWindow();
        int pathCount = checked(config.BenchmarkLaneColumns * config.BenchmarkLaneDecks);
        int interactiveBodyCount = Math.Min(bodyCount, scaleCity.InteractiveBodyLimit);
        int sparseBodyCount = bodyCount - interactiveBodyCount;
        if (sparseBodyCount > pathCount)
        {
            throw new InvalidOperationException(
                $"Scale City requires one unique sparse path per background body; requested {sparseBodyCount}, configured {pathCount}.");
        }

        if (scaleCity.LauncherWaveCount > interactiveBodyCount)
        {
            throw new InvalidOperationException(
                $"Scale City launcherWaveCount {scaleCity.LauncherWaveCount} exceeds the active foreground count {interactiveBodyCount}.");
        }

        float fixedDeltaSeconds = RequirePhysicsWorld().FixedDeltaSeconds;
        float authoredSpeed = (2f * config.BenchmarkTravelHalfWidthCm) /
                              (config.BenchmarkCycleSteps * fixedDeltaSeconds);
        if (MathF.Abs(authoredSpeed - config.BenchmarkSpeedCmPerSecond) > 0.01f)
        {
            throw new InvalidOperationException(
                $"Scale City sparse speed {config.BenchmarkSpeedCmPerSecond}cm/s does not traverse its authored width in " +
                $"{config.BenchmarkCycleSteps} fixed steps; expected {authoredSpeed:0.###}cm/s.");
        }

        _scaleCityInteractiveBodyCount = interactiveBodyCount;
        _scaleCitySparseBodyCount = sparseBodyCount;
        _scaleCityLastLauncherWaveIndex = -1;
        _scaleCityInteractiveRelaunchedBodiesLastStep = 0;
        _scaleCitySparseRecycledBodiesLastStep = 0;
        _scaleCityWindAccelerationXCmPerSecondSquared = 0f;
        _benchmarkPathCount = pathCount;
        _benchmarkWaveCount = config.BenchmarkWaveCount;
        _benchmarkRecycledBodiesLastStep = 0;

        AddFloor();
        _scaleCityFirstInteractiveBodyIndex = _bodyCount;
        for (int interactiveIndex = 0; interactiveIndex < interactiveBodyCount; interactiveIndex++)
        {
            CreateScaleCityInteractiveState(
                interactiveIndex,
                launched: false,
                out Vector3 position,
                out Quaternion orientation,
                out Vector3 linearVelocity);
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(config.BodySizeCm),
                0f,
                position,
                orientation,
                linearVelocity,
                Vector3.Zero,
                Physics3DContinuousDetectionMode.Passive,
                ScaleCityInteractiveColor(interactiveIndex));
        }

        _scaleCityFirstSparseBodyIndex = _bodyCount;
        for (int sparseIndex = 0; sparseIndex < sparseBodyCount; sparseIndex++)
        {
            int deck = sparseIndex / config.BenchmarkLaneColumns;
            int wave = deck % config.BenchmarkWaveCount;
            int ageSteps = BenchmarkWaveAgeSteps(wave, config.BenchmarkWaveCount);
            CreateSparseBenchmarkState(
                sparseIndex,
                ageSteps,
                out Vector3 position,
                out Quaternion orientation,
                out Vector3 linearVelocity,
                out Vector3 angularVelocity);
            Vector4 color = (deck % 3) switch
            {
                0 => DynamicBlue,
                1 => DynamicGold,
                _ => DynamicGreen
            };
            AddOwnedBody(
                Physics3DBodyKind.Dynamic,
                _boxShape,
                Physics3DShapeKind.Box,
                new Vector3(config.BodySizeCm),
                0f,
                position,
                orientation,
                linearVelocity,
                angularVelocity,
                Physics3DContinuousDetectionMode.Passive,
                color);
        }

        _benchmarkBodies = bodyCount;
    }

    private void EnsureScaleCityPerformanceStorage(int capacity)
    {
        if (_scaleCityPerformanceSamples.Length == capacity)
        {
            return;
        }

        _scaleCityPerformanceSamples = new double[capacity];
        _scaleCityPerformanceSortWorkspace = new double[capacity];
    }

    private void ResetScaleCityPerformanceWindow()
    {
        _scaleCityPerformanceSampleCount = 0;
        _scaleCityPerformanceNextSampleIndex = 0;
        _scaleCityStepP50Milliseconds = 0d;
        _scaleCityStepP95Milliseconds = 0d;
        _scaleCityStepP99Milliseconds = 0d;
        Array.Clear(_scaleCityPerformanceSamples, 0, _scaleCityPerformanceSamples.Length);
        Array.Clear(_scaleCityPerformanceSortWorkspace, 0, _scaleCityPerformanceSortWorkspace.Length);
    }

    private void RecordScaleCityPerformanceSample(double elapsedMilliseconds)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0d)
        {
            throw new InvalidOperationException(
                $"Scale City performance sample must be finite and non-negative, but was {elapsedMilliseconds}.");
        }

        _scaleCityPerformanceSamples[_scaleCityPerformanceNextSampleIndex] = elapsedMilliseconds;
        _scaleCityPerformanceNextSampleIndex++;
        if (_scaleCityPerformanceNextSampleIndex == _scaleCityPerformanceSamples.Length)
        {
            _scaleCityPerformanceNextSampleIndex = 0;
        }

        if (_scaleCityPerformanceSampleCount < _scaleCityPerformanceSamples.Length)
        {
            _scaleCityPerformanceSampleCount++;
        }

        Array.Copy(
            _scaleCityPerformanceSamples,
            _scaleCityPerformanceSortWorkspace,
            _scaleCityPerformanceSampleCount);
        SortScaleCityPerformanceWorkspace();
        _scaleCityStepP50Milliseconds = ScaleCityPercentile(0.50d);
        _scaleCityStepP95Milliseconds = ScaleCityPercentile(0.95d);
        _scaleCityStepP99Milliseconds = ScaleCityPercentile(0.99d);
    }

    private void SortScaleCityPerformanceWorkspace()
    {
        for (int index = 1; index < _scaleCityPerformanceSampleCount; index++)
        {
            double value = _scaleCityPerformanceSortWorkspace[index];
            int insertionIndex = index;
            while (insertionIndex > 0 &&
                   _scaleCityPerformanceSortWorkspace[insertionIndex - 1] > value)
            {
                _scaleCityPerformanceSortWorkspace[insertionIndex] =
                    _scaleCityPerformanceSortWorkspace[insertionIndex - 1];
                insertionIndex--;
            }

            _scaleCityPerformanceSortWorkspace[insertionIndex] = value;
        }
    }

    private double ScaleCityPercentile(double percentile)
    {
        if (_scaleCityPerformanceSampleCount == 0)
        {
            return 0d;
        }

        double index = (_scaleCityPerformanceSampleCount - 1) * percentile;
        int lowerIndex = (int)index;
        int upperIndex = Math.Min(lowerIndex + 1, _scaleCityPerformanceSampleCount - 1);
        double fraction = index - lowerIndex;
        double lower = _scaleCityPerformanceSortWorkspace[lowerIndex];
        return lower + ((_scaleCityPerformanceSortWorkspace[upperIndex] - lower) * fraction);
    }

    private void PrepareScaleCityFixedStep()
    {
        _scaleCityInteractiveRelaunchedBodiesLastStep = 0;
        _scaleCitySparseRecycledBodiesLastStep = 0;
        RelaunchScaleCityInteractiveWave();
        RecycleSparseBenchmarkWaves();
        ApplyScaleCityWind();
        _benchmarkRecycledBodiesLastStep =
            _scaleCityInteractiveRelaunchedBodiesLastStep + _scaleCitySparseRecycledBodiesLastStep;
    }

    private void RelaunchScaleCityInteractiveWave()
    {
        Physics3DScaleCityShowcaseConfig config = ActiveConfig.ScaleCity;
        long nextCompletedStep = _sceneStep + 1;
        if (nextCompletedStep % config.LauncherIntervalTicks != 0)
        {
            return;
        }

        int launcherSequence = checked((int)(nextCompletedStep / config.LauncherIntervalTicks) - 1);
        int waveIndex = launcherSequence % config.LauncherWaveCount;
        _scaleCityLastLauncherWaveIndex = waveIndex;
        for (int interactiveIndex = waveIndex;
             interactiveIndex < _scaleCityInteractiveBodyCount;
             interactiveIndex += config.LauncherWaveCount)
        {
            CreateScaleCityInteractiveState(
                interactiveIndex,
                launched: true,
                out Vector3 position,
                out Quaternion orientation,
                out Vector3 linearVelocity);
            var state = new Physics3DBodyState
            {
                PositionCm = position,
                Orientation = orientation,
                LinearVelocityCmPerSecond = linearVelocity,
                AngularVelocityRadiansPerSecond = Vector3.Zero,
                Awake = true
            };
            SetBodyStateAndPose(_scaleCityFirstInteractiveBodyIndex + interactiveIndex, in state);
            _scaleCityInteractiveRelaunchedBodiesLastStep++;
        }
    }

    private void ApplyScaleCityWind()
    {
        Physics3DScaleCityShowcaseConfig config = ActiveConfig.ScaleCity;
        int cycleTick = (int)(_sceneStep % config.WindCycleTicks);
        float phase = cycleTick / (float)config.WindCycleTicks;
        float accelerationX = MathF.Cos(phase * MathF.Tau) * config.WindAccelerationCmPerSecondSquared;
        _scaleCityWindAccelerationXCmPerSecondSquared = accelerationX;
        Vector3 acceleration = new(accelerationX, 0f, 0f);
        IPhysics3DWorld physics = RequirePhysicsWorld();
        int end = _scaleCityFirstInteractiveBodyIndex + _scaleCityInteractiveBodyCount;
        for (int bodyIndex = _scaleCityFirstInteractiveBodyIndex; bodyIndex < end; bodyIndex++)
        {
            physics.EnqueueAcceleration(_bodyIds[bodyIndex], acceleration);
        }
    }

    private void RecycleSparseBenchmarkWaves()
    {
        if (_scaleCitySparseBodyCount == 0)
        {
            return;
        }

        Physics3DShowcaseConfig config = ActiveConfig;
        int cycleStep = (int)(_sceneStep % config.BenchmarkCycleSteps);
        int laneColumns = config.BenchmarkLaneColumns;
        int usedDeckCount = (_scaleCitySparseBodyCount + laneColumns - 1) / laneColumns;
        for (int wave = 0; wave < _benchmarkWaveCount; wave++)
        {
            int authoredAge = BenchmarkWaveAgeSteps(wave, _benchmarkWaveCount);
            if ((authoredAge + cycleStep) % config.BenchmarkCycleSteps != config.BenchmarkCycleSteps - 1)
            {
                continue;
            }

            for (int deck = wave; deck < usedDeckCount; deck += _benchmarkWaveCount)
            {
                int firstSparseIndex = deck * laneColumns;
                int endSparseIndex = Math.Min(firstSparseIndex + laneColumns, _scaleCitySparseBodyCount);
                for (int sparseIndex = firstSparseIndex; sparseIndex < endSparseIndex; sparseIndex++)
                {
                    CreateSparseBenchmarkState(
                        sparseIndex,
                        0,
                        out Vector3 position,
                        out Quaternion orientation,
                        out Vector3 linearVelocity,
                        out Vector3 angularVelocity);
                    var state = new Physics3DBodyState
                    {
                        PositionCm = position,
                        Orientation = orientation,
                        LinearVelocityCmPerSecond = linearVelocity,
                        AngularVelocityRadiansPerSecond = angularVelocity,
                        Awake = true
                    };
                    SetBodyStateAndPose(_scaleCityFirstSparseBodyIndex + sparseIndex, in state);
                    _scaleCitySparseRecycledBodiesLastStep++;
                }
            }
        }
    }

    private void CreateScaleCityInteractiveState(
        int interactiveIndex,
        bool launched,
        out Vector3 position,
        out Quaternion orientation,
        out Vector3 linearVelocity)
    {
        Physics3DScaleCityShowcaseConfig config = ActiveConfig.ScaleCity;
        int cellsPerLayer = checked(config.InteractiveColumns * config.InteractiveRows);
        int layer = interactiveIndex / cellsPerLayer;
        int cell = interactiveIndex % cellsPerLayer;
        int column = cell % config.InteractiveColumns;
        int row = cell / config.InteractiveColumns;
        float x = (column - ((config.InteractiveColumns - 1) * 0.5f)) * config.InteractiveSpacingCm;
        float y = config.InteractiveBaseHeightCm + (layer * config.InteractiveLayerSpacingCm);
        float z = (row - ((config.InteractiveRows - 1) * 0.5f)) * config.InteractiveSpacingCm;
        position = new Vector3(x, y, z);
        orientation = Quaternion.Identity;
        if (!launched)
        {
            linearVelocity = Vector3.Zero;
            return;
        }

        Vector2 outward = new(x, z);
        float lengthSquared = outward.LengthSquared();
        if (lengthSquared > 0f)
        {
            outward /= MathF.Sqrt(lengthSquared);
        }

        linearVelocity = new Vector3(
            outward.X * config.LauncherOutwardSpeedCmPerSecond,
            config.LauncherUpSpeedCmPerSecond,
            outward.Y * config.LauncherOutwardSpeedCmPerSecond);
    }

    private Vector4 ScaleCityInteractiveColor(int interactiveIndex)
    {
        Physics3DScaleCityShowcaseConfig config = ActiveConfig.ScaleCity;
        int cellsPerLayer = checked(config.InteractiveColumns * config.InteractiveRows);
        int layer = interactiveIndex / cellsPerLayer;
        return (layer % 3) switch
        {
            0 => DynamicGold,
            1 => DynamicRed,
            _ => DynamicGreen
        };
    }

    private int BenchmarkWaveAgeSteps(int wave, int waveCount)
    {
        return checked((wave * ActiveConfig.BenchmarkCycleSteps) / waveCount);
    }

    private void CreateSparseBenchmarkState(
        int sparseIndex,
        int ageSteps,
        out Vector3 position,
        out Quaternion orientation,
        out Vector3 linearVelocity,
        out Vector3 angularVelocity)
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        int lane = sparseIndex % config.BenchmarkLaneColumns;
        int deck = sparseIndex / config.BenchmarkLaneColumns;
        float direction = ((lane + deck) & 1) == 0 ? 1f : -1f;
        float normalizedAge = ageSteps / (float)config.BenchmarkCycleSteps;
        float cycleDurationSeconds = config.BenchmarkCycleSteps * RequirePhysicsWorld().FixedDeltaSeconds;
        float x = direction * (-config.BenchmarkTravelHalfWidthCm + (2f * config.BenchmarkTravelHalfWidthCm * normalizedAge));
        float y = config.BenchmarkBaseHeightCm +
                  (deck * config.BenchmarkDeckSpacingCm) +
                  (4f * config.BenchmarkArcHeightCm * normalizedAge * (1f - normalizedAge));
        float z = (lane - ((config.BenchmarkLaneColumns - 1) * 0.5f)) * config.BenchmarkLaneSpacingCm;
        position = new Vector3(x, y, z);
        linearVelocity = new Vector3(
            direction * config.BenchmarkSpeedCmPerSecond,
            (4f * config.BenchmarkArcHeightCm / cycleDurationSeconds) * (1f - (2f * normalizedAge)),
            0f);
        float spin = config.BenchmarkSpinRadiansPerSecond;
        angularVelocity = new Vector3(
            ((lane & 1) == 0 ? 1f : -1f) * spin,
            ((deck & 1) == 0 ? 0.7f : -0.7f) * spin,
            direction * 0.45f * spin);
        orientation = Quaternion.CreateFromYawPitchRoll(
            normalizedAge * MathF.PI * direction,
            normalizedAge * MathF.PI * 0.5f,
            normalizedAge * MathF.PI * 0.25f);
    }
}
