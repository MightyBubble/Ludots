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
using Ludots.Core.Mathematics.Shapes;

namespace Ludots.Core.Physics2D.Navigation;

public static class NavObstacleAuthoringAdapter
{
    private static readonly HashSet<string> NavigationAuthoringComponents =
        new(StringComparer.Ordinal)
        {
            "WorldPositionCm",
            "FacingDirection",
            "ManifestationObstacleIntent2D",
            "ManifestationObstaclePolygon2D",
            "CompoundObstacle2D",
        };

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
        var builder = new EntityBuilder(world, SnapshotObstacleTemplates(templates), authoringContext);
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

            EntityTemplate template = templates[entityData.Template];
            bool hasNavigationAuthoring =
                HasNavigationAuthoringComponent(template.Components) ||
                HasNavigationAuthoringComponent(entityData.Overrides);
            if (!hasNavigationAuthoring)
            {
                continue;
            }

            builder
                .UseTemplate(entityData.Template)
                .WithEntityContext(
                    $"Map '{map.Id}' entity '{ResolveEntityContextId(entityData, i)}'");
            if (entityData.Overrides != null)
            {
                foreach (KeyValuePair<string, JsonNode> kvp in entityData.Overrides)
                {
                    if (NavigationAuthoringComponents.Contains(kvp.Key))
                    {
                        builder.WithOverride(kvp.Key, kvp.Value);
                    }
                }
            }

            if (entityData.PositionXCm.HasValue != entityData.PositionYCm.HasValue)
            {
                throw new InvalidOperationException(
                    $"Map '{map.Id}' entity '{ResolveEntityContextId(entityData, i)}' authors PositionXCm/PositionYCm partially; set both or neither.");
            }

            bool hasAuthoredWorldPosition =
                template.Components.ContainsKey("WorldPositionCm") ||
                (entityData.Overrides != null && entityData.Overrides.ContainsKey("WorldPositionCm"));
            if (entityData.PositionXCm.HasValue && !hasAuthoredWorldPosition)
            {
                builder.WithOverride(
                    "WorldPositionCm",
                    new JsonObject
                    {
                        ["Value"] = new JsonObject
                        {
                            ["X"] = entityData.PositionXCm.Value,
                            ["Y"] = entityData.PositionYCm!.Value,
                        },
                    });
            }

            Entity entity = builder.Build();
            sourceNames[entity.Id] = string.IsNullOrWhiteSpace(entityData.InstanceId)
                ? $"{entityData.Template}@{i}"
                : entityData.InstanceId;
        }

        new ManifestationObstacleBridge2DSystem(world, shapeStorage).Update(0f);
        return BuildFromMaterializedWorld(world, shapeStorage, sourceNames, layerId);
    }

    private static Dictionary<string, EntityTemplate> SnapshotObstacleTemplates(
        IReadOnlyDictionary<string, EntityTemplate> templates)
    {
        var snapshot = new Dictionary<string, EntityTemplate>(templates.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, EntityTemplate> kvp in templates)
        {
            var filtered = new EntityTemplate
            {
                Id = kvp.Value.Id,
                Components = new Dictionary<string, JsonNode>(StringComparer.Ordinal),
            };

            foreach (KeyValuePair<string, JsonNode> component in kvp.Value.Components)
            {
                if (NavigationAuthoringComponents.Contains(component.Key))
                {
                    filtered.Components[component.Key] = component.Value.DeepClone();
                }
            }

            snapshot[kvp.Key] = filtered;
        }

        return snapshot;
    }

    private static bool HasNavigationAuthoringComponent(
        IReadOnlyDictionary<string, JsonNode>? components)
    {
        if (components == null)
        {
            return false;
        }

        foreach (string componentName in components.Keys)
        {
            if (NavigationAuthoringComponents.Contains(componentName))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveEntityContextId(EntitySpawnData entityData, int index)
    {
        return string.IsNullOrWhiteSpace(entityData.InstanceId)
            ? $"{entityData.Template}@{index}"
            : entityData.InstanceId;
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
        };
        for (int i = 0; i < polygon.VertexCount; i++)
        {
            Fix64Vec2 vertex = ShapeWorldTransform2D.GetPolygonWorldVertex(worldPosition, rotation, polygon, i);
            obstacle.Points.Add(new NavPointCm(vertex.X.RoundToInt(), vertex.Y.RoundToInt()));
        }

        return obstacle;
    }

    private static string ResolveId(IReadOnlyDictionary<int, string> sourceNames, int entityId, string prefix)
    {
        return sourceNames.TryGetValue(entityId, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : $"{prefix}-{entityId}";
    }
}
