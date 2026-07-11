using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Map;
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
    bool CommandActor,
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
    bool CrowdCostEnabled,
    int CrowdStampBudgetAgentsPerRefresh,
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
    EmptyCommandActors = 3,
    UnauthorizedCommandActors = 4,
    OrderSubmitRejected = 5,
}

public sealed class MassNavigationSimulationRuntime
{
    public const string AgentLocomotionSpeedParamKey = "mass_navigation.agent.locomotion.speed";

    private int[] _teamIds = Array.Empty<int>();
    private Entity[] _commandActorScratch = Array.Empty<Entity>();
    private Entity[] _commandActors = Array.Empty<Entity>();
    private int _commandActorCount;
    private uint _commandActorSnapshotRevision;
    private bool _sceneResetRequested;
    private int _frameIndex;
    private int _nextSharedOrderId = 1;
    private readonly MassNavigationStreamingWindow _streamingWindow;
    private readonly MassNavigationSolverWindowCoordinator _solverWindow;

    public MassNavigationTelemetry Telemetry { get; } = new();
    public int CommandActorSnapshotCountFrame => Telemetry.CommandActorSnapshotCountFrame;
    public int CommandCountFrame => Telemetry.CommandCountFrame;
    public int StructuralChangesFrame => Telemetry.StructuralChangesFrame;
    public int StructuralChangeRevision => Telemetry.StructuralChangeRevision;
    public int FlowReconcileCountFrame => Telemetry.FlowReconcileCountFrame;
    public int FocusBudgetUpdatesFrame => Telemetry.FocusBudgetUpdatesFrame;
    public int SolverWindowMovesFrame => Telemetry.SolverWindowMovesFrame;
    public float FrameMs => Telemetry.FrameMs;
    public float Fps => Telemetry.Fps;
    public float CommandActorSyncMs => Telemetry.CommandActorSyncMs;
    public float FormationTargetMs => Telemetry.FormationTargetMs;
    public float FlowFieldRebuildMs => Telemetry.FlowFieldRebuildMs;
    public float StepPrepMs => Telemetry.StepPrepMs;
    public float LocalSteeringMs => Telemetry.LocalSteeringMs;
    public float SimStepMs => Telemetry.SimStepMs;
    public float HardResolveMs => Telemetry.HardResolveMs;
    public float EntitySyncMs => Telemetry.EntitySyncMs;
    public float PerformerCommandMs => Telemetry.PerformerCommandMs;
    public float CommandActorSyncHzObserved => Telemetry.CommandActorSyncHzObserved;
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
    public MapId MapId { get; }
    public MassNavigationRuntimePlan Plan { get; }
    public string RuntimeConfigSha256 { get; }
    public string? CapabilityProfileSha256 { get; private set; }
    public int? ScenarioRandomSeed { get; private set; }
    public MassNavigationAgentState AgentState { get; } = new();
    public MassNavigationFlowPlan FlowConfig { get; }
    public MassNavigationCadencePlan Cadence { get; }
    internal MassNavigationCadenceScheduler CadenceScheduler { get; }
    public MassNavigationFormationRuntime FormationRuntime { get; }
    public MassNavigationGroupRuntime NavGroupRuntime { get; }
    internal MassNavigationFlowSolverState MassNavigationFlow { get; }
    public WorldGridLoadedChunks LoadedChunks => _streamingWindow.LoadedChunks;
    public MassNavigationStreamingPlan Streaming { get; }
    public bool IsReadyForWorldOperations { get; private set; }

