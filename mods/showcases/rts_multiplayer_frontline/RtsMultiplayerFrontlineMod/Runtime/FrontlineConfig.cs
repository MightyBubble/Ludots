using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Networking.Runtime;

namespace RtsMultiplayerFrontlineMod.Runtime;

public sealed class FrontlineConfig
{
    public int SchemaVersion { get; set; }
    public string MapId { get; set; } = string.Empty;
    public int SimulationTickRateHz { get; set; }
    public string ReadyActionId { get; set; } = string.Empty;
    public string CastAbilityOrderTypeKey { get; set; } = string.Empty;
    public string TrainAbilityId { get; set; } = string.Empty;
    public string CrystalAttribute { get; set; } = string.Empty;
    public string HealthAttribute { get; set; } = string.Empty;
    public string HarvesterTag { get; set; } = string.Empty;
    public string InfantryTag { get; set; } = string.Empty;
    public string CrystalNodeTag { get; set; } = string.Empty;
    public int TrainCostCrystals { get; set; }
    public int ReadyCountdownTicks { get; set; }
    public int MatchDurationTicks { get; set; }
    public int DisconnectGraceTicks { get; set; }
    public FrontlineReplicationConfig Replication { get; set; } = new();
    public FrontlineSideConfig[] Sides { get; set; } = Array.Empty<FrontlineSideConfig>();
    public FrontlineHudConfig Hud { get; set; } = new();

    public static FrontlineConfig Load(JsonObject source)
    {
        using JsonDocument document = JsonDocument.Parse(source.ToJsonString());
        JsonElement root = document.RootElement;
        RequireProperties(
            root,
            "schemaVersion", "mapId", "simulationTickRateHz", "readyActionId",
            "castAbilityOrderTypeKey", "trainAbilityId", "crystalAttribute", "healthAttribute",
            "harvesterTag", "infantryTag", "crystalNodeTag",
            "trainCostCrystals",
            "readyCountdownTicks", "matchDurationTicks", "disconnectGraceTicks",
            "replication", "sides", "hud");

        FrontlineConfig? config = root.Deserialize<FrontlineConfig>(StrictJsonOptions.CreateCamelCase());
        if (config == null)
        {
            throw new InvalidOperationException("RTS Frontline config is empty.");
        }

        config.Validate();
        return config;
    }

    public int ResolveSideIndex(int teamId)
    {
        for (int i = 0; i < Sides.Length; i++)
        {
            if (Sides[i].TeamId == teamId)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"RTS Frontline team {teamId} is not declared by the match config.");
    }

    private void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidOperationException($"RTS Frontline config requires schemaVersion 1; got {SchemaVersion}.");
        }

        RequireText(MapId, nameof(MapId));
        RequireText(ReadyActionId, nameof(ReadyActionId));
        RequireText(CastAbilityOrderTypeKey, nameof(CastAbilityOrderTypeKey));
        RequireText(TrainAbilityId, nameof(TrainAbilityId));
        RequireText(CrystalAttribute, nameof(CrystalAttribute));
        RequireText(HealthAttribute, nameof(HealthAttribute));
        RequireText(HarvesterTag, nameof(HarvesterTag));
        RequireText(InfantryTag, nameof(InfantryTag));
        RequireText(CrystalNodeTag, nameof(CrystalNodeTag));
        RequirePositive(SimulationTickRateHz, nameof(SimulationTickRateHz));
        RequirePositive(TrainCostCrystals, nameof(TrainCostCrystals));
        RequirePositive(ReadyCountdownTicks, nameof(ReadyCountdownTicks));
        RequirePositive(MatchDurationTicks, nameof(MatchDurationTicks));
        RequirePositive(DisconnectGraceTicks, nameof(DisconnectGraceTicks));
        Replication.Validate(HealthAttribute, CrystalAttribute);
        if (Sides.Length != 2)
        {
            throw new InvalidOperationException("RTS Frontline config requires exactly two sides.");
        }

        for (int i = 0; i < Sides.Length; i++)
        {
            Sides[i].Validate(i);
        }

        if (Sides[0].PlayerId == Sides[1].PlayerId || Sides[0].TeamId == Sides[1].TeamId)
        {
            throw new InvalidOperationException("RTS Frontline sides require distinct player and team ids.");
        }

        Hud.Validate();
    }

    private static void RequireProperties(JsonElement root, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (!root.TryGetProperty(names[i], out _))
            {
                throw new InvalidOperationException($"RTS Frontline config requires explicit '{names[i]}'.");
            }
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"RTS Frontline config requires non-empty {name}.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"RTS Frontline config requires {name} > 0; got {value}.");
        }
    }
}

