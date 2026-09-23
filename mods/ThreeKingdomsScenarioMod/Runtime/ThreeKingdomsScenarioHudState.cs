namespace ThreeKingdomsScenarioMod.Runtime;

internal readonly record struct ThreeKingdomsScenarioHudState(
    string ScenarioTitle,
    string SelectionLabel,
    string SelectionType,
    string CommandHint,
    string ModeSummary,
    string RouteSummary,
    string GarrisonSummary,
    string SiegeSummary);
