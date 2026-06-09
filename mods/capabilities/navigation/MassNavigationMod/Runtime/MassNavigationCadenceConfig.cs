using System;
using Ludots.Core.Engine.Physics2D;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationCadenceConfig
{
    public int SimulationHz { get; set; } = 15;
    public int TargetUpdateHz { get; set; } = 15;
    public int FlowStepHz { get; set; } = 5;
    public int FlowCrowdStampHz { get; set; } = 5;
    public int FlowObstacleStampHz { get; set; } = 2;
    public int HardResolveHz { get; set; } = 10;
    public int EntitySyncHz { get; set; } = 15;
    public int MaxStepsPerFixedTick { get; set; } = 1;
    public int HardResolveCandidateThresholdAgents { get; set; } = 1;
    public int OrderIdleScanIntervalFrames { get; set; } = 6;

    public void Validate()
    {
        ValidateHz(nameof(SimulationHz), SimulationHz, allowZero: false);
        ValidateHz(nameof(TargetUpdateHz), TargetUpdateHz, allowZero: true);
        ValidateHz(nameof(FlowStepHz), FlowStepHz, allowZero: true);
        ValidateHz(nameof(FlowCrowdStampHz), FlowCrowdStampHz, allowZero: true);
        ValidateHz(nameof(FlowObstacleStampHz), FlowObstacleStampHz, allowZero: true);
        ValidateHz(nameof(HardResolveHz), HardResolveHz, allowZero: true);
        ValidateHz(nameof(EntitySyncHz), EntitySyncHz, allowZero: true);

        if (MaxStepsPerFixedTick < 1)
        {
            throw new InvalidOperationException("Mass-nav cadence requires MaxStepsPerFixedTick >= 1.");
        }

        if (HardResolveCandidateThresholdAgents < 1)
        {
            throw new InvalidOperationException("Mass-nav cadence requires HardResolveCandidateThresholdAgents >= 1.");
        }

        if (OrderIdleScanIntervalFrames < 1)
        {
            throw new InvalidOperationException("Mass-nav cadence requires OrderIdleScanIntervalFrames >= 1.");
        }
    }

    private static void ValidateHz(string name, int value, bool allowZero)
    {
        if (value < 0 || (!allowZero && value == 0))
        {
            throw new InvalidOperationException($"Mass-nav cadence {name} is invalid.");
        }
    }

    public void AdjustFlowStepHz(int delta)
    {
        FlowStepHz = ClampOptionalHz(FlowStepHz + delta);
    }

    public void AdjustFlowCrowdStampHz(int delta)
    {
        FlowCrowdStampHz = ClampOptionalHz(FlowCrowdStampHz + delta);
    }

    public void AdjustFlowObstacleStampHz(int delta)
    {
        FlowObstacleStampHz = ClampOptionalHz(FlowObstacleStampHz + delta);
    }

    public void AdjustHardResolveHz(int delta)
    {
        HardResolveHz = ClampOptionalHz(HardResolveHz + delta);
    }

    public void AdjustEntitySyncHz(int delta)
    {
        EntitySyncHz = ClampOptionalHz(EntitySyncHz + delta);
    }

    private int ClampOptionalHz(int value)
    {
        return Math.Clamp(value, 0, SimulationHz);
    }
}

internal readonly record struct MassNavigationCadenceStep(
    float SimulationDt,
    bool UpdateTargets,
    bool RefreshFlow,
    bool RefreshCrowd,
    bool RefreshObstacles,
    bool RunHardResolve,
    bool SyncEntities);

internal sealed class MassNavigationCadenceScheduler
{
    private readonly MassNavigationCadenceConfig _config;

    private DiscreteRateTickDistributor? _simulation;
    private DiscreteRateTickDistributor? _targetUpdate;
    private DiscreteRateTickDistributor? _flowStep;
    private DiscreteRateTickDistributor? _flowCrowd;
    private DiscreteRateTickDistributor? _flowObstacle;
    private DiscreteRateTickDistributor? _hardResolve;
    private DiscreteRateTickDistributor? _entitySync;

    private int _fixedHz;
    private int _simulationHz;
    private int _maxStepsPerFixedTick;
    private int _targetUpdateHz;
    private int _flowStepHz;
    private int _flowCrowdStampHz;
    private int _flowObstacleStampHz;
    private int _hardResolveHz;
    private int _entitySyncHz;

