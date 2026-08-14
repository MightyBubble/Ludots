using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace RtsMultiplayerFrontlineMod.Runtime;

public sealed class FrontlineConfig
{
    public int SchemaVersion { get; set; }
    public string MapId { get; set; } = string.Empty;
    public int SimulationTickRateHz { get; set; }
    public string GatherOrderTypeKey { get; set; } = string.Empty;
    public string MoveOrderTypeKey { get; set; } = string.Empty;
    public string AttackOrderTypeKey { get; set; } = string.Empty;
    public string CastAbilityOrderTypeKey { get; set; } = string.Empty;
    public string TrainAbilityId { get; set; } = string.Empty;
    public string DamageEffectId { get; set; } = string.Empty;
    public string ConsumeDefeatedUnitEffectId { get; set; } = string.Empty;
    public string CrystalAttribute { get; set; } = string.Empty;
    public string HealthAttribute { get; set; } = string.Empty;
    public string HarvesterTag { get; set; } = string.Empty;
    public string InfantryTag { get; set; } = string.Empty;
    public string CrystalNodeTag { get; set; } = string.Empty;
    public int StartingCrystals { get; set; }
    public int HarvestCargoCrystals { get; set; }
    public int HarvestLoadTicks { get; set; }
    public int TrainCostCrystals { get; set; }
    public int ArrivalRadiusCm { get; set; }
    public int AttackRangeCm { get; set; }
    public int AttackCooldownTicks { get; set; }
    public int ReadyCountdownTicks { get; set; }
    public int MatchDurationTicks { get; set; }
    public int DisconnectGraceTicks { get; set; }
    public FrontlineSideConfig[] Sides { get; set; } = Array.Empty<FrontlineSideConfig>();
    public FrontlineHudConfig Hud { get; set; } = new();

    public static FrontlineConfig Load(JsonObject source)
    {
        using JsonDocument document = JsonDocument.Parse(source.ToJsonString());
        JsonElement root = document.RootElement;
        RequireProperties(
            root,
            "schemaVersion", "mapId", "simulationTickRateHz", "gatherOrderTypeKey",
            "moveOrderTypeKey", "attackOrderTypeKey", "castAbilityOrderTypeKey",
            "trainAbilityId", "damageEffectId", "consumeDefeatedUnitEffectId", "crystalAttribute", "healthAttribute",
            "harvesterTag", "infantryTag", "crystalNodeTag", "startingCrystals",
            "harvestCargoCrystals", "harvestLoadTicks", "trainCostCrystals",
            "arrivalRadiusCm", "attackRangeCm", "attackCooldownTicks",
            "readyCountdownTicks", "matchDurationTicks", "disconnectGraceTicks", "sides", "hud");

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
        RequireText(GatherOrderTypeKey, nameof(GatherOrderTypeKey));
        RequireText(MoveOrderTypeKey, nameof(MoveOrderTypeKey));
        RequireText(AttackOrderTypeKey, nameof(AttackOrderTypeKey));
        RequireText(CastAbilityOrderTypeKey, nameof(CastAbilityOrderTypeKey));
        RequireText(TrainAbilityId, nameof(TrainAbilityId));
        RequireText(DamageEffectId, nameof(DamageEffectId));
        RequireText(ConsumeDefeatedUnitEffectId, nameof(ConsumeDefeatedUnitEffectId));
        RequireText(CrystalAttribute, nameof(CrystalAttribute));
        RequireText(HealthAttribute, nameof(HealthAttribute));
        RequireText(HarvesterTag, nameof(HarvesterTag));
        RequireText(InfantryTag, nameof(InfantryTag));
        RequireText(CrystalNodeTag, nameof(CrystalNodeTag));
        RequirePositive(SimulationTickRateHz, nameof(SimulationTickRateHz));
        RequirePositive(StartingCrystals, nameof(StartingCrystals));
        RequirePositive(HarvestCargoCrystals, nameof(HarvestCargoCrystals));
        RequirePositive(HarvestLoadTicks, nameof(HarvestLoadTicks));
        RequirePositive(TrainCostCrystals, nameof(TrainCostCrystals));
        RequirePositive(ArrivalRadiusCm, nameof(ArrivalRadiusCm));
        RequirePositive(AttackRangeCm, nameof(AttackRangeCm));
        RequirePositive(AttackCooldownTicks, nameof(AttackCooldownTicks));
        RequirePositive(ReadyCountdownTicks, nameof(ReadyCountdownTicks));
        RequirePositive(MatchDurationTicks, nameof(MatchDurationTicks));
        RequirePositive(DisconnectGraceTicks, nameof(DisconnectGraceTicks));
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

        if (!Sides[0].HasEqualStartingForce(Sides[1]))
        {
            throw new InvalidOperationException("RTS Frontline sides must declare the same initial unit counts.");
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

public sealed class FrontlineSideConfig
{
    public string Id { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public int InitialHarvesterCount { get; set; }
    public int InitialInfantryCount { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    internal void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) || PlayerId <= 0 || TeamId <= 0)
        {
            throw new InvalidOperationException($"RTS Frontline sides[{index}] requires id, displayName, playerId and teamId.");
        }

        if (InitialHarvesterCount <= 0 || InitialInfantryCount <= 0)
        {
            throw new InvalidOperationException($"RTS Frontline side '{Id}' requires positive initial unit counts.");
        }
    }

    internal bool HasEqualStartingForce(FrontlineSideConfig other)
    {
        return InitialHarvesterCount == other.InitialHarvesterCount &&
            InitialInfantryCount == other.InitialInfantryCount;
    }
}

public sealed class FrontlineHudConfig
{
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string GatherHint { get; set; } = string.Empty;
    public string TrainHint { get; set; } = string.Empty;
    public string AttackHint { get; set; } = string.Empty;
    public string WaitingText { get; set; } = string.Empty;
    public string CountdownText { get; set; } = string.Empty;
    public string ReadyText { get; set; } = string.Empty;
    public string NotReadyText { get; set; } = string.Empty;
    public string DisconnectedText { get; set; } = string.Empty;
    public string BattleStartedText { get; set; } = string.Empty;
    public string SideOneVictoryText { get; set; } = string.Empty;
    public string SideTwoVictoryText { get; set; } = string.Empty;
    public string DrawText { get; set; } = string.Empty;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) ||
            string.IsNullOrWhiteSpace(Objective) ||
            string.IsNullOrWhiteSpace(GatherHint) ||
            string.IsNullOrWhiteSpace(TrainHint) ||
            string.IsNullOrWhiteSpace(AttackHint) ||
            string.IsNullOrWhiteSpace(WaitingText) ||
            string.IsNullOrWhiteSpace(CountdownText) ||
            string.IsNullOrWhiteSpace(ReadyText) ||
            string.IsNullOrWhiteSpace(NotReadyText) ||
            string.IsNullOrWhiteSpace(DisconnectedText) ||
            string.IsNullOrWhiteSpace(BattleStartedText) ||
            string.IsNullOrWhiteSpace(SideOneVictoryText) ||
            string.IsNullOrWhiteSpace(SideTwoVictoryText) ||
            string.IsNullOrWhiteSpace(DrawText))
        {
            throw new InvalidOperationException("RTS Frontline HUD copy must be fully configured.");
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
