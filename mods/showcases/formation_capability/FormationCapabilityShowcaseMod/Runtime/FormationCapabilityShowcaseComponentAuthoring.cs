using Ludots.Core.Config;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation.Formation;

namespace FormationCapabilityShowcaseMod.Runtime;

internal static class FormationCapabilityShowcaseComponentAuthoring
{
    public const string FormationAgentLayer = "formationCapabilityShowcase.formationAgent";
    public const string FormationSoldierLayer = "formationCapabilityShowcase.formationSoldier";

    public static void Register(string modId)
    {
        LayerRegistry.Register(FormationAgentLayer);
        LayerRegistry.Register(FormationSoldierLayer);
        FormationComponentAuthoring.Register(modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationOutline>("FormationCapabilityShowcaseFormationOutline", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseObstacleOverlay>("FormationCapabilityShowcaseObstacleOverlay", modId);
    }
}
