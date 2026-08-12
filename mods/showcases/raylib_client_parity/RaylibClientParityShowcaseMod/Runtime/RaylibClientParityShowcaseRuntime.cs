using System;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RaylibClientParityShowcaseMod.Runtime;

internal sealed class RaylibClientParityShowcaseRuntime
{
    private const int BuildingInstances = 48;
    private const int CrowdAgents = 24;
    private const int GridWidth = 8;
    private const float BuildingSpacing = 6.5f;
    private const float CrowdSpacingCm = 180f;

    private RaylibBenchmarkInstance[]? _buildingInstances;
    private RaylibBenchmarkMaterialColor[]? _palette;
    private bool _sceneInstalled;
    private bool _crowdQueued;

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

        EnsureScene(engine);
        EnsureDemoEntities(engine);
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
                initialActiveInstanceCount: BuildingInstances,
                palette: new RaylibBenchmarkMaterialPalette(new Vector4(1f, 1f, 1f, 1f), _palette),
                camera: new RaylibBenchmarkCamera(
                    position: new Vector3(0f, 28f, 42f),
                    target: new Vector3(0f, 1.5f, 0f),
                    fovY: 50f),
                label: "Raylib client parity: static ISM + GpuSkinned crowd + host albedo + vfx_unlit_tint"));
            _sceneInstalled = true;
        }
    }

    private void EnsureDemoEntities(GameEngine engine)
    {
        if (_crowdQueued)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
        {
            throw new InvalidOperationException(
                "Raylib client parity showcase requires RuntimeEntitySpawnQueue for demo entity spawn.");
        }

        MapId mapId = engine.CurrentMapSession?.MapId
            ?? throw new InvalidOperationException("Raylib client parity showcase requires an active map session.");

        EnqueueTemplate(spawnQueue, mapId, "raylib_client_parity_albedo_demo", 650f, 0f);
        EnqueueTemplate(spawnQueue, mapId, "raylib_client_parity_vfx_demo", -650f, 0f);

        int cols = 8;
        float originX = -((cols - 1) * CrowdSpacingCm) * 0.5f;
        float originY = 900f;
        for (int i = 0; i < CrowdAgents; i++)
        {
            int row = i / cols;
            int col = i % cols;
            EnqueueTemplate(
                spawnQueue,
                mapId,
                RaylibClientParityShowcaseIds.CrowdTemplateId,
                originX + (col * CrowdSpacingCm),
                originY + (row * CrowdSpacingCm));
        }

        _crowdQueued = true;
    }

    private static void EnqueueTemplate(
        RuntimeEntitySpawnQueue spawnQueue,
        MapId mapId,
        string templateId,
        float xCm,
        float yCm)
    {
        var request = new RuntimeEntitySpawnRequest
        {
            Kind = RuntimeEntitySpawnKind.Template,
            TemplateId = templateId,
            MapId = mapId,
            HasWorldPosition = 1,
            WorldPositionCm = Fix64Vec2.FromFloat(xCm, yCm),
            HasFacing = 1,
            FacingAngleRad = 0f
        };
        if (!spawnQueue.TryEnqueue(in request))
        {
            throw new InvalidOperationException(
                $"Raylib client parity showcase failed to enqueue template '{templateId}'.");
        }
    }

    private void Disable(GameEngine engine)
    {
        IRaylibBenchmarkRenderer? renderer = ResolveRenderer(engine);
        renderer?.SetScene(default);
        _sceneInstalled = false;
        _crowdQueued = false;
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
        var items = new RaylibBenchmarkInstance[BuildingInstances];
        int rows = (int)MathF.Ceiling(BuildingInstances / (float)GridWidth);
        float xOrigin = -((GridWidth - 1) * BuildingSpacing) * 0.5f;
        float zOrigin = -((rows - 1) * BuildingSpacing) * 0.5f - 8f;

        for (int i = 0; i < items.Length; i++)
        {
            int row = i / GridWidth;
            int col = i % GridWidth;
            int meshIndex = i % meshAssetIds.Length;
            int materialId = 7100 + meshIndex;
            float yaw = (i % 8) * (MathF.PI / 4f);
            items[i] = new RaylibBenchmarkInstance(
                meshAssetId: meshAssetIds[meshIndex],
                materialId: materialId,
                position: new Vector3(xOrigin + (col * BuildingSpacing), 0f, zOrigin + (row * BuildingSpacing)),
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
            new RaylibBenchmarkMaterialColor(7100, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7101, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7102, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7103, new Vector4(1f, 1f, 1f, 1f)),
            new RaylibBenchmarkMaterialColor(7104, new Vector4(1f, 1f, 1f, 1f)),
        ];
    }
}
