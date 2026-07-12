using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace FormationCapabilityShowcaseMod.Runtime;

internal static class FormationCapabilityShowcaseComponentAuthoring
{
    public const string FormationAgentLayer = "formationCapabilityShowcase.formationAgent";
    public const string FormationSoldierLayer = "formationCapabilityShowcase.formationSoldier";

    public static void Register(string modId)
    {
        LayerRegistry.Register(FormationAgentLayer);
        LayerRegistry.Register(FormationSoldierLayer);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationSoldier>("FormationCapabilityShowcaseFormationSoldier", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationAgent>("FormationCapabilityShowcaseFormationAgent", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseCommandState>("FormationCapabilityShowcaseCommandState", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationState>("FormationCapabilityShowcaseFormationState", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseFormationOutline>("FormationCapabilityShowcaseFormationOutline", modId);
        ComponentRegistry.Register<FormationCapabilityShowcaseObstacleOverlay>("FormationCapabilityShowcaseObstacleOverlay", modId);
    }
}