public sealed class FrontlineReplicationConfig
{
    public int CoreSchemaId { get; set; }
    public int HarvesterSchemaId { get; set; }
    public int InfantrySchemaId { get; set; }
    public int CrystalNodeSchemaId { get; set; }
    public int MatchStateSchemaId { get; set; }
    public string OwnedUnitsCollectionKey { get; set; } = string.Empty;
    public string PublicResourcesCollectionKey { get; set; } = string.Empty;
    public string PublicMatchStateCollectionKey { get; set; } = string.Empty;
    public string[] VisibleEnemyAttributes { get; set; } = Array.Empty<string>();

    internal void Validate(string healthAttribute, string crystalAttribute)
    {
        if (string.IsNullOrWhiteSpace(OwnedUnitsCollectionKey) ||
            string.IsNullOrWhiteSpace(PublicResourcesCollectionKey) ||
            string.IsNullOrWhiteSpace(PublicMatchStateCollectionKey))
        {
            throw new InvalidOperationException(
                "RTS Frontline replication requires explicit Knowledge collection keys.");
        }
        if (string.Equals(OwnedUnitsCollectionKey, PublicResourcesCollectionKey, StringComparison.Ordinal) ||
            string.Equals(OwnedUnitsCollectionKey, PublicMatchStateCollectionKey, StringComparison.Ordinal) ||
            string.Equals(PublicResourcesCollectionKey, PublicMatchStateCollectionKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RTS Frontline replication Knowledge collection keys must be distinct.");
        }

        if (VisibleEnemyAttributes == null || VisibleEnemyAttributes.Length == 0)
        {
            throw new InvalidOperationException(
                "RTS Frontline replication requires explicit visibleEnemyAttributes.");
        }

        bool includesHealth = false;
        for (int i = 0; i < VisibleEnemyAttributes.Length; i++)
        {
            string attribute = VisibleEnemyAttributes[i];
            if (string.IsNullOrWhiteSpace(attribute) ||
                !string.Equals(attribute, attribute.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RTS Frontline replication visibleEnemyAttributes[{i}] must be a canonical non-empty attribute name.");
            }

            if (string.Equals(attribute, crystalAttribute, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RTS Frontline must not disclose private crystal resources through enemy vision.");
            }

            includesHealth |= string.Equals(attribute, healthAttribute, StringComparison.Ordinal);
            for (int prior = 0; prior < i; prior++)
            {
                if (string.Equals(VisibleEnemyAttributes[prior], attribute, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline replication visibleEnemyAttributes duplicates '{attribute}'.");
                }
            }
        }

        if (!includesHealth)
        {
            throw new InvalidOperationException(
                "RTS Frontline visible enemy disclosure must include the configured health attribute.");
        }

        int[] ids = { CoreSchemaId, HarvesterSchemaId, InfantrySchemaId, CrystalNodeSchemaId, MatchStateSchemaId };
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] <= 0)
            {
                throw new InvalidOperationException("RTS Frontline replication schema ids must be positive.");
            }

            for (int prior = 0; prior < i; prior++)
            {
                if (ids[prior] == ids[i])
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline replication schema id {ids[i]} is duplicated.");
                }
            }
        }
    }
}

public sealed class FrontlineSideConfig
{
    public string Id { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int VisionScopeKeyId { get; set; }

    internal void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) ||
            PlayerId <= 0 || TeamId <= 0 || VisionScopeKeyId <= 0)
        {
            throw new InvalidOperationException(
                $"RTS Frontline sides[{index}] requires id, displayName, playerId, teamId and visionScopeKeyId.");
        }
    }
}

