namespace ScopeSwitchShowcaseMod.UI;

internal readonly record struct ScopeSwitchPanelState(
    string Header,
    string Summary,
    string Controls,
    string ActiveLine,
    string[] ScopeLines,
    string[] VisibleLines,
    string[] SelectedLines,
    string Status);
