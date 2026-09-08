using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using ComponentRegistry = Ludots.Core.Config.ComponentRegistry;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Downstream physics component authoring (#1480): Collider2D and the static
    /// body state live in this assembly, below Core, so their template setters
    /// register here through <see cref="ComponentRegistry.RegisterAuthoring"/> —
    /// GameEngine invokes this once at physics install (the optional-assembly
    /// reflection seam the simulation system itself uses). Shape data registers
    /// into the shared <see cref="ShapeDataStorage2D"/> authoring-context service.
    /// </summary>
    public static class Physics2DComponentAuthoring
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            ComponentRegistry.RegisterAuthoring<Collider2D>("Collider2D", SetCollider2D);
            ComponentRegistry.RegisterAuthoring<Physics2DStaticBodyState>("Physics2DStaticBodyState", SetPhysics2DStaticBodyState);
        }

        private static void SetCollider2D(Entity entity, JsonNode data, ComponentAuthoringContext context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("Collider2D requires an object payload.");
            }

            foreach (var kvp in obj)
            {
                bool known = kvp.Key is "shape" or "radiusCm" or "halfWidthCm" or "halfHeightCm" or
                    "localCenterXCm" or "localCenterYCm" or "points";
                if (!known)
                {
                    throw new InvalidOperationException($"Collider2D contains unsupported property '{kvp.Key}'.");
                }
            }

            ShapeDataStorage2D storage = context.Require<ShapeDataStorage2D>(ComponentAuthoringServiceKeys.Physics2DShapeStorage);
            string shape = RequireString(obj, "shape");
            ColliderType2D type;
            int shapeDataIndex;
            switch (shape)
            {
                case "circle":
                    shapeDataIndex = storage.RegisterCircle(RequirePositiveFloat(obj, "radiusCm"), ReadLocalCenter(obj));
                    type = ColliderType2D.Circle;
                    break;
                case "box":
                    shapeDataIndex = storage.RegisterBox(
                        RequirePositiveFloat(obj, "halfWidthCm"),
                        RequirePositiveFloat(obj, "halfHeightCm"),
                        ReadLocalCenter(obj));
                    type = ColliderType2D.Box;
                    break;
                case "polygon":
                {
                    if (obj["points"] is not JsonArray points)
                    {
                        throw new InvalidOperationException("Collider2D polygon requires a 'points' array.");
                    }

                    if (points.Count is < 3 or > 8)
                    {
                        throw new InvalidOperationException(
                            $"Collider2D.points needs 3..8 vertices (got {points.Count}; physics polygon storage caps at 8).");
                    }

                    var vertices = new Fix64Vec2[points.Count];
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (points[i] is not JsonArray pair || pair.Count != 2)
                        {
                            throw new InvalidOperationException($"Collider2D.points[{i}] must be an [x, y] pair of numbers.");
                        }

                        vertices[i] = new Fix64Vec2(
                            Fix64.FromFloat(ReadFiniteFloat(pair[0], $"Collider2D.points[{i}][0]")),
                            Fix64.FromFloat(ReadFiniteFloat(pair[1], $"Collider2D.points[{i}][1]")));
                    }

                    shapeDataIndex = storage.RegisterPolygon(vertices);
                    type = ColliderType2D.Polygon;
                    break;
                }

                default:
                    throw new InvalidOperationException($"Collider2D.shape '{shape}' is not one of 'circle', 'box', 'polygon'.");
            }

            if (shapeDataIndex is < 0 or > byte.MaxValue)
            {
                throw new InvalidOperationException($"Collider2D shape slot {shapeDataIndex} exceeds the Collider2D byte slot range.");
            }

            if (entity.Has<Collider2D>())
            {
                entity.Set(new Collider2D { Type = type, ShapeDataIndex = shapeDataIndex });
            }
            else
            {
                entity.Add(new Collider2D { Type = type, ShapeDataIndex = shapeDataIndex });
            }
        }

        /// <summary>Static body authoring also stamps the initial cache-dirty marker.</summary>
        private static void SetPhysics2DStaticBodyState(Entity entity, JsonNode data, ComponentAuthoringContext context)
        {
            if (data is not JsonObject)
            {
                throw new InvalidOperationException("Physics2DStaticBodyState requires an object payload.");
            }

            if (entity.Has<Physics2DStaticBodyState>())
            {
                entity.Set(new Physics2DStaticBodyState());
            }
            else
            {
                entity.Add(new Physics2DStaticBodyState());
            }

            if (!entity.Has<Physics2DStaticBodyDirty>())
            {
                entity.Add(new Physics2DStaticBodyDirty());
            }
        }

        private static string RequireString(JsonObject obj, string field)
        {
            if (obj.TryGetPropertyValue(field, out JsonNode? node) && node is JsonValue v && v.TryGetValue<string>(out string? text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            throw new InvalidOperationException($"Collider2D.{field} requires a non-empty string.");
        }

        private static Fix64 RequirePositiveFloat(JsonObject obj, string field)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) ||
                node is not JsonValue v ||
                !v.TryGetValue<float>(out float raw) ||
                !float.IsFinite(raw) ||
                raw <= 0f)
            {
                throw new InvalidOperationException($"Collider2D.{field} requires a positive finite number.");
            }

            return Fix64.FromFloat(raw);
        }

        private static Fix64Vec2 ReadLocalCenter(JsonObject obj)
        {
            bool hasX = obj.TryGetPropertyValue("localCenterXCm", out _);
            bool hasY = obj.TryGetPropertyValue("localCenterYCm", out _);
            if (hasX != hasY)
            {
                throw new InvalidOperationException("Collider2D local center requires both localCenterXCm and localCenterYCm.");
            }

            if (!hasX)
            {
                return Fix64Vec2.Zero;
            }

            return new Fix64Vec2(
                Fix64.FromFloat(ReadFiniteFloat(obj["localCenterXCm"], "Collider2D.localCenterXCm")),
                Fix64.FromFloat(ReadFiniteFloat(obj["localCenterYCm"], "Collider2D.localCenterYCm")));
        }

        private static float ReadFiniteFloat(JsonNode? node, string context)
        {
            if (node is not JsonValue value ||
                !value.TryGetValue<float>(out float raw) ||
                !float.IsFinite(raw))
            {
                throw new InvalidOperationException($"{context} requires a finite number.");
            }

            return raw;
        }
    }
}