public sealed class FrontlineHudConfig
{
    private string[] _submitResultText = Array.Empty<string>();
    private string[] _admissionResultText = Array.Empty<string>();

    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string GatherHint { get; set; } = string.Empty;
    public string TrainHint { get; set; } = string.Empty;
    public string AttackHint { get; set; } = string.Empty;
    public string ReadyHint { get; set; } = string.Empty;
    public string WaitingText { get; set; } = string.Empty;
    public string CountdownText { get; set; } = string.Empty;
    public string ReadyText { get; set; } = string.Empty;
    public string NotReadyText { get; set; } = string.Empty;
    public string DisconnectedText { get; set; } = string.Empty;
    public string BattleStartedText { get; set; } = string.Empty;
    public string SynchronizingBattlefieldText { get; set; } = string.Empty;
    public string SideOneVictoryText { get; set; } = string.Empty;
    public string SideTwoVictoryText { get; set; } = string.Empty;
    public string DrawText { get; set; } = string.Empty;
    public string ConnectingText { get; set; } = string.Empty;
    public string ReconnectingText { get; set; } = string.Empty;
    public string OpponentOfflineText { get; set; } = string.Empty;
    public string ServiceInterruptedText { get; set; } = string.Empty;
    public string SmoothConnectionText { get; set; } = string.Empty;
    public string DelayedConnectionText { get; set; } = string.Empty;
    public int DelayedRoundTripThresholdMilliseconds { get; set; }
    public string CommandSendingText { get; set; } = string.Empty;
    public string CommandAcceptedText { get; set; } = string.Empty;
    public string CommandQueuedText { get; set; } = string.Empty;
    public string CommandPendingText { get; set; } = string.Empty;
    public string CommandStartedText { get; set; } = string.Empty;
    public FrontlineHudLayoutConfig Layout { get; set; } = new();
    public Dictionary<string, string> CommandSubmitRejectionText { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CommandAdmissionRejectionText { get; set; } = new(StringComparer.Ordinal);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) ||
            string.IsNullOrWhiteSpace(Objective) ||
            string.IsNullOrWhiteSpace(GatherHint) ||
            string.IsNullOrWhiteSpace(TrainHint) ||
            string.IsNullOrWhiteSpace(AttackHint) ||
            string.IsNullOrWhiteSpace(ReadyHint) ||
            string.IsNullOrWhiteSpace(WaitingText) ||
            string.IsNullOrWhiteSpace(CountdownText) ||
            string.IsNullOrWhiteSpace(ReadyText) ||
            string.IsNullOrWhiteSpace(NotReadyText) ||
            string.IsNullOrWhiteSpace(DisconnectedText) ||
            string.IsNullOrWhiteSpace(BattleStartedText) ||
            string.IsNullOrWhiteSpace(SynchronizingBattlefieldText) ||
            string.IsNullOrWhiteSpace(SideOneVictoryText) ||
            string.IsNullOrWhiteSpace(SideTwoVictoryText) ||
            string.IsNullOrWhiteSpace(DrawText) ||
            string.IsNullOrWhiteSpace(ConnectingText) ||
            string.IsNullOrWhiteSpace(ReconnectingText) ||
            string.IsNullOrWhiteSpace(OpponentOfflineText) ||
            string.IsNullOrWhiteSpace(ServiceInterruptedText) ||
            string.IsNullOrWhiteSpace(SmoothConnectionText) ||
            string.IsNullOrWhiteSpace(DelayedConnectionText) ||
            string.IsNullOrWhiteSpace(CommandSendingText) ||
            string.IsNullOrWhiteSpace(CommandAcceptedText) ||
            string.IsNullOrWhiteSpace(CommandQueuedText) ||
            string.IsNullOrWhiteSpace(CommandPendingText) ||
            string.IsNullOrWhiteSpace(CommandStartedText))
        {
            throw new InvalidOperationException("RTS Frontline HUD copy must be fully configured.");
        }

        if (DelayedRoundTripThresholdMilliseconds <= 0)
        {
            throw new InvalidOperationException("RTS Frontline HUD delayed RTT threshold must be positive.");
        }

        Layout.Validate();

        _submitResultText = CompileSubmitResultText(CommandSubmitRejectionText);
        _admissionResultText = CompileAdmissionResultText(CommandAdmissionRejectionText);
    }

    internal string ResolveSubmitRejection(ReplicatedClientCommandSubmitResult result)
    {
        int index = (int)result;
        if ((uint)index >= (uint)_submitResultText.Length || string.IsNullOrEmpty(_submitResultText[index]))
        {
            throw new InvalidOperationException($"RTS Frontline HUD has no local command result text for {result}.");
        }

        return _submitResultText[index];
    }

    internal string ResolveAdmissionRejection(OrderSubmitResult result)
    {
        int index = (int)result;
        if ((uint)index >= (uint)_admissionResultText.Length || string.IsNullOrEmpty(_admissionResultText[index]))
        {
            throw new InvalidOperationException($"RTS Frontline HUD has no server command result text for {result}.");
        }

        return _admissionResultText[index];
    }

    private static string[] CompileSubmitResultText(Dictionary<string, string> configured)
    {
        ReplicatedClientCommandSubmitResult[] values = Enum.GetValues<ReplicatedClientCommandSubmitResult>();
        var compiled = new string[values.Length];
        int expected = 0;
        for (int i = 0; i < values.Length; i++)
        {
            ReplicatedClientCommandSubmitResult value = values[i];
            if (value is ReplicatedClientCommandSubmitResult.None or ReplicatedClientCommandSubmitResult.Submitted)
            {
                continue;
            }

            expected++;
            string key = value.ToString();
            if (!configured.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"RTS Frontline HUD requires commandSubmitRejectionText.{key}.");
            }
            compiled[(int)value] = text;
        }

        if (configured.Count != expected)
        {
            throw new InvalidOperationException("RTS Frontline HUD commandSubmitRejectionText contains unknown or success keys.");
        }

        return compiled;
    }

    private static string[] CompileAdmissionResultText(Dictionary<string, string> configured)
    {
        OrderSubmitResult[] values = Enum.GetValues<OrderSubmitResult>();
        var compiled = new string[values.Length];
        int expected = 0;
        for (int i = 0; i < values.Length; i++)
        {
            OrderSubmitResult value = values[i];
            if (value is OrderSubmitResult.Activated or OrderSubmitResult.Queued or
                OrderSubmitResult.Pending or OrderSubmitResult.NetworkScheduled)
            {
                continue;
            }

            expected++;
            string key = value.ToString();
            if (!configured.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"RTS Frontline HUD requires commandAdmissionRejectionText.{key}.");
            }
            compiled[(int)value] = text;
        }

        if (configured.Count != expected)
        {
            throw new InvalidOperationException("RTS Frontline HUD commandAdmissionRejectionText contains unknown or success keys.");
        }

        return compiled;
    }
}

