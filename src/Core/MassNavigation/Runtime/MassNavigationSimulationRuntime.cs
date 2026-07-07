using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassNavigation.Runtime;

public readonly struct MassNavigationObstacleSnapshot
{
    public MassNavigationObstacleSnapshot(float worldXCm, float worldYCm, float radiusCm)
    {
        WorldXCm = worldXCm;
        WorldYCm = worldYCm;
        RadiusCm = radiusCm;
    }

    public float WorldXCm { get; }
    public float WorldYCm { get; }
    public float RadiusCm { get; }
}

public readonly record struct MassNavigationArrivalEvent(
    int AgentIndex,
    Entity Agent,
    float LocalXCm,
    float LocalYCm,
    float WorldXCm,
    float WorldYCm);

public readonly record struct MassNavigationCarriedRangeSyncResult(
    float CarrierLocalXCm,
    float CarrierLocalYCm,
    float CarrierWorldXCm,
    float CarrierWorldYCm,
    float DisplacementWorldXCm,
    float DisplacementWorldYCm,
    bool AppliedDisplacement);

public readonly record struct MassNavigationCarriedSlotTarget(
    float LocalXCm,
    float LocalYCm,
    float WorldXCm,
    float WorldYCm);

public readonly record struct MassNavigationSolverDiagnostics(
    bool FlowEnabled,
    int FlowIterationsPerStep,
    float FlowFieldRebuildMs,
    bool ArrivalRecoveryEnabled,
    int ArrivalTimeoutMs,
    int ArrivalProgressDistanceCm,
    int ArrivalWakePushDistanceCm,
    int ArrivalMaxRetryCount,
    int ArrivalSettledUnitCount,
    float ObstacleSoftPushPaddingCm,
    float TeamTargetClearanceCm,
    float GroupCenterClearanceCm,
    float TeamSlotClearanceCm,
    float LooseTargetClearanceCm,
    float GroupSlotClearanceCm,
    float UnitTargetStopThresholdCm,
    float GoalArrivalRadiusCm,
    float FormationFlowSlowRadiusCm,
    float DominantMassRatio,
    float FriendlyResponseScale,
    float NonFriendlyResponseScale,
    float DominantPushResponseScale);

public readonly record struct MassNavigationSolverRuntimeConfigSnapshot(
    int FieldWidthCm,
    int FieldHeightCm,
    int FlowCellSizeCm,
    int MaxObstacleCount,
    int ParallelWorkerCount,
    int SeparationHashCellSizeCm,
    int HardResolveHashCellSizeCm,
    float PlayAreaMinXCm,
    float PlayAreaMaxXCm);

public enum MassNavigationMoveCommandResult : byte
{
    Submitted = 1,
    OutsideWorld = 2,
    EmptySelection = 3,
    UnauthorizedSelection = 4,
    OrderSubmitRejected = 5,
}

public sealed class MassNavigationSimulationRuntime
{
    public const string AgentLocomotionSpeedParamKey = "mass_navigation.agent.locomotion.speed";

    private readonly int _initialSelectedTeamId;
    private int[] _teamIds = Array.Empty<int>();
    private Entity[] _selectionScratch = Array.Empty<Entity>();
    private Entity[] _selectedEntities = Array.Empty<Entity>();
    private int _selectedCount;
    private uint _selectionRevision;
    private bool _sceneResetRequested;
    private int _frameIndex;
    private int _nextSharedOrderId = 1;
    private readonly WorldGridLoadedChunks _loadedChunks;
    private readonly Dictionary<long, float> _loadedChunkLastTouchedSeconds;
    private readonly List<long> _loadedChunksToEvict;
    private readonly int _loadedChunkCapacity;
    private float _streamingClockSeconds;
    private int _streamingMinChunkX = int.MinValue;
    private int _streamingMaxChunkX = int.MinValue;
    private int _streamingMinChunkY = int.MinValue;
    private int _streamingMaxChunkY = int.MinValue;
    private int _streamingRadiusCm = int.MinValue;
    private WorldSizeSpec _boardWorldSize;
    private bool _boardWorldBound;
    private float _simWindowCenterXCm;
    private float _simWindowCenterYCm;
    private float _simWindowWidthCm;
    private float _simWindowHeightCm;
    private int _commandFocusTicksRemaining;
    private bool _hasCommandFocus;
    private float _lastCommandFocusXCm;
    private float _lastCommandFocusYCm;
    private int _lastCommandSelectionCount;
    private float _flowWorkAreaCenterXCm;
    private float _flowWorkAreaCenterYCm;
    private float _flowWorkAreaWidthCm;
    private float _flowWorkAreaHeightCm;
    private int _flowWorkAreaRevision;
    private string _flowWorkAreaReason = "initial contact";
    private string _solverWindowDriver = "initial nav area";

    public MassNavigationTelemetry Telemetry { get; } = new();
    public int SelectionSnapshotCountFrame => Telemetry.SelectionSnapshotCountFrame;
    public int CommandCountFrame => Telemetry.CommandCountFrame;
    public int StructuralChangesFrame => Telemetry.StructuralChangesFrame;
    public int StructuralChangeRevision => Telemetry.StructuralChangeRevision;
    public int FlowReconcileCountFrame => Telemetry.FlowReconcileCountFrame;
    public int FocusBudgetUpdatesFrame => Telemetry.FocusBudgetUpdatesFrame;
    public int SolverWindowMovesFrame => Telemetry.SolverWindowMovesFrame;
    public float FrameMs => Telemetry.FrameMs;
    public float Fps => Telemetry.Fps;
    public float SelectionSyncMs => Telemetry.SelectionSyncMs;
    public float FormationTargetMs => Telemetry.FormationTargetMs;
    public float FlowFieldRebuildMs => Telemetry.FlowFieldRebuildMs;
    public float StepPrepMs => Telemetry.StepPrepMs;
    public float LocalSteeringMs => Telemetry.LocalSteeringMs;
    public float SimStepMs => Telemetry.SimStepMs;
    public float HardResolveMs => Telemetry.HardResolveMs;
    public float EntitySyncMs => Telemetry.EntitySyncMs;
    public float PerformerCommandMs => Telemetry.PerformerCommandMs;
    public float SelectionSyncHzObserved => Telemetry.SelectionSyncHzObserved;
    public float ControlHzObserved => Telemetry.ControlHzObserved;
    public float CommandHzObserved => Telemetry.CommandHzObserved;
    public float SimHzObserved => Telemetry.SimHzObserved;
    public float PerformerHzObserved => Telemetry.PerformerHzObserved;
    public float PanelHzObserved => Telemetry.PanelHzObserved;
    public int CrowdInViewCount => Telemetry.CrowdInViewCount;
    public int CrowdSubmittedCount => Telemetry.CrowdSubmittedCount;
    public int ObstacleSubmittedCount => Telemetry.ObstacleSubmittedCount;
    public int PerformerDroppedCount => Telemetry.PerformerDroppedCount;
    public int StreamingWindowUpdatesFrame => Telemetry.StreamingWindowUpdatesFrame;
    public int CommandRejectsFrame => Telemetry.CommandRejectsFrame;
    public int CommandRejectsTotal => Telemetry.CommandRejectsTotal;
    public int FocusBudgetUpdatesTotal => Telemetry.FocusBudgetUpdatesTotal;
    public int SolverWindowMovesTotal => Telemetry.SolverWindowMovesTotal;
    public int ScenarioSpawnCount => Telemetry.ScenarioSpawnCount;
    public int SceneResetCount => Telemetry.SceneResetCount;
    public int AuthoredRuntimeBindingRevision => Telemetry.AuthoredRuntimeBindingRevision;
    public float LastRejectedCommandXCm => Telemetry.LastRejectedCommandXCm;
    public float LastRejectedCommandYCm => Telemetry.LastRejectedCommandYCm;
    public MassNavigationConfig Config { get; }
    public MassNavigationAgentState AgentState { get; } = new();
    public MassNavigationFlowTuning FlowTuning { get; }
    public MassNavigationCadenceConfig Cadence { get; }
    internal MassNavigationCadenceScheduler CadenceScheduler { get; }
    public MassNavigationFormationRuntime FormationRuntime { get; }
    public MassNavigationGroupRuntime NavGroupRuntime { get; }
    internal MassNavigationFlowSolverState MassNavigationFlow { get; }
    public MassNavigationWorldConfig WorldConfig { get; }
    public WorldGridLoadedChunks LoadedChunks => _loadedChunks;
    public MassNavigationStreamingConfig Streaming => Config.Streaming;
    public bool IsReadyForWorldOperations { get; private set; }

