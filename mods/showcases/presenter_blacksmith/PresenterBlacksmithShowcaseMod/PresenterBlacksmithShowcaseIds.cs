using System;
using Ludots.Core.Presentation.Presenters;

namespace PresenterBlacksmithShowcaseMod
{
    public static class PresenterBlacksmithShowcaseIds
    {
        public const string ShowcaseMapId = "presenter_blacksmith_showcase";
        public const string ScatterBenchmarkMapId = "presenter_blacksmith_scatter_benchmark";
        public const string ScatterHudBarBenchmarkMapId = "presenter_blacksmith_scatter_hudbar_benchmark";
        public const string ScatterHudTextBenchmarkMapId = "presenter_blacksmith_scatter_hudtext_benchmark";
        public const string MeshBenchmarkMapId = "presenter_blacksmith_mesh_ism_benchmark";
        public const string DynamicWorkerBenchmarkMapId = "presenter_blacksmith_dynamic_worker_benchmark";
        public const string DynamicWorkerLargeWorldBenchmarkMapId = "presenter_blacksmith_dynamic_worker_large_world_benchmark";
        public const string MinimapMarkerLargeWorldShowcaseMapId = "presenter_blacksmith_minimap_marker_large_world_showcase";
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
        public const string FieldMarkerDefinitionId = "blacksmith_field_marker";

        public const string ParamRegionKey = "blacksmith.region";
        public const string ParamDurabilityKey = "blacksmith.durability.ratio";
        public const string ParamWorkingVisibleKey = "blacksmith.working.visible";
        public const string ParamDayNightKey = "blacksmith.dayNight";
        public const string ParamWorkshopAssetStateKey = "blacksmith.workshop.assetState";
        public const string ParamWorkerSpeedKey = "blacksmith.worker.locomotion.speed";
        public const string ParamDurabilityRatioKey = "blacksmith.durability.hud.ratio";
        public const string ParamDurabilityCurrentKey = "blacksmith.durability.current";
        public const string ParamDurabilityBaseKey = "blacksmith.durability.base";
        public const string ParamWorkerProgressKey = "blacksmith.worker.route.progress";
        public const string ParamMinimapFacingKey = "blacksmith.minimap.facing";

        public static int ParamRegion => PresenterParamKeyRegistry.Register(ParamRegionKey);
        public static int ParamDurability => PresenterParamKeyRegistry.Register(ParamDurabilityKey);
        public static int ParamWorkingVisible => PresenterParamKeyRegistry.Register(ParamWorkingVisibleKey);
        public static int ParamDayNight => PresenterParamKeyRegistry.Register(ParamDayNightKey);
        public static int ParamWorkshopAssetState => PresenterParamKeyRegistry.Register(ParamWorkshopAssetStateKey);
        public static int ParamWorkerSpeed => PresenterParamKeyRegistry.Register(ParamWorkerSpeedKey);
        public static int ParamDurabilityRatio => PresenterParamKeyRegistry.Register(ParamDurabilityRatioKey);
        public static int ParamDurabilityCurrent => PresenterParamKeyRegistry.Register(ParamDurabilityCurrentKey);
        public static int ParamDurabilityBase => PresenterParamKeyRegistry.Register(ParamDurabilityBaseKey);
        public static int ParamWorkerProgress => PresenterParamKeyRegistry.Register(ParamWorkerProgressKey);
        public static int ParamMinimapFacing => PresenterParamKeyRegistry.Register(ParamMinimapFacingKey);

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
