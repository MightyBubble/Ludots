namespace ScopeSwitchShowcaseMod.Runtime;

public readonly record struct ScopeSwitchSnapshot(
    string ActiveScopeId,
    string ActiveScopeLabel,
    int VisibleCount,
    int SelectedCount,
    string[] VisibleLabels,
    string[] SelectedLabels,
    string[] ScopeLines,
    string Status);
