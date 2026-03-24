namespace ItemSystemShowcaseMod.UI;

internal enum ItemSystemShowcaseSceneKind
{
    Hub,
    LoadoutGarage,
    WeaponBench,
    RaidLoop,
    ForgeSocketLab
}

internal sealed record ItemSystemShowcasePanelState(
    ItemSystemShowcaseSceneKind SceneKind,
    string Header,
    string SceneSummary,
    string HeroSummary,
    string CreditsSummary,
    string DummySummary,
    string SelectionSummary,
    string[] PrimaryLines,
    string[] SecondaryLines,
    string[] TertiaryLines,
    string[] QuaternaryLines,
    string[] LogLines);