    public int NavigationAgentCount => MassNavigationFlow.UnitCount;
    public int NavigationObstacleCount => MassNavigationFlow.ObstacleCount;
    public int SelectedCount => _selectedCount;
    public uint SelectionRevision => _selectionRevision;
    public ReadOnlySpan<Entity> SelectedEntities => _selectedEntities.AsSpan(0, _selectedCount);
    public ReadOnlySpan<int> TeamIds => _teamIds;
    public int TeamCount => _teamIds.Length;
    public int FrameIndex => _frameIndex;
    public int AgentsPerTeam { get; private set; }
    public int SelectedTeamId { get; private set; }
    public MassNavigationFormationMode FormationMode { get; private set; } = MassNavigationFormationMode.None;
    public int LoadedChunkCount => _loadedChunks.ActiveChunkKeys.Count;
    public int StreamingChunkSizeCm => _loadedChunks.ChunkSizeCm;
    public float SolverWindowCenterXCm => _simWindowCenterXCm;
    public float SolverWindowCenterYCm => _simWindowCenterYCm;
    public float SolverWindowWidthCm => _simWindowWidthCm;
    public float SolverWindowHeightCm => _simWindowHeightCm;
    public float SolverWindowMinXCm => _simWindowCenterXCm - (_simWindowWidthCm * 0.5f);
    public float SolverWindowMinYCm => _simWindowCenterYCm - (_simWindowHeightCm * 0.5f);
    public float SolverWindowMaxXCm => _simWindowCenterXCm + (_simWindowWidthCm * 0.5f);
    public float SolverWindowMaxYCm => _simWindowCenterYCm + (_simWindowHeightCm * 0.5f);
    public float FlowWorkAreaCenterXCm => _flowWorkAreaCenterXCm;
    public float FlowWorkAreaCenterYCm => _flowWorkAreaCenterYCm;
    public float FlowWorkAreaWidthCm => _flowWorkAreaWidthCm;
    public float FlowWorkAreaHeightCm => _flowWorkAreaHeightCm;
    public float FlowWorkAreaMinXCm => _flowWorkAreaCenterXCm - (_flowWorkAreaWidthCm * 0.5f);
    public float FlowWorkAreaMinYCm => _flowWorkAreaCenterYCm - (_flowWorkAreaHeightCm * 0.5f);
    public float FlowWorkAreaMaxXCm => _flowWorkAreaCenterXCm + (_flowWorkAreaWidthCm * 0.5f);
    public float FlowWorkAreaMaxYCm => _flowWorkAreaCenterYCm + (_flowWorkAreaHeightCm * 0.5f);
    public int FlowWorkAreaRevision => _flowWorkAreaRevision;
    public string FlowWorkAreaReason => _flowWorkAreaReason;
    public int CommandFocusTicksRemaining => _commandFocusTicksRemaining;
    public bool HasCommandFocus => _hasCommandFocus && _commandFocusTicksRemaining > 0;
    public float CommandFocusXCm => _lastCommandFocusXCm;
    public float CommandFocusYCm => _lastCommandFocusYCm;
    public int LastCommandSelectionCount => _lastCommandSelectionCount;
    public float HotZoneMinXCm => SolverWindowMinXCm;
    public float HotZoneMinYCm => SolverWindowMinYCm;
    public float HotZoneMaxXCm => SolverWindowMaxXCm;
    public float HotZoneMaxYCm => SolverWindowMaxYCm;
    public string SolverWindowDriver => _solverWindowDriver;
    public int WorldWidthCm => RequireBoardWorldSize().Bounds.Width;
    public int WorldHeightCm => RequireBoardWorldSize().Bounds.Height;
    public WorldAabbCm WorldBounds => RequireBoardWorldSize().Bounds;
    public string ActiveHotZoneId => WorldConfig.ActiveHotZoneId;
    public string ActiveHotZoneLabel => WorldConfig.ActiveHotZoneLabel;
    public ReadOnlySpan<MassNavigationHotZoneConfig> HotZones => WorldConfig.HotZones;

    public MassNavigationSimulationRuntime(MassNavigationConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        MassNavigationFlow = new MassNavigationFlowSolverState(config.Solver);
        MassNavigationFlow.PreallocateAgentCapacity(config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity);
        WorldConfig = config.World ?? throw new InvalidOperationException("MassNavigationSimulationRuntime requires explicit world config.");
        Cadence = config.Cadence;
        CadenceScheduler = new MassNavigationCadenceScheduler(Cadence);
        _selectionScratch = new Entity[config.ScenarioRuntime.InitialSelectionScratchCapacity];
        _selectedEntities = new Entity[config.ScenarioRuntime.InitialSelectedEntityCapacity];
        _loadedChunkCapacity = config.ScenarioRuntime.RuntimeCapacity.LoadedChunkCapacity;
        _loadedChunkLastTouchedSeconds = new Dictionary<long, float>(_loadedChunkCapacity);
        _loadedChunksToEvict = new List<long>(_loadedChunkCapacity);
        _loadedChunks = new WorldGridLoadedChunks(WorldConfig.StreamingChunkSizeCm, _loadedChunkCapacity);
        _simWindowWidthCm = WorldConfig.SolverWindowWidthCm;
        _simWindowHeightCm = WorldConfig.SolverWindowHeightCm;
        _simWindowCenterXCm = WorldConfig.ActiveHotZone.CenterXCm;
        _simWindowCenterYCm = WorldConfig.ActiveHotZone.CenterYCm;
        _flowWorkAreaCenterXCm = _simWindowCenterXCm;
        _flowWorkAreaCenterYCm = _simWindowCenterYCm;
        _flowWorkAreaWidthCm = _simWindowWidthCm;
        _flowWorkAreaHeightCm = _simWindowHeightCm;
        FlowTuning = config.Flow;
        FormationRuntime = new MassNavigationFormationRuntime(config.Semantics.Group);
        NavGroupRuntime = new MassNavigationGroupRuntime(FormationRuntime, config.ScenarioRuntime.RuntimeCapacity);
        AgentsPerTeam = config.Scenario.AgentsPerTeam;
        _initialSelectedTeamId = config.Scenario.InitialSelectedTeamId;
        ConfigureScenarioTeams(CreateTeamIdArray(config.Scenario.Teams));
        SelectedTeamId = _initialSelectedTeamId;
        MassNavigationFlow.ArrivalTuning.CopyFrom(config.Arrival);
        MassNavigationFlow.AvoidanceTuning.CopyFrom(config.Avoidance);
        MassNavigationFlow.Semantics.CopyFrom(config.Semantics);
    }

    public void BindBoardWorld(WorldSizeSpec boardWorldSize)
    {
        ValidateInitialSolverWindow(boardWorldSize);
        _boardWorldSize = boardWorldSize;
        _boardWorldBound = true;
        _flowWorkAreaCenterXCm = _simWindowCenterXCm;
        _flowWorkAreaCenterYCm = _simWindowCenterYCm;
        MassNavigationFlow.SetWorldBounds(
            boardWorldSize.Bounds.Left,
            boardWorldSize.Bounds.Right,
            boardWorldSize.Bounds.Top,
            boardWorldSize.Bounds.Bottom);
        MassNavigationFlow.SetWorldOrigin(SolverWindowMinXCm, SolverWindowMinYCm);
        InvalidateStreamingWindowCache();
        UpdateStreamingWindow(ToWorldCm(new System.Numerics.Vector2(
            MassNavigationFlow.FieldWidthCm * 0.5f,
            MassNavigationFlow.FieldHeightCm * 0.5f)));
    }

    public void SetWorldOperationsReady(bool ready)
    {
        IsReadyForWorldOperations = ready;
    }

    public void BeginFrame(float dt)
    {
        _frameIndex++;
        Telemetry.BeginFrame(dt);
        _streamingClockSeconds += MathF.Max(0f, dt);
        AdvanceCommandFocus();
    }

