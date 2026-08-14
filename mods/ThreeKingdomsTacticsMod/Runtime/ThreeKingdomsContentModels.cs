using Arch.Core;

namespace ThreeKingdomsTacticsMod.Runtime;

public enum ThreeKingdomsSkillPattern
{
    Fire,
    Flood,
    Duel,
    Rally,
    Ambush,
    Heal,
    Siege,
    Supply,
    Mobility,
    Stratagem
}

public sealed record ThreeKingdomsGeneralDefinition(
    int Index,
    string Id,
    string Name,
    string Courtesy,
    string Faction,
    string Archetype,
    string SkillId,
    string SkillName,
    string SkillDetail,
    ThreeKingdomsSkillPattern SkillPattern,
    int SkillPower,
    int Leadership,
    int WarPower,
    int Strategy,
    int TroopTypeIndex);

public sealed record ThreeKingdomsTroopTypeDefinition(
    int Index,
    string Id,
    string Name,
    string Role,
    string TerrainAffinity,
    int Move,
    int Range,
    int Attack,
    int Defense,
    int SupplyCost,
    string ItemDefinitionId);

public sealed class ThreeKingdomsUnitState
{
    public required ThreeKingdomsGeneralDefinition General { get; init; }
    public required ThreeKingdomsTroopTypeDefinition TroopType { get; set; }
    public Entity Entity { get; set; }
    public int TeamId { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; }
    public int Morale { get; set; }
    public int Supplies { get; set; }
    public bool Acted { get; set; }
    public string Status { get; set; } = "Ready";
    public int Cooldown { get; set; }

    public bool IsAlive => Health > 0;
}

public sealed record ThreeKingdomsTacticsSnapshot(
    int Turn,
    int Round,
    string Phase,
    int MapWidth,
    int MapHeight,
    int SelectedIndex,
    string SelectedGeneral,
    string SelectedSkill,
    string SelectedTroop,
    int PlayerUnitsAlive,
    int EnemyUnitsAlive,
    int AllGenerals,
    int UniqueSkills,
    int TroopTypes,
    int ArsenalItems,
    int GasAbilityDefinitions,
    int GraphPrograms,
    string LastAction,
    IReadOnlyList<ThreeKingdomsUnitView> Units,
    IReadOnlyList<string> LogLines);

public sealed record ThreeKingdomsUnitView(
    int Index,
    string Name,
    string Faction,
    int TeamId,
    int X,
    int Y,
    int Health,
    int Morale,
    int Supplies,
    string Troop,
    string Skill,
    string Status,
    bool Selected,
    bool Alive);

public static partial class ThreeKingdomsContent
{
    public const int MapWidth = 60;
    public const int MapHeight = 36;

    public static IReadOnlyList<ThreeKingdomsGeneralDefinition> Generals => s_generals;

    public static IReadOnlyList<ThreeKingdomsTroopTypeDefinition> TroopTypes => s_troopTypes;
}
