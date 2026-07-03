using Ludots.Core.Map;

namespace FogVisionDecayShowcaseMod;

public static class FogVisionDecayIds
{
    public const string MapId = "fog_vision_decay_showcase";
    public static readonly MapId ShowcaseMap = new(MapId);
    public const string RuntimeServiceKey = "FogVisionDecayShowcase.Runtime";
    public const string InstalledKey = "FogVisionDecayShowcase.Installed";
    public const string TogglePatrolActionId = "FogVisionDecay.TogglePatrol";
    public const string StepPatrolActionId = "FogVisionDecay.StepPatrol";
    public const string CompactActionId = "FogVisionDecay.Compact";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.Ordinal);
    }
}