    private void AdvanceCommandFocus()
    {
        if (_commandFocusTicksRemaining <= 0)
        {
            _hasCommandFocus = false;
            return;
        }

        _commandFocusTicksRemaining--;
        if (_commandFocusTicksRemaining == 0)
        {
            _hasCommandFocus = false;
            UpdateStreamingWindow(ResolveStreamingFocus());
        }
    }

    public void ObserveSelectionSync(double sampleMs) => Telemetry.ObserveSelectionSync(sampleMs);
    public void ObserveFormationTargets(double sampleMs) => Telemetry.ObserveFormationTargets(sampleMs);
    public void ObserveFlowFieldRebuild(double sampleMs) => Telemetry.ObserveFlowFieldRebuild(sampleMs);
    public void ObserveStepPrep(double sampleMs) => Telemetry.ObserveStepPrep(sampleMs);
    public void ObserveLocalSteering(double sampleMs) => Telemetry.ObserveLocalSteering(sampleMs);
    public void ObserveSimStep(double sampleMs) => Telemetry.ObserveSimStep(sampleMs);
    public void ObserveHardResolve(double sampleMs) => Telemetry.ObserveHardResolve(sampleMs);
    public void ObserveEntitySync(double sampleMs) => Telemetry.ObserveEntitySync(sampleMs);
    public void ObservePerformerCommand(double sampleMs) => Telemetry.ObservePerformerCommand(sampleMs);

    public bool ToggleFlowEnabled()
    {
        FlowTuning.Enabled = !FlowTuning.Enabled;
        MassNavigationFlow.RequestFlowRebuild();
        return FlowTuning.Enabled;
    }

    public int AdjustFlowIterations(int delta)
    {
        FlowTuning.AdjustIterations(delta);
        MassNavigationFlow.RequestFlowRebuild();
        return FlowTuning.IterationsPerStep;
    }

    public int AdjustFlowStepHz(int delta)
    {
        Cadence.AdjustFlowStepHz(delta);
        MassNavigationFlow.RequestFlowRebuild();
        return Cadence.FlowStepHz;
    }

    public int AdjustFlowCrowdStampHz(int delta)
    {
        Cadence.AdjustFlowCrowdStampHz(delta);
        MassNavigationFlow.RequestFlowRebuild();
        return Cadence.FlowCrowdStampHz;
    }

    public int AdjustFlowObstacleStampHz(int delta)
    {
        Cadence.AdjustFlowObstacleStampHz(delta);
        MassNavigationFlow.RequestFlowRebuild();
        return Cadence.FlowObstacleStampHz;
    }

    public bool ToggleArrivalRecovery()
    {
        MassNavigationFlow.ArrivalTuning.Enabled = !MassNavigationFlow.ArrivalTuning.Enabled;
        return MassNavigationFlow.ArrivalTuning.Enabled;
    }

    public int AdjustArrivalTimeoutMs(int delta)
    {
        MassNavigationFlow.ArrivalTuning.AdjustTimeoutMs(delta);
        return MassNavigationFlow.ArrivalTuning.TimeoutMs;
    }

    public int AdjustArrivalProgressDistanceCm(int delta)
    {
        MassNavigationFlow.ArrivalTuning.AdjustProgressDistanceCm(delta);
        return MassNavigationFlow.ArrivalTuning.ProgressDistanceCm;
    }

    public int AdjustArrivalWakePushDistanceCm(int delta)
    {
        MassNavigationFlow.ArrivalTuning.AdjustWakePushDistanceCm(delta);
        return MassNavigationFlow.ArrivalTuning.WakePushDistanceCm;
    }

    public int AdjustArrivalMaxRetryCount(int delta)
    {
        MassNavigationFlow.ArrivalTuning.AdjustMaxRetryCount(delta);
        return MassNavigationFlow.ArrivalTuning.MaxRetryCount;
    }

    public MassNavigationSolverDiagnostics CaptureSolverDiagnostics()
    {
        return new MassNavigationSolverDiagnostics(
            FlowEnabled: FlowTuning.Enabled,
            FlowIterationsPerStep: FlowTuning.IterationsPerStep,
            FlowFieldRebuildMs: FlowFieldRebuildMs > 0.001f ? FlowFieldRebuildMs : MassNavigationFlow.LastFlowFieldRebuildMs,
            ArrivalRecoveryEnabled: MassNavigationFlow.ArrivalTuning.Enabled,
            ArrivalTimeoutMs: MassNavigationFlow.ArrivalTuning.TimeoutMs,
            ArrivalProgressDistanceCm: MassNavigationFlow.ArrivalTuning.ProgressDistanceCm,
            ArrivalWakePushDistanceCm: MassNavigationFlow.ArrivalTuning.WakePushDistanceCm,
            ArrivalMaxRetryCount: MassNavigationFlow.ArrivalTuning.MaxRetryCount,
            ArrivalSettledUnitCount: MassNavigationFlow.SettledUnitCount,
            ObstacleSoftPushPaddingCm: MassNavigationFlow.Semantics.Obstacle.SoftPushPaddingCm,
            TeamTargetClearanceCm: MassNavigationFlow.Semantics.TargetProjection.TeamTargetClearanceCm,
            GroupCenterClearanceCm: MassNavigationFlow.Semantics.TargetProjection.GroupCenterClearanceCm,
            TeamSlotClearanceCm: MassNavigationFlow.Semantics.TargetProjection.TeamSlotClearanceCm,
            LooseTargetClearanceCm: MassNavigationFlow.Semantics.TargetProjection.LooseTargetClearanceCm,
            GroupSlotClearanceCm: MassNavigationFlow.Semantics.TargetProjection.GroupSlotClearanceCm,
            UnitTargetStopThresholdCm: MassNavigationFlow.Semantics.Group.UnitTargetStopThresholdCm,
            GoalArrivalRadiusCm: MassNavigationFlow.Semantics.Steering.GoalArrivalRadiusCm,
            FormationFlowSlowRadiusCm: MassNavigationFlow.Semantics.Group.FormationFlowSlowRadiusCm,
            DominantMassRatio: MassNavigationFlow.AvoidanceTuning.DominantMassRatio,
            FriendlyResponseScale: MassNavigationFlow.AvoidanceTuning.FriendlyResponseScale,
            NonFriendlyResponseScale: MassNavigationFlow.AvoidanceTuning.NonFriendlyResponseScale,
            DominantPushResponseScale: MassNavigationFlow.AvoidanceTuning.DominantPushResponseScale);
    }

    public MassNavigationSolverRuntimeConfigSnapshot CaptureSolverRuntimeConfig()
    {
        return new MassNavigationSolverRuntimeConfigSnapshot(
            FieldWidthCm: MassNavigationFlow.FieldWidthCm,
            FieldHeightCm: MassNavigationFlow.FieldHeightCm,
            FlowCellSizeCm: MassNavigationFlow.FlowCellSizeCm,
            MaxObstacleCount: MassNavigationFlow.MaxObstacleCount,
            ParallelWorkerCount: MassNavigationFlow.ParallelWorkerCount,
            SeparationHashCellSizeCm: MassNavigationFlow.SeparationHashCellSizeCm,
            HardResolveHashCellSizeCm: MassNavigationFlow.HardResolveHashCellSizeCm,
            PlayAreaMinXCm: MassNavigationFlow.PlayAreaMinXCm,
            PlayAreaMaxXCm: MassNavigationFlow.PlayAreaMaxXCm);
    }

    public void ObservePerformerCoverage(int crowdInViewCount, int crowdSubmittedCount, int obstacleSubmittedCount, int performerDroppedCount)
    {
        Telemetry.ObservePerformerCoverage(
            crowdInViewCount,
            crowdSubmittedCount,
            obstacleSubmittedCount,
            performerDroppedCount);
    }

    public void ObserveSelectionSyncTick() => Telemetry.ObserveSelectionSyncTick();
    public void ObserveControlTick() => Telemetry.ObserveControlTick();
    public void ObserveCommandTick() => Telemetry.ObserveCommandTick();
    public void ObserveSimTick() => Telemetry.ObserveSimTick();
    public void ObservePerformerTick() => Telemetry.ObservePerformerTick();
    public void ObservePanelTick() => Telemetry.ObservePanelTick();

