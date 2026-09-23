namespace SanguoGrandStrategyMod.UI;

internal sealed record SanguoGrandStrategyPanelState(
    string Header,
    string Summary,
    string WebUiStatus,
    string SelectedCityLine,
    string SelectedUnitLine,
    string EquipmentLine,
    string CommanderLine,
    string PendingBattleLine,
    string[] OverviewLines,
    string[] CityLines,
    string[] FactionLines,
    string[] UnitLines,
    string[] LogLines);
