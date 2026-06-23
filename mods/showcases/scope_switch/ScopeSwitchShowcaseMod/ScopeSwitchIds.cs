using Ludots.Core.Map;

namespace ScopeSwitchShowcaseMod;

public static class ScopeSwitchIds
{
    public const string InstalledKey = "ScopeSwitchShowcase.Installed";
    public const string RuntimeServiceKey = "ScopeSwitchShowcase.Runtime";
    public const string InputContextId = "ScopeSwitchShowcase.Controls";
    public const string SelfActionId = "ScopeSwitch.Self";
    public const string SquadActionId = "ScopeSwitch.Squad";
    public const string TeamActionId = "ScopeSwitch.Team";
    public const string CityActionId = "ScopeSwitch.City";
    public const string MapId = "scope_switch_showcase";
    public static readonly MapId ShowcaseMap = new(MapId);

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