    public Span<Entity> EnsureSelectionScratch(int required)
    {
        if (required > _selectionScratch.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation selection scratch required {required} entities, exceeding configured scenarioRuntime.initialSelectionScratchCapacity {_selectionScratch.Length}.");
        }

        return _selectionScratch.AsSpan(0, required);
    }

    public void SetSelection(ReadOnlySpan<Entity> entities, uint revision)
    {
        if (entities.Length > _selectedEntities.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation selected entity snapshot required {entities.Length} entities, exceeding configured scenarioRuntime.initialSelectedEntityCapacity {_selectedEntities.Length}.");
        }

        entities.CopyTo(_selectedEntities.AsSpan(0, entities.Length));
        _selectedCount = entities.Length;
        _selectionRevision = revision;
        Telemetry.MarkSelectionSnapshot();
        MassNavigationFlow.SetSelectedFlags(AgentState, _selectedEntities.AsSpan(0, _selectedCount));
    }

    public void ClearSelection()
    {
        if (_selectedCount == 0)
        {
            MassNavigationFlow.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
            return;
        }

        _selectedCount = 0;
        _selectionRevision++;
        Telemetry.MarkSelectionSnapshot();
        MassNavigationFlow.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
    }

    public void MarkStructuralChange()
    {
        Telemetry.MarkStructuralChange();
    }

    public void MarkCommandApply()
    {
        Telemetry.MarkCommandApply();
    }

    public void MarkScenarioSpawned()
    {
        Telemetry.MarkScenarioSpawned();
    }

    public void MarkSceneResetExecuted()
    {
        Telemetry.MarkSceneResetExecuted();
    }

    public void MarkFlowReconcile()
    {
        Telemetry.MarkFlowReconcile();
    }

    public void RejectCommandOutsideWorld(float worldXCm, float worldYCm)
    {
        Telemetry.MarkCommandRejected(worldXCm, worldYCm);
    }

    public void RejectCommandWithoutSelection(float worldXCm, float worldYCm)
    {
        Telemetry.MarkCommandRejected(worldXCm, worldYCm);
    }

    public void RejectCommandUnauthorizedSelection(float worldXCm, float worldYCm)
    {
        Telemetry.MarkCommandRejected(worldXCm, worldYCm);
    }

    public void RejectCommandOrderSubmit(float worldXCm, float worldYCm)
    {
        Telemetry.MarkCommandRejected(worldXCm, worldYCm);
    }

    public void SetAgentsPerTeam(int agentsPerTeam)
    {
        if (agentsPerTeam < 0)
        {
            throw new InvalidOperationException("MassNavigationSimulationRuntime.SetAgentsPerTeam requires agentsPerTeam >= 0.");
        }

        if (AgentsPerTeam == agentsPerTeam)
        {
            return;
        }

        AgentsPerTeam = agentsPerTeam;
        RequestSceneReset();
    }

    public void SetSelectedTeam(int teamId)
    {
        if (Array.IndexOf(_teamIds, teamId) < 0)
        {
            throw new InvalidOperationException($"MassNavigationSimulationRuntime selected team {teamId} is not configured.");
        }

        SelectedTeamId = teamId;
    }

    public void ConfigureScenarioTeams(ReadOnlySpan<int> teamIds)
    {
        if (teamIds.Length <= 0)
        {
            throw new InvalidOperationException("MassNavigationSimulationRuntime requires at least one configured team.");
        }

        if (_teamIds.Length != teamIds.Length)
        {
            _teamIds = new int[teamIds.Length];
        }

        teamIds.CopyTo(_teamIds);
        if (Array.IndexOf(_teamIds, SelectedTeamId) < 0)
        {
            if (Array.IndexOf(_teamIds, _initialSelectedTeamId) < 0)
            {
                throw new InvalidOperationException("MassNavigationSimulationRuntime configured teams do not include the initial selected team.");
            }

            SelectedTeamId = _initialSelectedTeamId;
        }
    }

    public int AllocateSharedOrderId()
    {
        int next = _nextSharedOrderId++;
        if (next <= 0)
        {
            _nextSharedOrderId = 1;
            next = _nextSharedOrderId++;
        }

        return next;
    }

    public void CycleSelectedTeam()
    {
        if (_teamIds.Length <= 0)
        {
            return;
        }

        int index = Array.IndexOf(_teamIds, SelectedTeamId);
        if (index < 0)
        {
            SelectedTeamId = _initialSelectedTeamId;
            return;
        }

        SelectedTeamId = _teamIds[(index + 1) % _teamIds.Length];
    }

    public void SetFormationMode(MassNavigationFormationMode mode)
    {
        if (!Enum.IsDefined(typeof(MassNavigationFormationMode), mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "MassNavigation formation mode is not defined.");
        }

        FormationMode = mode;
    }

    public void RequestSceneReset()
    {
        _sceneResetRequested = true;
    }

    public void ResetRuntimeState(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ClearSelection();
        NavGroupRuntime.Reset();
        AgentState.DestroyTracked(world);
        MarkAuthoredRuntimeBindingChanged();
    }

    public void ResetRuntimeState(World world, ReadOnlySpan<MassNavigationAgentSeed> agentSeeds)
    {
        ResetRuntimeState(world);
        MassNavigationFlow.ResetAuthoredAgents(agentSeeds);
    }

    public void ClearAuthoredRuntimeBindings(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ClearSelection();
        NavGroupRuntime.Reset();
        AgentState.ClearRuntimeBindings(world);
        MassNavigationFlow.ResetAuthoredAgents(ReadOnlySpan<MassNavigationAgentSeed>.Empty);
        MarkAuthoredRuntimeBindingChanged();
    }

    public void RebuildFromAuthoredAgents(
        World world,
        ReadOnlySpan<Entity> entities,
        ReadOnlySpan<MassNavigationAgentSeed> agentSeeds,
        ReadOnlySpan<bool> controllableFlags)
    {
        if (entities.Length != agentSeeds.Length || entities.Length != controllableFlags.Length)
        {
            throw new InvalidOperationException("MassNavigation authored rebuild requires matching entity, seed, and controllable spans.");
        }

        int membershipCapacity = Config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity;
        if (agentSeeds.Length > membershipCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored rebuild required {agentSeeds.Length} agent slots, exceeding configured scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {membershipCapacity}.");
        }

        int previousSelectedCount = _selectedCount;
        uint previousSelectionRevision = _selectionRevision;
        Span<Entity> previousSelectedEntities = previousSelectedCount > 0
            ? EnsureSelectionScratch(previousSelectedCount)
            : Span<Entity>.Empty;
        if (previousSelectedCount > 0)
        {
            _selectedEntities.AsSpan(0, previousSelectedCount).CopyTo(previousSelectedEntities);
        }

        var previousGroupSnapshot = NavGroupRuntime.CaptureAuthoredRebuildSnapshot();
        ClearAuthoredRuntimeBindings(world);
        MassNavigationFlow.ResetAuthoredAgents(agentSeeds);
        for (int i = 0; i < entities.Length; i++)
        {
            BindSpawnedAgent(world, entities[i], i, controllableFlags[i]);
        }

        NavGroupRuntime.RestoreAuthoredRebuildSnapshot(world, MassNavigationFlow, AgentState, previousGroupSnapshot);
        RestoreSelectionAfterAuthoredRebuild(world, previousSelectedEntities, previousSelectionRevision);
        MarkStructuralChange();
    }

    public void AppendAuthoredAgents(
        World world,
        ReadOnlySpan<Entity> newEntities,
        ReadOnlySpan<MassNavigationAgentSeed> newAgentSeeds,
        ReadOnlySpan<bool> controllableFlags)
    {
        if (newEntities.Length != newAgentSeeds.Length || newEntities.Length != controllableFlags.Length)
        {
            throw new InvalidOperationException("MassNavigation authored append requires matching entity, seed, and controllable spans.");
        }

        if (newAgentSeeds.Length <= 0)
        {
            return;
        }

        int newTotal = checked(AgentState.TotalAgents + newAgentSeeds.Length);
        int membershipCapacity = Config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity;
        if (newTotal > membershipCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored append required {newTotal} agent slots, exceeding configured scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {membershipCapacity}.");
        }

        int startIndex = MassNavigationFlow.UnitCount;
        MassNavigationFlow.AppendAuthoredAgents(newAgentSeeds);
        for (int i = 0; i < newEntities.Length; i++)
        {
            BindSpawnedAgent(world, newEntities[i], startIndex + i, controllableFlags[i]);
        }

        MarkStructuralChange();
    }

