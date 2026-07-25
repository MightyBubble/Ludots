using System.Text;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Scripting;

namespace RtsMultiplayerFrontlineThreeProcessAcceptanceMod.Runtime;

internal sealed class AcceptanceEvidence
{
    public int SchemaVersion { get; set; } = 4;
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
    public int FirstTrainedInfantryObservedCommittedTick { get; set; } = -1;
    public int FirstTrainedInfantryObservedCount { get; set; } = -1;
    public string HarvesterHandle { get; set; } = string.Empty;
    public AcceptancePositionEvidence? HarvesterStartPosition { get; set; }
    public AcceptancePositionEvidence? HarvesterEndPosition { get; set; }
    public string[] SelectedInfantryHandles { get; set; } = Array.Empty<string>();
    public AcceptancePositionEvidence[] MoveStartPositions { get; set; } = Array.Empty<AcceptancePositionEvidence>();
    public AcceptancePositionEvidence[] MoveEndPositions { get; set; } = Array.Empty<AcceptancePositionEvidence>();
    public int InitialVisibleEnemyInfantryCount { get; set; } = -1;
    public int InitialVisibleEnemyCoreCount { get; set; } = -1;
    public bool EnemyInfantryEnteredVision { get; set; }
    public bool EnemyCoreEnteredVision { get; set; }
    public string AttackTargetHandle { get; set; } = string.Empty;
    public float AttackTargetHealthBefore { get; set; } = -1f;
    public float AttackTargetHealthAfter { get; set; } = -1f;
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
    public int VisibleEnemyCoreCount { get; set; } = -1;
    public int CommittedTick { get; set; }
    public string MatchPhase { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
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

internal sealed class AcceptanceProgress
{
    public AcceptanceProgressStage Stage { get; set; }
    public string Detail { get; set; } = string.Empty;
}