    public int NavigationAgentCount => MassNavigationFlow.UnitCount;
    public int NavigationObstacleCount => MassNavigationFlow.ObstacleCount;
    public int NavigationSettledAgentCount => MassNavigationFlow.SettledUnitCount;
    public int PreparedAgentCapacity => MassNavigationFlow.PreparedAgentCapacity;
    public int AgentStorageAllocationCount => MassNavigationFlow.AgentStorageAllocationCount;
    public int FlowStateCount => MassNavigationFlow.FlowStateCount;
    public int FlowStateCapacity => MassNavigationFlow.FlowStateCapacity;
    public int FlowStateStorageAllocationCount => MassNavigationFlow.FlowStateStorageAllocationCount;
    public int PeakAgentCount => MassNavigationFlow.PeakUnitCount;
    public int PeakCommandActorScratchCount { get; private set; }
    public int PeakCommandActorSnapshotCount { get; private set; }
    public int PeakOrderIngestionTokenCount { get; private set; }
    public int PeakOrderIngestionMemberCount { get; private set; }
    public int PeakTeamCount { get; private set; }
    public int PeakLoadedChunkCount => _streamingWindow.PeakLoadedChunkCount;
    public int PeakFlowStateCount => MassNavigationFlow.PeakFlowStateCount;
    public int CommandActorCount => _commandActorCount;
    public uint CommandActorSnapshotRevision => _commandActorSnapshotRevision;
    public ReadOnlySpan<Entity> CommandActors => _commandActors.AsSpan(0, _commandActorCount);
    public ReadOnlySpan<int> TeamIds => _teamIds;
    public int TeamCount => _teamIds.Length;
    public int FrameIndex => _frameIndex;
    public int AgentsPerTeam { get; private set; }
    public int ActiveTeamId { get; private set; }
    public MassNavigationFormationMode FormationMode { get; private set; } = MassNavigationFormationMode.None;
    public int LoadedChunkCount => _streamingWindow.LoadedChunkCount;
    public int StreamingChunkSizeCm => _streamingWindow.ChunkSizeCm ?? Plan.World.StreamingChunkSizeCm;
    public float SolverWindowCenterXCm => _solverWindow.CenterXCm;
    public float SolverWindowCenterYCm => _solverWindow.CenterYCm;
    public float SolverWindowWidthCm => _solverWindow.WidthCm;
    public float SolverWindowHeightCm => _solverWindow.HeightCm;
    public float SolverWindowMinXCm => _solverWindow.MinXCm;
    public float SolverWindowMinYCm => _solverWindow.MinYCm;
    public float SolverWindowMaxXCm => _solverWindow.MaxXCm;
    public float SolverWindowMaxYCm => _solverWindow.MaxYCm;
    public float FlowWorkAreaCenterXCm => _solverWindow.WorkAreaCenterXCm;
    public float FlowWorkAreaCenterYCm => _solverWindow.WorkAreaCenterYCm;
    public float FlowWorkAreaWidthCm => _solverWindow.WorkAreaWidthCm;
    public float FlowWorkAreaHeightCm => _solverWindow.WorkAreaHeightCm;
    public float FlowWorkAreaMinXCm => _solverWindow.WorkAreaMinXCm;
    public float FlowWorkAreaMinYCm => _solverWindow.WorkAreaMinYCm;
    public float FlowWorkAreaMaxXCm => _solverWindow.WorkAreaMaxXCm;
    public float FlowWorkAreaMaxYCm => _solverWindow.WorkAreaMaxYCm;
    public int FlowWorkAreaRevision => _solverWindow.WorkAreaRevision;
    public string FlowWorkAreaReason => _solverWindow.WorkAreaReason;
    public int CommandFocusTicksRemaining => _solverWindow.CommandFocusTicksRemaining;
    public bool HasCommandFocus => _solverWindow.HasCommandFocus;
    public float CommandFocusXCm => _solverWindow.CommandFocusXCm;
    public float CommandFocusYCm => _solverWindow.CommandFocusYCm;
    public int LastCommandActorCount => _solverWindow.LastCommandActorCount;
    public string SolverWindowDriver => _solverWindow.Driver;
    public int WorldWidthCm => _solverWindow.BoardWorldSize.Bounds.Width;
    public int WorldHeightCm => _solverWindow.BoardWorldSize.Bounds.Height;
    public WorldAabbCm WorldBounds => _solverWindow.BoardWorldSize.Bounds;
    public string ActiveHotZoneId => Plan.World.InitialHotZone.Id;
    public string ActiveHotZoneLabel => Plan.World.InitialHotZone.Label;
    public ReadOnlySpan<MassNavigationHotZonePlan> HotZones => Plan.World.HotZones;

    public MassNavigationSimulationRuntime(MapId mapId, MassNavigationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(mapId.Value))
        {
            throw new ArgumentException("MassNavigation simulation requires a non-empty active map id.", nameof(mapId));
        }