    private void RestoreSelectionAfterAuthoredRebuild(
        World world,
        ReadOnlySpan<Entity> previousSelectedEntities,
        uint previousSelectionRevision)
    {
        if (previousSelectedEntities.Length <= 0)
        {
            return;
        }

        int restoredCount = 0;
        Span<Entity> restored = EnsureSelectionScratch(previousSelectedEntities.Length);
        for (int i = 0; i < previousSelectedEntities.Length; i++)
        {
            Entity entity = previousSelectedEntities[i];
            if (world.IsAlive(entity) && AgentState.TryGetControllableIndex(entity, out _))
            {
                restored[restoredCount++] = entity;
            }
        }

        if (restoredCount <= 0)
        {
            return;
        }

        SetSelection(restored[..restoredCount], previousSelectionRevision);
    }

    public void FocusSimulationWindow(System.Numerics.Vector2 worldCenterCm)
    {
        ObserveFlowWorkArea(worldCenterCm, _simWindowWidthCm, _simWindowHeightCm, ReadOnlySpan<Entity>.Empty, "manual focus");
        MoveSolverWindow(worldCenterCm, "manual nav focus");
        UpdateStreamingWindow(ResolveStreamingFocus());
    }

    public void FocusCommandTarget(System.Numerics.Vector2 worldCenterCm, ReadOnlySpan<Entity> selectedEntities)
    {
        _hasCommandFocus = true;
        _lastCommandFocusXCm = worldCenterCm.X;
        _lastCommandFocusYCm = worldCenterCm.Y;
        _lastCommandSelectionCount = selectedEntities.Length;
        _commandFocusTicksRemaining = WorldConfig.CommandFocusHoldTicks;
        ObserveFlowWorkArea(
            worldCenterCm,
            _simWindowWidthCm,
            _simWindowHeightCm,
            selectedEntities,
            selectedEntities.Length > 0 ? "actor command" : "team command");
        MoveSolverWindow(ResolveSolverFocusForWorkArea(), selectedEntities.Length > 0 ? "actor command" : "team command");
        UpdateStreamingWindow(ResolveStreamingFocus());
    }

    public void FocusCommandTargetForEntities(System.Numerics.Vector2 worldCenterCm, Entity[] selectedEntities)
    {
        FocusCommandTarget(worldCenterCm, selectedEntities.AsSpan());
    }

    public void ObserveRuntimeFocus(System.Numerics.Vector2 focusCenterCm, float focusWidthCm, float focusHeightCm)
    {
        ObserveFlowWorkArea(
            focusCenterCm,
            MathF.Max(1f, focusWidthCm),
            MathF.Max(1f, focusHeightCm),
            ReadOnlySpan<Entity>.Empty,
            _hasCommandFocus && _commandFocusTicksRemaining > 0 ? "runtime focus + command hold" : "runtime focus");
        Telemetry.MarkFocusBudgetUpdated();
        UpdateStreamingWindow(ResolveStreamingFocus());
    }

    public System.Numerics.Vector2 ToLocalCm(System.Numerics.Vector2 worldCm)
    {
        return new System.Numerics.Vector2(worldCm.X - SolverWindowMinXCm, worldCm.Y - SolverWindowMinYCm);
    }

    public System.Numerics.Vector2 ToWorldCm(System.Numerics.Vector2 localCm)
    {
        return new System.Numerics.Vector2(localCm.X + SolverWindowMinXCm, localCm.Y + SolverWindowMinYCm);
    }

    public float ToWorldXCm(float localXCm) => localXCm + SolverWindowMinXCm;
    public float ToWorldYCm(float localYCm) => localYCm + SolverWindowMinYCm;
    public float ToLocalXCm(float worldXCm) => worldXCm - SolverWindowMinXCm;
    public float ToLocalYCm(float worldYCm) => worldYCm - SolverWindowMinYCm;

    public System.Numerics.Vector2 GetAgentLocalPositionCm(int agentIndex)
    {
        RequireAgentIndex(agentIndex);
        return new System.Numerics.Vector2(
            MassNavigationFlow.GetPositionX(agentIndex),
            MassNavigationFlow.GetPositionY(agentIndex));
    }

    public System.Numerics.Vector2 GetAgentWorldPositionCm(int agentIndex)
    {
        return ToWorldCm(GetAgentLocalPositionCm(agentIndex));
    }

    public bool TryGetAgentWorldPositionCm(World world, Entity agent, out System.Numerics.Vector2 worldCm)
    {
        ArgumentNullException.ThrowIfNull(world);
        worldCm = default;
        if (!world.IsAlive(agent) ||
            !world.TryGet(agent, out MassNavigationAgentIndex index))
        {
            return false;
        }

        int agentIndex = index.Value;
        if ((uint)agentIndex >= (uint)MassNavigationFlow.UnitCount)
        {
            return false;
        }

        worldCm = GetAgentWorldPositionCm(agentIndex);
        return true;
    }

    public MassNavigationGroupSemantics GetRuntimeGroupSemantics()
    {
        return MassNavigationFlow.Semantics.Group;
    }

    public MassNavigationFlowSolverState GetFlowSolverForTests()
    {
        return MassNavigationFlow;
    }

    public float GetAgentBodyRadiusCm(int agentIndex)
    {
        RequireAgentIndex(agentIndex);
        return MassNavigationFlow.GetBodyRadiusCm(agentIndex);
    }

    public MassNavigationObstacleSnapshot GetObstacleWorldSnapshot(int obstacleIndex)
    {
        RequireObstacleIndex(obstacleIndex);
        return new MassNavigationObstacleSnapshot(
            ToWorldXCm(MassNavigationFlow.GetObstacleX(obstacleIndex)),
            ToWorldYCm(MassNavigationFlow.GetObstacleY(obstacleIndex)),
            MassNavigationFlow.GetObstacleRadius(obstacleIndex));
    }

    public void RebuildRuntimeObstacles(ReadOnlySpan<MassNavigationObstacleSnapshot> obstacles)
    {
        MassNavigationFlow.ResetRuntimeObstaclesFromWorld(obstacles);
    }

    public MassNavigationCarriedRangeSyncResult SyncCarriedAgentRangeToCarrier(
        int carrierAgentIndex,
        int firstMemberAgentIndex,
        int memberAgentCount,
        bool previousCarrierSnapshotInitialized,
        float previousCarrierWorldXCm,
        float previousCarrierWorldYCm)
    {
        RequireAgentIndex(carrierAgentIndex);
        RequireAgentRange(firstMemberAgentIndex, memberAgentCount, nameof(firstMemberAgentIndex));

        System.Numerics.Vector2 carrierLocal = GetAgentLocalPositionCm(carrierAgentIndex);
        float carrierWorldX = ToWorldXCm(carrierLocal.X);
        float carrierWorldY = ToWorldYCm(carrierLocal.Y);
        float deltaX = previousCarrierSnapshotInitialized ? carrierWorldX - previousCarrierWorldXCm : 0f;
        float deltaY = previousCarrierSnapshotInitialized ? carrierWorldY - previousCarrierWorldYCm : 0f;
        bool applied = previousCarrierSnapshotInitialized && (deltaX != 0f || deltaY != 0f);
        if (applied)
        {
            MassNavigationFlow.ApplyExternalDisplacementRange(firstMemberAgentIndex, memberAgentCount, deltaX, deltaY);
        }

        return new MassNavigationCarriedRangeSyncResult(
            carrierLocal.X,
            carrierLocal.Y,
            carrierWorldX,
            carrierWorldY,
            deltaX,
            deltaY,
            applied);
    }

