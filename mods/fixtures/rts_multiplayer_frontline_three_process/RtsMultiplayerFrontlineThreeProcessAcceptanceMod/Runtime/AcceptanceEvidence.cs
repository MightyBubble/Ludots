using System.Text;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RtsMultiplayerFrontlineThreeProcessAcceptanceMod.Runtime;

internal sealed class AcceptanceEvidence
{
    public int SchemaVersion { get; set; }
    public string Status { get; set; } = "running";
    public string Role { get; set; } = string.Empty;
    public string? Failure { get; set; }
    public string StartedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string? CompletedAtUtc { get; set; }
    public string? PlanFingerprint { get; set; }
    public string ContentFingerprint { get; set; } = string.Empty;
    public ulong SessionEpoch { get; set; }
    public int PlayerId { get; set; }
    public int SeatSlot { get; set; } = -1;
    public int SideIndex { get; set; } = -1;
    public int FaultCount { get; set; }
    public List<AcceptanceSeatEvidence> Seats { get; set; } = new();
    public List<AcceptanceStepEvidence> Steps { get; set; } = new();
    public List<AcceptanceCommandEvidence> Commands { get; set; } = new();
    public AcceptanceGameplayEvidence Gameplay { get; set; } = new();
    public AcceptanceNetworkFaultInjectionEvidence NetworkFaultInjection { get; set; } = new();
    public AcceptanceRuntimeCheckpoint Runtime { get; set; } = new();
}

internal sealed class AcceptanceNetworkFaultInjectionEvidence
{
    public string Role { get; set; } = string.Empty;
    public AcceptanceNetworkFaultInjectionConfigurationEvidence Configuration { get; set; } = new();
    public long DelayedInboundPacketCount { get; set; }
    public long DroppedInboundPacketCount { get; set; }
    public long ReorderedInboundStateDatagramCount { get; set; }

    public void Capture(in NetworkFaultInjectionObservationSnapshot snapshot)
    {
        Role = snapshot.Role switch
        {
            NetworkProcessRole.AuthoritativeServer => "authoritativeServer",
            NetworkProcessRole.ReplicatedClient => "replicatedClient",
            _ => throw new InvalidOperationException(
                $"Acceptance cannot capture fault injection metrics for network role '{snapshot.Role}'."),
        };
        NetworkFaultInjectionConfigurationSnapshot configuration = snapshot.Configuration;
        Configuration.Capture(in configuration);
        DelayedInboundPacketCount = snapshot.DelayedInboundPacketCount;
        DroppedInboundPacketCount = snapshot.DroppedInboundPacketCount;
        ReorderedInboundStateDatagramCount = snapshot.ReorderedInboundStateDatagramCount;
    }
}

internal sealed class AcceptanceNetworkFaultInjectionConfigurationEvidence
{
    public string TransportIdentity { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public int Seed { get; set; }
    public int RoundTripLatencyMilliseconds { get; set; }
    public int JitterMilliseconds { get; set; }
    public int PacketLossPermille { get; set; }
    public int StateReorderPermille { get; set; }
    public bool IsEnabled { get; set; }

    public void Capture(in NetworkFaultInjectionConfigurationSnapshot snapshot)
    {
        if (!snapshot.IsValid)
        {
            throw new InvalidOperationException("Acceptance received an invalid fault injection configuration snapshot.");
        }

        TransportIdentity = snapshot.TransportIdentity;
        ProfileId = snapshot.ProfileId;
        Seed = snapshot.Seed;
        RoundTripLatencyMilliseconds = snapshot.RoundTripLatencyMilliseconds;
        JitterMilliseconds = snapshot.JitterMilliseconds;
        PacketLossPermille = snapshot.PacketLossPermille;
        StateReorderPermille = snapshot.StateReorderPermille;
        IsEnabled = snapshot.IsEnabled;
    }
}

internal sealed class AcceptanceSeatEvidence
{
    public int SeatSlot { get; set; }
    public int PlayerId { get; set; }
    public string ConnectionState { get; set; } = string.Empty;
    public string ReadyState { get; set; } = string.Empty;
}

internal sealed class AcceptanceStepEvidence
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public long StartedInputRevision { get; set; }
    public long CompletedInputRevision { get; set; }
    public int StartedCommittedTick { get; set; }
    public int CompletedCommittedTick { get; set; }
    public string StartedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string? CompletedAtUtc { get; set; }
    public string? Detail { get; set; }
}