    public MassNavigationCadenceScheduler(MassNavigationCadenceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public int BeginFixedTick(float fixedDt)
    {
        EnsureFixedCadence(fixedDt);
        return _simulation!.NextStepCount();
    }

    public MassNavigationCadenceStep NextSimulationStep()
    {
        EnsureSimulationCadence();
        return new MassNavigationCadenceStep(
            _simulation!.TargetDeltaTime,
            ShouldRun(_targetUpdate),
            ShouldRun(_flowStep),
            ShouldRun(_flowCrowd),
            ShouldRun(_flowObstacle),
            ShouldRun(_hardResolve),
            ShouldRun(_entitySync));
    }

    private void EnsureFixedCadence(float fixedDt)
    {
        int fixedHz = FixedHzFromDeltaTime(fixedDt);
        if (_simulation != null &&
            _fixedHz == fixedHz &&
            _simulationHz == _config.SimulationHz &&
            _maxStepsPerFixedTick == _config.MaxStepsPerFixedTick)
        {
            return;
        }

        _fixedHz = fixedHz;
        _simulationHz = _config.SimulationHz;
        _maxStepsPerFixedTick = _config.MaxStepsPerFixedTick;
        _simulation = Reset(_simulation, fixedHz, _config.SimulationHz, _config.MaxStepsPerFixedTick);
        ResetSimulationCadence();
    }

    private void EnsureSimulationCadence()
    {
        if (_targetUpdate != null &&
            _targetUpdateHz == _config.TargetUpdateHz &&
            _flowStepHz == _config.FlowStepHz &&
            _flowCrowdStampHz == _config.FlowCrowdStampHz &&
            _flowObstacleStampHz == _config.FlowObstacleStampHz &&
            _hardResolveHz == _config.HardResolveHz &&
            _entitySyncHz == _config.EntitySyncHz)
        {
            return;
        }

        int simHz = _config.SimulationHz;
        _targetUpdateHz = _config.TargetUpdateHz;
        _flowStepHz = _config.FlowStepHz;
        _flowCrowdStampHz = _config.FlowCrowdStampHz;
        _flowObstacleStampHz = _config.FlowObstacleStampHz;
        _hardResolveHz = _config.HardResolveHz;
        _entitySyncHz = _config.EntitySyncHz;
        _targetUpdate = Reset(_targetUpdate, simHz, _config.TargetUpdateHz, 1);
        _flowStep = Reset(_flowStep, simHz, _config.FlowStepHz, 1);
        _flowCrowd = Reset(_flowCrowd, simHz, _config.FlowCrowdStampHz, 1);
        _flowObstacle = Reset(_flowObstacle, simHz, _config.FlowObstacleStampHz, 1);
        _hardResolve = Reset(_hardResolve, simHz, _config.HardResolveHz, 1);
        _entitySync = Reset(_entitySync, simHz, _config.EntitySyncHz, 1);
    }

    private void ResetSimulationCadence()
    {
        _targetUpdate = null;
        _flowStep = null;
        _flowCrowd = null;
        _flowObstacle = null;
        _hardResolve = null;
        _entitySync = null;
    }

    private static DiscreteRateTickDistributor Reset(
        DiscreteRateTickDistributor? distributor,
        int fixedHz,
        int targetHz,
        int maxStepsPerFixedTick)
    {
        if (distributor == null)
        {
            return new DiscreteRateTickDistributor(fixedHz, targetHz, maxStepsPerFixedTick);
        }

        distributor.Reset(fixedHz, targetHz, maxStepsPerFixedTick);
        return distributor;
    }

    private static bool ShouldRun(DiscreteRateTickDistributor? distributor)
    {
        return distributor != null && distributor.NextStepCount() > 0;
    }

    private static int FixedHzFromDeltaTime(float fixedDt)
    {
        if (!(fixedDt > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(fixedDt));
        }

        int hz = (int)MathF.Round(1f / fixedDt);
        if (hz <= 0)
        {
            throw new InvalidOperationException("Mass-nav fixed delta must map to a positive Hz.");
        }

        return hz;
    }
}

