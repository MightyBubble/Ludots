using System;
using Ludots.Core.Presentation.Performers;

namespace CapabilityStandardStaticPerformer30kMod
{
    public static class CapabilityStandardStaticPerformer30kIds
    {
        public const string ShowcaseMapId = "capability_standard_static_performer_30k_showcase";
        public const string ScatterBenchmarkMapId = "capability_standard_static_performer_30k_scatter_benchmark";
        public const string ScatterHudBarBenchmarkMapId = "capability_standard_static_performer_30k_scatter_hudbar_benchmark";
        public const string ScatterHudTextBenchmarkMapId = "capability_standard_static_performer_30k_scatter_hudtext_benchmark";
        public const string MeshBenchmarkMapId = "capability_standard_static_performer_30k_mesh_ism_benchmark";
        public const string DynamicWorkerBenchmarkMapId = "capability_standard_static_performer_30k_dynamic_worker_benchmark";
        public const string DynamicWorkerLargeWorldBenchmarkMapId = "capability_standard_static_performer_30k_dynamic_worker_large_world_benchmark";
        public const string MinimapMarkerLargeWorldShowcaseMapId = "capability_standard_static_performer_30k_minimap_marker_large_world_showcase";
        public const string EntityName = "CapabilityStaticPerformer";
        public const string TemplateId = "capability_static_performer_building";
        public const string MeshBenchmarkEntityName = "CapabilityStaticPerformerMeshBenchmark";
        public const string MeshBenchmarkTemplateId = "capability_static_performer_mesh_benchmark_entity";
        public const string MeshBenchmarkDefinitionId = "capability_static_performer_mesh_benchmark_ism";
        public const string MeshHudBarBenchmarkEntityName = "CapabilityStaticPerformerMeshHudBarBenchmark";
        public const string MeshHudBarBenchmarkTemplateId = "capability_static_performer_mesh_hudbar_benchmark_entity";
        public const string MeshHudBarBenchmarkDefinitionId = "capability_static_performer_mesh_benchmark_hudbar";
        public const string MeshHudTextBenchmarkEntityName = "CapabilityStaticPerformerMeshHudTextBenchmark";
        public const string MeshHudTextBenchmarkTemplateId = "capability_static_performer_mesh_hudtext_benchmark_entity";
        public const string MeshHudTextBenchmarkDefinitionId = "capability_static_performer_mesh_benchmark_hudtext";
        public const string DynamicWorkerEntityName = "CapabilityStaticPerformerDynamicWorker";
        public const string DynamicWorkerTemplateId = "capability_static_performer_dynamic_worker_entity";
        public const string DynamicWorkerDefinitionId = "capability_static_performer_dynamic_worker_actor";
        public const string MinimapMarkerBallEntityName = "CapabilityStaticPerformerMinimapMarkerBall";
        public const string MinimapMarkerBallTemplateId = "capability_static_performer_minimap_marker_ball_entity";
        public const string MinimapMarkerBallDefinitionId = "capability_static_performer_minimap_marker_ball";
        public const string RootDefinitionId = "capability_static_performer_root";
        public const string WorkshopLeftDefinitionId = "capability_static_performer_workshop_left_mesh";
        public const string WorkshopRightDefinitionId = "capability_static_performer_workshop_right_mesh";
        public const string ChimneyDefinitionId = "capability_static_performer_chimney_mesh";
        public const string SmokeDefinitionId = "capability_static_performer_chimney_smoke_vfx";
        public const string RouteSplineDefinitionId = "capability_static_performer_worker_route_spline";
        public const string DecalDefinitionId = "capability_static_performer_forge_decal";
        public const string WorkerDefinitionId = "capability_static_performer_worker_actor";
        public const string DurabilityBarDefinitionId = "capability_static_performer_durability_hud_bar";
        public const string DurabilityTextDefinitionId = "capability_static_performer_durability_hud_text";

        public const string ParamRegionKey = "capability_static_performer.region";
        public const string ParamDurabilityKey = "capability_static_performer.durability.ratio";
        public const string ParamWorkingVisibleKey = "capability_static_performer.working.visible";
        public const string ParamDayNightKey = "capability_static_performer.dayNight";
        public const string ParamWorkshopAssetStateKey = "capability_static_performer.workshop.assetState";
        public const string ParamWorkerSpeedKey = "capability_static_performer.worker.locomotion.speed";
        public const string ParamDurabilityRatioKey = "capability_static_performer.durability.hud.ratio";
        public const string ParamDurabilityCurrentKey = "capability_static_performer.durability.current";
        public const string ParamDurabilityBaseKey = "capability_static_performer.durability.base";
        public const string ParamWorkerProgressKey = "capability_static_performer.worker.route.progress";
        public const string ParamMinimapFacingKey = "capability_static_performer.minimap.facing";

        public static int ParamRegion => PerformerParamKeyRegistry.Register(ParamRegionKey);
        public static int ParamDurability => PerformerParamKeyRegistry.Register(ParamDurabilityKey);
        public static int ParamWorkingVisible => PerformerParamKeyRegistry.Register(ParamWorkingVisibleKey);
        public static int ParamDayNight => PerformerParamKeyRegistry.Register(ParamDayNightKey);
        public static int ParamWorkshopAssetState => PerformerParamKeyRegistry.Register(ParamWorkshopAssetStateKey);
        public static int ParamWorkerSpeed => PerformerParamKeyRegistry.Register(ParamWorkerSpeedKey);
        public static int ParamDurabilityRatio => PerformerParamKeyRegistry.Register(ParamDurabilityRatioKey);
        public static int ParamDurabilityCurrent => PerformerParamKeyRegistry.Register(ParamDurabilityCurrentKey);
        public static int ParamDurabilityBase => PerformerParamKeyRegistry.Register(ParamDurabilityBaseKey);
        public static int ParamWorkerProgress => PerformerParamKeyRegistry.Register(ParamWorkerProgressKey);
        public static int ParamMinimapFacing => PerformerParamKeyRegistry.Register(ParamMinimapFacingKey);

        public const string EffectSetDurabilityIntact = "Effect.Showcase.CapabilityStaticPerformer.SetDurabilityIntact";
        public const string EffectSetDurabilityDamaged = "Effect.Showcase.CapabilityStaticPerformer.SetDurabilityDamaged";
        public const string EffectSetDurabilityRuined = "Effect.Showcase.CapabilityStaticPerformer.SetDurabilityRuined";

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
            return string.Equals(mapId, ShowcaseMapId, StringComparison.Ordinal);
        }

        public static bool IsScatterBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, ScatterBenchmarkMapId, StringComparison.Ordinal);
        }

        public static bool IsScatterHudBarBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, ScatterHudBarBenchmarkMapId, StringComparison.Ordinal);
        }

        public static bool IsScatterHudTextBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, ScatterHudTextBenchmarkMapId, StringComparison.Ordinal);
        }

        public static bool IsMeshBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, MeshBenchmarkMapId, StringComparison.Ordinal);
        }

        public static bool IsDynamicWorkerBenchmarkMap(string? mapId)
        {
            return string.Equals(mapId, DynamicWorkerBenchmarkMapId, StringComparison.Ordinal) ||
                   string.Equals(mapId, DynamicWorkerLargeWorldBenchmarkMapId, StringComparison.Ordinal);
        }

        public static bool IsMinimapMarkerLargeWorldShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, MinimapMarkerLargeWorldShowcaseMapId, StringComparison.Ordinal);
        }
    }
}
