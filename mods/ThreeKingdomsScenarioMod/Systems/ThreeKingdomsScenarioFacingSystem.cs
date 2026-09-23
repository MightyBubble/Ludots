using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;

namespace ThreeKingdomsScenarioMod.Systems;

internal sealed class ThreeKingdomsScenarioFacingSystem : ISystem<float>
{
    private static readonly QueryDescription FacingQuery = new QueryDescription().WithAll<WorldPositionCm, PreviousWorldPositionCm>();

    private readonly World _world;
    private readonly GameEngine _engine;

    public ThreeKingdomsScenarioFacingSystem(World world, GameEngine engine)
    {
        _world = world;
        _engine = engine;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!ThreeKingdomsScenarioIds.IsScenarioMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        _world.Query(in FacingQuery, (Entity entity, ref WorldPositionCm current, ref PreviousWorldPositionCm previous) =>
        {
            float dx = current.Value.X.ToFloat() - previous.Value.X.ToFloat();
            float dy = current.Value.Y.ToFloat() - previous.Value.Y.ToFloat();
            if (MathF.Abs(dx) < 0.01f && MathF.Abs(dy) < 0.01f)
            {
                return;
            }

            float angle = MathF.Atan2(dy, dx);
            if (_world.Has<FacingDirection>(entity))
            {
                ref var facing = ref _world.Get<FacingDirection>(entity);
                facing.AngleRad = angle;
            }
            else
            {
                _world.Add(entity, new FacingDirection { AngleRad = angle });
            }
        });
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }
}
