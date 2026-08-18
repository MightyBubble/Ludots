namespace Ludots.App.RaylibEngineGallery
{
    public sealed record SceneDescriptor(string Id, string Title, string Summary);

    public static class SceneCatalog
    {
        private sealed class SceneEntry
        {
            public SceneEntry(string id, string title, string summary, Func<IEngineScene> factory)
            {
                Id = id;
                Title = title;
                Summary = summary;
                Factory = factory;
            }

            public string Id { get; }
            public string Title { get; }
            public string Summary { get; }
            public Func<IEngineScene> Factory { get; }
        }

        private static readonly SceneEntry[] Entries =
        {
            new("skybox", "天空盒", "RaylibSkyboxRenderer 程序化渐变天空 + 太阳方位驱动", static () => new Scenes.SkyboxScene()),
            new("sky_daynight", "昼夜天空", "RaylibSkyEnvironment 渐变烘焙 + 全天相位驱动", static () => new Scenes.SkyDayNightScene()),
            new("water", "反射水面", "RaylibWaterPass 反射/折射双通道 + DUDV 扭曲", static () => new Scenes.WaterScene()),
            new("terrain_surface", "地表着色", "RaylibTerrainRenderer chunk 网格 + 分带顶点色", static () => new Scenes.TerrainSurfaceScene()),
            new("terrain_heightmap", "视觉高度图", "RaylibVisualHeightmapRenderer 程序化岛屿高度场", static () => new Scenes.TerrainHeightmapScene()),
            new("atmosphere_fog", "距离雾与环境", "RaylibRenderEnvironmentRenderer 雾 + 环境色调", static () => new Scenes.AtmosphereFogScene()),
            new("frame_lighting", "帧光照", "RaylibFrameLighting 日光/环境全天摆动", static () => new Scenes.FrameLightingScene()),
            new("postprocess", "后处理调色", "RaylibPostProcessRenderer 曝光/对比/饱和/暗角调制", static () => new Scenes.PostProcessScene()),
            new("gpu_skinning", "GPU 骨骼蒙皮", "RaylibGpuSkinnedModelCache + RaylibSkinnedPlayback 多相位实例", static () => new Scenes.GpuSkinningScene()),
            new("instancing", "GPU 实例化合批", "IRaylibBenchmarkRenderer 30k 纯数据实例阵", static () => new Scenes.InstancingScene()),
            new("particles", "Quarks 粒子", "ParticleVfxAssetData 火花/烟雾/拉伸火星三组效果", static () => new Scenes.ParticlesScene()),
            new("decal_projection", "投影贴花", "decal_project shader 地表移动投影贴花", static () => new Scenes.DecalProjectionScene()),
            new("vegetation_cutout", "植被透贴", "vegetation_cutout shader 程序化草丛 billboard", static () => new Scenes.VegetationCutoutScene()),
            new("material_binding", "材质绑定", "RaylibMaterialHostBinder 同网格多材质/混合模式", static () => new Scenes.MaterialBindingScene()),
            new("ribbon_overlay", "样条带覆盖层", "GroundOverlayBuffer + 样条带世界覆盖层", static () => new Scenes.RibbonOverlayScene()),
            new("skia_overlay", "Skia 2D 覆盖层", "RaylibSkiaRenderer + SkiaRasterLayer HUD 合成", static () => new Scenes.SkiaOverlayScene()),
            new("debug_draw", "调试绘制", "RaylibDebugDrawRenderer + DebugDrawCommandBuffer", static () => new Scenes.DebugDrawScene()),
            new("primitives", "图元群体渲染", "RaylibPrimitiveRenderer 纯数据图元阵 + 原型动效", static () => new Scenes.PrimitivesScene()),
            new("lighting", "光照全效", "GGX 粗糙度×金属度梯度 + 环绕太阳 + 天空环境近似 + 深度阴影", static () => new Scenes.LightingScene()),
            new("crowd_anim", "大量动画实例合批", "4k mannequin 环形行军——GpuSkinnedInstance 真 GPU 蒙皮合批", static () => new Scenes.CrowdAnimScene()),
        };

        public static IReadOnlyList<string> Ids { get; } = Entries.Select(e => e.Id).ToArray();

        public static List<SceneDescriptor> Descriptors => Entries
            .Select(e => new SceneDescriptor(e.Id, e.Title, e.Summary))
            .ToList();

        public static bool TryCreate(string id, out IEngineScene? scene)
        {
            SceneEntry? entry = Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                scene = null;
                return false;
            }

            scene = entry.Factory();
            return true;
        }

        public static IEngineScene Create(string id)
        {
            if (!TryCreate(id, out IEngineScene? scene) || scene == null)
            {
                throw new InvalidOperationException($"Unknown gallery scene '{id}'.");
            }

            return scene;
        }
    }
}