        MapId = mapId;
        RuntimeConfigSha256 = ComputeRuntimeConfigSha256(config);
        Plan = MassNavigationRuntimePlan.Compile(config);
        MassNavigationFlow = new MassNavigationFlowSolverState(config.Solver, config.Capacity.FlowStateCapacity);
        Cadence = Plan.Cadence;
        CadenceScheduler = new MassNavigationCadenceScheduler(Cadence);
        _commandActorScratch = new Entity[Plan.Capacity.InitialCommandActorScratchCapacity];
        _commandActors = new Entity[Plan.Capacity.InitialCommandActorSnapshotCapacity];
        FlowConfig = Plan.Flow;
        Streaming = Plan.Streaming;
        _streamingWindow = new MassNavigationStreamingWindow(
            $"mass-navigation:{MapId.Value}",
            Plan.Capacity.LoadedChunkCapacity,
            Streaming);
        _solverWindow = new MassNavigationSolverWindowCoordinator(
            Plan.World.InitialHotZone,
            Plan.World,
            config.Solver.FieldWidthCm,
            config.Solver.FieldHeightCm);
        FormationRuntime = new MassNavigationFormationRuntime(config.Semantics.Group);
        NavGroupRuntime = new MassNavigationGroupRuntime(FormationRuntime, config.Capacity);
        AgentsPerTeam = 0;
        ActiveTeamId = 0;
        MassNavigationFlow.ArrivalTuning.CopyFrom(config.Arrival);
        MassNavigationFlow.AvoidanceTuning.CopyFrom(config.Avoidance);
        MassNavigationFlow.Semantics.CopyFrom(config.Semantics);
        MassNavigationFlow.PreallocateAgentCapacity(Plan.Capacity.GroupMembershipAgentCapacity);
    }

    internal void SetScenarioRandomSeed(int randomSeed)
    {
        ScenarioRandomSeed = randomSeed;
    }

    internal void BindCapabilityProfileProvenance(MassNavigationCapabilityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string json = JsonSerializer.Serialize(profile, Ludots.Core.Config.StrictJsonOptions.CreateCamelCase());
        CapabilityProfileSha256 = ComputeSha256(json);
    }

    private static string ComputeRuntimeConfigSha256(MassNavigationConfig config)
    {
        string json = JsonSerializer.Serialize(config, Ludots.Core.Config.StrictJsonOptions.CreateCamelCase());
        return ComputeSha256(json);
    }

    private static string ComputeSha256(string json)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public void BindBoardWorld(WorldSizeSpec boardWorldSize, WorldGridLoadedChunks loadedChunks)
    {
        ArgumentNullException.ThrowIfNull(loadedChunks);
        if (loadedChunks.ChunkSizeCm != Plan.World.StreamingChunkSizeCm)
        {
            throw new InvalidOperationException(
                $"MassNavigation streaming chunk size {Plan.World.StreamingChunkSizeCm} cm must match board loaded-chunk size {loadedChunks.ChunkSizeCm} cm.");
        }

        _solverWindow.BindBoardWorld(boardWorldSize, Plan.World.InitialHotZone.Id);
        _streamingWindow.Bind(loadedChunks);
        MassNavigationFlow.SetWorldBounds(
            boardWorldSize.Bounds.Left,
            boardWorldSize.Bounds.Right,
            boardWorldSize.Bounds.Top,
            boardWorldSize.Bounds.Bottom);
        MassNavigationFlow.RebaseWorldOrigin(SolverWindowMinXCm, SolverWindowMinYCm);
        UpdateStreamingWindow(ToWorldCm(new System.Numerics.Vector2(
            MassNavigationFlow.FieldWidthCm * 0.5f,
            MassNavigationFlow.FieldHeightCm * 0.5f)));
    }

    public void ReleaseStreamingWindow()
    {
        _streamingWindow.Release();
    }

    public void SetWorldOperationsReady(bool ready)
    {
        IsReadyForWorldOperations = ready;
    }

    public void BeginFrame(float dt)
    {
        _frameIndex++;
        Telemetry.BeginFrame(dt);
        _streamingWindow.AdvanceClock(dt);
        MassNavigationSolverWindowTransition transition = _solverWindow.PlanAdvanceCommandFocus(
            out bool streamingUpdateRequired);
        if (!streamingUpdateRequired)
        {
            _solverWindow.Commit(transition);
            return;
        }

        MassNavigationStreamingWindowUpdate streamingUpdate = _streamingWindow.PrepareUpdate(
            transition.StreamingFocus);
        _solverWindow.Commit(transition);
        if (_streamingWindow.ApplyUpdate(streamingUpdate))
        {
            Telemetry.MarkStreamingWindowUpdated();
        }
    }

    public MassNavigationSolverDiagnostics CaptureSolverDiagnostics()
    {
        return new MassNavigationSolverDiagnostics(
            CrowdCostEnabled: FlowConfig.CrowdCostEnabled,
            CrowdStampBudgetAgentsPerRefresh: FlowConfig.CrowdStampBudgetAgentsPerRefresh,
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
                CommandActor: MassNavigationFlow.IsCommandActor(i),
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

    public Span<Entity> EnsureCommandActorScratch(int required)
    {
        if (required > _commandActorScratch.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation command actor scratch required {required} entities, exceeding runtime.capacity.initialCommandActorScratchCapacity {_commandActorScratch.Length}.");
        }

        PeakCommandActorScratchCount = Math.Max(PeakCommandActorScratchCount, required);
        return _commandActorScratch.AsSpan(0, required);
    }

    public void SetCommandActorSnapshot(ReadOnlySpan<Entity> entities, uint revision)
    {
        if (entities.Length > _commandActors.Length)
        {
            throw new InvalidOperationException(
                $"MassNavigation command actor snapshot required {entities.Length} entities, exceeding runtime.capacity.initialCommandActorSnapshotCapacity {_commandActors.Length}.");
        }

        entities.CopyTo(_commandActors.AsSpan(0, entities.Length));
        _commandActorCount = entities.Length;
        PeakCommandActorSnapshotCount = Math.Max(PeakCommandActorSnapshotCount, _commandActorCount);
        _commandActorSnapshotRevision = revision;
        Telemetry.MarkCommandActorSnapshot();
        MassNavigationFlow.SetCommandActorFlags(AgentState, _commandActors.AsSpan(0, _commandActorCount));
    }

    public void ClearCommandActorSnapshot()
    {
        if (_commandActorCount == 0)
        {
            MassNavigationFlow.SetCommandActorFlags(AgentState, ReadOnlySpan<Entity>.Empty);
            return;
        }

        _commandActorCount = 0;
        _commandActorSnapshotRevision++;
        Telemetry.MarkCommandActorSnapshot();
        MassNavigationFlow.SetCommandActorFlags(AgentState, ReadOnlySpan<Entity>.Empty);
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

    public void RejectCommandWithoutCommandActors(float worldXCm, float worldYCm)
    {
        Telemetry.MarkCommandRejected(worldXCm, worldYCm);
    }

    public void RejectCommandUnauthorizedCommandActors(float worldXCm, float worldYCm)
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

    public void SetActiveTeam(int teamId)
    {
        if (Array.IndexOf(_teamIds, teamId) < 0)
        {
            throw new InvalidOperationException($"MassNavigationSimulationRuntime active team {teamId} is not configured.");
        }

        ActiveTeamId = teamId;
    }

    public void ConfigureTeams(ReadOnlySpan<int> teamIds)
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
        PeakTeamCount = Math.Max(PeakTeamCount, _teamIds.Length);
        if (Array.IndexOf(_teamIds, ActiveTeamId) < 0)
        {
            ActiveTeamId = _teamIds[0];
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

    public void CycleActiveTeam()
    {
        if (_teamIds.Length <= 0)
        {
            return;
        }

        int index = Array.IndexOf(_teamIds, ActiveTeamId);
        if (index < 0)
        {
            ActiveTeamId = _teamIds[0];
            return;
        }

        ActiveTeamId = _teamIds[(index + 1) % _teamIds.Length];
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
        ClearCommandActorSnapshot();
        NavGroupRuntime.Reset();
        AgentState.DestroyTracked(world);
        Telemetry.MarkAuthoredRuntimeBindingChanged();
    }

    public void ResetRuntimeState(World world, ReadOnlySpan<MassNavigationAgentSeed> agentSeeds)
    {
        ResetRuntimeState(world);
        MassNavigationFlow.ResetAuthoredAgents(agentSeeds);
    }

    public void ClearAuthoredRuntimeBindings(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ClearCommandActorSnapshot();
        NavGroupRuntime.Reset();
        AgentState.ClearRuntimeBindings(world);
        MassNavigationFlow.ResetAuthoredAgents(ReadOnlySpan<MassNavigationAgentSeed>.Empty);
        Telemetry.MarkAuthoredRuntimeBindingChanged();
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

        int membershipCapacity = Plan.Capacity.GroupMembershipAgentCapacity;
        if (agentSeeds.Length > membershipCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored rebuild required {agentSeeds.Length} agent slots, exceeding runtime.capacity.groupMembershipAgentCapacity {membershipCapacity}.");
        }

        int previousCommandActorCount = _commandActorCount;
        uint previousCommandActorSnapshotRevision = _commandActorSnapshotRevision;
        Span<Entity> previousCommandActors = previousCommandActorCount > 0
            ? EnsureCommandActorScratch(previousCommandActorCount)
            : Span<Entity>.Empty;
        if (previousCommandActorCount > 0)
        {
            _commandActors.AsSpan(0, previousCommandActorCount).CopyTo(previousCommandActors);
        }

        var previousGroupSnapshot = NavGroupRuntime.CaptureAuthoredRebuildSnapshot();
        ClearAuthoredRuntimeBindings(world);
        MassNavigationFlow.ResetAuthoredAgents(agentSeeds);
        for (int i = 0; i < entities.Length; i++)
        {
            BindSpawnedAgent(world, entities[i], i, controllableFlags[i]);
        }

        NavGroupRuntime.RestoreAuthoredRebuildSnapshot(world, MassNavigationFlow, AgentState, previousGroupSnapshot);
        RestoreCommandActorSnapshotAfterAuthoredRebuild(world, previousCommandActors, previousCommandActorSnapshotRevision);
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
        int membershipCapacity = Plan.Capacity.GroupMembershipAgentCapacity;
        if (newTotal > membershipCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation authored append required {newTotal} agent slots, exceeding runtime.capacity.groupMembershipAgentCapacity {membershipCapacity}.");
        }

        int startIndex = MassNavigationFlow.UnitCount;
        MassNavigationFlow.AppendAuthoredAgents(newAgentSeeds);
        for (int i = 0; i < newEntities.Length; i++)
        {
            BindSpawnedAgent(world, newEntities[i], startIndex + i, controllableFlags[i]);
        }

        MarkStructuralChange();
    }

    internal void ObserveOrderIngestionOccupancy(int tokenCount, int memberCount)
    {
        PeakOrderIngestionTokenCount = Math.Max(PeakOrderIngestionTokenCount, tokenCount);
        PeakOrderIngestionMemberCount = Math.Max(PeakOrderIngestionMemberCount, memberCount);
    }

    private void RestoreCommandActorSnapshotAfterAuthoredRebuild(
        World world,
        ReadOnlySpan<Entity> previousCommandActors,
        uint previousCommandActorSnapshotRevision)
    {
        if (previousCommandActors.Length <= 0)
        {
            return;
        }

        int restoredCount = 0;
        Span<Entity> restored = EnsureCommandActorScratch(previousCommandActors.Length);
        for (int i = 0; i < previousCommandActors.Length; i++)
        {
            Entity entity = previousCommandActors[i];
            if (world.IsAlive(entity) && AgentState.TryGetControllableIndex(entity, out _))
            {
                restored[restoredCount++] = entity;
            }
        }

        if (restoredCount <= 0)
        {
            return;
        }

        SetCommandActorSnapshot(restored[..restoredCount], previousCommandActorSnapshotRevision);
    }

    public void FocusSimulationWindow(System.Numerics.Vector2 worldCenterCm)
    {
        MassNavigationSolverWindowTransition transition = _solverWindow.PlanManualFocus(
            worldCenterCm,
            SolverWindowWidthCm,
            SolverWindowHeightCm,
            ReadOnlySpan<Entity>.Empty,
            AgentState,
            MassNavigationFlow,
            "manual focus",
            "manual nav focus");
        MassNavigationStreamingWindowUpdate streamingUpdate = _streamingWindow.PrepareUpdate(
            transition.StreamingFocus);
        CommitFocusTransition(transition, streamingUpdate, markFocusBudgetUpdated: false);
    }

    public void FocusCommandTarget(System.Numerics.Vector2 worldCenterCm, ReadOnlySpan<Entity> commandActors)
    {
        string reason = commandActors.Length > 0 ? "actor command" : "team command";
        MassNavigationSolverWindowTransition transition = _solverWindow.PlanCommandFocus(
            worldCenterCm,
            commandActors.Length,
            SolverWindowWidthCm,
            SolverWindowHeightCm,
            commandActors,
            AgentState,
            MassNavigationFlow,
            reason,
            reason);
        MassNavigationStreamingWindowUpdate streamingUpdate = _streamingWindow.PrepareUpdate(
            transition.StreamingFocus);
        CommitFocusTransition(transition, streamingUpdate, markFocusBudgetUpdated: false);
    }

    public void FocusCommandTargetForEntities(System.Numerics.Vector2 worldCenterCm, Entity[] commandActors)
    {
        FocusCommandTarget(worldCenterCm, commandActors.AsSpan());
    }

    public void ObserveRuntimeFocus(System.Numerics.Vector2 focusCenterCm, float focusWidthCm, float focusHeightCm)
    {
        string reason = _solverWindow.HasCommandFocus ? "runtime focus + command hold" : "runtime focus";
        MassNavigationSolverWindowTransition transition = _solverWindow.PlanRuntimeFocus(
            focusCenterCm,
            MathF.Max(1f, focusWidthCm),
            MathF.Max(1f, focusHeightCm),
            ReadOnlySpan<Entity>.Empty,
            AgentState,
            MassNavigationFlow,
            reason);
        MassNavigationStreamingWindowUpdate streamingUpdate = _streamingWindow.PrepareUpdate(
            transition.StreamingFocus);
        CommitFocusTransition(transition, streamingUpdate, markFocusBudgetUpdated: true);
    }

    public System.Numerics.Vector2 ToLocalCm(System.Numerics.Vector2 worldCm)
    {
        return _solverWindow.ToLocalCm(worldCm);
    }

    public System.Numerics.Vector2 ToWorldCm(System.Numerics.Vector2 localCm)
    {
        return _solverWindow.ToWorldCm(localCm);
    }

    public float ToWorldXCm(float localXCm) => _solverWindow.ToWorldXCm(localXCm);
    public float ToWorldYCm(float localYCm) => _solverWindow.ToWorldYCm(localYCm);
    public float ToLocalXCm(float worldXCm) => _solverWindow.ToLocalXCm(worldXCm);
    public float ToLocalYCm(float worldYCm) => _solverWindow.ToLocalYCm(worldYCm);

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
        return _solverWindow.ContainsWorldPoint(worldXCm, worldYCm);
    }

    public void UpdateStreamingWindow(System.Numerics.Vector2 worldCenterCm)
    {
        MassNavigationStreamingWindowUpdate update = _streamingWindow.PrepareUpdate(worldCenterCm);
        if (_streamingWindow.ApplyUpdate(update))
        {
            Telemetry.MarkStreamingWindowUpdated();
        }
    }

    private void CommitFocusTransition(
        in MassNavigationSolverWindowTransition transition,
        in MassNavigationStreamingWindowUpdate streamingUpdate,
        bool markFocusBudgetUpdated)
    {
        _solverWindow.Commit(transition);
        if (transition.SolverMoved)
        {
            MassNavigationFlow.RebaseWorldOrigin(
                transition.CenterXCm - (SolverWindowWidthCm * 0.5f),
                transition.CenterYCm - (SolverWindowHeightCm * 0.5f));
            Telemetry.MarkSolverWindowMoved();
        }

        if (_streamingWindow.ApplyUpdate(streamingUpdate))
        {
            Telemetry.MarkStreamingWindowUpdated();
        }

        if (markFocusBudgetUpdated)
        {
            Telemetry.MarkFocusBudgetUpdated();
        }
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

    public bool ConsumeSceneResetRequest()
    {
        if (!_sceneResetRequested)
        {
            return false;
        }

        _sceneResetRequested = false;
        return true;
    }

}
