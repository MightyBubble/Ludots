using Arch.Core;

namespace SanguoGrandStrategyMod.Runtime;

internal sealed record FactionDefinition(
    int TeamId,
    string Id,
    string Name,
    string Ruler,
    string Color,
    string Style);

internal sealed record UnitTypeDefinition(
    string Id,
    string Name,
    string Category,
    int Tier,
    int Attack,
    int Defense,
    int CostGold,
    int CostFood,
    int Supply,
    int Mobility);

internal sealed record CityDefinition(
    string Id,
    string Name,
    string Region,
    int TeamId,
    int X,
    int Y,
    int Population,
    int Troops,
    int Food,
    int Gold,
    int Production,
    int Defense,
    int Morale,
    int Loyalty,
    int Training);

internal sealed class CityRuntimeState
{
    public CityRuntimeState(CityDefinition definition, Entity entity)
    {
        Definition = definition;
        Entity = entity;
        TeamId = definition.TeamId;
        Population = definition.Population;
        Troops = definition.Troops;
        Food = definition.Food;
        Gold = definition.Gold;
        Production = definition.Production;
        Defense = definition.Defense;
        Morale = definition.Morale;
        Loyalty = definition.Loyalty;
        Training = definition.Training;
        Supply = 70 + ((definition.Production + definition.Defense) % 31);
        TechProgress = 0;
        Command = 12 + ((definition.Training + definition.Defense) % 44);
    }

    public CityDefinition Definition { get; }
    public string Name => Definition.Name;
    public string Region => Definition.Region;
    public Entity Entity { get; }
    public int TeamId { get; set; }
    public int Population { get; set; }
    public int Troops { get; set; }
    public int Food { get; set; }
    public int Gold { get; set; }
    public int Production { get; set; }
    public int Defense { get; set; }
    public int Morale { get; set; }
    public int Loyalty { get; set; }
    public int Training { get; set; }
    public int Supply { get; set; }
    public int TechProgress { get; set; }
    public int Command { get; set; }
}

internal sealed class FactionRuntimeState
{
    public FactionRuntimeState(FactionDefinition definition)
    {
        Definition = definition;
        TrustWithPlayer = definition.TeamId == 1 ? 100 : definition.TeamId is 2 or 3 or 5 or 7 ? 12 : 42;
    }

    public FactionDefinition Definition { get; }
    public int Cities { get; set; }
    public int Population { get; set; }
    public int Troops { get; set; }
    public int Food { get; set; }
    public int Gold { get; set; }
    public int Production { get; set; }
    public int TrustWithPlayer { get; set; }
    public string Stance { get; set; } = "Neutral";
}

internal sealed record PendingBattle(
    string SourceCityId,
    string TargetCityId,
    int AttackerTeamId,
    int Troops,
    UnitTypeDefinition UnitType,
    int TurnsRemaining);

public sealed record SanguoGrandStrategySnapshot(
    int Turn,
    int CityCount,
    int UnitTypeCount,
    int FactionCount,
    string SelectedCity,
    string SelectedUnitType,
    string WebUiStatus,
    string EquipmentLine,
    string CommanderLine,
    SanguoGrandStrategyGraphView Graph,
    SanguoGrandStrategyFactionView[] Factions,
    SanguoGrandStrategyCityView[] Cities,
    string[] LogLines);

public sealed record SanguoGrandStrategyGraphView(
    int Executions,
    int CityCount,
    int WeiFrontierCount,
    int Population,
    int Food,
    int Gold,
    int WeiTroops,
    string BestProductionCity,
    string WeiStrongestCity);

public sealed record SanguoGrandStrategyFactionView(
    int TeamId,
    string Name,
    string Color,
    int Cities,
    int Population,
    int Troops,
    int Food,
    int Gold,
    int Production,
    string Stance,
    int TrustWithPlayer);

public sealed record SanguoGrandStrategyCityView(
    string Id,
    string Name,
    string Region,
    int TeamId,
    int X,
    int Y,
    int Population,
    int Troops,
    int Food,
    int Gold,
    int Production,
    int Defense,
    int Morale,
    int Loyalty,
    bool Selected);
