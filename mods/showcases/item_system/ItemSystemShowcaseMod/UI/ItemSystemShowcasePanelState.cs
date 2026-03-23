namespace ItemSystemShowcaseMod.UI;

internal sealed record ItemSystemShowcasePanelState(
    string Header,
    string HeroSummary,
    string CreditsSummary,
    string DummySummary,
    string[] StatLines,
    string[] AbilityLines,
    string[] BuffLines,
    string[] EquipmentLines,
    string[] BackpackLines,
    string[] SecureLines,
    string[] StashLines,
    string[] VendorLines,
    string[] LogLines);
