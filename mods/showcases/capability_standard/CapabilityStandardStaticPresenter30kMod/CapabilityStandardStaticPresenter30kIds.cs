using System;
using Ludots.Core.Presentation.Presenters;

namespace CapabilityStandardStaticPresenter30kMod
{
    public static class CapabilityStandardStaticPresenter30kIds
    {
        public const string ShowcaseMapId = "capability_standard_static_presenter_30k_showcase";
        public const string ScatterBenchmarkMapId = "capability_standard_static_presenter_30k_scatter_benchmark";
        public const string ScatterHudBarBenchmarkMapId = "capability_standard_static_presenter_30k_scatter_hudbar_benchmark";
        public const string ScatterHudTextBenchmarkMapId = "capability_standard_static_presenter_30k_scatter_hudtext_benchmark";
        public const string MeshBenchmarkMapId = "capability_standard_static_presenter_30k_mesh_ism_benchmark";
        public const string DynamicWorkerBenchmarkMapId = "capability_standard_static_presenter_30k_dynamic_worker_benchmark";
        public const string DynamicWorkerLargeWorldBenchmarkMapId = "capability_standard_static_presenter_30k_dynamic_worker_large_world_benchmark";
        public const string MinimapMarkerLargeWorldShowcaseMapId = "capability_standard_static_presenter_30k_minimap_marker_large_world_showcase";
        public const string EntityName = "CapabilityStaticPresenter";
        public const string TemplateId = "capability_static_presenter_building";
        public const string MeshBenchmarkEntityName = "CapabilityStaticPresenterMeshBenchmark";
        public const string MeshBenchmarkTemplateId = "capability_static_presenter_mesh_benchmark_entity";
        public const string MeshBenchmarkDefinitionId = "capability_static_presenter_mesh_benchmark_ism";
        public const string MeshHudBarBenchmarkEntityName = "CapabilityStaticPresenterMeshHudBarBenchmark";
        public const string MeshHudBarBenchmarkTemplateId = "capability_static_presenter_mesh_hudbar_benchmark_entity";
        public const string MeshHudBarBenchmarkDefinitionId = "capability_static_presenter_mesh_benchmark_hudbar";
        public const string MeshHudTextBenchmarkEntityName = "CapabilityStaticPresenterMeshHudTextBenchmark";
        public const string MeshHudTextBenchmarkTemplateId = "capability_static_presenter_mesh_hudtext_benchmark_entity";
        public const string MeshHudTextBenchmarkDefinitionId = "capability_static_presenter_mesh_benchmark_hudtext";
        public const string DynamicWorkerEntityName = "CapabilityStaticPresenterDynamicWorker";
        public const string DynamicWorkerTemplateId = "capability_static_presenter_dynamic_worker_entity";
        public const string DynamicWorkerDefinitionId = "capability_static_presenter_dynamic_worker_actor";
        public const string MinimapMarkerBallEntityName = "CapabilityStaticPresenterMinimapMarkerBall";
        public const string MinimapMarkerBallTemplateId = "capability_static_presenter_minimap_marker_ball_entity";
        public const string MinimapMarkerBallDefinitionId = "capability_static_presenter_minimap_marker_ball";
        public const string RootDefinitionId = "capability_static_presenter_root";
        public const string WorkshopLeftDefinitionId = "capability_static_presenter_workshop_left_mesh";
        public const string WorkshopRightDefinitionId = "capability_static_presenter_workshop_right_mesh";
        public const string ChimneyDefinitionId = "capability_static_presenter_chimney_mesh";
        public const string SmokeDefinitionId = "capability_static_presenter_chimney_smoke_vfx";
        public const string RouteSplineDefinitionId = "capability_static_presenter_worker_route_spline";
        public const string DecalDefinitionId = "capability_static_presenter_forge_decal";
        public const string WorkerDefinitionId = "capability_static_presenter_worker_actor";
        public const string DurabilityBarDefinitionId = "capability_static_presenter_durability_hud_bar";
        public const string DurabilityTextDefinitionId = "capability_static_presenter_durability_hud_text";

        public const string ParamRegionKey = "capability_static_presenter.region";
        public const string ParamDurabilityKey = "capability_static_presenter.durability.ratio";
        public const string ParamWorkingVisibleKey = "capability_static_presenter.working.visible";
        public const string ParamDayNightKey = "capability_static_presenter.dayNight";
        public const string ParamWorkshopAssetStateKey = "capability_static_presenter.workshop.assetState";
        public const string ParamWorkerSpeedKey = "capability_static_presenter.worker.locomotion.speed";
        public const string ParamDurabilityRatioKey = "capability_static_presenter.durability.hud.ratio";
        public const string ParamDurabilityCurrentKey = "capability_static_presenter.durability.current";
        public const string ParamDurabilityBaseKey = "capability_static_presenter.durability.base";
        public const string ParamWorkerProgressKey = "capability_static_presenter.worker.route.progress";
        public const string ParamMinimapFacingKey = "capability_static_presenter.minimap.facing";

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

        public const string EffectSetDurabilityIntact = "Effect.Showcase.CapabilityStaticPresenter.SetDurabilityIntact";
        public const string EffectSetDurabilityDamaged = "Effect.Showcase.CapabilityStaticPresenter.SetDurabilityDamaged";
        public const string EffectSetDurabilityRuined = "Effect.Showcase.CapabilityStaticPresenter.SetDurabilityRuined";

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
