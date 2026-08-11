using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace NavMeshRuntimeShowcaseMod.Systems;

/// <summary>
/// Spawns and destroys a box obstacle entity on a fixed cadence so the runtime navmesh
/// rebake path is exercised on screen: the overlay must carve a hole and restore it.
/// </summary>
public sealed class NavMeshShowcaseObstacleCycleSystem : BaseSystem<World, float>
{
    private const float CycleIntervalSeconds = 3f;
    private const int ObstacleCenterXcm = 200;
    private const int ObstacleCenterZcm = 200;
    private const int ObstacleHalfExtentCm = 50;

    private float _elapsedSeconds;
    private Entity _obstacle = Entity.Null;

    public NavMeshShowcaseObstacleCycleSystem(GameEngine engine)
        : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
    {
    }

    public override void Update(in float dt)
    {
        _elapsedSeconds += dt;
        if (_elapsedSeconds < CycleIntervalSeconds)
        {
            return;
        }

        _elapsedSeconds -= CycleIntervalSeconds;

        if (_obstacle != Entity.Null && World.IsAlive(_obstacle))
        {
            World.Destroy(_obstacle);
            _obstacle = Entity.Null;
            return;
        }

        _obstacle = World.Create(
            WorldPositionCm.FromCm(ObstacleCenterXcm, ObstacleCenterZcm),
            new ManifestationObstacleIntent2D
            {
                Shape = ManifestationObstacleShape2D.Box,
                SinkPhysicsCollider = 0,
                SinkNavigationObstacle = 1,
                HalfWidthCm = ObstacleHalfExtentCm,
                HalfHeightCm = ObstacleHalfExtentCm,
                NavRadiusCm = ObstacleHalfExtentCm
            },
            new RuntimeNavMeshStructuralObstacle());
    }
}
