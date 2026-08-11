using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.DebugDraw;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Systems;

internal sealed class UiPlayerAggregateGraphMvpPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly UiPlayerAggregateGraphMvpRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly QueryDescription _producerQuery = new QueryDescription()
        .WithAll<Name, WorldPositionCm, AttributeBuffer, Team>();

    public UiPlayerAggregateGraphMvpPresentationSystem(
        GameEngine engine,
        UiPlayerAggregateGraphMvpRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
    {
        _engine = engine;
        _runtime = runtime;
        _debugDraw = debugDraw;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float t)
    {
    }

    public void Update(in float t)
    {
        _runtime.RefreshPanel(_engine);
        DrawProducerMarkers();
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }

    private void DrawProducerMarkers()
    {
        if (!UiPlayerAggregateGraphMvpIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        int oreId = AttributeRegistry.GetId("Showcase.Resource.Ore");
        int crystalId = AttributeRegistry.GetId("Showcase.Resource.Crystal");
        if (oreId == AttributeRegistry.InvalidId || crystalId == AttributeRegistry.InvalidId)
        {
            return;
        }

        _debugDraw.Clear();
        World world = _engine.World;
        world.Query(
            in _producerQuery,
            (Entity entity, ref Name name, ref WorldPositionCm pos, ref AttributeBuffer attrs, ref Team team) =>
            {
                if (team.Id != 1 ||
                    !attrs.HasAttribute(oreId) ||
                    !attrs.HasAttribute(crystalId))
                {
                    return;
                }

                Vector3 meters = WorldUnits.WorldCmToVisualMeters(in pos.Value);
                float x = meters.X;
                float z = meters.Z;
                float stock = attrs.GetCurrent(oreId) + attrs.GetCurrent(crystalId);
                bool offline = stock <= 0.01f;

                DebugDrawColor color = offline
                    ? new DebugDrawColor(180, 70, 70)
                    : new DebugDrawColor(80, 200, 140);

                float half = 0.85f;
                _debugDraw.Boxes.Add(new DebugDrawBox2D
                {
                    Center = new Vector2(x, z),
                    HalfWidth = half,
                    HalfHeight = half,
                    Thickness = 0.1f,
                    Color = color
                });
                _debugDraw.Boxes.Add(new DebugDrawBox2D
                {
                    Center = new Vector2(x, z),
                    HalfWidth = half * 0.55f,
                    HalfHeight = half * 0.55f,
                    Thickness = 0.08f,
                    Color = color
                });
                _debugDraw.Circles.Add(new DebugDrawCircle2D
                {
                    Center = new Vector2(x, z),
                    Radius = offline ? 0.28f : 0.42f,
                    Thickness = 0.08f,
                    Color = offline ? DebugDrawColor.Red : DebugDrawColor.Yellow
                });

                _ = entity;
                _ = name;
            });
    }
}
