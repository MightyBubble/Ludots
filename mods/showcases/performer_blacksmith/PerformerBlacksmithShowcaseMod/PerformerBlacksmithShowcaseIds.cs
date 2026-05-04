using System;

namespace PerformerBlacksmithShowcaseMod
{
    public static class PerformerBlacksmithShowcaseIds
    {
        public const string ShowcaseMapId = "performer_blacksmith_showcase";
        public const string ScatterBenchmarkMapId = "performer_blacksmith_scatter_benchmark";
        public const string ScatterHudBarBenchmarkMapId = "performer_blacksmith_scatter_hudbar_benchmark";
        public const string ScatterHudTextBenchmarkMapId = "performer_blacksmith_scatter_hudtext_benchmark";
        public const string MeshBenchmarkMapId = "performer_blacksmith_mesh_ism_benchmark";
        public const string DynamicWorkerBenchmarkMapId = "performer_blacksmith_dynamic_worker_benchmark";
        public const string DynamicWorkerLargeWorldBenchmarkMapId = "performer_blacksmith_dynamic_worker_large_world_benchmark";
        public const string MinimapMarkerLargeWorldShowcaseMapId = "performer_blacksmith_minimap_marker_large_world_showcase";
        public const string EntityName = "Blacksmith";
        public const string TemplateId = "blacksmith_building";
        public const string MeshBenchmarkEntityName = "BlacksmithMeshBenchmark";
        public const string MeshBenchmarkTemplateId = "blacksmith_mesh_benchmark_entity";
        public const string MeshBenchmarkDefinitionId = "blacksmith_mesh_benchmark_ism";
        public const string MeshHudBarBenchmarkEntityName = "BlacksmithMeshHudBarBenchmark";
        public const string MeshHudBarBenchmarkTemplateId = "blacksmith_mesh_hudbar_benchmark_entity";
        public const string MeshHudBarBenchmarkDefinitionId = "blacksmith_mesh_benchmark_hudbar";
        public const string MeshHudTextBenchmarkEntityName = "BlacksmithMeshHudTextBenchmark";
        public const string MeshHudTextBenchmarkTemplateId = "blacksmith_mesh_hudtext_benchmark_entity";
        public const string MeshHudTextBenchmarkDefinitionId = "blacksmith_mesh_benchmark_hudtext";
        public const string DynamicWorkerEntityName = "BlacksmithDynamicWorker";
        public const string DynamicWorkerTemplateId = "blacksmith_dynamic_worker_entity";
        public const string DynamicWorkerDefinitionId = "blacksmith_dynamic_worker_actor";
        public const string MinimapMarkerBallEntityName = "BlacksmithMinimapMarkerBall";
        public const string MinimapMarkerBallTemplateId = "blacksmith_minimap_marker_ball_entity";
        public const string MinimapMarkerBallDefinitionId = "blacksmith_minimap_marker_ball";
        public const int ScatterBenchmarkDefaultTotal = 30_000;
        public const int MeshBenchmarkDefaultTotal = 30_000;
        public const int DynamicWorkerBenchmarkDefaultTotal = 10_000;
        public const int MinimapMarkerShowcaseDefaultTotal = 300;
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
        public const int ParamMinimapFacing = 110;

        public const string EffectSetDurabilityIntact = "Effect.Showcase.Blacksmith.SetDurabilityIntact";
        public const string EffectSetDurabilityDamaged = "Effect.Showcase.Blacksmith.SetDurabilityDamaged";
        public const string EffectSetDurabilityRuined = "Effect.Showcase.Blacksmith.SetDurabilityRuined";

        public static bool IsShowcaseMap(string? mapId)
        {
            return IsInteractiveShowcaseMap(mapId) ||
                   IsScatterBenchmarkMap(mapId) ||
                   IsScatterHudBarBenchmarkMap(mapId) ||
                   IsScatterHudTextBenchmarkMap(mapId) ||
                   IsMeshBenchmarkMap(mapId) ||
                   IsDynamicWorkerBenchmarkMap(mapId) ||
                   IsMinimapMarkerLargeWorldShowcaseMap(mapId);
        }

        public static bool IsInteractiveShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, ShowcaseMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsScatterBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, ScatterBenchmarkMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsScatterHudBarBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, ScatterHudBarBenchmarkMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsScatterHudTextBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, ScatterHudTextBenchmarkMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMeshBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, MeshBenchmarkMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDynamicWorkerBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, DynamicWorkerBenchmarkMapId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mapId, DynamicWorkerLargeWorldBenchmarkMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMinimapMarkerLargeWorldShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, MinimapMarkerLargeWorldShowcaseMapId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
