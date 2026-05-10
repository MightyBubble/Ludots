using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Spatial;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationSimulationRuntime
{
    private const float TimingWeight = 0.18f;

    private readonly int _initialSelectedTeamId;
    private int[] _teamIds = Array.Empty<int>();
    private Entity[] _selectionScratch = new Entity[256];
    private Entity[] _selectedEntities = new Entity[64];
    private int _selectedCount;
    private uint _selectionRevision;
    private bool _sceneResetRequested;
    private int _frameIndex;
    private int _nextSharedOrderId = 1;
    private readonly WorldGridLoadedChunks _loadedChunks;
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
    private float _lastCameraFocusXCm;
    private float _lastCameraFocusYCm;
    private float _lastCameraViewWidthCm;
    private float _lastCameraViewHeightCm;
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
    private long _commandDispatchTick;
    private long _simTick;
    private long _performerTick;
    private long _hudTick;
    private long _panelTick;
    private string _solverWindowDriver = "initial nav area";

    public int SelectionSnapshotCountFrame { get; private set; }
    public int CommandCountFrame { get; private set; }
    public int StructuralChangesFrame { get; private set; }
    public int StructuralChangeRevision { get; private set; }
    public int FlowReconcileCountFrame { get; private set; }
    public int CameraBudgetUpdatesFrame { get; private set; }
    public int SolverWindowMovesFrame { get; private set; }
    public float FrameMs { get; private set; }
    public float Fps { get; private set; }
    public float SelectionSyncMs { get; private set; }
    public float CommandApplyMs { get; private set; }
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
    public float CommandDispatchHzObserved { get; private set; }
    public float SimHzObserved { get; private set; }
    public float PerformerHzObserved { get; private set; }
    public float HudHzObserved { get; private set; }
    public float PanelHzObserved { get; private set; }
    public int CrowdInViewCount { get; private set; }
    public int CrowdSubmittedCount { get; private set; }
    public int ObstacleSubmittedCount { get; private set; }
    public int PerformerDroppedCount { get; private set; }
    public int StreamingWindowUpdatesFrame { get; private set; }
    public int CommandRejectsFrame { get; private set; }
    public int CommandRejectsTotal { get; private set; }
    public int CameraBudgetUpdatesTotal { get; private set; }
    public int SolverWindowMovesTotal { get; private set; }
    public int ScenarioSpawnCount { get; private set; }
    public int SceneResetCount { get; private set; }
    public float LastRejectedCommandXCm { get; private set; }
    public float LastRejectedCommandYCm { get; private set; }
    public MassNavigationConfig Config { get; }
    public MassNavigationAgentState AgentState { get; } = new();
    public MassNavigationCommandRuntime Commands { get; } = new();
    public MassFlowTuning FlowTuning { get; }
    public MassNavigationCadenceConfig Cadence { get; }
    internal MassNavigationCadenceScheduler CadenceScheduler { get; }
    public MassNavigationFormationRuntime FormationRuntime { get; }
    public MassNavigationGroupRuntime NavGroupRuntime { get; }
    internal MassNavigationSpawnReceiptRuntime SpawnReceipts { get; } = new();
    public MassFlowSimulationState MassFlow { get; }
    public MassNavigationWorldConfig WorldConfig { get; }
    public WorldGridLoadedChunks LoadedChunks => _loadedChunks;

    public int SelectedCount => _selectedCount;
    public uint SelectionRevision => _selectionRevision;
    public ReadOnlySpan<Entity> SelectedEntities => _selectedEntities.AsSpan(0, _selectedCount);
    public ReadOnlySpan<int> TeamIds => _teamIds;
    public int TeamCount => _teamIds.Length;
    public int FrameIndex => _frameIndex;
    public int PendingCommandCount => Commands.PendingCommandCount;
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
        MassFlow = new MassFlowSimulationState();
        WorldConfig = config.World ?? throw new InvalidOperationException("MassNavigationSimulationRuntime requires explicit world config.");
        Cadence = config.Cadence;
        CadenceScheduler = new MassNavigationCadenceScheduler(Cadence);
        _loadedChunks = new WorldGridLoadedChunks(WorldConfig.StreamingChunkSizeCm);
        _simWindowWidthCm = WorldConfig.SolverWindowWidthCm;
        _simWindowHeightCm = WorldConfig.SolverWindowHeightCm;
        _simWindowCenterXCm = WorldConfig.ActiveHotZone.CenterXCm;
        _simWindowCenterYCm = WorldConfig.ActiveHotZone.CenterYCm;
        _lastCameraFocusXCm = _simWindowCenterXCm;
        _lastCameraFocusYCm = _simWindowCenterYCm;
        _lastCameraViewWidthCm = _simWindowWidthCm;
        _lastCameraViewHeightCm = _simWindowHeightCm;
        _flowWorkAreaCenterXCm = _simWindowCenterXCm;
        _flowWorkAreaCenterYCm = _simWindowCenterYCm;
        _flowWorkAreaWidthCm = _simWindowWidthCm;
        _flowWorkAreaHeightCm = _simWindowHeightCm;
        FlowTuning = config.Flow;
        FormationRuntime = new MassNavigationFormationRuntime();
        NavGroupRuntime = new MassNavigationGroupRuntime(FormationRuntime);
        AgentsPerTeam = config.Scenario.AgentsPerTeam;
        _initialSelectedTeamId = config.Scenario.InitialSelectedTeamId;
        ConfigureScenarioTeams(CreateTeamIdArray(config.Scenario.Teams));
        SelectedTeamId = _initialSelectedTeamId;
        MassFlow.ArrivalTuning.Enabled = config.Arrival.Enabled;
        MassFlow.ArrivalTuning.TimeoutMs = config.Arrival.TimeoutMs;
        MassFlow.ArrivalTuning.ProgressDistanceCm = config.Arrival.ProgressDistanceCm;
        MassFlow.ArrivalTuning.WakePushDistanceCm = config.Arrival.WakePushDistanceCm;
        MassFlow.ArrivalTuning.MaxRetryCount = config.Arrival.MaxRetryCount;
        MassFlow.AvoidanceTuning.LightNavMass = config.Avoidance.LightNavMass;
        MassFlow.AvoidanceTuning.HeavyNavMass = config.Avoidance.HeavyNavMass;
        MassFlow.AvoidanceTuning.LightVisualScale = config.Avoidance.LightVisualScale;
        MassFlow.AvoidanceTuning.HeavyVisualScale = config.Avoidance.HeavyVisualScale;
        MassFlow.AvoidanceTuning.DominantMassRatio = config.Avoidance.DominantMassRatio;
        MassFlow.AvoidanceTuning.FriendlyResponseScale = config.Avoidance.FriendlyResponseScale;
        MassFlow.AvoidanceTuning.NonFriendlyResponseScale = config.Avoidance.NonFriendlyResponseScale;
        MassFlow.AvoidanceTuning.DominantPushResponseScale = config.Avoidance.DominantPushResponseScale;
        MassFlow.Semantics.Obstacle.AgentBodyRadiusCm = config.Semantics.Obstacle.AgentBodyRadiusCm;
        MassFlow.Semantics.Obstacle.HardResolveCandidateDistanceCm = config.Semantics.Obstacle.HardResolveCandidateDistanceCm;
        MassFlow.Semantics.Obstacle.SoftPushPaddingCm = config.Semantics.Obstacle.SoftPushPaddingCm;
        MassFlow.Semantics.Obstacle.SoftPushForceScale = config.Semantics.Obstacle.SoftPushForceScale;
        MassFlow.Semantics.TargetProjection.TeamTargetClearanceCm = config.Semantics.TargetProjection.TeamTargetClearanceCm;
        MassFlow.Semantics.TargetProjection.GroupCenterClearanceCm = config.Semantics.TargetProjection.GroupCenterClearanceCm;
        MassFlow.Semantics.TargetProjection.TeamSlotClearanceCm = config.Semantics.TargetProjection.TeamSlotClearanceCm;
        MassFlow.Semantics.TargetProjection.GroupSlotClearanceCm = config.Semantics.TargetProjection.GroupSlotClearanceCm;
        MassFlow.Semantics.TargetProjection.LooseTargetClearanceCm = config.Semantics.TargetProjection.LooseTargetClearanceCm;
        MassFlow.Semantics.Group.SpawnSpacingCm = config.Semantics.Group.SpawnSpacingCm;
        MassFlow.Semantics.Group.TeamSlotSpacingCm = config.Semantics.Group.TeamSlotSpacingCm;
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
        MassFlow.Semantics.Steering.SpeedCmPerSecond = config.Semantics.Steering.SpeedCmPerSecond;
        MassFlow.Semantics.Steering.SeparationRadiusCm = config.Semantics.Steering.SeparationRadiusCm;
        MassFlow.Semantics.Steering.GoalArrivalRadiusCm = config.Semantics.Steering.GoalArrivalRadiusCm;
        MassFlow.Semantics.Steering.FlowObstacleAvoidanceScale = config.Semantics.Steering.FlowObstacleAvoidanceScale;
        MassFlow.Semantics.Steering.FormationSeparationScale = config.Semantics.Steering.FormationSeparationScale;
        MassFlow.Semantics.Steering.LooseSeparationScale = config.Semantics.Steering.LooseSeparationScale;
        MassFlow.Semantics.Steering.VelocityBlendPerSecond = config.Semantics.Steering.VelocityBlendPerSecond;
    }

    public void BindBoardWorld(WorldSizeSpec boardWorldSize)
    {
        _boardWorldSize = boardWorldSize;
        _boardWorldBound = true;
        ClampSolverWindowCenter(ref _simWindowCenterXCm, ref _simWindowCenterYCm);
        _lastCameraFocusXCm = _simWindowCenterXCm;
        _lastCameraFocusYCm = _simWindowCenterYCm;
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
            MassFlowSimulationState.FieldWidthCm * 0.5f,
            MassFlowSimulationState.FieldHeightCm * 0.5f)));
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
        CameraBudgetUpdatesFrame = 0;
        SolverWindowMovesFrame = 0;
        FrameMs = dt > 0f ? dt * 1000f : 0f;
        Fps = FrameMs > 0.001f ? 1000f / FrameMs : 0f;
    }

    public void ObserveSelectionSync(double sampleMs) => SelectionSyncMs = Smooth(SelectionSyncMs, (float)sampleMs);
    public void ObserveCommandApply(double sampleMs) => CommandApplyMs = Smooth(CommandApplyMs, (float)sampleMs);
    public void ObserveFormationTargets(double sampleMs) => FormationTargetMs = Smooth(FormationTargetMs, (float)sampleMs);
    public void ObserveFlowFieldRebuild(double sampleMs) => FlowFieldRebuildMs = Smooth(FlowFieldRebuildMs, (float)sampleMs);
    public void ObserveStepPrep(double sampleMs) => StepPrepMs = Smooth(StepPrepMs, (float)sampleMs);
    public void ObserveLocalSteering(double sampleMs) => LocalSteeringMs = Smooth(LocalSteeringMs, (float)sampleMs);
    public void ObserveSimStep(double sampleMs) => SimStepMs = Smooth(SimStepMs, (float)sampleMs);
    public void ObserveHardResolve(double sampleMs) => HardResolveMs = Smooth(HardResolveMs, (float)sampleMs);
    public void ObserveEntitySync(double sampleMs) => EntitySyncMs = Smooth(EntitySyncMs, (float)sampleMs);
    public void ObservePerformerCommand(double sampleMs) => PerformerCommandMs = Smooth(PerformerCommandMs, (float)sampleMs);

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
    public void ObserveCommandDispatchTick() => CommandDispatchHzObserved = ObserveHz(ref _commandDispatchTick, CommandDispatchHzObserved);
    public void ObserveSimTick() => SimHzObserved = ObserveHz(ref _simTick, SimHzObserved);
    public void ObservePerformerTick() => PerformerHzObserved = ObserveHz(ref _performerTick, PerformerHzObserved);
    public void ObserveHudTick() => HudHzObserved = ObserveHz(ref _hudTick, HudHzObserved);
    public void ObservePanelTick() => PanelHzObserved = ObserveHz(ref _panelTick, PanelHzObserved);

    public Span<Entity> EnsureSelectionScratch(int required)
    {
        if (required > _selectionScratch.Length)
        {
            int next = _selectionScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _selectionScratch, next);
        }

        return _selectionScratch.AsSpan(0, required);
    }

    public void SetSelection(ReadOnlySpan<Entity> entities, uint revision)
    {
        if (entities.Length > _selectedEntities.Length)
        {
            int next = _selectedEntities.Length;
            while (next < entities.Length)
            {
                next *= 2;
            }

            Array.Resize(ref _selectedEntities, next);
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

    public void SetAgentsPerTeam(int agentsPerTeam)
    {
        int next = Math.Max(0, agentsPerTeam);
        if (AgentsPerTeam == next)
        {
            return;
        }

        AgentsPerTeam = next;
        RequestSceneReset();
    }

    public void SetSelectedTeam(int teamId)
    {
        if (Array.IndexOf(_teamIds, teamId) >= 0)
        {
            SelectedTeamId = teamId;
        }
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
        FormationMode = mode;
    }

    public void RequestSceneReset()
    {
        _sceneResetRequested = true;
    }

    public void FocusSimulationWindow(System.Numerics.Vector2 worldCenterCm)
    {
        ObserveFlowWorkArea(worldCenterCm, ReadOnlySpan<Entity>.Empty, "manual focus");
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
        ObserveFlowWorkArea(worldCenterCm, selectedEntities, selectedEntities.Length > 0 ? "selection command" : "team command");
        MoveSolverWindow(ResolveSolverFocusForWorkArea(), selectedEntities.Length > 0 ? "selection command" : "team command");
        UpdateStreamingWindow(ResolveStreamingFocus());
    }

    public void FocusCommandTargetForEntities(System.Numerics.Vector2 worldCenterCm, Entity[] selectedEntities)
    {
        FocusCommandTarget(worldCenterCm, selectedEntities.AsSpan());
    }

    public void ObserveCameraFocus(System.Numerics.Vector2 cameraCenterCm)
    {
        ObserveCameraFocus(cameraCenterCm, _lastCameraViewWidthCm, _lastCameraViewHeightCm);
    }

    public void ObserveCameraFocus(System.Numerics.Vector2 cameraCenterCm, float viewWidthCm, float viewHeightCm)
    {
        _lastCameraFocusXCm = cameraCenterCm.X;
        _lastCameraFocusYCm = cameraCenterCm.Y;
        _lastCameraViewWidthCm = MathF.Max(1f, viewWidthCm);
        _lastCameraViewHeightCm = MathF.Max(1f, viewHeightCm);
        ObserveFlowWorkArea(
            cameraCenterCm,
            ReadOnlySpan<Entity>.Empty,
            _hasCommandFocus && _commandFocusTicksRemaining > 0 ? "camera budget + command hold" : "camera budget");
        CameraBudgetUpdatesFrame++;
        CameraBudgetUpdatesTotal++;
        if (_commandFocusTicksRemaining > 0)
        {
            _commandFocusTicksRemaining--;
            if (_commandFocusTicksRemaining == 0)
            {
                _hasCommandFocus = false;
            }

            UpdateStreamingWindow(ResolveStreamingFocus());
            return;
        }

        _hasCommandFocus = false;
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
        int radius = WorldConfig.StreamingRadiusCm;
        int chunkSize = _loadedChunks.ChunkSizeCm;
        int minChunkX = MathUtil.FloorDiv(centerX - radius, chunkSize);
        int maxChunkX = MathUtil.FloorDiv(centerX + radius, chunkSize);
        int minChunkY = MathUtil.FloorDiv(centerY - radius, chunkSize);
        int maxChunkY = MathUtil.FloorDiv(centerY + radius, chunkSize);
        if (minChunkX == _streamingMinChunkX &&
            maxChunkX == _streamingMaxChunkX &&
            minChunkY == _streamingMinChunkY &&
            maxChunkY == _streamingMaxChunkY &&
            radius == _streamingRadiusCm)
        {
            return;
        }

        _streamingMinChunkX = minChunkX;
        _streamingMaxChunkX = maxChunkX;
        _streamingMinChunkY = minChunkY;
        _streamingMaxChunkY = maxChunkY;
        _streamingRadiusCm = radius;
        _loadedChunks.Update(centerX, centerY, radius);
        StreamingWindowUpdatesFrame++;
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

    private void ObserveFlowWorkArea(System.Numerics.Vector2 focusCm, ReadOnlySpan<Entity> selectedEntities, string reason)
    {
        float minX = _lastCameraFocusXCm - (_lastCameraViewWidthCm * 0.5f);
        float maxX = _lastCameraFocusXCm + (_lastCameraViewWidthCm * 0.5f);
        float minY = _lastCameraFocusYCm - (_lastCameraViewHeightCm * 0.5f);
        float maxY = _lastCameraFocusYCm + (_lastCameraViewHeightCm * 0.5f);

        IncludePoint(ref minX, ref maxX, ref minY, ref maxY, focusCm.X, focusCm.Y);
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

    private WorldSizeSpec RequireBoardWorldSize()
    {
        if (!_boardWorldBound)
        {
            throw new InvalidOperationException("MassNavigationSimulationRuntime requires PrimaryBoard.WorldSize to be bound before world operations.");
        }

        return _boardWorldSize;
    }

    private static float ClampWindowCenterToBounds(float worldCm, int minCm, int maxCm, float windowSizeCm)
    {
        float halfSize = windowSizeCm * 0.5f;
        float min = minCm + halfSize;
        float max = maxCm - halfSize;
        return min <= max ? Math.Clamp(worldCm, min, max) : (minCm + maxCm) * 0.5f;
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


