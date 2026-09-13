using System;
using Ludots.Core.Engine.TimeFlow;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationCadenceConfig
{
    public int SimulationHz { get; set; }
    public int TargetUpdateHz { get; set; }
    public int FlowStepHz { get; set; }
    public int FlowCrowdStampHz { get; set; }
    public int FlowObstacleStampHz { get; set; }
    public int HardResolveHz { get; set; }
    public int EntitySyncHz { get; set; }
    public int MaxStepsPerFixedTick { get; set; }
    public int HardResolveCandidateThresholdAgents { get; set; }

    /// <summary>
    /// Agent 分片合同：把一个 simulation 轮次（simulationHz 语义、每个 agent 的步长 dt 不变）
    /// 拆成 N 个连续 fixed tick 上的分片步。每个 agent 仍以 simulationHz 更新，
    /// 但单帧不再全量处理人群——steering/hard resolve/entity sync 按稠密代理索引分片摊销。
    /// 1 = 现行为（逐帧全量精确语义）；N &gt; 1 要求 simulationHz × N 能被 fixed tick 预算承载。
    /// </summary>
    public int AgentSliceCount { get; set; } = 1;

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
            throw new InvalidOperationException("MassNavigation cadence requires MaxStepsPerFixedTick >= 1.");
        }

        if (HardResolveCandidateThresholdAgents < 1)
        {
            throw new InvalidOperationException("MassNavigation cadence requires HardResolveCandidateThresholdAgents >= 1.");
        }

        if (AgentSliceCount < 1)
        {
            throw new InvalidOperationException("MassNavigation cadence requires AgentSliceCount >= 1.");
        }

    }

    private static void ValidateHz(string name, int value, bool allowZero)
    {
        if (value < 0 || (!allowZero && value == 0))
        {
            throw new InvalidOperationException($"MassNavigation cadence {name} is invalid.");
        }
    }

}

internal readonly record struct MassNavigationCadenceStep(
    float SimulationDt,
    bool UpdateTargets,
    bool RefreshFlow,
    bool RefreshCrowd,
    bool RefreshObstacles,
    bool RunHardResolve,
    bool SyncEntities,
    int AgentSliceIndex,
    int AgentSliceCount)
{
    public bool AgentSliceRoundStart => AgentSliceIndex == 0;
}

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
    private int _agentSliceCount;
    private int _targetUpdateHz;
    private int _flowStepHz;
    private int _flowCrowdStampHz;
    private int _flowObstacleStampHz;
    private int _hardResolveHz;
    private int _entitySyncHz;

    private int _sliceStepOrdinal;
    private bool _roundUpdateTargets;
    private bool _roundRefreshFlow;
    private bool _roundRefreshCrowd;
    private bool _roundRefreshObstacles;
    private bool _roundRunHardResolve;
    private bool _roundSyncEntities;

    public MassNavigationCadenceScheduler(MassNavigationCadenceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public int AgentSliceCount => _config.AgentSliceCount;

    public int BeginFixedTick(float fixedDt)
    {
        if (fixedDt <= 0f)
        {
            return 0;
        }

        EnsureFixedCadence(fixedDt);
        return _simulation!.NextStepCount();
    }

    public MassNavigationCadenceStep NextSimulationStep()
    {
        EnsureSimulationCadence();
        if (_sliceStepOrdinal == 0)
        {
            _roundUpdateTargets = ShouldRun(_targetUpdate);
            _roundRefreshFlow = ShouldRun(_flowStep);
            _roundRefreshCrowd = ShouldRun(_flowCrowd);
            _roundRefreshObstacles = ShouldRun(_flowObstacle);
            _roundRunHardResolve = ShouldRun(_hardResolve);
            _roundSyncEntities = ShouldRun(_entitySync);
        }

        int sliceIndex = _sliceStepOrdinal;
        _sliceStepOrdinal++;
        if (_sliceStepOrdinal >= _agentSliceCount)
        {
            _sliceStepOrdinal = 0;
        }

        return new MassNavigationCadenceStep(
            _simulationHz > 0 ? 1f / _simulationHz : 0f,
            _roundUpdateTargets,
            _roundRefreshFlow,
            _roundRefreshCrowd,
            _roundRefreshObstacles,
            _roundRunHardResolve,
            _roundSyncEntities,
            sliceIndex,
            _agentSliceCount);
    }

    private void EnsureFixedCadence(float fixedDt)
    {
        int fixedHz = FixedHzFromDeltaTime(fixedDt);
        if (_simulation != null &&
            _fixedHz == fixedHz &&
            _simulationHz == _config.SimulationHz &&
            _maxStepsPerFixedTick == _config.MaxStepsPerFixedTick &&
            _agentSliceCount == _config.AgentSliceCount)
        {
            return;
        }

        _fixedHz = fixedHz;
        _simulationHz = _config.SimulationHz;
        _maxStepsPerFixedTick = _config.MaxStepsPerFixedTick;
        _agentSliceCount = _config.AgentSliceCount;
        int sliceStepHz = checked(_simulationHz * _agentSliceCount);
        int maxSliceStepsPerFixedTick = (sliceStepHz + fixedHz - 1) / fixedHz;
        if (maxSliceStepsPerFixedTick > _maxStepsPerFixedTick)
        {
            throw new InvalidOperationException(
                $"MassNavigation cadence simulationHz {_simulationHz} x agentSliceCount {_agentSliceCount} requires {maxSliceStepsPerFixedTick} slice steps per fixed tick at fixedHz {fixedHz}, exceeding MaxStepsPerFixedTick {_maxStepsPerFixedTick}.");
        }

        _simulation = Reset(_simulation, fixedHz, sliceStepHz, _maxStepsPerFixedTick);
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
        _sliceStepOrdinal = 0;
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
            throw new InvalidOperationException("MassNavigation fixed delta must map to a positive Hz.");
        }

        return hz;
    }
}
