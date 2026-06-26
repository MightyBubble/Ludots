using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassCrowd.Runtime;

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
    private const float TimingWeight = 0.18f;
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
    private long _selectionSyncTick;
    private long _controlTick;
    private long _commandTick;
    private long _simTick;
    private long _performerTick;
    private long _panelTick;
    private string _solverWindowDriver = "initial nav area";

    public int SelectionSnapshotCountFrame { get; private set; }
    public int CommandCountFrame { get; private set; }
    public int StructuralChangesFrame { get; private set; }
    public int StructuralChangeRevision { get; private set; }
    public int FlowReconcileCountFrame { get; private set; }
    public int FocusBudgetUpdatesFrame { get; private set; }
    public int SolverWindowMovesFrame { get; private set; }
    public float FrameMs { get; private set; }
    public float Fps { get; private set; }
    public float SelectionSyncMs { get; private set; }
    public float FormationTargetMs { get; private set; }
    public float FlowFieldRebuildMs { get; private set; }
    public float StepPrepMs { get; private set; }
    public float LocalSteeringMs { get; private set; }
    public float SimStepMs { get; private set; }
    public float HardResolveMs { get; private set; }
    public float EntitySyncMs { get; private set; }
    public float PerformerCommandMs { get; private set; }
    public float SelectionSyncHzObserved { get; private set; }
    public float ControlHzObserved { get; private set; }
    public float CommandHzObserved { get; private set; }
    public float SimHzObserved { get; private set; }
    public float PerformerHzObserved { get; private set; }
    public float PanelHzObserved { get; private set; }
    public int CrowdInViewCount { get; private set; }
    public int CrowdSubmittedCount { get; private set; }
    public int ObstacleSubmittedCount { get; private set; }
    public int PerformerDroppedCount { get; private set; }
    public int StreamingWindowUpdatesFrame { get; private set; }
    public int CommandRejectsFrame { get; private set; }
    public int CommandRejectsTotal { get; private set; }
    public int FocusBudgetUpdatesTotal { get; private set; }
    public int SolverWindowMovesTotal { get; private set; }
    public int ScenarioSpawnCount { get; private set; }
    public int SceneResetCount { get; private set; }
    public int AuthoredRuntimeBindingRevision { get; private set; }
    public float LastRejectedCommandXCm { get; private set; }
    public float LastRejectedCommandYCm { get; private set; }
    public MassNavigationConfig Config { get; }
    public MassNavigationAgentState AgentState { get; } = new();
    public MassFlowTuning FlowTuning { get; }
    public MassNavigationCadenceConfig Cadence { get; }
    internal MassNavigationCadenceScheduler CadenceScheduler { get; }
    public MassNavigationFormationRuntime FormationRuntime { get; }
    public MassNavigationGroupRuntime NavGroupRuntime { get; }
    internal MassFlowSimulationState MassFlow { get; }
    public MassNavigationWorldConfig WorldConfig { get; }
    public WorldGridLoadedChunks LoadedChunks => _loadedChunks;
    public MassNavigationStreamingConfig Streaming => Config.Streaming;
    public bool IsReadyForWorldOperations { get; private set; }

    public int NavigationAgentCount => MassFlow.UnitCount;
    public int NavigationObstacleCount => MassFlow.ObstacleCount;
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
        MassFlow = new MassFlowSimulationState(config.Solver);
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
        MassFlow.ArrivalTuning.Enabled = config.Arrival.Enabled;
        MassFlow.ArrivalTuning.TimeoutMs = config.Arrival.TimeoutMs;
        MassFlow.ArrivalTuning.TimeoutMinMs = config.Arrival.TimeoutMinMs;
        MassFlow.ArrivalTuning.TimeoutMaxMs = config.Arrival.TimeoutMaxMs;
        MassFlow.ArrivalTuning.ProgressDistanceCm = config.Arrival.ProgressDistanceCm;
        MassFlow.ArrivalTuning.ProgressDistanceMinCm = config.Arrival.ProgressDistanceMinCm;
        MassFlow.ArrivalTuning.ProgressDistanceMaxCm = config.Arrival.ProgressDistanceMaxCm;
        MassFlow.ArrivalTuning.WakePushDistanceCm = config.Arrival.WakePushDistanceCm;
        MassFlow.ArrivalTuning.WakePushDistanceMinCm = config.Arrival.WakePushDistanceMinCm;
        MassFlow.ArrivalTuning.WakePushDistanceMaxCm = config.Arrival.WakePushDistanceMaxCm;
        MassFlow.ArrivalTuning.MaxRetryCountMin = config.Arrival.MaxRetryCountMin;
        MassFlow.ArrivalTuning.MaxRetryCountMax = config.Arrival.MaxRetryCountMax;
        MassFlow.ArrivalTuning.MaxRetryCount = config.Arrival.MaxRetryCount;
        MassFlow.AvoidanceTuning.CopyFrom(config.Avoidance);
        MassFlow.Semantics.Obstacle.HardResolveCandidateDistanceCm = config.Semantics.Obstacle.HardResolveCandidateDistanceCm;
        MassFlow.Semantics.Obstacle.SoftPushPaddingCm = config.Semantics.Obstacle.SoftPushPaddingCm;
        MassFlow.Semantics.Obstacle.SoftPushForceScale = config.Semantics.Obstacle.SoftPushForceScale;
        MassFlow.Semantics.TargetProjection.TeamTargetClearanceCm = config.Semantics.TargetProjection.TeamTargetClearanceCm;
        MassFlow.Semantics.TargetProjection.GroupCenterClearanceCm = config.Semantics.TargetProjection.GroupCenterClearanceCm;
        MassFlow.Semantics.TargetProjection.TeamSlotClearanceCm = config.Semantics.TargetProjection.TeamSlotClearanceCm;
        MassFlow.Semantics.TargetProjection.GroupSlotClearanceCm = config.Semantics.TargetProjection.GroupSlotClearanceCm;
        MassFlow.Semantics.TargetProjection.LooseTargetClearanceCm = config.Semantics.TargetProjection.LooseTargetClearanceCm;
        MassFlow.Semantics.Group.SpawnSpacingCm = config.Semantics.Group.SpawnSpacingCm;
        MassFlow.Semantics.Group.SpawnJitterCm = config.Semantics.Group.SpawnJitterCm;
        MassFlow.Semantics.Group.TeamSlotSpacingCm = config.Semantics.Group.TeamSlotSpacingCm;
        MassFlow.Semantics.Group.FormationLineSpacingCm = config.Semantics.Group.FormationLineSpacingCm;
        MassFlow.Semantics.Group.FormationSquareSpacingCm = config.Semantics.Group.FormationSquareSpacingCm;
        MassFlow.Semantics.Group.FormationCircleSpacingCm = config.Semantics.Group.FormationCircleSpacingCm;
        MassFlow.Semantics.Group.FormationCircleMinRadiusCm = config.Semantics.Group.FormationCircleMinRadiusCm;
        MassFlow.Semantics.Group.FormationWedgeSpacingCm = config.Semantics.Group.FormationWedgeSpacingCm;
        MassFlow.Semantics.Group.FormationRotationEpsilonRadians = config.Semantics.Group.FormationRotationEpsilonRadians;
        MassFlow.Semantics.Group.FormationRotationSpeedRadiansPerSecond = config.Semantics.Group.FormationRotationSpeedRadiansPerSecond;
        MassFlow.Semantics.Group.PullDeadZoneCm = config.Semantics.Group.PullDeadZoneCm;
        MassFlow.Semantics.Group.PullClampCm = config.Semantics.Group.PullClampCm;
        MassFlow.Semantics.Group.ArrivedRadiusCm = config.Semantics.Group.ArrivedRadiusCm;
        MassFlow.Semantics.Group.FormationArriveThresholdCm = config.Semantics.Group.FormationArriveThresholdCm;
        MassFlow.Semantics.Group.LooseArriveThresholdCm = config.Semantics.Group.LooseArriveThresholdCm;
        MassFlow.Semantics.Group.UnitTargetStopThresholdCm = config.Semantics.Group.UnitTargetStopThresholdCm;
        MassFlow.Semantics.Group.FormationFlowSlowRadiusCm = config.Semantics.Group.FormationFlowSlowRadiusCm;
        MassFlow.Semantics.Group.NearSlotBlend = config.Semantics.Group.NearSlotBlend;
        MassFlow.Semantics.Group.FarSlotBlend = config.Semantics.Group.FarSlotBlend;
        MassFlow.Semantics.Group.NearSlotBlendDistanceSq = config.Semantics.Group.NearSlotBlendDistanceSq;
        MassFlow.Semantics.Steering.SeparationRadiusCm = config.Semantics.Steering.SeparationRadiusCm;
        MassFlow.Semantics.Steering.GoalArrivalRadiusCm = config.Semantics.Steering.GoalArrivalRadiusCm;
        MassFlow.Semantics.Steering.FlowObstacleAvoidanceScale = config.Semantics.Steering.FlowObstacleAvoidanceScale;
        MassFlow.Semantics.Steering.FormationSeparationScale = config.Semantics.Steering.FormationSeparationScale;
        MassFlow.Semantics.Steering.LooseSeparationScale = config.Semantics.Steering.LooseSeparationScale;
        MassFlow.Semantics.Steering.VelocityBlendPerSecond = config.Semantics.Steering.VelocityBlendPerSecond;
        MassFlow.Semantics.Solver.MinNavMass = config.Semantics.Solver.MinNavMass;
        MassFlow.Semantics.Solver.MinVisualScale = config.Semantics.Solver.MinVisualScale;
        MassFlow.Semantics.Solver.MaxStepDtSeconds = config.Semantics.Solver.MaxStepDtSeconds;
        MassFlow.Semantics.Solver.ParallelStepMinAgents = config.Semantics.Solver.ParallelStepMinAgents;
        MassFlow.Semantics.Solver.DirectionEpsilonSq = config.Semantics.Solver.DirectionEpsilonSq;
        MassFlow.Semantics.Solver.NormalizationEpsilonSq = config.Semantics.Solver.NormalizationEpsilonSq;
        MassFlow.Semantics.Solver.InverseSqrtMinValue = config.Semantics.Solver.InverseSqrtMinValue;
        MassFlow.Semantics.Solver.EntitySyncPositionEpsilonSq = config.Semantics.Solver.EntitySyncPositionEpsilonSq;
        MassFlow.Semantics.Solver.EntitySyncVelocityEpsilonSq = config.Semantics.Solver.EntitySyncVelocityEpsilonSq;
        MassFlow.Semantics.Solver.FacingVelocityEpsilonSq = config.Semantics.Solver.FacingVelocityEpsilonSq;
        MassFlow.Semantics.Solver.FlowBlockedCellCost = config.Semantics.Solver.FlowBlockedCellCost;
        MassFlow.Semantics.Solver.FlowBlockedCellThreshold = config.Semantics.Solver.FlowBlockedCellThreshold;
        MassFlow.Semantics.Solver.FlowTargetStopDistanceSq = config.Semantics.Solver.FlowTargetStopDistanceSq;
        MassFlow.Semantics.Solver.FlowObstacleNeighborRadiusCells = config.Semantics.Solver.FlowObstacleNeighborRadiusCells;
        MassFlow.Semantics.Solver.FlowObstacleNeighborWeight = config.Semantics.Solver.FlowObstacleNeighborWeight;
        MassFlow.Semantics.Solver.FlowObstacleAvoidanceWeight = config.Semantics.Solver.FlowObstacleAvoidanceWeight;
        MassFlow.Semantics.Solver.CoincidentPairHashBucketCount = config.Semantics.Solver.CoincidentPairHashBucketCount;
        MassFlow.Semantics.Solver.CoincidentPairHashPrimeA = config.Semantics.Solver.CoincidentPairHashPrimeA;
        MassFlow.Semantics.Solver.CoincidentPairHashPrimeB = config.Semantics.Solver.CoincidentPairHashPrimeB;
    }

    public void BindBoardWorld(WorldSizeSpec boardWorldSize)
    {
        ValidateInitialSolverWindow(boardWorldSize);
        _boardWorldSize = boardWorldSize;
        _boardWorldBound = true;
        _flowWorkAreaCenterXCm = _simWindowCenterXCm;
        _flowWorkAreaCenterYCm = _simWindowCenterYCm;
        MassFlow.SetWorldBounds(
            boardWorldSize.Bounds.Left,
            boardWorldSize.Bounds.Right,
            boardWorldSize.Bounds.Top,
            boardWorldSize.Bounds.Bottom);
        MassFlow.SetWorldOrigin(SolverWindowMinXCm, SolverWindowMinYCm);
        InvalidateStreamingWindowCache();
        UpdateStreamingWindow(ToWorldCm(new System.Numerics.Vector2(
            MassFlow.FieldWidthCm * 0.5f,
            MassFlow.FieldHeightCm * 0.5f)));
    }

    public void SetWorldOperationsReady(bool ready)
    {
        IsReadyForWorldOperations = ready;
    }

    public void BeginFrame(float dt)
    {
        _frameIndex++;
        SelectionSnapshotCountFrame = 0;
        CommandCountFrame = 0;
        StructuralChangesFrame = 0;
        FlowReconcileCountFrame = 0;
        StreamingWindowUpdatesFrame = 0;
        CommandRejectsFrame = 0;
        FocusBudgetUpdatesFrame = 0;
        SolverWindowMovesFrame = 0;
        FrameMs = dt > 0f ? dt * 1000f : 0f;
        Fps = FrameMs > 0.001f ? 1000f / FrameMs : 0f;
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

    public void ObserveSelectionSync(double sampleMs) => SelectionSyncMs = Smooth(SelectionSyncMs, (float)sampleMs);
    public void ObserveFormationTargets(double sampleMs) => FormationTargetMs = Smooth(FormationTargetMs, (float)sampleMs);
    public void ObserveFlowFieldRebuild(double sampleMs) => FlowFieldRebuildMs = Smooth(FlowFieldRebuildMs, (float)sampleMs);
    public void ObserveStepPrep(double sampleMs) => StepPrepMs = Smooth(StepPrepMs, (float)sampleMs);
    public void ObserveLocalSteering(double sampleMs) => LocalSteeringMs = Smooth(LocalSteeringMs, (float)sampleMs);
    public void ObserveSimStep(double sampleMs) => SimStepMs = Smooth(SimStepMs, (float)sampleMs);
    public void ObserveHardResolve(double sampleMs) => HardResolveMs = Smooth(HardResolveMs, (float)sampleMs);
    public void ObserveEntitySync(double sampleMs) => EntitySyncMs = Smooth(EntitySyncMs, (float)sampleMs);
    public void ObservePerformerCommand(double sampleMs) => PerformerCommandMs = Smooth(PerformerCommandMs, (float)sampleMs);

    public bool ToggleFlowEnabled()
    {
        FlowTuning.Enabled = !FlowTuning.Enabled;
        MassFlow.RequestFlowRebuild();
        return FlowTuning.Enabled;
    }

    public int AdjustFlowIterations(int delta)
    {
        FlowTuning.AdjustIterations(delta);
        MassFlow.RequestFlowRebuild();
        return FlowTuning.IterationsPerStep;
    }

    public int AdjustFlowStepHz(int delta)
    {
        Cadence.AdjustFlowStepHz(delta);
        MassFlow.RequestFlowRebuild();
        return Cadence.FlowStepHz;
    }

    public int AdjustFlowCrowdStampHz(int delta)
    {
        Cadence.AdjustFlowCrowdStampHz(delta);
        MassFlow.RequestFlowRebuild();
        return Cadence.FlowCrowdStampHz;
    }

    public int AdjustFlowObstacleStampHz(int delta)
    {
        Cadence.AdjustFlowObstacleStampHz(delta);
        MassFlow.RequestFlowRebuild();
        return Cadence.FlowObstacleStampHz;
    }

    public bool ToggleArrivalRecovery()
    {
        MassFlow.ArrivalTuning.Enabled = !MassFlow.ArrivalTuning.Enabled;
        return MassFlow.ArrivalTuning.Enabled;
    }

    public int AdjustArrivalTimeoutMs(int delta)
    {
        MassFlow.ArrivalTuning.AdjustTimeoutMs(delta);
        return MassFlow.ArrivalTuning.TimeoutMs;
    }

    public int AdjustArrivalProgressDistanceCm(int delta)
    {
        MassFlow.ArrivalTuning.AdjustProgressDistanceCm(delta);
        return MassFlow.ArrivalTuning.ProgressDistanceCm;
    }

    public int AdjustArrivalWakePushDistanceCm(int delta)
    {
        MassFlow.ArrivalTuning.AdjustWakePushDistanceCm(delta);
        return MassFlow.ArrivalTuning.WakePushDistanceCm;
    }

    public int AdjustArrivalMaxRetryCount(int delta)
    {
        MassFlow.ArrivalTuning.AdjustMaxRetryCount(delta);
        return MassFlow.ArrivalTuning.MaxRetryCount;
    }

    public MassNavigationSolverDiagnostics CaptureSolverDiagnostics()
    {
        return new MassNavigationSolverDiagnostics(
            FlowEnabled: FlowTuning.Enabled,
            FlowIterationsPerStep: FlowTuning.IterationsPerStep,
            FlowFieldRebuildMs: FlowFieldRebuildMs > 0.001f ? FlowFieldRebuildMs : MassFlow.LastFlowFieldRebuildMs,
            ArrivalRecoveryEnabled: MassFlow.ArrivalTuning.Enabled,
            ArrivalTimeoutMs: MassFlow.ArrivalTuning.TimeoutMs,
            ArrivalProgressDistanceCm: MassFlow.ArrivalTuning.ProgressDistanceCm,
            ArrivalWakePushDistanceCm: MassFlow.ArrivalTuning.WakePushDistanceCm,
            ArrivalMaxRetryCount: MassFlow.ArrivalTuning.MaxRetryCount,
            ArrivalSettledUnitCount: MassFlow.SettledUnitCount,
            ObstacleSoftPushPaddingCm: MassFlow.Semantics.Obstacle.SoftPushPaddingCm,
            TeamTargetClearanceCm: MassFlow.Semantics.TargetProjection.TeamTargetClearanceCm,
            GroupCenterClearanceCm: MassFlow.Semantics.TargetProjection.GroupCenterClearanceCm,
            TeamSlotClearanceCm: MassFlow.Semantics.TargetProjection.TeamSlotClearanceCm,
            LooseTargetClearanceCm: MassFlow.Semantics.TargetProjection.LooseTargetClearanceCm,
            GroupSlotClearanceCm: MassFlow.Semantics.TargetProjection.GroupSlotClearanceCm,
            UnitTargetStopThresholdCm: MassFlow.Semantics.Group.UnitTargetStopThresholdCm,
            GoalArrivalRadiusCm: MassFlow.Semantics.Steering.GoalArrivalRadiusCm,
            FormationFlowSlowRadiusCm: MassFlow.Semantics.Group.FormationFlowSlowRadiusCm,
            DominantMassRatio: MassFlow.AvoidanceTuning.DominantMassRatio,
            FriendlyResponseScale: MassFlow.AvoidanceTuning.FriendlyResponseScale,
            NonFriendlyResponseScale: MassFlow.AvoidanceTuning.NonFriendlyResponseScale,
            DominantPushResponseScale: MassFlow.AvoidanceTuning.DominantPushResponseScale);
    }

    public MassNavigationSolverRuntimeConfigSnapshot CaptureSolverRuntimeConfig()
    {
        return new MassNavigationSolverRuntimeConfigSnapshot(
            FieldWidthCm: MassFlow.FieldWidthCm,
            FieldHeightCm: MassFlow.FieldHeightCm,
            FlowCellSizeCm: MassFlow.FlowCellSizeCm,
            MaxObstacleCount: MassFlow.MaxObstacleCount,
            ParallelWorkerCount: MassFlow.ParallelWorkerCount,
            SeparationHashCellSizeCm: MassFlow.SeparationHashCellSizeCm,
            HardResolveHashCellSizeCm: MassFlow.HardResolveHashCellSizeCm,
            PlayAreaMinXCm: MassFlow.PlayAreaMinXCm,
            PlayAreaMaxXCm: MassFlow.PlayAreaMaxXCm);
    }

    public void ObservePerformerCoverage(int crowdInViewCount, int crowdSubmittedCount, int obstacleSubmittedCount, int performerDroppedCount)
    {
        CrowdInViewCount = Math.Max(0, crowdInViewCount);
        CrowdSubmittedCount = Math.Max(0, crowdSubmittedCount);
        ObstacleSubmittedCount = Math.Max(0, obstacleSubmittedCount);
        PerformerDroppedCount = Math.Max(0, performerDroppedCount);
    }

    public void ObserveSelectionSyncTick() => SelectionSyncHzObserved = ObserveHz(ref _selectionSyncTick, SelectionSyncHzObserved);
    public void ObserveControlTick() => ControlHzObserved = ObserveHz(ref _controlTick, ControlHzObserved);
    public void ObserveCommandTick() => CommandHzObserved = ObserveHz(ref _commandTick, CommandHzObserved);
    public void ObserveSimTick() => SimHzObserved = ObserveHz(ref _simTick, SimHzObserved);
    public void ObservePerformerTick() => PerformerHzObserved = ObserveHz(ref _performerTick, PerformerHzObserved);
    public void ObservePanelTick() => PanelHzObserved = ObserveHz(ref _panelTick, PanelHzObserved);

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
        SelectionSnapshotCountFrame++;
        MassFlow.SetSelectedFlags(AgentState, _selectedEntities.AsSpan(0, _selectedCount));
    }

    public void ClearSelection()
    {
        if (_selectedCount == 0)
        {
            MassFlow.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
            return;
        }

        _selectedCount = 0;
        _selectionRevision++;
        SelectionSnapshotCountFrame++;
        MassFlow.SetSelectedFlags(AgentState, ReadOnlySpan<Entity>.Empty);
    }

    public void MarkStructuralChange()
    {
        StructuralChangeRevision++;
        StructuralChangesFrame++;
    }

    public void MarkCommandApply()
    {
        CommandCountFrame++;
    }

    public bool RotateSelectedFormation(World world, float deltaRadians, int localPlayerId)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_selectedCount <= 0 ||
            !(MathF.Abs(deltaRadians) > Config.Semantics.Group.FormationRotationEpsilonRadians))
        {
            return false;
        }

        if (!CanLocalPlayerCommandSelection(world, SelectedEntities, localPlayerId))
        {
            CommandRejectsFrame++;
            CommandRejectsTotal++;
            return false;
        }

        NavGroupRuntime.RotateSelected(world, AgentState, SelectedEntities, deltaRadians);
        MarkCommandApply();
        return true;
    }

    private static bool CanLocalPlayerCommandSelection(World world, ReadOnlySpan<Entity> selected, int localPlayerId)
    {
        int liveCommandableActors = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity actor = selected[i];
            if (!world.IsAlive(actor))
            {
                continue;
            }

            if (!world.TryGet(actor, out PlayerOwner owner) ||
                owner.PlayerId != localPlayerId)
            {
                return false;
            }

            liveCommandableActors++;
        }

        return liveCommandableActors > 0;
    }

    public void MarkScenarioSpawned()
    {
        ScenarioSpawnCount++;
    }

    public void MarkSceneResetExecuted()
    {
        SceneResetCount++;
    }

    public void MarkFlowReconcile()
    {
        FlowReconcileCountFrame++;
    }

    public void RejectCommandOutsideWorld(float worldXCm, float worldYCm)
    {
        LastRejectedCommandXCm = worldXCm;
        LastRejectedCommandYCm = worldYCm;
        CommandRejectsFrame++;
        CommandRejectsTotal++;
    }

    public void RejectCommandWithoutSelection(float worldXCm, float worldYCm)
    {
        LastRejectedCommandXCm = worldXCm;
        LastRejectedCommandYCm = worldYCm;
        CommandRejectsFrame++;
        CommandRejectsTotal++;
    }

    public void RejectCommandUnauthorizedSelection(float worldXCm, float worldYCm)
    {
        LastRejectedCommandXCm = worldXCm;
        LastRejectedCommandYCm = worldYCm;
        CommandRejectsFrame++;
        CommandRejectsTotal++;
    }

    public void RejectCommandOrderSubmit(float worldXCm, float worldYCm)
    {
        LastRejectedCommandXCm = worldXCm;
        LastRejectedCommandYCm = worldYCm;
        CommandRejectsFrame++;
        CommandRejectsTotal++;
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
        MassFlow.ResetAuthoredAgents(agentSeeds);
    }

    public void ClearAuthoredRuntimeBindings(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ClearSelection();
        NavGroupRuntime.Reset();
        AgentState.ClearRuntimeBindings(world);
        MassFlow.ResetAuthoredAgents(ReadOnlySpan<MassNavigationAgentSeed>.Empty);
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
            throw new InvalidOperationException("MassCrowd authored rebuild requires matching entity, seed, and controllable spans.");
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

        ClearAuthoredRuntimeBindings(world);
        MassFlow.ResetAuthoredAgents(agentSeeds);
        for (int i = 0; i < entities.Length; i++)
        {
            BindSpawnedAgent(world, entities[i], i, controllableFlags[i]);
        }

        RestoreSelectionAfterAuthoredRebuild(world, previousSelectedEntities, previousSelectionRevision);
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
            selectedEntities.Length > 0 ? "selection command" : "team command");
        MoveSolverWindow(ResolveSolverFocusForWorkArea(), selectedEntities.Length > 0 ? "selection command" : "team command");
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
        FocusBudgetUpdatesFrame++;
        FocusBudgetUpdatesTotal++;
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
            MassFlow.GetPositionX(agentIndex),
            MassFlow.GetPositionY(agentIndex));
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
            !world.TryGet(agent, out MassCrowdAgentIndex index))
        {
            return false;
        }

        int agentIndex = index.Value;
        if ((uint)agentIndex >= (uint)MassFlow.UnitCount)
        {
            return false;
        }

        worldCm = GetAgentWorldPositionCm(agentIndex);
        return true;
    }

    public MassNavigationGroupSemantics GetRuntimeGroupSemantics()
    {
        return MassFlow.Semantics.Group;
    }

    public float GetAgentBodyRadiusCm(int agentIndex)
    {
        RequireAgentIndex(agentIndex);
        return MassFlow.GetBodyRadiusCm(agentIndex);
    }

    public MassNavigationObstacleSnapshot GetObstacleWorldSnapshot(int obstacleIndex)
    {
        RequireObstacleIndex(obstacleIndex);
        return new MassNavigationObstacleSnapshot(
            ToWorldXCm(MassFlow.GetObstacleX(obstacleIndex)),
            ToWorldYCm(MassFlow.GetObstacleY(obstacleIndex)),
            MassFlow.GetObstacleRadius(obstacleIndex));
    }

    public void RebuildRuntimeObstacles(ReadOnlySpan<MassNavigationObstacleSnapshot> obstacles)
    {
        MassFlow.ResetRuntimeObstaclesFromWorld(obstacles);
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
            MassFlow.ApplyExternalDisplacementRange(firstMemberAgentIndex, memberAgentCount, deltaX, deltaY);
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
            MassFlow.ApplyExternalDisplacement(memberAgentIndices, deltaX, deltaY);
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
        MassFlow.SyncEntities(world, AgentState);
    }

    public MassNavigationCarriedSlotTarget ResolveCarriedAgentSlotTarget(
        int memberAgentIndex,
        float carrierLocalXCm,
        float carrierLocalYCm,
        float slotOffsetLocalXCm,
        float slotOffsetLocalYCm)
    {
        RequireAgentIndex(memberAgentIndex);
        System.Numerics.Vector2 resolvedLocal = MassFlow.ResolveUnitNavigableTarget(
            memberAgentIndex,
            carrierLocalXCm + slotOffsetLocalXCm,
            carrierLocalYCm + slotOffsetLocalYCm,
            slotOffsetLocalXCm,
            slotOffsetLocalYCm,
            MassFlow.Semantics.TargetProjection.GroupSlotClearanceCm);
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
        return MassFlow.TryGetUnitTarget(agentIndex, out xCm, out yCm);
    }

    public bool TryGetAgentNavigationTargetWorldCm(int agentIndex, out float xCm, out float yCm)
    {
        RequireAgentIndex(agentIndex);
        if (!MassFlow.TryGetUnitTarget(agentIndex, out float localX, out float localY))
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
        return MassFlow.SetUnitTarget(agentIndex, xCm, yCm, resetRecovery);
    }

    public bool SetAgentNavigationTargetLocalCm(
        int agentIndex,
        float xCm,
        float yCm,
        float stopThresholdCm,
        bool resetRecovery = false)
    {
        RequireAgentIndex(agentIndex);
        return MassFlow.SetUnitTarget(agentIndex, xCm, yCm, stopThresholdCm, resetRecovery);
    }

    public bool SetAgentNavigationTargetWorldCm(int agentIndex, float worldXCm, float worldYCm, bool resetRecovery = false)
    {
        RequireAgentIndex(agentIndex);
        return MassFlow.SetUnitTarget(agentIndex, ToLocalXCm(worldXCm), ToLocalYCm(worldYCm), resetRecovery);
    }

    public bool SetAgentNavigationTargetWorldCm(
        int agentIndex,
        float worldXCm,
        float worldYCm,
        float stopThresholdCm,
        bool resetRecovery = false)
    {
        RequireAgentIndex(agentIndex);
        return MassFlow.SetUnitTarget(
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
        MassFlow.ReleaseUnitToTeamTarget(agentIndex);
    }

    public int DrainArrivalEvents(Span<MassNavigationArrivalEvent> destination)
    {
        return MassFlow.DrainArrivalEvents(destination, AgentState, SolverWindowMinXCm, SolverWindowMinYCm);
    }

    public void StepNavigationForTests(World world, float dt, bool runHardResolve = false, int hardResolveCandidateThresholdAgents = 1)
    {
        ArgumentNullException.ThrowIfNull(world);
        MassFlow.Step(
            dt,
            world,
            NavGroupRuntime,
            runHardResolve,
            hardResolveCandidateThresholdAgents);
    }

    public bool TryGetAgentLocomotionSpeedNormalized(int agentIndex, out float speed)
    {
        speed = 0f;
        if ((uint)agentIndex >= (uint)MassFlow.UnitCount)
        {
            return false;
        }

        if (!MassFlow.HasUnitTarget(agentIndex) ||
            MassFlow.IsUnitSettled(agentIndex))
        {
            return true;
        }

        Vector2 velocity = MassFlow.GetVelocityCmPerSecond(agentIndex);
        float authoredSpeed = MassFlow.GetSpeedCmPerSecond(agentIndex);
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
        return MassFlow.SetUnitTarget(memberAgentIndex, target.LocalXCm, target.LocalYCm, resetRecovery);
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

        if ((uint)agentIndex >= (uint)MassFlow.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation spawned agent index {agentIndex} exceeds current agent count {MassFlow.UnitCount}.");
        }

        if (world.Has<MassCrowdAgentIndex>(entity) || world.Has<MassCrowdAgentProfile>(entity))
        {
            throw new InvalidOperationException($"MassNavigation entity {entity.Id} was already bound as an agent.");
        }

        int teamId = MassFlow.GetTeam(agentIndex);
        UpsertComponent(world, entity, new Team { Id = teamId });
        int profileId = world.TryGet(entity, out MassCrowdAgent agent) ? agent.ProfileId : 0;
        world.Add(entity, new MassCrowdAgentIndex { Value = agentIndex });
        world.Add(entity, new MassCrowdAgentProfile
        {
            ProfileId = profileId,
            Heavy = MassFlow.IsHeavyProfile(agentIndex),
            VisualScale = MassFlow.GetVisualScale(agentIndex),
            SpeedCmPerSecond = MassFlow.GetSpeedCmPerSecond(agentIndex),
        });
        AgentState.RegisterAgentAtIndex(entity, agentIndex, controllable);
    }

    public static int ResolveAgentLocomotionSpeedParamKey()
    {
        return PerformerParamKeyRegistry.Register(AgentLocomotionSpeedParamKey);
    }

    public MassNavigationMoveCommandResult SubmitMoveCommand(
        World world,
        Dictionary<string, object> globals,
        OrderBufferSystem orderBufferSystem,
        OrderTypeRegistry orderTypeRegistry,
        Vector2 centerCm,
        int playerId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(orderBufferSystem);
        ArgumentNullException.ThrowIfNull(orderTypeRegistry);

        if (!ContainsWorldPoint(centerCm.X, centerCm.Y))
        {
            RejectCommandOutsideWorld(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.OutsideWorld;
        }

        ReadOnlySpan<Entity> selected = SelectedEntities;
        if (selected.Length <= 0)
        {
            RejectCommandWithoutSelection(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.EmptySelection;
        }

        if (!CanSubmitSelectionMoveOrders(world, selected, playerId))
        {
            RejectCommandUnauthorizedSelection(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.UnauthorizedSelection;
        }

        if (!SelectionContextRuntime.TryGetCurrentContainer(world, globals, out Entity selectionContainer))
        {
            throw new InvalidOperationException("MassCrowd runtime requires a current selection container before submitting move orders.");
        }

        if (!orderTypeRegistry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException($"MassCrowd runtime requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        int sharedOrderId = AllocateSharedOrderId();
        int submitted = 0;
        float rotationRadians = NavGroupRuntime.SelectedRotationRadians;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity actor = selected[i];
            if (!world.IsAlive(actor))
            {
                continue;
            }

            var order = new Order
            {
                OrderId = sharedOrderId,
                OrderTypeId = moveOrderTypeId,
                PlayerId = playerId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = MassNavigationMoveOrderArgs.Encode(
                    centerCm,
                    FormationMode,
                    rotationRadians,
                    selectionContainer)
            };

            OrderSubmitResult result = orderBufferSystem.SubmitOrder(actor, in order);
            if (IsAcceptedOrderSubmit(result))
            {
                submitted++;
            }
        }

        if (submitted <= 0)
        {
            RejectCommandOrderSubmit(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.OrderSubmitRejected;
        }

        FocusCommandTarget(centerCm, selected);
        MarkCommandApply();
        return MassNavigationMoveCommandResult.Submitted;
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
            StreamingWindowUpdatesFrame++;
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

        float previousOriginX = MassFlow.WorldOriginXCm;
        float previousOriginY = MassFlow.WorldOriginYCm;
        _simWindowCenterXCm = nextCenterX;
        _simWindowCenterYCm = nextCenterY;
        float nextOriginX = SolverWindowMinXCm;
        float nextOriginY = SolverWindowMinYCm;
        MassFlow.ShiftLocalFrame(nextOriginX - previousOriginX, nextOriginY - previousOriginY);
        MassFlow.SetWorldOrigin(nextOriginX, nextOriginY);
        MassFlow.RequestFlowRebuild();
        SolverWindowMovesFrame++;
        SolverWindowMovesTotal++;
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
                (uint)unitIndex >= (uint)MassFlow.UnitCount)
            {
                continue;
            }

            float worldX = ToWorldXCm(MassFlow.GetPositionX(unitIndex));
            float worldY = ToWorldYCm(MassFlow.GetPositionY(unitIndex));
            IncludePoint(ref minX, ref maxX, ref minY, ref maxY, worldX, worldY);
        }
    }

    private static bool CanSubmitSelectionMoveOrders(World world, ReadOnlySpan<Entity> selected, int localPlayerId)
    {
        int liveCommandableActors = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity actor = selected[i];
            if (!world.IsAlive(actor))
            {
                continue;
            }

            if (!CanLocalPlayerCommand(world, actor, localPlayerId))
            {
                return false;
            }

            liveCommandableActors++;
        }

        return liveCommandableActors > 0;
    }

    private static bool CanLocalPlayerCommand(World world, Entity actor, int localPlayerId)
    {
        return world.TryGet(actor, out PlayerOwner owner) &&
               owner.PlayerId == localPlayerId;
    }

    private static bool IsAcceptedOrderSubmit(OrderSubmitResult result)
    {
        return result == OrderSubmitResult.Activated ||
               result == OrderSubmitResult.Queued;
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
        if ((uint)agentIndex >= (uint)MassFlow.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation agent index {agentIndex} exceeds current agent count {MassFlow.UnitCount}.");
        }
    }

    private void RequireAgentRange(int firstAgentIndex, int agentCount, string fieldName)
    {
        int end = firstAgentIndex + agentCount;
        if (firstAgentIndex < 0 ||
            agentCount <= 0 ||
            end < firstAgentIndex ||
            end > MassFlow.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation agent range '{fieldName}' [{firstAgentIndex}, {end}) must be within current agent count {MassFlow.UnitCount}.");
        }
    }

    private void RequireObstacleIndex(int obstacleIndex)
    {
        if ((uint)obstacleIndex >= (uint)MassFlow.ObstacleCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation obstacle index {obstacleIndex} exceeds current obstacle count {MassFlow.ObstacleCount}.");
        }
    }

    private void MarkAuthoredRuntimeBindingChanged()
    {
        AuthoredRuntimeBindingRevision++;
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

    private static float Smooth(float current, float sampleMs)
    {
        if (sampleMs < 0f)
        {
            sampleMs = 0f;
        }

        return current <= 0.001f
            ? sampleMs
            : (current * (1f - TimingWeight)) + (sampleMs * TimingWeight);
    }

    private static float ObserveHz(ref long lastTick, float current)
    {
        long now = Stopwatch.GetTimestamp();
        if (lastTick == 0)
        {
            lastTick = now;
            return current;
        }

        double elapsedTicks = now - lastTick;
        lastTick = now;
        if (elapsedTicks <= 0d)
        {
            return current;
        }

        float hz = (float)(Stopwatch.Frequency / elapsedTicks);
        return current <= 0.001f
            ? hz
            : (current * (1f - TimingWeight)) + (hz * TimingWeight);
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
