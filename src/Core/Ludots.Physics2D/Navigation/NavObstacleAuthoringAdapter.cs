using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;

namespace Ludots.Core.Physics2D.Navigation;

public static class NavObstacleAuthoringAdapter
{
    public static NavObstacleSet BuildFromMapAuthoring(
        MapConfig map,
        IReadOnlyDictionary<string, EntityTemplate> templates,
        string layerId = "Ground")
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(templates);

        if (map.Entities == null)
        {
            throw new InvalidOperationException($"Map '{map.Id}' requires an explicit entities list for nav obstacle authoring.");
        }

        var shapeStorage = new ShapeDataStorage2D();
        var authoringContext = new ComponentAuthoringContext();
        authoringContext.Set(ComponentAuthoringServiceKeys.Physics2DShapeStorage, shapeStorage);
        using World world = World.Create();
        var builder = new EntityBuilder(world, SnapshotTemplates(templates), authoringContext);
        var sourceNames = new Dictionary<int, string>();

        for (int i = 0; i < map.Entities.Count; i++)
        {
            EntitySpawnData entityData = map.Entities[i]
                ?? throw new InvalidOperationException($"Map '{map.Id}' contains null entity entry at index {i}.");
            if (string.IsNullOrWhiteSpace(entityData.Template))
            {
                throw new InvalidOperationException($"Map '{map.Id}' entity[{i}] requires a template.");
            }

            if (!templates.ContainsKey(entityData.Template))
            {
                throw new InvalidOperationException(
                    $"Map '{map.Id}' entity[{i}] references unknown template '{entityData.Template}'.");
            }

            builder.UseTemplate(entityData.Template);
            if (entityData.Overrides != null)
            {
                foreach (KeyValuePair<string, JsonNode> kvp in entityData.Overrides)
                {
                    builder.WithOverride(kvp.Key, kvp.Value);
                }
            }

            Entity entity = builder.Build();
            sourceNames[entity.Id] = string.IsNullOrWhiteSpace(entityData.InstanceId)
                ? $"{entityData.Template}@{i}"
                : entityData.InstanceId;
        }