    public MassNavigationCarriedRangeSyncResult SyncCarriedAgentsToCarrier(
        int carrierAgentIndex,
        ReadOnlySpan<int> memberAgentIndices,
        bool previousCarrierSnapshotInitialized,
        float previousCarrierWorldXCm,
        float previousCarrierWorldYCm)
    {
        RequireAgentIndex(carrierAgentIndex);
        for (int i = 0; i < memberAgentIndices.Length; i++)
        {
            RequireAgentIndex(memberAgentIndices[i]);
        }

        System.Numerics.Vector2 carrierLocal = GetAgentLocalPositionCm(carrierAgentIndex);
        float carrierWorldX = ToWorldXCm(carrierLocal.X);
        float carrierWorldY = ToWorldYCm(carrierLocal.Y);
        float deltaX = previousCarrierSnapshotInitialized ? carrierWorldX - previousCarrierWorldXCm : 0f;
        float deltaY = previousCarrierSnapshotInitialized ? carrierWorldY - previousCarrierWorldYCm : 0f;
        bool applied = previousCarrierSnapshotInitialized && (deltaX != 0f || deltaY != 0f);
        if (applied)
        {
            MassNavigationFlow.ApplyExternalDisplacement(memberAgentIndices, deltaX, deltaY);
        }

        return new MassNavigationCarriedRangeSyncResult(
            carrierLocal.X,
            carrierLocal.Y,
            carrierWorldX,
            carrierWorldY,
            deltaX,
            deltaY,
            applied);
    }

    public void SyncAgentEntitiesNow(World world)
    {
        MassNavigationFlow.SyncEntities(world, AgentState);
    }

    public MassNavigationCarriedSlotTarget ResolveCarriedAgentSlotTarget(
        int memberAgentIndex,
        float carrierLocalXCm,
        float carrierLocalYCm,
        float slotOffsetLocalXCm,
        float slotOffsetLocalYCm)
    {
        RequireAgentIndex(memberAgentIndex);
        System.Numerics.Vector2 resolvedLocal = MassNavigationFlow.ResolveUnitNavigableTarget(
            memberAgentIndex,
            carrierLocalXCm + slotOffsetLocalXCm,
            carrierLocalYCm + slotOffsetLocalYCm,
            slotOffsetLocalXCm,
            slotOffsetLocalYCm,
            MassNavigationFlow.Semantics.TargetProjection.GroupSlotClearanceCm);
        System.Numerics.Vector2 resolvedWorld = ToWorldCm(resolvedLocal);
        return new MassNavigationCarriedSlotTarget(
            resolvedLocal.X,
            resolvedLocal.Y,
            resolvedWorld.X,
            resolvedWorld.Y);
    }

    public bool TryGetAgentNavigationTargetLocalCm(int agentIndex, out float xCm, out float yCm)
    {
        RequireAgentIndex(agentIndex);
        return MassNavigationFlow.TryGetUnitTarget(agentIndex, out xCm, out yCm);
    }

    public bool TryGetAgentNavigationTargetWorldCm(int agentIndex, out float xCm, out float yCm)
    {
        RequireAgentIndex(agentIndex);
        if (!MassNavigationFlow.TryGetUnitTarget(agentIndex, out float localX, out float localY))
        {
            xCm = 0f;
            yCm = 0f;
            return false;
        }

        xCm = ToWorldXCm(localX);
        yCm = ToWorldYCm(localY);
        return true;
    }

    public bool SetAgentNavigationTargetLocalCm(int agentIndex, float xCm, float yCm, bool resetRecovery = false)
    {
        RequireAgentIndex(agentIndex);
        return MassNavigationFlow.SetUnitTarget(agentIndex, xCm, yCm, resetRecovery);
    }

    public bool SetAgentNavigationTargetLocalCm(
        int agentIndex,
        float xCm,
        float yCm,
        float stopThresholdCm,
        bool resetRecovery = false)
    {
        RequireAgentIndex(agentIndex);
        return MassNavigationFlow.SetUnitTarget(agentIndex, xCm, yCm, stopThresholdCm, resetRecovery);
    }

    public bool SetAgentNavigationTargetWorldCm(int agentIndex, float worldXCm, float worldYCm, bool resetRecovery = false)
    {
        RequireAgentIndex(agentIndex);
        return MassNavigationFlow.SetUnitTarget(agentIndex, ToLocalXCm(worldXCm), ToLocalYCm(worldYCm), resetRecovery);
    }

    public bool SetAgentNavigationTargetWorldCm(
        int agentIndex,
        float worldXCm,
        float worldYCm,
        float stopThresholdCm,
        bool resetRecovery = false)
    {
        RequireAgentIndex(agentIndex);
        return MassNavigationFlow.SetUnitTarget(
            agentIndex,
            ToLocalXCm(worldXCm),
            ToLocalYCm(worldYCm),
            stopThresholdCm,
            resetRecovery);
    }

    public bool SetAgentNavigationTargetWorldCm(Entity agent, Vector2 worldCm, bool resetRecovery = false)
    {
        if (!AgentState.TryGetControllableIndex(agent, out int agentIndex))
        {
            return false;
        }

        return SetAgentNavigationTargetWorldCm(agentIndex, worldCm.X, worldCm.Y, resetRecovery);
    }

    public bool SetAgentNavigationTargetWorldCm(
        Entity agent,
        Vector2 worldCm,
        float stopThresholdCm,
        bool resetRecovery = false)
    {
        if (!AgentState.TryGetControllableIndex(agent, out int agentIndex))
        {
            return false;
        }

        return SetAgentNavigationTargetWorldCm(agentIndex, worldCm.X, worldCm.Y, stopThresholdCm, resetRecovery);
    }

    public void ReleaseAgentNavigationTarget(int agentIndex)
    {
        RequireAgentIndex(agentIndex);
        MassNavigationFlow.ReleaseUnitToTeamTarget(agentIndex);
    }

    public int DrainArrivalEvents(Span<MassNavigationArrivalEvent> destination)
    {
        return MassNavigationFlow.DrainArrivalEvents(destination, AgentState, SolverWindowMinXCm, SolverWindowMinYCm);
    }

    public void StepNavigationForTests(World world, float dt, bool runHardResolve = false, int hardResolveCandidateThresholdAgents = 1)
    {
        ArgumentNullException.ThrowIfNull(world);
        MassNavigationFlow.Step(
            dt,
            world,
            NavGroupRuntime,
            runHardResolve,
            hardResolveCandidateThresholdAgents);
    }

    public bool TryGetAgentLocomotionSpeedNormalized(int agentIndex, out float speed)
    {
        speed = 0f;
        if ((uint)agentIndex >= (uint)MassNavigationFlow.UnitCount)
        {
            return false;
        }

        if (!MassNavigationFlow.HasUnitTarget(agentIndex) ||
            MassNavigationFlow.IsUnitSettled(agentIndex))
        {
            return true;
        }

        Vector2 velocity = MassNavigationFlow.GetVelocityCmPerSecond(agentIndex);
        float authoredSpeed = MassNavigationFlow.GetSpeedCmPerSecond(agentIndex);
        if (!(authoredSpeed > 0f))
        {
            return true;
        }

        float normalized = velocity.Length() / authoredSpeed;
        speed = float.IsFinite(normalized) ? MathF.Max(0f, normalized) : 0f;
        return true;
    }

    public bool ApplyCarriedAgentSlotTarget(
        int memberAgentIndex,
        in MassNavigationCarriedSlotTarget target,
        bool resetRecovery)
    {
        RequireAgentIndex(memberAgentIndex);
        return MassNavigationFlow.SetUnitTarget(memberAgentIndex, target.LocalXCm, target.LocalYCm, resetRecovery);
    }

