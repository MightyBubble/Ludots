namespace ItemSystemShowcaseMod.UI;

internal enum ItemSystemShowcaseSceneKind
{
    Hub,
    LoadoutGarage,
    WeaponBench,
    RaidLoop
}

internal sealed record ItemSystemShowcasePanelState(
    ItemSystemShowcaseSceneKind SceneKind,
    string Header,
    string SceneSummary,
    string HeroSummary,
    string CreditsSummary,
    string DummySummary,
    string[] PrimaryLines,
    string[] SecondaryLines,
    string[] TertiaryLines,
    string[] QuaternaryLines,
    string[] LogLines);