internal sealed class AcceptanceCommandEvidence
{
    public string Action { get; set; } = string.Empty;
    public ulong ClientBatchSequence { get; set; }
    public int ActorCount { get; set; }
    public string AdmissionStage { get; set; } = string.Empty;
    public string AdmissionResult { get; set; } = string.Empty;
    public string[] ActorHandles { get; set; } = Array.Empty<string>();
    public AcceptanceAdmissionTransitionEvidence[] AdmissionHistory { get; set; } = Array.Empty<AcceptanceAdmissionTransitionEvidence>();
    public AcceptanceActorAdmissionEvidence[] ActorAdmissions { get; set; } = Array.Empty<AcceptanceActorAdmissionEvidence>();
}

internal sealed class AcceptanceAdmissionTransitionEvidence
{
    public string Stage { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public int AdmissionBatchIndex { get; set; }
    public long ObservedInputRevision { get; set; }
    public int ObservedCommittedTick { get; set; }
    public int AuthoritativeCommittedTick { get; set; }
}

internal sealed class AcceptanceActorAdmissionEvidence
{
    public int BatchIndex { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
}

internal sealed class AcceptanceGameplayEvidence
{
    public int InitialCrystals { get; set; } = -1;
    public int HarvestedCrystals { get; set; } = -1;
    public int PostTrainingCrystals { get; set; } = -1;
    public int InitialInfantryCount { get; set; } = -1;
    public int TrainedInfantryCount { get; set; } = -1;
    public int[] FirstTrainedInfantrySpawnCommittedTickBySide { get; set; } = [-1, -1];
    public int[] SecondTrainedInfantrySpawnCommittedTickBySide { get; set; } = [-1, -1];
    public int FirstTrainedInfantryObservedCommittedTick { get; set; } = -1;
    public int FirstTrainedInfantryObservedCount { get; set; } = -1;
    public int SecondTrainedInfantryObservedCommittedTick { get; set; } = -1;
    public int SecondTrainedInfantryObservedCount { get; set; } = -1;
    public string HarvesterHandle { get; set; } = string.Empty;
    public AcceptancePositionEvidence? HarvesterStartPosition { get; set; }
    public AcceptancePositionEvidence? HarvesterEndPosition { get; set; }
    public string[] SelectedInfantryHandles { get; set; } = Array.Empty<string>();
    public AcceptancePositionEvidence[] MoveStartPositions { get; set; } = Array.Empty<AcceptancePositionEvidence>();
    public AcceptancePositionEvidence[] MoveEndPositions { get; set; } = Array.Empty<AcceptancePositionEvidence>();
    public AcceptanceWorldPointEvidence? MeetingPoint { get; set; }
    public AcceptanceWorldPointEvidence? SiegePoint { get; set; }
    public int InitialVisibleEnemyInfantryCount { get; set; } = -1;
    public int InitialVisibleEnemyCoreCount { get; set; } = -1;
    public bool EnemyInfantryEnteredVision { get; set; }
    public bool EnemyCoreEnteredVision { get; set; }
    public string AttackTargetHandle { get; set; } = string.Empty;
    public AcceptancePositionEvidence? AttackTargetPositionBefore { get; set; }
    public float AttackTargetHealthBefore { get; set; } = -1f;
    public float AttackTargetHealthAfter { get; set; } = -1f;
    public AcceptancePositionEvidence? DefeatedCoreLastPosition { get; set; }
    public AcceptanceWorldPointEvidence? CompletedCameraTarget { get; set; }
    public int CompletedLosingCoreCount { get; set; } = -1;
    public int CompletedWinnerInfantryNearDefeatedCoreCount { get; set; } = -1;
    public AcceptancePositionEvidence[] CompletedWinnerInfantryNearDefeatedCorePositions { get; set; } =
        Array.Empty<AcceptancePositionEvidence>();
    public int CompletedPresentationFrameId { get; set; } = -1;
    public float?[] ObservedCoreHealthBySide { get; set; } = new float?[2];
    public int?[] ObservedInfantryCountBySide { get; set; } = new int?[2];
    public string MatchPhase { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string OutcomeSource { get; set; } = string.Empty;
    public int WinningSideIndex { get; set; } = -1;
    public int CommittedTick { get; set; }
    public string CommittedTickSource { get; set; } = string.Empty;
}

internal sealed class AcceptancePositionEvidence
{
    public string Handle { get; set; } = string.Empty;
    public int PresentationStableId { get; set; }
    public int XCm { get; set; }
    public int YCm { get; set; }
}

internal sealed class AcceptanceWorldPointEvidence
{
    public int XCm { get; set; }
    public int YCm { get; set; }
}

internal sealed class AcceptanceRuntimeCheckpoint
{
    public string CapturedAtUtc { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public int Substep { get; set; }
    public string PendingCommandAction { get; set; } = string.Empty;
    public ulong PendingCommandSequence { get; set; }
    public AcceptanceSelectedActorCheckpoint[] SelectedActors { get; set; } = Array.Empty<AcceptanceSelectedActorCheckpoint>();
    public bool HasBattlePoints { get; set; }
    public AcceptanceWorldPointCheckpoint? MeetingPoint { get; set; }
    public AcceptanceWorldPointCheckpoint? SiegePoint { get; set; }
    public AcceptanceWorldPointCheckpoint? DefeatedCorePoint { get; set; }
    public int VisibleEnemyCoreCount { get; set; } = -1;
    public int CommittedTick { get; set; }
    public string MatchPhase { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public AcceptanceAdvancingPresentationCheckpoint? AdvancingPresentation { get; set; }
}

internal sealed class AcceptanceSelectedActorCheckpoint
{
    public string Handle { get; set; } = string.Empty;
    public bool IsAlive { get; set; }
    public bool HasReplicationIdentity { get; set; }
    public int? XCm { get; set; }
    public int? YCm { get; set; }
}

internal sealed class AcceptanceWorldPointCheckpoint
{
    public int XCm { get; set; }
    public int YCm { get; set; }
}

internal sealed class AcceptanceAdvancingPresentationCheckpoint
{
    public bool HaveAllSelectedActorsMoved { get; set; }
    public bool AreSelectedActorsNear { get; set; }
    public bool AreSelectedActorsVisibleWithPerformerPayload { get; set; }
    public bool AreMovedActorsOnscreenInPresentationReceipts { get; set; }
    public int PresentationFrameId { get; set; } = -1;
    public int ReceiptFrameRevision { get; set; } = -1;
    public int ReceiptProjectionRevision { get; set; } = -1;
    public int CurrentProjectionRevision { get; set; } = -1;
    public int ReceiptCount { get; set; }
    public AcceptanceVector2Checkpoint CameraTargetCm { get; set; } = new();
    public AcceptanceVector2Checkpoint CullingCameraTargetCm { get; set; } = new();
    public int CullingVisibilityRevision { get; set; }
    public int CullingVisibleEntityCount { get; set; }
    public int CullingCulledEntityCount { get; set; }
    public AcceptanceAdvancingActorCheckpoint[] Actors { get; set; } = Array.Empty<AcceptanceAdvancingActorCheckpoint>();
    public AcceptancePresentationReceiptCheckpoint[] InfantryBodyReceipts { get; set; } =
        Array.Empty<AcceptancePresentationReceiptCheckpoint>();
}

internal sealed class AcceptanceAdvancingActorCheckpoint
{
    public string Handle { get; set; } = string.Empty;
    public int CapturedPresentationStableId { get; set; }
    public int CurrentPresentationStableId { get; set; }
    public bool IsAlive { get; set; }
    public bool HasMoved { get; set; }
    public bool IsNearMeetingPoint { get; set; }
    public bool HasVisiblePerformerPayload { get; set; }
    public bool HasOnscreenPresentationReceipt { get; set; }
    public int? WorldXCm { get; set; }
    public int? WorldYCm { get; set; }
    public AcceptanceVector3Checkpoint? VisualPosition { get; set; }
    public bool HasOwnerCullState { get; set; }
    public bool OwnerCullVisible { get; set; }
    public bool HasPerformerPayload { get; set; }
    public int PerformerPayloadCount { get; set; }
    public int PerformerRootCount { get; set; }
    public AcceptancePerformerCheckpoint? RootPerformer { get; set; }
    public AcceptancePerformerCheckpoint[] BodyPerformers { get; set; } = Array.Empty<AcceptancePerformerCheckpoint>();
    public AcceptancePresentationReceiptCheckpoint[] MatchingBodyReceipts { get; set; } =
        Array.Empty<AcceptancePresentationReceiptCheckpoint>();
}

internal sealed class AcceptancePerformerCheckpoint
{
    public int EntityId { get; set; }
    public int DefinitionId { get; set; }
    public int StableId { get; set; }
    public int OwnerStableId { get; set; }
    public bool IsAlive { get; set; }
    public bool HasPosition { get; set; }
    public AcceptanceVector3Checkpoint? Position { get; set; }
    public bool HasCullState { get; set; }
    public bool OwnerCullVisible { get; set; }
}

internal sealed class AcceptancePresentationReceiptCheckpoint
{
    public int OwnerStableId { get; set; }
    public int VisualStableId { get; set; }
    public int TemplateId { get; set; }
    public AcceptanceVector3Checkpoint WorldPosition { get; set; } = new();
    public AcceptanceVector3Checkpoint Position { get; set; } = new();
}

internal sealed class AcceptanceVector2Checkpoint
{
    public float X { get; set; }
    public float Y { get; set; }
}

internal sealed class AcceptanceVector3Checkpoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

internal static class AcceptanceEvidenceWriter
{
    private const int ReplaceAttemptLimit = 20;
    private const int ReplaceRetryDelayMilliseconds = 25;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
        options.WriteIndented = true;
        return options;
    }

    public static string ResolvePath(AcceptancePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, plan.EvidenceFileName));
    }

