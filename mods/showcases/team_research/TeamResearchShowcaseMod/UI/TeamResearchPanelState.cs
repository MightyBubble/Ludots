namespace TeamResearchShowcaseMod.UI;

internal sealed record TeamResearchPanelState(
    string Header,
    string Summary,
    string Controls,
    string ProgressLine,
    string RequirementLine,
    string[] MemberLines,
    string[] UnlockLines,
    string Status);
