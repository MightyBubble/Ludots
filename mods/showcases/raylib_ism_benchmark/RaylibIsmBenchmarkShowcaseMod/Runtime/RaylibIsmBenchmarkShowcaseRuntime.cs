using System;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RaylibIsmBenchmarkShowcaseMod.Runtime;

internal sealed class RaylibIsmBenchmarkShowcaseRuntime
{
    private const int MaxInstances = 300_000;
    private const int DefaultInstances = 30_000;
    private const int GridWidth = 200;
    private const float Spacing = 3.25f;

    private RaylibBenchmarkInstance[]? _instances;
    private RaylibBenchmarkMaterialColor[]? _palette;
    private bool _sceneInstalled;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!RaylibIsmBenchmarkShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            Disable(engine);
            return Task.CompletedTask;
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
            return;
        }

        MeshAssetRegistry? meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
        if (meshes == null)
        {
            throw new InvalidOperationException("Raylib ISM benchmark showcase requires PresentationMeshAssetRegistry.");
        }

        int[] meshAssetIds = ResolveBlacksmithMeshAssetIds(meshes);
        if (meshAssetIds.Length == 0)
        {
            throw new InvalidOperationException("Raylib ISM benchmark showcase requires blacksmith mesh assets.");
        }

        _instances ??= BuildInstances(meshAssetIds);
        _palette ??= BuildPalette();

        if (!_sceneInstalled)
        {
            renderer.SetScene(new RaylibBenchmarkScene(
                enabled: true,
                instances: _instances,
                initialActiveInstanceCount: DefaultInstances,
                palette: new RaylibBenchmarkMaterialPalette(new Vector4(1f, 1f, 1f, 1f), _palette),
                camera: new RaylibBenchmarkCamera(
                    position: new Vector3(0f, 150f, 230f),
                    target: new Vector3(0f, 0f, 0f),
                    fovY: 55f),
                label: "Raylib blacksmith mesh ISM final-render benchmark"));
            _sceneInstalled = true;
        }
    }

    private void Disable(GameEngine engine)
    {
        IRaylibBenchmarkRenderer? renderer = ResolveRenderer(engine);
        renderer?.SetScene(default);
        _sceneInstalled = false;
    }

    private static IRaylibBenchmarkRenderer? ResolveRenderer(GameEngine engine)
    {
        return engine.GetService(new ServiceKey<IRaylibBenchmarkRenderer>(RaylibIsmBenchmarkShowcaseIds.RendererServiceKey));
    }

    private static int[] ResolveBlacksmithMeshAssetIds(MeshAssetRegistry meshes)
    {
        string[] keys = RaylibIsmBenchmarkShowcaseIds.BlacksmithMeshKeys;
        var ids = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            int id = meshes.GetId(keys[i]);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Raylib ISM benchmark showcase requires blacksmith mesh asset '{keys[i]}'.");
            }

            if (!meshes.TryGetDescriptor(id, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Model)
            {
                throw new InvalidOperationException(
                    $"Raylib ISM benchmark showcase requires '{keys[i]}' to be a Model mesh asset.");
            }

            ids[i] = id;
        }

        return ids;
    }

    private static RaylibBenchmarkInstance[] BuildInstances(int[] meshAssetIds)
    {
        var items = new RaylibBenchmarkInstance[MaxInstances];
        int rows = (int)MathF.Ceiling(MaxInstances / (float)GridWidth);
        float xOrigin = -((GridWidth - 1) * Spacing) * 0.5f;
        float zOrigin = -((rows - 1) * Spacing) * 0.5f;

        for (int i = 0; i < items.Length; i++)
        {
            int row = i / GridWidth;
            int col = i % GridWidth;
            int meshIndex = i % meshAssetIds.Length;
            int materialId = 7000 + meshIndex;
            float yaw = (i % 16) * (MathF.PI / 8f);
            float height = ((i / 97) % 3) * 0.05f;
            items[i] = new RaylibBenchmarkInstance(
                meshAssetId: meshAssetIds[meshIndex],
                materialId: materialId,
                position: new Vector3(xOrigin + (col * Spacing), height, zOrigin + (row * Spacing)),
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw),
                scale: ResolveScale(meshIndex, i),
                color: Vector4.One);
        }

        return items;
    }

    private static Vector3 ResolveScale(int meshIndex, int instanceIndex)
    {
        float variation = 1f + ((instanceIndex % 5) * 0.03f);
        return meshIndex == 5
            ? new Vector3(1.2f, 1.2f, 1.2f) * variation
            : new Vector3(0.8f, 0.8f, 0.8f) * variation;
    }

    private static RaylibBenchmarkMaterialColor[] BuildPalette()
    {
        return
        [
            new RaylibBenchmarkMaterialColor(7000, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7001, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7002, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7003, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7004, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7005, new Vector4(1f, 1f, 1f, 1f)),
        ];
    }

}
