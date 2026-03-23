using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Config
{
    public static class Physics2DComponentAuthoring
    {
        public static void Register(string modId)
        {
            Ludots.Core.Config.ComponentRegistry.Register("Collider2D", SetCollider2D, modId);
            Ludots.Core.Config.ComponentRegistry.Register("PhysicsMaterial2D", SetPhysicsMaterial2D, modId);
            Ludots.Core.Config.ComponentRegistry.Register("NavKinematics2D", SetNavKinematics2D, modId);
        }

        private static void SetCollider2D(Entity entity, JsonNode data)
        {
            JsonObject obj = RequireObject(data, "Collider2D");
            if (TryReadInt(obj, "shapeDataIndex", "ShapeDataIndex", out int existingShapeIndex))
            {
                entity.Add(new Collider2D
                {
                    Type = ParseColliderType(ReadRequiredString(obj, "type", "Type")),
                    ShapeDataIndex = existingShapeIndex
                });
                return;
            }

            JsonObject shape = GetRequiredObject(obj, "shape", "Shape");
            ColliderType2D type = ParseColliderType(ReadRequiredString(shape, "type", "Type"));
            Fix64Vec2 localCenter = ParseOptionalVector2(shape, "localCenterCm", "LocalCenterCm", "localCenter", "LocalCenter");
            int shapeDataIndex = type switch
            {
                ColliderType2D.Circle => ShapeDataStorage2D.RegisterCircle(
                    Fix64.FromFloat(ReadRequiredFloat(shape, "radiusCm", "RadiusCm")),
                    localCenter),
                ColliderType2D.Box => ShapeDataStorage2D.RegisterBox(
                    Fix64.FromFloat(ReadHalfExtent(shape, "halfWidthCm", "HalfWidthCm", "widthCm", "WidthCm")),
                    Fix64.FromFloat(ReadHalfExtent(shape, "halfHeightCm", "HalfHeightCm", "heightCm", "HeightCm")),
                    localCenter),
                ColliderType2D.Polygon => ShapeDataStorage2D.RegisterPolygon(ParsePolygonVertices(shape)),
                _ => throw new InvalidOperationException($"Unsupported Collider2D type '{type}'."),
            };

            entity.Add(new Collider2D
            {
                Type = type,
                ShapeDataIndex = shapeDataIndex
            });
        }

        private static void SetNavKinematics2D(Entity entity, JsonNode data)
        {
            JsonObject obj = RequireObject(data, "NavKinematics2D");
            entity.Add(new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromFloat(ReadOptionalFloat(obj, "maxSpeedCmPerSec", "MaxSpeedCmPerSec", 0f)),
                MaxAccelCmPerSec2 = Fix64.FromFloat(ReadOptionalFloat(obj, "maxAccelCmPerSec2", "MaxAccelCmPerSec2", 0f)),
                RadiusCm = Fix64.FromFloat(ReadOptionalFloat(obj, "radiusCm", "RadiusCm", 0f)),
                NeighborDistCm = Fix64.FromFloat(ReadOptionalFloat(obj, "neighborDistCm", "NeighborDistCm", 0f)),
                TimeHorizonSec = Fix64.FromFloat(ReadOptionalFloat(obj, "timeHorizonSec", "TimeHorizonSec", 0f)),
                MaxNeighbors = ReadOptionalInt(obj, "maxNeighbors", "MaxNeighbors", 0)
            });
        }

        private static void SetPhysicsMaterial2D(Entity entity, JsonNode data)
        {
            JsonObject obj = RequireObject(data, "PhysicsMaterial2D");
            entity.Add(new PhysicsMaterial2D
            {
                Friction = Fix64.FromFloat(ReadOptionalFloat(obj, "friction", "Friction", PhysicsMaterial2D.Default.Friction.ToFloat())),
                Restitution = Fix64.FromFloat(ReadOptionalFloat(obj, "restitution", "Restitution", PhysicsMaterial2D.Default.Restitution.ToFloat())),
                BaseDamping = Fix64.FromFloat(ReadOptionalFloat(obj, "baseDamping", "BaseDamping", PhysicsMaterial2D.Default.BaseDamping.ToFloat()))
            });
        }

        private static ColliderType2D ParseColliderType(string type)
        {
            return type.Trim().ToLowerInvariant() switch
            {
                "circle" => ColliderType2D.Circle,
                "box" => ColliderType2D.Box,
                "polygon" => ColliderType2D.Polygon,
                _ => throw new InvalidOperationException($"Unsupported Collider2D type '{type}'."),
            };
        }

        private static Fix64Vec2[] ParsePolygonVertices(JsonObject shape)
        {
            JsonNode? verticesNode =
                GetOptionalProperty(shape, "verticesCm") ??
                GetOptionalProperty(shape, "VerticesCm") ??
                GetOptionalProperty(shape, "vertices") ??
                GetOptionalProperty(shape, "Vertices");
            if (verticesNode is not JsonArray vertices || vertices.Count < 3)
            {
                throw new InvalidOperationException("Polygon Collider2D requires at least 3 vertices.");
            }

            var result = new Fix64Vec2[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                JsonNode? vertex = vertices[i];
                if (vertex == null)
                {
                    throw new InvalidOperationException("Polygon Collider2D vertices cannot contain null entries.");
                }

                result[i] = ParseVector2(vertex);
            }

            return result;
        }

        private static float ReadHalfExtent(JsonObject obj, string halfPrimary, string halfSecondary, string fullPrimary, string fullSecondary)
        {
            if (TryReadFloat(obj, halfPrimary, halfSecondary, out float halfExtent))
            {
                return halfExtent;
            }

            return ReadRequiredFloat(obj, fullPrimary, fullSecondary) * 0.5f;
        }

        private static Fix64Vec2 ParseVector2(JsonNode node)
        {
            JsonObject obj = UnwrapObject(RequireObject(node, "vector2"));
            return Fix64Vec2.FromFloat(
                ReadRequiredFloat(obj, "x", "X"),
                ReadRequiredFloat(obj, "y", "Y"));
        }

        private static Fix64Vec2 ParseOptionalVector2(JsonObject obj, string primary, string secondary, string fallbackPrimary, string fallbackSecondary)
        {
            JsonNode? node =
                GetOptionalProperty(obj, primary) ??
                GetOptionalProperty(obj, secondary) ??
                GetOptionalProperty(obj, fallbackPrimary) ??
                GetOptionalProperty(obj, fallbackSecondary);
            return node != null ? ParseVector2(node) : Fix64Vec2.Zero;
        }

        private static JsonObject GetRequiredObject(JsonObject obj, string primary, string secondary)
        {
            if (!TryGetProperty(obj, primary, secondary, out JsonNode? node))
            {
                throw new InvalidOperationException($"Missing required object property '{primary}'.");
            }

            return RequireObject(node!, primary);
        }

        private static JsonObject RequireObject(JsonNode node, string name)
        {
            if (node is JsonObject obj)
            {
                return obj;
            }

            throw new InvalidOperationException($"{name} requires an object payload.");
        }

        private static JsonObject UnwrapObject(JsonObject obj)
        {
            if (TryGetProperty(obj, "Value", "value", out JsonNode? valueNode) &&
                valueNode is JsonObject valueObj)
            {
                return valueObj;
            }

            return obj;
        }

        private static string ReadRequiredString(JsonObject obj, string primary, string secondary)
        {
            if (TryReadString(obj, primary, secondary, out string? value))
            {
                return value!;
            }

            throw new InvalidOperationException($"Missing required property '{primary}'.");
        }

        private static float ReadRequiredFloat(JsonObject obj, string primary, string secondary)
        {
            if (TryReadFloat(obj, primary, secondary, out float value))
            {
                return value;
            }

            throw new InvalidOperationException($"Missing required numeric property '{primary}'.");
        }

        private static float ReadOptionalFloat(JsonObject obj, string primary, string secondary, float fallback)
        {
            return TryReadFloat(obj, primary, secondary, out float value) ? value : fallback;
        }

        private static int ReadOptionalInt(JsonObject obj, string primary, string secondary, int fallback)
        {
            return TryReadInt(obj, primary, secondary, out int value) ? value : fallback;
        }

        private static bool TryReadString(JsonObject obj, string primary, string secondary, out string? value)
        {
            value = null;
            if (!TryGetProperty(obj, primary, secondary, out JsonNode? node) || node == null)
            {
                return false;
            }

            if (node.GetValueKind() == JsonValueKind.String)
            {
                value = node.GetValue<string>();
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static bool TryReadFloat(JsonObject obj, string primary, string secondary, out float value)
        {
            value = 0f;
            if (!TryGetProperty(obj, primary, secondary, out JsonNode? node) || node == null)
            {
                return false;
            }

            return TryParseFloat(node, out value);
        }

        private static bool TryReadInt(JsonObject obj, string primary, string secondary, out int value)
        {
            value = 0;
            if (!TryGetProperty(obj, primary, secondary, out JsonNode? node) || node == null)
            {
                return false;
            }

            return TryParseInt(node, out value);
        }

        private static bool TryParseFloat(JsonNode node, out float value)
        {
            JsonValueKind kind = node.GetValueKind();
            if (kind == JsonValueKind.Number)
            {
                value = node.GetValue<float>();
                return true;
            }

            if (kind == JsonValueKind.String &&
                float.TryParse(node.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                value = parsed;
                return true;
            }

            value = 0f;
            return false;
        }

        private static bool TryParseInt(JsonNode node, out int value)
        {
            JsonValueKind kind = node.GetValueKind();
            if (kind == JsonValueKind.Number)
            {
                value = node.GetValue<int>();
                return true;
            }

            if (kind == JsonValueKind.String &&
                int.TryParse(node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                value = parsed;
                return true;
            }

            value = 0;
            return false;
        }

        private static JsonNode? GetOptionalProperty(JsonObject obj, string name)
        {
            return obj.TryGetPropertyValue(name, out JsonNode? node) ? node : null;
        }

        private static bool TryGetProperty(JsonObject obj, string primary, string secondary, out JsonNode? node)
        {
            if (obj.TryGetPropertyValue(primary, out node) && node != null)
            {
                return true;
            }

            if (obj.TryGetPropertyValue(secondary, out node) && node != null)
            {
                return true;
            }

            node = null;
            return false;
        }
    }
}
