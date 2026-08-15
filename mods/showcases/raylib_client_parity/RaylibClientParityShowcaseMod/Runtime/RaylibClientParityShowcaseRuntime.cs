using System;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RaylibClientParityShowcaseMod.Runtime;

internal sealed class RaylibClientParityShowcaseRuntime : IBenchmarkSceneController
{
    private const int VisibleBuildingInstances = 48;
    private const int GridWidth = 8;
    private const float BuildingSpacing = 6.5f;

    private RaylibBenchmarkInstance[]? _buildingInstances;
    private RaylibBenchmarkMaterialColor[]? _palette;
    private bool _sceneInstalled;
    private GameEngine? _activeEngine;

    public bool IsActive =>
        _activeEngine != null &&
        RaylibClientParityShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);

    public bool SupportsScatterControl => false;

    public bool IsCleanPerformanceScene => false;

    public bool SuppressHostDiagnosticUi => IsActive;

    public bool SuppressHostDebugGuides => IsActive;

    public int ScatterMin => 0;

    public int ScatterMax => 0;

    public int ScatterTarget => 0;

    public int ScatterAppliedTotal => 0;

    public void SetScatterTargetFromRatio(float ratio)
    {
    }

    public void ApplyScatterTarget()
    {
    }

    public void ApplyScatterLayout(int total)
    {
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!RaylibClientParityShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            Disable(engine);
            return Task.CompletedTask;
        }

        _activeEngine = engine;
        engine.SetService(CoreServiceKeys.BenchmarkSceneController, (IBenchmarkSceneController)this);
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            // Linux Raylib host Skia overlay currently resolves WGL/opengl32; keep parity scene GPU-only.
            renderDebug.DrawSkiaUi = false;
            renderDebug.DrawPrimitives = true;
        }

        EnsureScene(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is GameEngine engine)
        {
            Disable(engine);
        }

        return Task.CompletedTask;
    }

    private void EnsureScene(GameEngine engine)
    {
        IRaylibBenchmarkRenderer? renderer = ResolveRenderer(engine);
        if (renderer == null)
        {
            throw new InvalidOperationException(
                "Raylib client parity showcase requires IRaylibBenchmarkRenderer (Raylib adapter).");
        }

        MeshAssetRegistry? meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
        if (meshes == null)
        {
            throw new InvalidOperationException("Raylib client parity showcase requires PresentationMeshAssetRegistry.");
        }

        int[] meshAssetIds = ResolveBlacksmithMeshAssetIds(meshes);
        _buildingInstances ??= BuildBuildingInstances(meshAssetIds);
        _palette ??= BuildPalette();

        if (!_sceneInstalled)
        {
            renderer.SetScene(new RaylibBenchmarkScene(
                enabled: true,
                instances: _buildingInstances,
                initialActiveInstanceCount: VisibleBuildingInstances,
                palette: new RaylibBenchmarkMaterialPalette(new Vector4(1f, 1f, 1f, 1f), _palette),
                camera: new RaylibBenchmarkCamera(
                    position: new Vector3(0f, 28f, 42f),
                    target: new Vector3(0f, 1.5f, 0f),
                    fovY: 50f),
                label: "Raylib client parity: static ISM + GpuSkinned crowd + host albedo + vfx_unlit_tint"));
            _sceneInstalled = true;
        }
    }

    private void Disable(GameEngine engine)
    {
        IRaylibBenchmarkRenderer? renderer = ResolveRenderer(engine);
        renderer?.SetScene(default);
        _sceneInstalled = false;
        if (ReferenceEquals(_activeEngine, engine))
        {
            _activeEngine = null;
        }
    }

    private static IRaylibBenchmarkRenderer? ResolveRenderer(GameEngine engine)
    {
        return engine.GetService(new ServiceKey<IRaylibBenchmarkRenderer>(RaylibClientParityShowcaseIds.RendererServiceKey));
    }

    private static int[] ResolveBlacksmithMeshAssetIds(MeshAssetRegistry meshes)
    {
        string[] keys = RaylibClientParityShowcaseIds.BlacksmithMeshKeys;
        var ids = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            int id = meshes.GetId(keys[i]);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Raylib client parity showcase requires blacksmith mesh asset '{keys[i]}'.");
            }

            if (!meshes.TryGetDescriptor(id, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Model)
            {
                throw new InvalidOperationException(
                    $"Raylib client parity showcase requires '{keys[i]}' to be a Model mesh asset.");
            }

            ids[i] = id;
        }

        return ids;
    }

    private static RaylibBenchmarkInstance[] BuildBuildingInstances(int[] meshAssetIds)
    {
        var items = new RaylibBenchmarkInstance[VisibleBuildingInstances];
        int rows = (int)MathF.Ceiling(VisibleBuildingInstances / (float)GridWidth);
        float xOrigin = -((GridWidth - 1) * BuildingSpacing) * 0.5f;
        float zOrigin = -((rows - 1) * BuildingSpacing) * 0.5f - 8f;

        for (int i = 0; i < items.Length; i++)
        {
            int row = i / GridWidth;
            int col = i % GridWidth;
            int meshIndex = i % meshAssetIds.Length;
            // materialId=0: imported model maps only (no host albedo lookup; W2 binder fail-louds on unknown ids).
            float yaw = (i % 8) * (MathF.PI / 4f);
            items[i] = new RaylibBenchmarkInstance(
                meshAssetId: meshAssetIds[meshIndex],
                materialId: 0,
                position: new Vector3(
                    xOrigin + (col * BuildingSpacing),
                    0f,
                    zOrigin + (row * BuildingSpacing)),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw),
                scale: new Vector3(0.85f, 0.85f, 0.85f),
                color: Vector4.One);
        }

        return items;
    }

    private static RaylibBenchmarkMaterialColor[] BuildPalette()
    {
        return
        [
            new RaylibBenchmarkMaterialColor(0, new Vector4(1f, 1f, 1f, 1f)),
        ];
    }
}
