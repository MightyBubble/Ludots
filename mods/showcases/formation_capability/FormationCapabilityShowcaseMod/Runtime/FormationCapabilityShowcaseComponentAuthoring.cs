using Ludots.Core.Config;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation;

namespace FormationCapabilityShowcaseMod.Runtime;

internal static class FormationCapabilityShowcaseComponentAuthoring
{
    public static void Register(string modId)
    {
        LayerRegistry.Register(MassNavigationLayerNames.FormationAgent);
        LayerRegistry.Register(MassNavigationLayerNames.FormationSoldierAgent);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationSoldier>("FormationCapabilityShowcaseFormationSoldier", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationAgent>("FormationCapabilityShowcaseFormationAgent", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationState>("FormationCapabilityShowcaseFormationState", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationOutline>("FormationCapabilityShowcaseFormationOutline", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseObstacleOverlay>("FormationCapabilityShowcaseObstacleOverlay", modId);
    }
}
