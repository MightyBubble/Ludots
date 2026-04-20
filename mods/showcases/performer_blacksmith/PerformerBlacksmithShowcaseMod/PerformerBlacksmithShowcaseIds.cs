using System;

namespace PerformerBlacksmithShowcaseMod
{
    public static class PerformerBlacksmithShowcaseIds
    {
        public const string ShowcaseMapId = "performer_blacksmith_showcase";
        public const string EntityName = "Blacksmith";
        public const string TemplateId = "blacksmith_building";
        public const string RootDefinitionId = "blacksmith_root";
        public const string WorkshopLeftDefinitionId = "blacksmith_workshop_left_mesh";
        public const string WorkshopRightDefinitionId = "blacksmith_workshop_right_mesh";
        public const string ChimneyDefinitionId = "blacksmith_chimney_mesh";
        public const string SmokeDefinitionId = "blacksmith_chimney_smoke_vfx";
        public const string RouteSplineDefinitionId = "blacksmith_worker_route_spline";
        public const string DecalDefinitionId = "blacksmith_forge_decal";
        public const string WorkerDefinitionId = "blacksmith_worker_actor";
        public const string DurabilityBarDefinitionId = "blacksmith_durability_hud_bar";
        public const string DurabilityTextDefinitionId = "blacksmith_durability_hud_text";

        public const int ParamRegion = 100;
        public const int ParamDurability = 101;
        public const int ParamWorkingVisible = 102;
        public const int ParamDayNight = 103;
        public const int ParamWorkshopAssetState = 104;
        public const int ParamWorkerSpeed = 105;
        public const int ParamDurabilityRatio = 106;
        public const int ParamDurabilityCurrent = 107;
        public const int ParamDurabilityBase = 108;
        public const int ParamWorkerProgress = 109;

        public const string EffectSetDurabilityIntact = "Effect.Showcase.Blacksmith.SetDurabilityIntact";
        public const string EffectSetDurabilityDamaged = "Effect.Showcase.Blacksmith.SetDurabilityDamaged";
        public const string EffectSetDurabilityRuined = "Effect.Showcase.Blacksmith.SetDurabilityRuined";

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, ShowcaseMapId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