    public static void WriteAtomic(AcceptanceEvidence evidence, string destination)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Evidence destination is required.", nameof(destination));
        }

        string fullPath = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Evidence destination has no directory.");
        Directory.CreateDirectory(directory);
        string temporary = fullPath + $".{Environment.ProcessId}.tmp";
        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evidence, JsonOptions) + Environment.NewLine);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, fullPath, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException &&
                attempt < ReplaceAttemptLimit)
            {
                Thread.Sleep(ReplaceRetryDelayMilliseconds);
            }
        }
    }
}

internal static class AcceptanceContentIdentity
{
    public static (string? PlanFingerprint, string ContentFingerprint) Resolve(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ResolvedModLoadPlan plan = engine.GetService(CoreServiceKeys.ModLoadPlan)
            ?? throw new InvalidOperationException("Acceptance requires the resolved Mod load plan.");
        ContentFingerprint fingerprint = engine.GetService(CoreServiceKeys.NetworkContentFingerprint);
        if (fingerprint.IsEmpty)
        {
            throw new InvalidOperationException("Acceptance requires the installed network content fingerprint.");
        }

        return (plan.PlanFingerprint, fingerprint.ToHexString());
    }
}

internal enum AcceptanceProgressStage : byte
{
    Connecting = 0,
    Ready = 1,
    Gathering = 2,
    Training = 3,
    Advancing = 4,
    Engaging = 5,
    Completed = 6,
    Failed = 7,
}

