using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using RtsMultiplayerFrontlineMod.Runtime;

namespace RtsMultiplayerFrontlineThreeProcessAcceptanceMod.Runtime;

internal sealed class AcceptancePlan
{
    internal const string RelativePath = "RtsMultiplayerFrontlineThreeProcessAcceptancePlan.json";

    public int SchemaVersion { get; set; }
    public string EvidenceFileName { get; set; } = string.Empty;
    public int EvidenceCheckpointSeconds { get; set; }
    public int OverallTimeoutSeconds { get; set; }
    public AcceptanceStageTimeouts StageTimeoutSeconds { get; set; } = new();
    public AcceptanceExpectedValues Expected { get; set; } = new();
    public AcceptanceBattlePlan Battle { get; set; } = new();
    public AcceptancePresentationCopy Presentation { get; set; } = new();

    public static AcceptancePlan Load(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string uri = $"{context.ModId}:assets/{RelativePath}";
        if (!context.VFS.TryResolveFullPath(uri, out string path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"Three-process acceptance plan is missing: {uri}", path);
        }

        AcceptancePlan? plan = JsonSerializer.Deserialize<AcceptancePlan>(
            File.ReadAllText(path),
            StrictJsonOptions.CreateCamelCase());
        if (plan == null)
        {
            throw new InvalidOperationException("Three-process acceptance plan is empty.");
        }

        plan.Validate();
        return plan;
    }

    public static FrontlineConfig LoadFrontlineConfig(IModContext context)
    {
        const string uri = "RtsMultiplayerFrontlineMod:assets/RtsMultiplayerFrontlineConfig.json";
        if (!context.VFS.TryResolveFullPath(uri, out string path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"Frontline config is missing: {uri}", path);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return FrontlineConfig.Load(
            System.Text.Json.Nodes.JsonNode.Parse(document.RootElement.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException("Frontline config root is empty."));
    }

    private void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Acceptance plan requires schemaVersion 1; got {SchemaVersion}.");
        }
        if (string.IsNullOrWhiteSpace(EvidenceFileName) ||
            Path.IsPathRooted(EvidenceFileName) ||
            !string.Equals(Path.GetFileName(EvidenceFileName), EvidenceFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Acceptance evidenceFileName must be one relative file name.");
        }
        RequirePositive(EvidenceCheckpointSeconds, nameof(EvidenceCheckpointSeconds));
        RequirePositive(OverallTimeoutSeconds, nameof(OverallTimeoutSeconds));
        StageTimeoutSeconds.Validate();
        Expected.Validate();
        Battle.Validate();
        Presentation.Validate();
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Acceptance plan requires {name} > 0; got {value}.");
        }
    }

    internal sealed class AcceptanceStageTimeouts
    {
        public int Connect { get; set; }
        public int Ready { get; set; }
        public int Gather { get; set; }
        public int Train { get; set; }
        public int Move { get; set; }
        public int Attack { get; set; }

        internal void Validate()
        {
            RequirePositive(Connect, nameof(Connect));
            RequirePositive(Ready, nameof(Ready));
            RequirePositive(Gather, nameof(Gather));
            RequirePositive(Train, nameof(Train));
            RequirePositive(Move, nameof(Move));
            RequirePositive(Attack, nameof(Attack));
        }
    }

    internal sealed class AcceptanceExpectedValues
    {
        public int InitialCrystals { get; set; }
        public int HarvestedCrystals { get; set; }
        public int PostTrainingCrystals { get; set; }
        public int InitialInfantryCount { get; set; }
        public int TrainedInfantryCount { get; set; }
        public int WinningSideIndex { get; set; }
        public int WinnerMinimumAttackers { get; set; }
        public int LoserAttackers { get; set; }

        internal void Validate()
        {
            if (InitialCrystals < 0 || HarvestedCrystals <= InitialCrystals || PostTrainingCrystals < 0)
            {
                throw new InvalidOperationException("Acceptance crystal expectations are invalid.");
            }
            if (InitialInfantryCount <= 0 || TrainedInfantryCount <= InitialInfantryCount ||
                WinnerMinimumAttackers <= 0 || WinnerMinimumAttackers > TrainedInfantryCount ||
                LoserAttackers <= 0 || LoserAttackers > TrainedInfantryCount)
            {
                throw new InvalidOperationException("Acceptance infantry expectations are invalid.");
            }
            if (WinningSideIndex is < 0 or > 1)
            {
                throw new InvalidOperationException("Acceptance winningSideIndex must be 0 or 1.");
            }
        }
    }

    internal sealed class AcceptanceBattlePlan
    {
        public int MeetingOffsetCm { get; set; }
        public int SiegeBeyondFarResourceCm { get; set; }
        public int ArrivalToleranceCm { get; set; }
        public int MinimumObservedMoveCm { get; set; }
        public int WinnerHoldAtMeetingSeconds { get; set; }
        public int CompletionCameraToleranceCm { get; set; }
        public int CompletionWitnessRadiusCm { get; set; }
        public int MinimumCompletionWinnerInfantry { get; set; }

        internal void Validate()
        {
            RequirePositive(MeetingOffsetCm, nameof(MeetingOffsetCm));
            RequirePositive(SiegeBeyondFarResourceCm, nameof(SiegeBeyondFarResourceCm));
            RequirePositive(ArrivalToleranceCm, nameof(ArrivalToleranceCm));
            RequirePositive(MinimumObservedMoveCm, nameof(MinimumObservedMoveCm));
            RequirePositive(WinnerHoldAtMeetingSeconds, nameof(WinnerHoldAtMeetingSeconds));
            RequirePositive(CompletionCameraToleranceCm, nameof(CompletionCameraToleranceCm));
            RequirePositive(CompletionWitnessRadiusCm, nameof(CompletionWitnessRadiusCm));
            RequirePositive(MinimumCompletionWinnerInfantry, nameof(MinimumCompletionWinnerInfantry));
        }
    }

    internal sealed class AcceptancePresentationCopy
    {
        public string Title { get; set; } = string.Empty;
        public string Connecting { get; set; } = string.Empty;
        public string Ready { get; set; } = string.Empty;
        public string Gathering { get; set; } = string.Empty;
        public string Training { get; set; } = string.Empty;
        public string Advancing { get; set; } = string.Empty;
        public string Engaging { get; set; } = string.Empty;
        public string Completed { get; set; } = string.Empty;
        public string Failed { get; set; } = string.Empty;

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Connecting) ||
                string.IsNullOrWhiteSpace(Ready) || string.IsNullOrWhiteSpace(Gathering) ||
                string.IsNullOrWhiteSpace(Training) || string.IsNullOrWhiteSpace(Advancing) ||
                string.IsNullOrWhiteSpace(Engaging) || string.IsNullOrWhiteSpace(Completed) ||
                string.IsNullOrWhiteSpace(Failed))
            {
                throw new InvalidOperationException("Acceptance presentation copy must be fully configured.");
            }
        }
    }
}
