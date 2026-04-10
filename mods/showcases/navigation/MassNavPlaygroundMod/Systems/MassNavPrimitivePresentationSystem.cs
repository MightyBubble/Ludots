using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod.Systems;

internal sealed class MassNavPrimitivePresentationSystem : ISystem<float>
{
    private static readonly QueryDescription AgentQuery = new QueryDescription()
        .WithAll<MassNavAgentTag, VisualTransform, MassNavTeam>();

    private static readonly QueryDescription BlockerQuery = new QueryDescription()
        .WithAll<MassNavBlocker, VisualTransform>();

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly MassNavSimulationRuntime _simulation;
    private int _sphereMeshId;

    public MassNavPrimitivePresentationSystem(GameEngine engine, MassNavSimulationRuntime simulation, MeshAssetRegistry meshes)
    {
        _engine = engine;
        _world = engine.World;
        _simulation = simulation;
        _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
    }

    public void Initialize()
    {
        var registry = _engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
        _sphereMeshId = registry?.GetId(WellKnownMeshKeys.Sphere) ?? _sphereMeshId;
    }

    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.MapId.Name, out var mapObj) ||
            mapObj is not string mapId ||
            !MassNavPlaygroundIds.IsPlaygroundMap(mapId))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is not PrimitiveDrawBuffer draw)
        {
            return;
        }

        foreach (ref var chunk in _world.Query(in AgentQuery))
        {
            var transforms = chunk.GetSpan<VisualTransform>();
            var teams = chunk.GetSpan<MassNavTeam>();
            for (int i = 0; i < chunk.Count; i++)
            {
                Vector4 color = teams[i].Id == 0
                    ? new Vector4(0.16f, 0.88f, 0.28f, 1f)
                    : new Vector4(0.94f, 0.22f, 0.22f, 1f);
                ref var transform = ref transforms[i];
                draw.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = _sphereMeshId,
                    Position = new Vector3(transform.Position.X, 0.25f, transform.Position.Z),
                    Scale = new Vector3(0.6f, 0.6f, 0.6f),
                    Color = color
                });
            }
        }

        ReadOnlySpan<Entity> selected = _simulation.SelectedEntities;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity entity = selected[i];
            if (!_world.IsAlive(entity) || !_world.TryGet(entity, out VisualTransform transform))
            {
                continue;
            }

            draw.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = _sphereMeshId,
                Position = new Vector3(transform.Position.X, 0.27f, transform.Position.Z),
                Scale = new Vector3(0.82f, 0.82f, 0.82f),
                Color = new Vector4(0.94f, 0.98f, 0.42f, 0.95f)
            });
        }

        foreach (ref var chunk in _world.Query(in BlockerQuery))
        {
            var transforms = chunk.GetSpan<VisualTransform>();
            for (int i = 0; i < chunk.Count; i++)
            {
                ref var transform = ref transforms[i];
                draw.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = _sphereMeshId,
                    Position = new Vector3(transform.Position.X, 0.25f, transform.Position.Z),
                    Scale = new Vector3(1.2f, 1.2f, 1.2f),
                    Color = new Vector4(0.35f, 0.55f, 1f, 1f)
                });
            }
        }
    }
}
