using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace ChampionSkillSandboxMod.Runtime
{
    internal static class ChampionSkillSandboxComponentAuthoring
    {
        public static void Register(string modId)
        {
            Ludots.Core.Config.ComponentRegistry.Register("Collider2D", SetCollider2D, modId);
            Ludots.Core.Config.ComponentRegistry.Register("PhysicsMaterial2D", SetPhysicsMaterial2D, modId);
        }

        private static void SetCollider2D(Entity entity, JsonNode data, ComponentAuthoringContext context)
        {
            JsonObject obj = RequireObject(data, "Collider2D");
            ValidateProperties(obj, "Collider2D", "shape");
            var shapeStorage = context.Require<ShapeDataStorage2D>(ComponentAuthoringServiceKeys.Physics2DShapeStorage);

            JsonObject shape = GetRequiredObject(obj, "shape", "Collider2D");
            ColliderType2D type = ParseColliderType(ReadRequiredString(shape, "type", "Collider2D.shape"));
            int shapeDataIndex = type switch
            {
                ColliderType2D.Circle => RegisterCircle(shapeStorage, shape),
                ColliderType2D.Box => RegisterBox(shapeStorage, shape),
                ColliderType2D.Polygon => RegisterPolygon(shapeStorage, shape),
                _ => throw new InvalidOperationException($"Unsupported Collider2D type '{type}'."),
            };

            entity.Add(new Collider2D
            {
                Type = type,
                ShapeDataIndex = shapeDataIndex
            });
        }

        private static void SetPhysicsMaterial2D(Entity entity, JsonNode data)
        {
            JsonObject obj = RequireObject(data, "PhysicsMaterial2D");
            ValidateProperties(obj, "PhysicsMaterial2D", "friction", "restitution", "baseDamping");

            entity.Add(new PhysicsMaterial2D
            {
                Friction = Fix64.FromFloat(ReadRequiredFloat(obj, "friction", "PhysicsMaterial2D")),
                Restitution = Fix64.FromFloat(ReadRequiredFloat(obj, "restitution", "PhysicsMaterial2D")),
                BaseDamping = Fix64.FromFloat(ReadRequiredFloat(obj, "baseDamping", "PhysicsMaterial2D"))
            });
        }

        private static ColliderType2D ParseColliderType(string type)
        {
            return type switch
            {
                "Circle" => ColliderType2D.Circle,
                "Box" => ColliderType2D.Box,
                "Polygon" => ColliderType2D.Polygon,
                _ => throw new InvalidOperationException($"Unsupported Collider2D type '{type}'. Expected Circle, Box, or Polygon."),
            };
        }

        private static int RegisterCircle(ShapeDataStorage2D shapeStorage, JsonObject shape)
        {
            ValidateProperties(shape, "Collider2D.shape Circle", "type", "radiusCm", "localCenterCm");
            return shapeStorage.RegisterCircle(
                Fix64.FromFloat(ReadRequiredFloat(shape, "radiusCm", "Collider2D.shape Circle")),
                ReadRequiredVector2(shape, "localCenterCm", "Collider2D.shape Circle"));
        }

        private static int RegisterBox(ShapeDataStorage2D shapeStorage, JsonObject shape)
        {
            ValidateProperties(shape, "Collider2D.shape Box", "type", "halfWidthCm", "halfHeightCm", "localCenterCm");
            return shapeStorage.RegisterBox(
                Fix64.FromFloat(ReadRequiredFloat(shape, "halfWidthCm", "Collider2D.shape Box")),
                Fix64.FromFloat(ReadRequiredFloat(shape, "halfHeightCm", "Collider2D.shape Box")),
                ReadRequiredVector2(shape, "localCenterCm", "Collider2D.shape Box"));
        }

        private static int RegisterPolygon(ShapeDataStorage2D shapeStorage, JsonObject shape)
        {
            ValidateProperties(shape, "Collider2D.shape Polygon", "type", "verticesCm");
            return shapeStorage.RegisterPolygon(ParsePolygonVertices(shape));
        }

        private static Fix64Vec2[] ParsePolygonVertices(JsonObject shape)
        {
            JsonNode verticesNode = RequireProperty(shape, "verticesCm", "Collider2D.shape Polygon");
            if (verticesNode is not JsonArray vertices || vertices.Count < 3)
            {
                throw new InvalidOperationException("Collider2D.shape Polygon requires at least 3 verticesCm entries.");
            }

            var result = new Fix64Vec2[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                JsonNode? vertex = vertices[i];
                if (vertex == null)
                {
                    throw new InvalidOperationException("Collider2D.shape Polygon verticesCm cannot contain null entries.");
                }

                result[i] = ParseVector2(vertex, $"Collider2D.shape Polygon.verticesCm[{i}]");
            }

            return result;
        }

        private static JsonObject GetRequiredObject(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            if (node is JsonObject child)
            {
                return child;
            }

            throw new InvalidOperationException($"{context}.{name} requires an object payload.");
        }

        private static JsonObject RequireObject(JsonNode node, string name)
        {
            if (node is JsonObject obj)
            {
                return obj;
            }

            throw new InvalidOperationException($"{name} requires an object payload.");
        }

        private static Fix64Vec2 ReadRequiredVector2(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            return ParseVector2(node, $"{context}.{name}");
        }

        private static Fix64Vec2 ParseVector2(JsonNode node, string context)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object payload.");
            }

            ValidateProperties(obj, context, "x", "y");
            return Fix64Vec2.FromFloat(
                ReadRequiredFloat(obj, "x", context),
                ReadRequiredFloat(obj, "y", context));
        }

        private static string ReadRequiredString(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            if (node.GetValueKind() != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{context}.{name} requires a string value.");
            }

            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context}.{name} requires a non-empty string value.");
            }

            return value;
        }

        private static float ReadRequiredFloat(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            if (node.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"{context}.{name} requires a numeric value.");
            }

            return node.GetValue<float>();
        }

        private static int ReadRequiredInt(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            if (node.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"{context}.{name} requires an integer value.");
            }

            return node.GetValue<int>();
        }

        private static JsonNode RequireProperty(JsonObject obj, string name, string context)
        {
            if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node == null)
            {
                throw new InvalidOperationException($"{context} requires explicit '{name}'.");
            }

            return node;
        }

        private static void ValidateProperties(JsonObject obj, string context, params string[] allowedNames)
        {
            foreach (var kvp in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(kvp.Key, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException($"{context} contains unsupported property '{kvp.Key}'.");
                }
            }
        }
    }
}