internal sealed class AcceptanceProgress : IPresentationCaptureMilestoneSource
{
    private AcceptanceProgressStage _stage = AcceptanceProgressStage.Connecting;
    private string _detail = string.Empty;
    private uint _revision = 1;

    public AcceptanceProgressStage Stage => _stage;

    public string Detail => _detail;

    public PresentationCaptureMilestoneSnapshot Current => new(
        GetMilestoneId(_stage),
        GetMilestoneOrder(_stage),
        _revision);

    public void TransitionTo(AcceptanceProgressStage stage, string detail)
    {
        detail ??= string.Empty;
        int currentOrder = GetMilestoneOrder(_stage);
        int nextOrder = GetMilestoneOrder(stage);
        if (nextOrder < currentOrder)
        {
            throw new InvalidOperationException(
                $"Acceptance progress cannot move backward from '{GetMilestoneId(_stage)}' to '{GetMilestoneId(stage)}'.");
        }

        if (stage == _stage && string.Equals(detail, _detail, StringComparison.Ordinal))
        {
            return;
        }

        _revision = checked(_revision + 1);
        _stage = stage;
        _detail = detail;
    }

    public bool TryResolveOrder(string milestoneId, out int order)
    {
        order = milestoneId switch
        {
            "connecting" => 0,
            "ready" => 1,
            "gathering" => 2,
            "training" => 3,
            "advancing" => 4,
            "engaging" => 5,
            "completed" => 6,
            "failed" => 7,
            _ => -1,
        };
        return order >= 0;
    }

    private static string GetMilestoneId(AcceptanceProgressStage stage) => stage switch
    {
        AcceptanceProgressStage.Connecting => "connecting",
        AcceptanceProgressStage.Ready => "ready",
        AcceptanceProgressStage.Gathering => "gathering",
        AcceptanceProgressStage.Training => "training",
        AcceptanceProgressStage.Advancing => "advancing",
        AcceptanceProgressStage.Engaging => "engaging",
        AcceptanceProgressStage.Completed => "completed",
        AcceptanceProgressStage.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown acceptance progress stage."),
    };

    private static int GetMilestoneOrder(AcceptanceProgressStage stage) => stage switch
    {
        AcceptanceProgressStage.Connecting => 0,
        AcceptanceProgressStage.Ready => 1,
        AcceptanceProgressStage.Gathering => 2,
        AcceptanceProgressStage.Training => 3,
        AcceptanceProgressStage.Advancing => 4,
        AcceptanceProgressStage.Engaging => 5,
        AcceptanceProgressStage.Completed => 6,
        AcceptanceProgressStage.Failed => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown acceptance progress stage."),
    };
}