public sealed class FrontlineHudLayoutConfig
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Padding { get; set; }
    public int InstructionColumnX { get; set; }
    public int TitleFontSize { get; set; }
    public int StatusFontSize { get; set; }
    public int BodyFontSize { get; set; }
    public int LineHeight { get; set; }
    public int OutcomeWidth { get; set; }
    public int OutcomeHeight { get; set; }
    public int OutcomeGap { get; set; }

    internal void Validate()
    {
        if (X < 0 || Y < 0 ||
            Width < 640 || Height < 140 ||
            Padding < 8 ||
            InstructionColumnX < 280 || InstructionColumnX > Width - 280 ||
            TitleFontSize < 16 || StatusFontSize < 12 || BodyFontSize < 10 ||
            LineHeight < BodyFontSize + 4 ||
            OutcomeWidth < 240 || OutcomeHeight < 44 || OutcomeGap < 0 ||
            checked((Padding * 2) + TitleFontSize + 6 + (LineHeight * 4)) > Height)
        {
            throw new InvalidOperationException(
                "RTS Frontline HUD layout must provide two readable 1280x720 status and instruction columns.");
        }
    }
}

internal sealed class FrontlineConfigLoader
{
    public const string RelativePath = "RtsMultiplayerFrontlineConfig.json";
    private readonly ConfigPipeline _pipeline;

    public FrontlineConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public FrontlineConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"RTS Frontline config '{RelativePath}' is not registered.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"RTS Frontline config '{RelativePath}' requires Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        return merged == null
            ? throw new InvalidOperationException($"RTS Frontline config '{RelativePath}' did not resolve to an object.")
            : FrontlineConfig.Load(merged);
    }
}
