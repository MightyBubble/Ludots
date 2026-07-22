using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace FormationCapabilityShowcaseMod.Runtime;

internal static class FormationCapabilityShowcaseComponentAuthoring
{
    public const string FormationAnchorLayer = "formationCapabilityShowcase.formationAnchor";
    public const string FormationSoldierLayer = "formationCapabilityShowcase.formationSoldier";

    public static void Register(string modId)
    {
        LayerRegistry.Register(FormationAnchorLayer);
        LayerRegistry.Register(FormationSoldierLayer);
        FormationCapabilityShowcaseFormationComponentAuthoring.Register(modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationOutline>("FormationCapabilityShowcaseFormationOutline", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseObstacleOverlay>("FormationCapabilityShowcaseObstacleOverlay", modId);
    }
}