        new ManifestationObstacleBridge2DSystem(world, shapeStorage).Update(0f);
        return BuildFromMaterializedWorld(world, shapeStorage, sourceNames, layerId);
    }

    private static NavObstacleSet BuildFromMaterializedWorld(
        World world,
        ShapeDataStorage2D shapeStorage,
        IReadOnlyDictionary<int, string> sourceNames,
        string layerId)
    {
        var set = new NavObstacleSet();
        var singleQuery = new QueryDescription().WithAll<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState>();
        world.Query(in singleQuery, (Entity entity, ref WorldPositionCm position, ref ManifestationObstacleIntent2D intent, ref ManifestationObstacleBridge2DState state) =>
        {
            if (intent.SinkNavigationObstacle == 0)
            {
                return;
            }

            string id = ResolveId(sourceNames, entity.Id, "obstacle");
            set.Obstacles.Add(BuildObstacle(
                world,
                entity,
                id,
                intent.Shape,
                state.ShapeDataIndex,
                position.Value,
                shapeStorage,
                layerId));
        });

        var compoundQuery = new QueryDescription().WithAll<WorldPositionCm, CompoundObstacle2DState>();
        world.Query(in compoundQuery, (Entity entity, ref WorldPositionCm position, ref CompoundObstacle2DState state) =>
        {
            if (state.SinkNavigationObstacle == 0)
            {
                return;
            }

            string id = ResolveId(sourceNames, entity.Id, "compound-obstacle");
            for (int i = 0; i < state.PieceCount; i++)
            {
                set.Obstacles.Add(BuildObstacle(
                    world,
                    entity,
                    $"{id}.piece{i}",
                    state.GetShape(i),
                    state.GetShapeDataIndex(i),
                    position.Value,
                    shapeStorage,
                    layerId));
            }
        });

        return set;
    }

    private static NavObstacle BuildObstacle(
        World world,
        Entity entity,
        string id,
        ManifestationObstacleShape2D shape,
        int shapeDataIndex,
        Fix64Vec2 worldPosition,
        ShapeDataStorage2D shapeStorage,
        string layerId)
    {
        Fix64 rotation = world.TryGet(entity, out FacingDirection facing)
            ? Fix64.FromFloat(facing.AngleRad)
            : Fix64.Zero;
        return shape switch
        {
            ManifestationObstacleShape2D.Circle => BuildCircle(id, shapeDataIndex, worldPosition, rotation, shapeStorage, layerId),
            ManifestationObstacleShape2D.Box => BuildBox(id, shapeDataIndex, worldPosition, rotation, shapeStorage, layerId),
            ManifestationObstacleShape2D.Polygon => BuildPolygon(id, shapeDataIndex, worldPosition, rotation, shapeStorage, layerId),
            _ => throw new InvalidOperationException($"Unsupported nav obstacle shape '{shape}'.")
        };
    }

    private static NavObstacle BuildCircle(
        string id,
        int shapeDataIndex,
        Fix64Vec2 worldPosition,
        Fix64 rotation,
        ShapeDataStorage2D shapeStorage,
        string layerId)
    {
        if (!shapeStorage.TryGetCircle(shapeDataIndex, out CircleShapeData circle))
        {
            throw new InvalidOperationException($"Circle obstacle '{id}' references missing shape data index {shapeDataIndex}.");
        }

        Fix64Vec2 center = ShapeWorldTransform2D.GetCircleCenter(worldPosition, rotation, circle);
        return new NavObstacle
        {
            Id = id,
            Enabled = true,
            Kind = NavObstacleKind.Circle,
            LayerId = layerId,
            Center = new NavPointCm(center.X.RoundToInt(), center.Y.RoundToInt()),
            RadiusCm = circle.Radius.RoundToInt(),
            MinYcm = 0,
            MaxYcm = NavObstacle.DefaultPhysics2DVerticalExtentCm,
        };
    }

    private static NavObstacle BuildBox(
        string id,
        int shapeDataIndex,
        Fix64Vec2 worldPosition,
        Fix64 rotation,
        ShapeDataStorage2D shapeStorage,
        string layerId)
    {
        if (!shapeStorage.TryGetBox(shapeDataIndex, out BoxShapeData box))
        {
            throw new InvalidOperationException($"Box obstacle '{id}' references missing shape data index {shapeDataIndex}.");
        }

        Fix64Vec2 center = ShapeWorldTransform2D.GetBoxCenter(worldPosition, rotation, box);
        var corners = new[]
        {
            new Fix64Vec2(-box.HalfWidth, -box.HalfHeight),
            new Fix64Vec2(box.HalfWidth, -box.HalfHeight),
            new Fix64Vec2(box.HalfWidth, box.HalfHeight),
            new Fix64Vec2(-box.HalfWidth, box.HalfHeight),
        };
        var obstacle = new NavObstacle
        {
            Id = id,
            Enabled = true,
            Kind = NavObstacleKind.Polygon,
            LayerId = layerId,
            MinYcm = 0,
            MaxYcm = NavObstacle.DefaultPhysics2DVerticalExtentCm,
        };
        for (int i = 0; i < corners.Length; i++)
        {
            Fix64Vec2 vertex = center + ShapeWorldTransform2D.RotateLocal(corners[i], rotation);
            obstacle.Points.Add(new NavPointCm(vertex.X.RoundToInt(), vertex.Y.RoundToInt()));
        }

        return obstacle;
    }

    private static NavObstacle BuildPolygon(
        string id,
        int shapeDataIndex,
        Fix64Vec2 worldPosition,
        Fix64 rotation,
        ShapeDataStorage2D shapeStorage,
        string layerId)
    {
        if (!shapeStorage.TryGetPolygon(shapeDataIndex, out PolygonShapeData polygon))
        {
            throw new InvalidOperationException($"Polygon obstacle '{id}' references missing shape data index {shapeDataIndex}.");
        }

        var obstacle = new NavObstacle
        {
            Id = id,
            Enabled = true,
            Kind = NavObstacleKind.Polygon,
            LayerId = layerId,
            MinYcm = 0,
            MaxYcm = NavObstacle.DefaultPhysics2DVerticalExtentCm,
        };
        for (int i = 0; i < polygon.VertexCount; i++)
        {
            Fix64Vec2 vertex = ShapeWorldTransform2D.GetPolygonWorldVertex(worldPosition, rotation, polygon, i);
            obstacle.Points.Add(new NavPointCm(vertex.X.RoundToInt(), vertex.Y.RoundToInt()));
        }

        return obstacle;
    }

    private static Dictionary<string, EntityTemplate> SnapshotTemplates(IReadOnlyDictionary<string, EntityTemplate> templates)
    {
        var snapshot = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, EntityTemplate> kvp in templates)
        {
            snapshot[kvp.Key] = kvp.Value;
        }

        return snapshot;
    }

    private static string ResolveId(IReadOnlyDictionary<int, string> sourceNames, int entityId, string prefix)
    {
        return sourceNames.TryGetValue(entityId, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : $"{prefix}-{entityId}";
    }
}
