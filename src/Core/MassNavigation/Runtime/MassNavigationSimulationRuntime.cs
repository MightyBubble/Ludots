using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.MovePlanning;
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

public readonly record struct MassNavigationAvoidanceAgentSnapshot(
    int AgentIndex,
    float LocalXCm,
    float LocalYCm,
    float WorldXCm,
    float WorldYCm,
    int TeamId,
    float BodyRadiusCm,
    bool HeavyProfile,
    bool Settled,
    bool InsidePlayArea);

public readonly record struct MassNavigationAvoidanceSnapshot(
    int UnitCount,
    int ObstacleCount,
    int SettledUnitCount,
    float PlayAreaMinXCm,
    float PlayAreaMaxXCm,
    float PlayAreaMinYCm,
    float PlayAreaMaxYCm);

public readonly record struct MassNavigationArrivalEvent(
    int AgentIndex,
    Entity Agent,
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
    float GroupedAgentFlowSlowRadiusCm,
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

public sealed class MassNavigationSimulationRuntime
{
    public const string AgentLocomotionSpeedParamKey = "mass_navigation.agent.locomotion.speed";

    private MassNavigationDomainStanceProjection? _domainStanceProjection;
    private int[] _teamIds = Array.Empty<int>();
    private int _frameIndex;
    private ILoadedChunkWindowSource? _loadedChunks;
    private ILoadedChunkContributor? _loadedChunkContributor;
    private readonly Dictionary<long, float> _loadedChunkLastTouchedSeconds;
    private readonly List<long> _loadedChunksToEvict;
    private readonly List<long> _loadedChunksAddedDuringUpdate;
    private readonly List<long> _streamingWindowChunkKeys;
    private readonly HashSet<Entity> _authoredBindingSeenEntities;
    private readonly int _loadedChunkCapacity;
    private float _streamingClockSeconds;
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
    private int _lastOrderMemberCount;
    private float _flowWorkAreaCenterXCm;
    private float _flowWorkAreaCenterYCm;
    private float _flowWorkAreaWidthCm;
    private float _flowWorkAreaHeightCm;
    private int _flowWorkAreaRevision;
    private string _flowWorkAreaReason = "initial contact";
    private string _solverWindowDriver = "initial nav area";
    private readonly string _activeHotZoneId;
    private readonly string _activeHotZoneLabel;
    private readonly int _activeHotZoneCenterXCm;
    private readonly int _activeHotZoneCenterYCm;
    private readonly int _activeHotZoneWidthCm;
    private readonly int _activeHotZoneHeightCm;
    private bool _authoredAgentBindingPassComplete;
    private bool _environmentBindingPassComplete;

    private struct FocusState
    {
        public float SolverCenterX;
        public float SolverCenterY;
        public int CommandTicksRemaining;
        public bool HasCommandFocus;
        public float CommandFocusX;
        public float CommandFocusY;
        public int LastOrderMemberCount;
        public float WorkAreaCenterX;
        public float WorkAreaCenterY;
        public float WorkAreaWidth;
        public float WorkAreaHeight;
        public int WorkAreaRevision;
        public string WorkAreaReason;
        public string SolverDriver;
    }

    public MassNavigationTelemetry Telemetry { get; } = new();
    public int CommandCountFrame => Telemetry.CommandCountFrame;
    public int StructuralChangesFrame => Telemetry.StructuralChangesFrame;
    public int StructuralChangeRevision => Telemetry.StructuralChangeRevision;
    public int FlowReconcileCountFrame => Telemetry.FlowReconcileCountFrame;
    public int FocusBudgetUpdatesFrame => Telemetry.FocusBudgetUpdatesFrame;
    public int SolverWindowMovesFrame => Telemetry.SolverWindowMovesFrame;
    public float FrameMs => Telemetry.FrameMs;
    public float Fps => Telemetry.Fps;
    public float GroupTargetUpdateMs => Telemetry.GroupTargetUpdateMs;
    public float FlowFieldRebuildMs => Telemetry.FlowFieldRebuildMs;
    public float StepPrepMs => Telemetry.StepPrepMs;
    public float LocalSteeringMs => Telemetry.LocalSteeringMs;
    public float SimStepMs => Telemetry.SimStepMs;
    public float HardResolveMs => Telemetry.HardResolveMs;
    public float EntitySyncMs => Telemetry.EntitySyncMs;
    public float PerformerCommandMs => Telemetry.PerformerCommandMs;
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
    public int FocusBudgetUpdatesTotal => Telemetry.FocusBudgetUpdatesTotal;
    public int SolverWindowMovesTotal => Telemetry.SolverWindowMovesTotal;
    public int ScenarioSpawnCount => Telemetry.ScenarioSpawnCount;
    public int AuthoredRuntimeBindingRevision => Telemetry.AuthoredRuntimeBindingRevision;
    internal bool RuntimeBindingPreparationComplete => _authoredAgentBindingPassComplete && _environmentBindingPassComplete;
    public MassNavigationConfig Config { get; }
    internal MassNavigationAgentState AgentState { get; }
    public MassNavigationFlowTuning FlowTuning { get; }
    public MassNavigationCadenceConfig Cadence { get; }
    internal MassNavigationCadenceScheduler CadenceScheduler { get; }
    internal MassNavigationGroupRuntime NavGroupRuntime { get; }
    internal MassNavigationFlowSolverState MassNavigationFlow { get; }
    public MassNavigationWorldConfig WorldConfig { get; }
    public ILoadedChunks LoadedChunks => RequireLoadedChunks();
    public MassNavigationStreamingConfig Streaming => Config.Streaming;

    public int NavigationAgentCount => MassNavigationFlow.UnitCount;
    public int GetAgentDomainId(int agentIndex)
    {
        RequireAgentIndex(agentIndex);
        return MassNavigationFlow.GetTeam(agentIndex);
    }
    public int NavigationObstacleCount => MassNavigationFlow.ObstacleCount;
    public int NavigationSettledAgentCount => MassNavigationFlow.SettledUnitCount;
    public ReadOnlySpan<int> TeamIds => _teamIds;
    public int TeamCount => _teamIds.Length;
    public int FrameIndex => _frameIndex;
    public int AgentsPerTeam => Config.Scenario.AgentsPerTeam;
    public int LoadedChunkCount => _loadedChunkContributor?.ActiveChunkKeys.Count ?? 0;
    public int StreamingChunkSizeCm => WorldConfig.StreamingChunkSizeCm;
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
    public int LastOrderMemberCount => _lastOrderMemberCount;
    public float HotZoneMinXCm => SolverWindowMinXCm;
    public float HotZoneMinYCm => SolverWindowMinYCm;
    public float HotZoneMaxXCm => SolverWindowMaxXCm;
    public float HotZoneMaxYCm => SolverWindowMaxYCm;
    public string SolverWindowDriver => _solverWindowDriver;
    public int WorldWidthCm => RequireBoardWorldSize().Bounds.Width;
    public int WorldHeightCm => RequireBoardWorldSize().Bounds.Height;
    public WorldAabbCm WorldBounds => RequireBoardWorldSize().Bounds;
    public string ActiveHotZoneId => _activeHotZoneId;
    public string ActiveHotZoneLabel => _activeHotZoneLabel;
    public int ActiveHotZoneCenterXCm => _activeHotZoneCenterXCm;
    public int ActiveHotZoneCenterYCm => _activeHotZoneCenterYCm;
    public int ActiveHotZoneWidthCm => _activeHotZoneWidthCm;
    public int ActiveHotZoneHeightCm => _activeHotZoneHeightCm;
    public ReadOnlySpan<MassNavigationHotZoneConfig> HotZones => WorldConfig.HotZones;

    public MassNavigationSimulationRuntime(MassNavigationConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        int membershipCapacity = config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity;
        if (membershipCapacity <= 0)
        {
            throw new InvalidOperationException(
                "MassNavigationSimulationRuntime requires scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity > 0.");
        }

        AgentState = new MassNavigationAgentState(membershipCapacity);
        _authoredBindingSeenEntities = new HashSet<Entity>(membershipCapacity);
        MassNavigationFlow = new MassNavigationFlowSolverState(config.Solver);
        MassNavigationFlow.PreallocateAgentCapacity(membershipCapacity);
        MassNavigationFlow.PreallocateDomainRelationshipCapacity(config.ScenarioRuntime.RuntimeCapacity.RelationshipDomainCapacity);
        MassNavigationFlow.PreallocateDisplacedAgentCapacity(config.ScenarioRuntime.RuntimeCapacity.DisplacedAgentCapacity);
        WorldConfig = config.World ?? throw new InvalidOperationException("MassNavigationSimulationRuntime requires explicit world config.");
        MassNavigationHotZoneConfig activeHotZone = WorldConfig.GetRequiredHotZone(WorldConfig.ActiveHotZoneId);
        _activeHotZoneId = activeHotZone.Id;
        _activeHotZoneLabel = activeHotZone.Label;
        _activeHotZoneCenterXCm = activeHotZone.CenterXCm;
        _activeHotZoneCenterYCm = activeHotZone.CenterYCm;
        _activeHotZoneWidthCm = activeHotZone.WidthCm;
        _activeHotZoneHeightCm = activeHotZone.HeightCm;
        Cadence = config.Cadence;
        CadenceScheduler = new MassNavigationCadenceScheduler(Cadence);
        _loadedChunkCapacity = config.ScenarioRuntime.RuntimeCapacity.LoadedChunkCapacity;
        _loadedChunkLastTouchedSeconds = new Dictionary<long, float>(_loadedChunkCapacity);
        _loadedChunksToEvict = new List<long>(_loadedChunkCapacity);
        _loadedChunksAddedDuringUpdate = new List<long>(_loadedChunkCapacity);
        _streamingWindowChunkKeys = new List<long>(_loadedChunkCapacity);
        _simWindowWidthCm = WorldConfig.SolverWindowWidthCm;
        _simWindowHeightCm = WorldConfig.SolverWindowHeightCm;
        _simWindowCenterXCm = _activeHotZoneCenterXCm;
        _simWindowCenterYCm = _activeHotZoneCenterYCm;
        _flowWorkAreaCenterXCm = _simWindowCenterXCm;
        _flowWorkAreaCenterYCm = _simWindowCenterYCm;
        _flowWorkAreaWidthCm = _simWindowWidthCm;
        _flowWorkAreaHeightCm = _simWindowHeightCm;
        FlowTuning = config.Flow;
        NavGroupRuntime = new MassNavigationGroupRuntime(config.Semantics.Group, config.ScenarioRuntime.RuntimeCapacity);
        ConfigureScenarioTeams(CreateTeamIdArray(config.Scenario.Teams));
        MassNavigationFlow.ArrivalTuning.CopyFrom(config.Arrival);
        MassNavigationFlow.AvoidanceTuning.CopyFrom(config.Avoidance);
        MassNavigationFlow.Semantics.CopyFrom(config.Semantics);
    }

    public void BindBoardWorld(WorldSizeSpec boardWorldSize, ILoadedChunkWindowSource loadedChunks)
    {
        ArgumentNullException.ThrowIfNull(loadedChunks);

        if (!ReferenceEquals(_loadedChunks, loadedChunks))
        {
            ReleaseLoadedChunkContribution();
            _loadedChunks = loadedChunks;
        }

        ValidateInitialSolverWindow(boardWorldSize);
        _loadedChunkContributor ??= loadedChunks.AcquireContributor(
            $"MassNavigation:{Config.MapId}",
            _loadedChunkCapacity);
        try
        {
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
        catch
        {
            _boardWorldBound = false;
            ReleaseLoadedChunkContribution();
            throw;
        }
    }

    public void ReleaseLoadedChunkContribution()
    {
        _loadedChunkContributor?.Dispose();
        _loadedChunkContributor = null;
        _loadedChunkLastTouchedSeconds.Clear();
        _loadedChunksToEvict.Clear();
        _loadedChunksAddedDuringUpdate.Clear();
        InvalidateStreamingWindowCache();
    }

    public void BeginFrame(float dt)
    {
        _frameIndex++;
        Telemetry.BeginFrame(dt);
        _streamingClockSeconds += MathF.Max(0f, dt);
        AdvanceCommandFocus();
    }

    internal void SetDomainRelationshipProjection(MassNavigationDomainStanceProjection projection)
    {
        _domainStanceProjection = projection ?? throw new ArgumentNullException(nameof(projection));
        MassNavigationFlow.SetDomainRelationshipProjection(projection);
    }

    private void AdvanceCommandFocus()
    {
        if (_commandFocusTicksRemaining <= 0)
        {
            _hasCommandFocus = false;
            return;
        }

        FocusState previous = CaptureFocusState();
        _commandFocusTicksRemaining--;
        if (_commandFocusTicksRemaining == 0)
        {
            try
            {
                _hasCommandFocus = false;
                UpdateStreamingWindow(ResolveStreamingFocus());
            }
            catch
            {
                RestoreFocusState(in previous);
                throw;
            }
        }
    }

    public void ObserveGroupTargetUpdate(double sampleMs) => Telemetry.ObserveGroupTargetUpdate(sampleMs);
    public void ObserveFlowFieldRebuild(double sampleMs) => Telemetry.ObserveFlowFieldRebuild(sampleMs);
    public void ObserveStepPrep(double sampleMs) => Telemetry.ObserveStepPrep(sampleMs);
    public void ObserveLocalSteering(double sampleMs) => Telemetry.ObserveLocalSteering(sampleMs);
    public void ObserveSimStep(double sampleMs) => Telemetry.ObserveSimStep(sampleMs);
    public void ObserveHardResolve(double sampleMs) => Telemetry.ObserveHardResolve(sampleMs);
    public void ObserveEntitySync(double sampleMs) => Telemetry.ObserveEntitySync(sampleMs);
    public void ObservePerformerCommand(double sampleMs) => Telemetry.ObservePerformerCommand(sampleMs);

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
            GroupedAgentFlowSlowRadiusCm: MassNavigationFlow.Semantics.Group.GroupedAgentFlowSlowRadiusCm,
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

    public MassNavigationAvoidanceSnapshot CaptureAvoidanceSnapshot(
        Span<MassNavigationAvoidanceAgentSnapshot> agents,
        Span<MassNavigationObstacleSnapshot> obstacles)
    {
        int unitCount = MassNavigationFlow.UnitCount;
        if (agents.Length < unitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation avoidance snapshot requires {unitCount} agent slots, received {agents.Length}.");
        }

        for (int i = 0; i < unitCount; i++)
        {
            float localX = MassNavigationFlow.GetPositionX(i);
            float localY = MassNavigationFlow.GetPositionY(i);
            agents[i] = new MassNavigationAvoidanceAgentSnapshot(
                AgentIndex: i,
                LocalXCm: localX,
                LocalYCm: localY,
                WorldXCm: ToWorldXCm(localX),
                WorldYCm: ToWorldYCm(localY),
                TeamId: MassNavigationFlow.GetTeam(i),
                BodyRadiusCm: MassNavigationFlow.GetBodyRadiusCm(i),
                HeavyProfile: MassNavigationFlow.IsHeavyProfile(i),
                Settled: MassNavigationFlow.IsUnitSettled(i),
                InsidePlayArea: localX >= MassNavigationFlow.PlayAreaMinXCm &&
                    localX <= MassNavigationFlow.PlayAreaMaxXCm &&
                    localY >= MassNavigationFlow.PlayAreaMinYCm &&
                    localY <= MassNavigationFlow.PlayAreaMaxYCm);
        }

        int obstacleCount = MassNavigationFlow.ObstacleCount;
        if (obstacles.Length < obstacleCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation avoidance snapshot requires {obstacleCount} obstacle slots, received {obstacles.Length}.");
        }

        for (int i = 0; i < obstacleCount; i++)
        {
            obstacles[i] = new MassNavigationObstacleSnapshot(
                MassNavigationFlow.GetObstacleWorldX(i),
                MassNavigationFlow.GetObstacleWorldY(i),
                MassNavigationFlow.GetObstacleRadius(i));
        }

        return new MassNavigationAvoidanceSnapshot(
            UnitCount: unitCount,
            ObstacleCount: obstacleCount,
            SettledUnitCount: MassNavigationFlow.SettledUnitCount,
            PlayAreaMinXCm: MassNavigationFlow.PlayAreaMinXCm,
            PlayAreaMaxXCm: MassNavigationFlow.PlayAreaMaxXCm,
            PlayAreaMinYCm: MassNavigationFlow.PlayAreaMinYCm,
            PlayAreaMaxYCm: MassNavigationFlow.PlayAreaMaxYCm);
    }

    public void ObservePerformerCoverage(int crowdInViewCount, int crowdSubmittedCount, int obstacleSubmittedCount, int performerDroppedCount)
    {
        Telemetry.ObservePerformerCoverage(
            crowdInViewCount,
            crowdSubmittedCount,
            obstacleSubmittedCount,
            performerDroppedCount);
    }

    public void ObserveControlTick() => Telemetry.ObserveControlTick();
    public void ObserveCommandTick() => Telemetry.ObserveCommandTick();
    public void ObserveSimTick() => Telemetry.ObserveSimTick();
    public void ObservePerformerTick() => Telemetry.ObservePerformerTick();
    public void ObservePanelTick() => Telemetry.ObservePanelTick();

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

    internal void BeginRuntimeBindingPreparation()
    {
        _authoredAgentBindingPassComplete = false;
        _environmentBindingPassComplete = false;
    }

    internal void MarkAuthoredAgentBindingPassComplete()
    {
        _authoredAgentBindingPassComplete = true;
    }

    internal void MarkEnvironmentBindingPassComplete()
    {
        _environmentBindingPassComplete = true;
    }

    public void MarkFlowReconcile()
    {
        Telemetry.MarkFlowReconcile();
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
    }

    public void ResetRuntimeState(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
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
        NavGroupRuntime.Reset();
        AgentState.ClearRuntimeBindings(world);
        MassNavigationFlow.ResetAuthoredAgents(ReadOnlySpan<MassNavigationAgentSeed>.Empty);
        _domainStanceProjection?.ResetDomains(ReadOnlySpan<MassNavigationAgentSeed>.Empty);
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

        PreflightAuthoredAgentBindings(
            world,
            entities,
            controllableFlags,
            startIndex: 0,
            unitCountAfterCommit: agentSeeds.Length,
            allowExistingRuntimeBinding: true);

        var previousGroupSnapshot = NavGroupRuntime.CaptureAuthoredRebuildSnapshot();
        _domainStanceProjection?.ValidateResetDomains(agentSeeds);
        ClearAuthoredRuntimeBindings(world);
        _domainStanceProjection?.ResetDomains(agentSeeds);
        MassNavigationFlow.ResetAuthoredAgents(agentSeeds);
        for (int i = 0; i < entities.Length; i++)
        {
            BindSpawnedAgent(world, entities[i], i, controllableFlags[i]);
        }

        NavGroupRuntime.RestoreAuthoredRebuildSnapshot(world, MassNavigationFlow, AgentState, previousGroupSnapshot);
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
        PreflightAuthoredAgentBindings(
            world,
            newEntities,
            controllableFlags,
            startIndex,
            unitCountAfterCommit: checked(startIndex + newAgentSeeds.Length),
            allowExistingRuntimeBinding: false);

        _domainStanceProjection?.ValidateAppendDomains(newAgentSeeds);
        _domainStanceProjection?.AppendDomains(newAgentSeeds);
        MassNavigationFlow.AppendAuthoredAgents(newAgentSeeds);
        for (int i = 0; i < newEntities.Length; i++)
        {
            BindSpawnedAgent(world, newEntities[i], startIndex + i, controllableFlags[i]);
        }

        MarkStructuralChange();
    }

    public void FocusSimulationWindow(System.Numerics.Vector2 worldCenterCm)
    {
        FocusState previous = CaptureFocusState();
        try
        {
            ObserveFlowWorkArea(worldCenterCm, _simWindowWidthCm, _simWindowHeightCm, ReadOnlySpan<Entity>.Empty, "manual focus");
            ValidateStreamingWindowCapacity(ResolveStreamingFocus());
            MoveSolverWindow(worldCenterCm, "manual nav focus");
            UpdateStreamingWindow(ResolveStreamingFocus());
        }
        catch
        {
            RestoreFocusState(in previous);
            throw;
        }
    }

    internal void FocusOrderTarget(System.Numerics.Vector2 worldCenterCm, ReadOnlySpan<Entity> orderMembers)
    {
        FocusState previous = CaptureFocusState();
        try
        {
            _hasCommandFocus = true;
            _lastCommandFocusXCm = worldCenterCm.X;
            _lastCommandFocusYCm = worldCenterCm.Y;
            _lastOrderMemberCount = orderMembers.Length;
            _commandFocusTicksRemaining = WorldConfig.CommandFocusHoldTicks;
            ObserveFlowWorkArea(
                worldCenterCm,
                _simWindowWidthCm,
                _simWindowHeightCm,
                orderMembers,
                orderMembers.Length > 0 ? "order members" : "order");
            ValidateStreamingWindowCapacity(ResolveStreamingFocus());
            MoveSolverWindow(ResolveSolverFocusForWorkArea(), orderMembers.Length > 0 ? "order members" : "order");
            UpdateStreamingWindow(ResolveStreamingFocus());
        }
        catch
        {
            RestoreFocusState(in previous);
            throw;
        }
    }

    internal void PreflightOrderTarget(System.Numerics.Vector2 worldCenterCm, ReadOnlySpan<Entity> orderMembers)
    {
        FocusState previous = CaptureFocusState();
        try
        {
            _hasCommandFocus = true;
            _lastCommandFocusXCm = worldCenterCm.X;
            _lastCommandFocusYCm = worldCenterCm.Y;
            _lastOrderMemberCount = orderMembers.Length;
            _commandFocusTicksRemaining = WorldConfig.CommandFocusHoldTicks;
            ObserveFlowWorkArea(
                worldCenterCm,
                _simWindowWidthCm,
                _simWindowHeightCm,
                orderMembers,
                orderMembers.Length > 0 ? "order members" : "order");
            ValidateStreamingWindowCapacity(ResolveStreamingFocus());
        }
        finally
        {
            RestoreFocusState(in previous);
        }
    }

    public void ObserveRuntimeFocus(System.Numerics.Vector2 focusCenterCm, float focusWidthCm, float focusHeightCm)
    {
        FocusState previous = CaptureFocusState();
        try
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
        catch
        {
            RestoreFocusState(in previous);
            throw;
        }
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

    public MassNavigationRouteSemantics GetRuntimeRouteSemantics()
    {
        return MassNavigationFlow.Semantics.Route;
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

    public void SyncAgentEntitiesNow(World world)
    {
        MassNavigationFlow.SyncEntities(world, AgentState);
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

    public Vector2 ResolveAgentNavigableTargetWorldCm(
        int agentIndex,
        Vector2 targetWorldCm,
        Vector2 projectionHintWorldCm,
        float minimumClearanceCm)
    {
        RequireAgentIndex(agentIndex);
        if (!float.IsFinite(targetWorldCm.X) ||
            !float.IsFinite(targetWorldCm.Y) ||
            !float.IsFinite(projectionHintWorldCm.X) ||
            !float.IsFinite(projectionHintWorldCm.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetWorldCm),
                "MassNavigation navigable-target projection requires finite target and hint coordinates.");
        }

        if (!float.IsFinite(minimumClearanceCm) || minimumClearanceCm < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumClearanceCm),
                minimumClearanceCm,
                "MassNavigation navigable-target projection requires finite minimumClearanceCm >= 0.");
        }

        Vector2 targetLocalCm = ToLocalCm(targetWorldCm);
        Vector2 resolvedLocalCm = MassNavigationFlow.ResolveUnitNavigableTarget(
            agentIndex,
            targetLocalCm.X,
            targetLocalCm.Y,
            projectionHintWorldCm.X,
            projectionHintWorldCm.Y,
            minimumClearanceCm);
        return ToWorldCm(resolvedLocalCm);
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

    public void BindSpawnedAgent(
        World world,
        Entity entity,
        int agentIndex,
        bool controllable)
    {
        ArgumentNullException.ThrowIfNull(world);
        MassNavigationAgent agent = ValidateSpawnedAgentBinding(
            world,
            entity,
            agentIndex,
            controllable,
            MassNavigationFlow.UnitCount,
            allowExistingRuntimeBinding: false);
        int profileId = agent.ProfileId;
        world.Add(entity, new MassNavigationAgentIndex { Value = agentIndex });
        world.Add(entity, new MassNavigationAgentProfile
        {
            ProfileId = profileId,
            Heavy = MassNavigationFlow.IsHeavyProfile(agentIndex),
            VisualScale = MassNavigationFlow.GetVisualScale(agentIndex),
            SpeedCmPerSecond = MassNavigationFlow.GetSpeedCmPerSecond(agentIndex),
        });
        if (world.Has<MovePlanExecutionIntent>(entity))
        {
            world.Set(entity, default(MovePlanExecutionIntent));
        }
        else
        {
            world.Add(entity, default(MovePlanExecutionIntent));
        }

        if (world.Has<MovePlanExecutionResult>(entity))
        {
            world.Set(entity, default(MovePlanExecutionResult));
        }
        else
        {
            world.Add(entity, default(MovePlanExecutionResult));
        }
        AgentState.RegisterAgentAtIndex(entity, agentIndex, controllable);
    }

    private void PreflightAuthoredAgentBindings(
        World world,
        ReadOnlySpan<Entity> entities,
        ReadOnlySpan<bool> controllableFlags,
        int startIndex,
        int unitCountAfterCommit,
        bool allowExistingRuntimeBinding)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (entities.Length != controllableFlags.Length)
        {
            throw new InvalidOperationException("MassNavigation authored binding preflight requires matching entity and controllable spans.");
        }

        _authoredBindingSeenEntities.Clear();
        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!_authoredBindingSeenEntities.Add(entity))
                {
                    throw new InvalidOperationException($"MassNavigation authored binding contains duplicate entity {entity.Id}.");
                }

                ValidateSpawnedAgentBinding(
                    world,
                    entity,
                    checked(startIndex + i),
                    controllableFlags[i],
                    unitCountAfterCommit,
                    allowExistingRuntimeBinding);
            }
        }
        finally
        {
            _authoredBindingSeenEntities.Clear();
        }
    }

    private MassNavigationAgent ValidateSpawnedAgentBinding(
        World world,
        Entity entity,
        int agentIndex,
        bool controllable,
        int unitCountAfterCommit,
        bool allowExistingRuntimeBinding)
    {
        if (!IsAliveInWorld(world, entity))
        {
            throw new InvalidOperationException("MassNavigation cannot bind a spawned agent on a dead entity.");
        }

        if ((uint)agentIndex >= (uint)unitCountAfterCommit)
        {
            throw new InvalidOperationException(
                $"MassNavigation spawned agent index {agentIndex} exceeds current agent count {unitCountAfterCommit}.");
        }

        bool hasRuntimeBinding =
            world.Has<MassNavigationAgentIndex>(entity) ||
            world.Has<MassNavigationAgentProfile>(entity);
        if (hasRuntimeBinding &&
            (!allowExistingRuntimeBinding || !IsCommittedRuntimeBindingEntity(entity)))
        {
            throw new InvalidOperationException($"MassNavigation entity {entity.Id} was already bound as an agent.");
        }

        if (!world.TryGet(entity, out MassNavigationAgent agent))
        {
            throw new InvalidOperationException(
                $"MassNavigation spawned agent entity {entity.Id} requires MassNavigationAgent before binding.");
        }

        if (agent.ProfileId <= MassNavigationProfileRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"MassNavigation spawned agent entity {entity.Id} requires a resolved positive profileId.");
        }

        // Participation contract (issue #643): a Dynamic physics presence derives Physics pose
        // authority, which cannot coexist with a nav-agent binding in this increment.
        if (world.TryGet(entity, out Ludots.Core.Components.MovementParticipation participation) &&
            participation.PhysicsPresence == Ludots.Core.Components.PhysicsPresenceKind.Dynamic)
        {
            throw new InvalidOperationException(
                $"MassNavigation cannot bind entity {entity.Id} as a nav agent: MovementParticipation.physicsPresence 'dynamic' assigns pose authority to Physics.");
        }

        if (!allowExistingRuntimeBinding)
        {
            AgentState.ValidateAgentRegistration(agentIndex, controllable);
        }

        return agent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAliveInWorld(World world, Entity entity)
    {
        return entity != Entity.Null && entity.WorldId == world.Id && world.IsAlive(entity);
    }

    private bool IsCommittedRuntimeBindingEntity(Entity entity)
    {
        IReadOnlyList<Entity> agents = AgentState.AllAgents;
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i].Equals(entity))
            {
                return true;
            }
        }

        return false;
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
        ILoadedChunkWindowSource loadedChunks = RequireLoadedChunks();
        _streamingWindowChunkKeys.Clear();
        loadedChunks.CollectWindowChunkKeys(centerX, centerY, radius, _streamingWindowChunkKeys);
        bool changed = radius != _streamingRadiusCm;
        ValidateStreamingWindowCapacity(_streamingWindowChunkKeys);
        _loadedChunksAddedDuringUpdate.Clear();
        try
        {
            for (int i = 0; i < _streamingWindowChunkKeys.Count; i++)
            {
                long chunkKey = _streamingWindowChunkKeys[i];
                bool wasTracked = _loadedChunkLastTouchedSeconds.ContainsKey(chunkKey);
                TouchStreamingChunk(chunkKey);
                if (!wasTracked)
                {
                    changed = true;
                    _loadedChunksAddedDuringUpdate.Add(chunkKey);
                }
            }

            if (EvictExpiredStreamingChunks(_streamingWindowChunkKeys))
            {
                changed = true;
            }

            _streamingRadiusCm = radius;
        }
        catch
        {
            for (int i = _loadedChunksAddedDuringUpdate.Count - 1; i >= 0; i--)
            {
                long chunkKey = _loadedChunksAddedDuringUpdate[i];
                if (_loadedChunkLastTouchedSeconds.Remove(chunkKey))
                {
                    RequireLoadedChunkContributor().SetLoaded(chunkKey, false);
                }
            }

            InvalidateStreamingWindowCache();
            throw;
        }

        if (changed)
        {
            Telemetry.MarkStreamingWindowUpdated();
        }
    }

    private void ValidateStreamingWindowCapacity(System.Numerics.Vector2 worldCenterCm)
    {
        int centerX = (int)MathF.Round(worldCenterCm.X);
        int centerY = (int)MathF.Round(worldCenterCm.Y);
        _streamingWindowChunkKeys.Clear();
        RequireLoadedChunks().CollectWindowChunkKeys(centerX, centerY, Streaming.RadiusCm, _streamingWindowChunkKeys);
        ValidateStreamingWindowCapacity(_streamingWindowChunkKeys);
    }

    private void ValidateStreamingWindowCapacity(List<long> currentWindowKeys)
    {
        int requiredCount = 0;
        foreach (KeyValuePair<long, float> pair in _loadedChunkLastTouchedSeconds)
        {
            bool inNextWindow = currentWindowKeys.Contains(pair.Key);
            float elapsedSeconds = _streamingClockSeconds - pair.Value;
            bool expired = !inNextWindow &&
                ((Streaming.RetainSeconds == 0f && elapsedSeconds >= 0f) || elapsedSeconds > Streaming.RetainSeconds);
            if (!expired)
            {
                requiredCount++;
            }
        }

        for (int i = 0; i < currentWindowKeys.Count; i++)
        {
            if (!_loadedChunkLastTouchedSeconds.ContainsKey(currentWindowKeys[i]))
            {
                requiredCount++;
            }
        }

        if (requiredCount > _loadedChunkCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation streaming transition requires {requiredCount} retained chunks, exceeding configured loadedChunkCapacity {_loadedChunkCapacity}.");
        }
    }

    private bool EvictExpiredStreamingChunks(List<long> currentWindowKeys)
    {
        float retainSeconds = Streaming.RetainSeconds;
        if (retainSeconds < 0f)
        {
            return false;
        }

        _loadedChunksToEvict.Clear();
        foreach (KeyValuePair<long, float> pair in _loadedChunkLastTouchedSeconds)
        {
            bool inCurrentWindow = currentWindowKeys.Contains(pair.Key);
            if (!inCurrentWindow && _streamingClockSeconds - pair.Value > retainSeconds)
            {
                _loadedChunksToEvict.Add(pair.Key);
            }
        }

        for (int i = 0; i < _loadedChunksToEvict.Count; i++)
        {
            long chunkKey = _loadedChunksToEvict[i];
            _loadedChunkLastTouchedSeconds.Remove(chunkKey);
            RequireLoadedChunkContributor().SetLoaded(chunkKey, false);
        }

        return _loadedChunksToEvict.Count > 0;
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
            RequireLoadedChunkContributor().SetLoaded(chunkKey, true);
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
            _activeHotZoneId,
            "x");
        EnsurePointInsideWindowCenterBounds(
            _simWindowCenterYCm,
            bounds.Top,
            bounds.Bottom,
            _simWindowHeightCm,
            _activeHotZoneId,
            "y");
    }

    private void ObserveFlowWorkArea(
        System.Numerics.Vector2 focusCm,
        float focusWidthCm,
        float focusHeightCm,
        ReadOnlySpan<Entity> orderMembers,
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

        if (orderMembers.Length > 0)
        {
            IncludeOrderMemberBounds(ref minX, ref maxX, ref minY, ref maxY, orderMembers);
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

    private void IncludeOrderMemberBounds(ref float minX, ref float maxX, ref float minY, ref float maxY, ReadOnlySpan<Entity> orderMembers)
    {
        for (int i = 0; i < orderMembers.Length; i++)
        {
            if (!AgentState.TryGetControllableIndex(orderMembers[i], out int unitIndex) ||
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

    private FocusState CaptureFocusState()
    {
        return new FocusState
        {
            SolverCenterX = _simWindowCenterXCm,
            SolverCenterY = _simWindowCenterYCm,
            CommandTicksRemaining = _commandFocusTicksRemaining,
            HasCommandFocus = _hasCommandFocus,
            CommandFocusX = _lastCommandFocusXCm,
            CommandFocusY = _lastCommandFocusYCm,
            LastOrderMemberCount = _lastOrderMemberCount,
            WorkAreaCenterX = _flowWorkAreaCenterXCm,
            WorkAreaCenterY = _flowWorkAreaCenterYCm,
            WorkAreaWidth = _flowWorkAreaWidthCm,
            WorkAreaHeight = _flowWorkAreaHeightCm,
            WorkAreaRevision = _flowWorkAreaRevision,
            WorkAreaReason = _flowWorkAreaReason,
            SolverDriver = _solverWindowDriver,
        };
    }

    private void RestoreFocusState(in FocusState state)
    {
        _simWindowCenterXCm = state.SolverCenterX;
        _simWindowCenterYCm = state.SolverCenterY;
        _commandFocusTicksRemaining = state.CommandTicksRemaining;
        _hasCommandFocus = state.HasCommandFocus;
        _lastCommandFocusXCm = state.CommandFocusX;
        _lastCommandFocusYCm = state.CommandFocusY;
        _lastOrderMemberCount = state.LastOrderMemberCount;
        _flowWorkAreaCenterXCm = state.WorkAreaCenterX;
        _flowWorkAreaCenterYCm = state.WorkAreaCenterY;
        _flowWorkAreaWidthCm = state.WorkAreaWidth;
        _flowWorkAreaHeightCm = state.WorkAreaHeight;
        _flowWorkAreaRevision = state.WorkAreaRevision;
        _flowWorkAreaReason = state.WorkAreaReason;
        _solverWindowDriver = state.SolverDriver;
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

    private ILoadedChunkWindowSource RequireLoadedChunks()
    {
        return _loadedChunks
            ?? throw new InvalidOperationException("MassNavigation requires board-owned loaded chunk window source before streaming operations.");
    }

    private ILoadedChunkContributor RequireLoadedChunkContributor()
    {
        return _loadedChunkContributor
            ?? throw new InvalidOperationException("MassNavigation requires an active board loaded-chunk contribution before streaming operations.");
    }

    private void RequireAgentIndex(int agentIndex)
    {
        if ((uint)agentIndex >= (uint)MassNavigationFlow.UnitCount)
        {
            throw new InvalidOperationException(
                $"MassNavigation agent index {agentIndex} exceeds current agent count {MassNavigationFlow.UnitCount}.");
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
        _streamingRadiusCm = int.MinValue;
        _streamingWindowChunkKeys.Clear();
    }
}
