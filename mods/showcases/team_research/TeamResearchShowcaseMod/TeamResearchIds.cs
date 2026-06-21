using Ludots.Core.Map;

namespace TeamResearchShowcaseMod;

public static class TeamResearchIds
{
    public const string InstalledKey = "TeamResearchShowcase.Installed";
    public const string RuntimeServiceKey = "TeamResearchShowcase.Runtime";
    public const string InputContextId = "TeamResearchShowcase.Controls";
    public const string AddMemberActionId = "TeamResearch.AddMember";
    public const string ResearchPulseActionId = "TeamResearch.ResearchPulse";
    public const string ResetActionId = "TeamResearch.Reset";
    public const string MapId = "team_research_showcase";
    public static readonly MapId ShowcaseMap = new(MapId);

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
