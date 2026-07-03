using Ludots.Core.Map;

namespace AssociationStressShowcaseMod;

public static class AssociationStressIds
{
    public const string InstalledKey = "AssociationStressShowcase.Installed";
    public const string RuntimeServiceKey = "AssociationStressShowcase.Runtime";
    public const string InputContextId = "AssociationStressShowcase.Controls";
    public const string IncreaseScaleActionId = "AssociationStress.IncreaseScale";
    public const string DecreaseScaleActionId = "AssociationStress.DecreaseScale";
    public const string TogglePulseActionId = "AssociationStress.TogglePulse";
    public const string CompactActionId = "AssociationStress.Compact";
    public const string MapId = "association_stress_showcase";
    public const string PanelOwnerId = "association-stress.showcase";
    public const string ScenarioLabel = "AssociationStress.Node";
    public static readonly MapId ShowcaseMap = new(MapId);

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