    public void BindSpawnedAgent(
        World world,
        Entity entity,
        int agentIndex,
        bool controllable)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException("MassNavigation cannot bind a spawned agent on a dead entity.");
        }

        if ((uint)agentIndex >= (uint)MassNavigationFlow.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation spawned agent index {agentIndex} exceeds current agent count {MassNavigationFlow.UnitCount}.");
        }

        if (world.Has<MassNavigationAgentIndex>(entity) || world.Has<MassNavigationAgentProfile>(entity))
        {
            throw new InvalidOperationException($"MassNavigation entity {entity.Id} was already bound as an agent.");
        }

        int teamId = MassNavigationFlow.GetTeam(agentIndex);
        UpsertComponent(world, entity, new Team { Id = teamId });
        int profileId = world.TryGet(entity, out MassNavigationAgent agent) ? agent.ProfileId : 0;
        world.Add(entity, new MassNavigationAgentIndex { Value = agentIndex });
        world.Add(entity, new MassNavigationAgentProfile
        {
            ProfileId = profileId,
            Heavy = MassNavigationFlow.IsHeavyProfile(agentIndex),
            VisualScale = MassNavigationFlow.GetVisualScale(agentIndex),
            SpeedCmPerSecond = MassNavigationFlow.GetSpeedCmPerSecond(agentIndex),
        });
        AgentState.RegisterAgentAtIndex(entity, agentIndex, controllable);
    }

    public static int ResolveAgentLocomotionSpeedParamKey()
    {
        return PerformerParamKeyRegistry.Register(AgentLocomotionSpeedParamKey);
    }

    public bool ContainsWorldPoint(float worldXCm, float worldYCm)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        return worldXCm >= bounds.Left &&
            worldXCm <= bounds.Right &&
            worldYCm >= bounds.Top &&
            worldYCm <= bounds.Bottom;
    }

    public void UpdateStreamingWindow(System.Numerics.Vector2 worldCenterCm)
    {
        int centerX = (int)MathF.Round(worldCenterCm.X);
        int centerY = (int)MathF.Round(worldCenterCm.Y);
        int radius = Streaming.RadiusCm;
        int chunkSize = _loadedChunks.ChunkSizeCm;
        int minChunkX = MathUtil.FloorDiv(centerX - radius, chunkSize);
        int maxChunkX = MathUtil.FloorDiv(centerX + radius, chunkSize);
        int minChunkY = MathUtil.FloorDiv(centerY - radius, chunkSize);
        int maxChunkY = MathUtil.FloorDiv(centerY + radius, chunkSize);
        bool changed = minChunkX != _streamingMinChunkX ||
            maxChunkX != _streamingMaxChunkX ||
            minChunkY != _streamingMinChunkY ||
            maxChunkY != _streamingMaxChunkY ||
            radius != _streamingRadiusCm;
        if (minChunkX == _streamingMinChunkX &&
            maxChunkX == _streamingMaxChunkX &&
            minChunkY == _streamingMinChunkY &&
            maxChunkY == _streamingMaxChunkY &&
            radius == _streamingRadiusCm)
        {
            EvictExpiredStreamingChunks();
            return;
        }

        _streamingMinChunkX = minChunkX;
        _streamingMaxChunkX = maxChunkX;
        _streamingMinChunkY = minChunkY;
        _streamingMaxChunkY = maxChunkY;
        _streamingRadiusCm = radius;
        for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
        {
            for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                long chunkKey = GraphChunkKey.Pack(chunkX, chunkY);
                TouchStreamingChunk(chunkKey);
            }
        }

        EvictExpiredStreamingChunks();
        if (changed)
        {
            Telemetry.MarkStreamingWindowUpdated();
        }
    }

    public void AdjustStreamingRetainSeconds(float deltaSeconds)
    {
        float next = Streaming.RetainSeconds + deltaSeconds;
        if (next < 0f)
        {
            throw new InvalidOperationException(
                $"MassNavigation streaming.retainSeconds adjustment would produce invalid value {next:0.###}.");
        }

        Streaming.RetainSeconds = next;
    }

    public void AdjustStreamingRadiusCm(int deltaCm)
    {
        int next = Streaming.RadiusCm + deltaCm;
        if (next < WorldConfig.StreamingChunkSizeCm)
        {
            throw new InvalidOperationException(
                $"MassNavigation streaming.radiusCm adjustment would produce {next}, below streaming chunk size {WorldConfig.StreamingChunkSizeCm}.");
        }

        Streaming.RadiusCm = next;
        InvalidateStreamingWindowCache();
    }

    private void EvictExpiredStreamingChunks()
    {
        float retainSeconds = Streaming.RetainSeconds;
        if (retainSeconds < 0f)
        {
            return;
        }

        _loadedChunksToEvict.Clear();
        foreach (KeyValuePair<long, float> pair in _loadedChunkLastTouchedSeconds)
        {
            if (_streamingClockSeconds - pair.Value > retainSeconds)
            {
                _loadedChunksToEvict.Add(pair.Key);
            }
        }

        for (int i = 0; i < _loadedChunksToEvict.Count; i++)
        {
            long chunkKey = _loadedChunksToEvict[i];
            _loadedChunkLastTouchedSeconds.Remove(chunkKey);
            _loadedChunks.SetLoaded(chunkKey, false);
        }
    }

    private void TouchStreamingChunk(long chunkKey)
    {
        ref float lastTouchedSeconds = ref CollectionsMarshal.GetValueRefOrNullRef(_loadedChunkLastTouchedSeconds, chunkKey);
        if (Unsafe.IsNullRef(ref lastTouchedSeconds))
        {
            if (_loadedChunkLastTouchedSeconds.Count >= _loadedChunkCapacity)
            {
                throw new InvalidOperationException(
                    $"MassNavigation streaming required more than configured scenarioRuntime.runtimeCapacity.loadedChunkCapacity {_loadedChunkCapacity} chunks.");
            }

            _loadedChunkLastTouchedSeconds.Add(chunkKey, _streamingClockSeconds);
            _loadedChunks.SetLoaded(chunkKey, true);
            return;
        }

        lastTouchedSeconds = _streamingClockSeconds;
    }

    private void MoveSolverWindow(System.Numerics.Vector2 requestedCenterCm, string reason)
    {
        float nextCenterX = requestedCenterCm.X;
        float nextCenterY = requestedCenterCm.Y;
        ClampSolverWindowCenter(ref nextCenterX, ref nextCenterY);
        if (MathF.Abs(nextCenterX - _simWindowCenterXCm) < 0.5f &&
            MathF.Abs(nextCenterY - _simWindowCenterYCm) < 0.5f)
        {
            return;
        }

        float previousOriginX = MassNavigationFlow.WorldOriginXCm;
        float previousOriginY = MassNavigationFlow.WorldOriginYCm;
        _simWindowCenterXCm = nextCenterX;
        _simWindowCenterYCm = nextCenterY;
        float nextOriginX = SolverWindowMinXCm;
        float nextOriginY = SolverWindowMinYCm;
        MassNavigationFlow.ShiftLocalFrame(nextOriginX - previousOriginX, nextOriginY - previousOriginY);
        MassNavigationFlow.SetWorldOrigin(nextOriginX, nextOriginY);
        MassNavigationFlow.RequestFlowRebuild();
        Telemetry.MarkSolverWindowMoved();
        _solverWindowDriver = reason;
    }

    private void ClampSolverWindowCenter(ref float centerX, ref float centerY)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        centerX = ClampWindowCenterToBounds(centerX, bounds.Left, bounds.Right, _simWindowWidthCm);
        centerY = ClampWindowCenterToBounds(centerY, bounds.Top, bounds.Bottom, _simWindowHeightCm);
    }

    private void ValidateInitialSolverWindow(WorldSizeSpec boardWorldSize)
    {
        WorldAabbCm bounds = boardWorldSize.Bounds;
        EnsureWindowFitsBoard(_simWindowWidthCm, bounds.Width, "solver window width");
        EnsureWindowFitsBoard(_simWindowHeightCm, bounds.Height, "solver window height");
        EnsurePointInsideWindowCenterBounds(
            _simWindowCenterXCm,
            bounds.Left,
            bounds.Right,
            _simWindowWidthCm,
            WorldConfig.ActiveHotZoneId,
            "x");
        EnsurePointInsideWindowCenterBounds(
            _simWindowCenterYCm,
            bounds.Top,
            bounds.Bottom,
            _simWindowHeightCm,
            WorldConfig.ActiveHotZoneId,
            "y");
    }

    private void ObserveFlowWorkArea(
        System.Numerics.Vector2 focusCm,
        float focusWidthCm,
        float focusHeightCm,
        ReadOnlySpan<Entity> selectedEntities,
        string reason)
    {
        float clampedWidth = MathF.Max(1f, focusWidthCm);
        float clampedHeight = MathF.Max(1f, focusHeightCm);
        float minX = focusCm.X - (clampedWidth * 0.5f);
        float maxX = focusCm.X + (clampedWidth * 0.5f);
        float minY = focusCm.Y - (clampedHeight * 0.5f);
        float maxY = focusCm.Y + (clampedHeight * 0.5f);

        if (_hasCommandFocus && _commandFocusTicksRemaining > 0)
        {
            IncludePoint(ref minX, ref maxX, ref minY, ref maxY, _lastCommandFocusXCm, _lastCommandFocusYCm);
        }

        if (selectedEntities.Length > 0)
        {
            IncludeSelectedBounds(ref minX, ref maxX, ref minY, ref maxY, selectedEntities);
        }

        float padding = WorldConfig.WorkAreaPaddingCm;
        minX -= padding;
        maxX += padding;
        minY -= padding;
        maxY += padding;
        ClampWorkArea(ref minX, ref maxX, ref minY, ref maxY);

        float width = MathF.Min(MathF.Max(_simWindowWidthCm, maxX - minX), WorldConfig.WorkAreaMaxWidthCm);
        float height = MathF.Min(MathF.Max(_simWindowHeightCm, maxY - minY), WorldConfig.WorkAreaMaxHeightCm);
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        ClampWorkAreaCenter(ref centerX, ref centerY, width, height);

        if (MathF.Abs(centerX - _flowWorkAreaCenterXCm) < 0.5f &&
            MathF.Abs(centerY - _flowWorkAreaCenterYCm) < 0.5f &&
            MathF.Abs(width - _flowWorkAreaWidthCm) < 0.5f &&
            MathF.Abs(height - _flowWorkAreaHeightCm) < 0.5f &&
            string.Equals(_flowWorkAreaReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _flowWorkAreaCenterXCm = centerX;
        _flowWorkAreaCenterYCm = centerY;
        _flowWorkAreaWidthCm = width;
        _flowWorkAreaHeightCm = height;
        _flowWorkAreaReason = reason;
        _flowWorkAreaRevision++;
    }

    private void IncludeSelectedBounds(ref float minX, ref float maxX, ref float minY, ref float maxY, ReadOnlySpan<Entity> selectedEntities)
    {
        for (int i = 0; i < selectedEntities.Length; i++)
        {
            if (!AgentState.TryGetControllableIndex(selectedEntities[i], out int unitIndex) ||
                (uint)unitIndex >= (uint)MassNavigationFlow.UnitCount)
            {
                continue;
            }

            float worldX = ToWorldXCm(MassNavigationFlow.GetPositionX(unitIndex));
            float worldY = ToWorldYCm(MassNavigationFlow.GetPositionY(unitIndex));
            IncludePoint(ref minX, ref maxX, ref minY, ref maxY, worldX, worldY);
        }
    }

    private void ClampWorkArea(ref float minX, ref float maxX, ref float minY, ref float maxY)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        minX = Math.Clamp(minX, bounds.Left, bounds.Right);
        maxX = Math.Clamp(maxX, bounds.Left, bounds.Right);
        minY = Math.Clamp(minY, bounds.Top, bounds.Bottom);
        maxY = Math.Clamp(maxY, bounds.Top, bounds.Bottom);
        if (minX > maxX)
        {
            (minX, maxX) = (maxX, minX);
        }

        if (minY > maxY)
        {
            (minY, maxY) = (maxY, minY);
        }
    }

    private void ClampWorkAreaCenter(ref float centerX, ref float centerY, float width, float height)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        centerX = ClampWindowCenterToBounds(centerX, bounds.Left, bounds.Right, width);
        centerY = ClampWindowCenterToBounds(centerY, bounds.Top, bounds.Bottom, height);
    }

    private System.Numerics.Vector2 ResolveSolverFocusForWorkArea()
    {
        return _hasCommandFocus && _commandFocusTicksRemaining > 0
            ? new System.Numerics.Vector2(_lastCommandFocusXCm, _lastCommandFocusYCm)
            : new System.Numerics.Vector2(_flowWorkAreaCenterXCm, _flowWorkAreaCenterYCm);
    }

    private System.Numerics.Vector2 ResolveStreamingFocus()
    {
        return new System.Numerics.Vector2(_flowWorkAreaCenterXCm, _flowWorkAreaCenterYCm);
    }

    private static void IncludePoint(ref float minX, ref float maxX, ref float minY, ref float maxY, float x, float y)
    {
        minX = MathF.Min(minX, x);
        maxX = MathF.Max(maxX, x);
        minY = MathF.Min(minY, y);
        maxY = MathF.Max(maxY, y);
    }

    private static void UpsertComponent<T>(World world, Entity entity, T component)
    {
        if (world.Has<T>(entity))
        {
            world.Set(entity, component);
        }
        else
        {
            world.Add(entity, component);
        }
    }

    private WorldSizeSpec RequireBoardWorldSize()
    {
        if (!_boardWorldBound)
        {
            throw new InvalidOperationException("MassNavigationSimulationRuntime requires PrimaryBoard.WorldSize to be bound before world operations.");
        }

        return _boardWorldSize;
    }

    private void RequireAgentIndex(int agentIndex)
    {
        if ((uint)agentIndex >= (uint)MassNavigationFlow.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation agent index {agentIndex} exceeds current agent count {MassNavigationFlow.UnitCount}.");
        }
    }

    private void RequireAgentRange(int firstAgentIndex, int agentCount, string fieldName)
    {
        int end = firstAgentIndex + agentCount;
        if (firstAgentIndex < 0 ||
            agentCount <= 0 ||
            end < firstAgentIndex ||
            end > MassNavigationFlow.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation agent range '{fieldName}' [{firstAgentIndex}, {end}) must be within current agent count {MassNavigationFlow.UnitCount}.");
        }
    }

    private void RequireObstacleIndex(int obstacleIndex)
    {
        if ((uint)obstacleIndex >= (uint)MassNavigationFlow.ObstacleCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation obstacle index {obstacleIndex} exceeds current obstacle count {MassNavigationFlow.ObstacleCount}.");
        }
    }

    private void MarkAuthoredRuntimeBindingChanged()
    {
        Telemetry.MarkAuthoredRuntimeBindingChanged();
    }

    private static float ClampWindowCenterToBounds(float worldCm, int minCm, int maxCm, float windowSizeCm)
    {
        float halfSize = windowSizeCm * 0.5f;
        float min = minCm + halfSize;
        float max = maxCm - halfSize;
        if (min > max)
        {
            throw new InvalidOperationException(
                $"MassNavigation solver/work area window {windowSizeCm:0.###} cm exceeds board span {maxCm - minCm} cm.");
        }

        return Math.Clamp(worldCm, min, max);
    }

    private static void EnsureWindowFitsBoard(float windowSizeCm, int boardSpanCm, string windowName)
    {
        if (windowSizeCm > boardSpanCm)
        {
            throw new InvalidOperationException(
                $"MassNavigation initial {windowName} {windowSizeCm:0.###} cm exceeds board span {boardSpanCm} cm.");
        }
    }

    private static void EnsurePointInsideWindowCenterBounds(
        float centerCm,
        int minCm,
        int maxCm,
        float windowSizeCm,
        string hotZoneId,
        string axisName)
    {
        float halfSize = windowSizeCm * 0.5f;
        float minCenter = minCm + halfSize;
        float maxCenter = maxCm - halfSize;
        if (centerCm < minCenter || centerCm > maxCenter)
        {
            throw new InvalidOperationException(
                $"MassNavigation active hot zone '{hotZoneId}' center {axisName}={centerCm:0.###} cannot host solver window {windowSizeCm:0.###} cm inside board center range [{minCenter:0.###}, {maxCenter:0.###}].");
        }
    }

    public bool ConsumeSceneResetRequest()
    {
        if (!_sceneResetRequested)
        {
            return false;
        }

        _sceneResetRequested = false;
        return true;
    }

    private static int[] CreateTeamIdArray(MassNavigationScenarioTeamConfig[] teams)
    {
        var ids = new int[teams.Length];
        for (int i = 0; i < teams.Length; i++)
        {
            ids[i] = teams[i].Id;
        }

        return ids;
    }

    private void InvalidateStreamingWindowCache()
    {
        _streamingMinChunkX = int.MinValue;
        _streamingMaxChunkX = int.MinValue;
        _streamingMinChunkY = int.MinValue;
        _streamingMaxChunkY = int.MinValue;
        _streamingRadiusCm = int.MinValue;
    }
}
