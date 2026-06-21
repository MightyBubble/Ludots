using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace CapabilityStandardPhysics2DMod.Runtime;

internal static class CapabilityStandardPhysics2DComponentAuthoring
{
    public static void Register(string modId)
    {
        Ludots.Core.Config.ComponentRegistry.Register("CapabilityStandardPhysics2D.RigidBody", SetRigidBody, modId);
        Ludots.Core.Config.ComponentRegistry.Register("CapabilityStandardPhysics2D.DampingField", SetDampingField, modId);
    }

    private static void SetRigidBody(Entity entity, JsonNode data, ComponentAuthoringContext context)
    {
        JsonObject obj = RequireObject(data, "CapabilityStandardPhysics2D.RigidBody");
        ValidateProperties(
            obj,
            "CapabilityStandardPhysics2D.RigidBody",
            "positionCm",
            "previousPositionCm",
            "rotationRad",
            "velocityCmPerSec",
            "angularVelocityRadPerSec",
            "inverseMass",
            "inverseInertia",
            "shape",
            "material",
            "forceCmPerSec2");

        Fix64Vec2 position = ReadRequiredVector2(obj, "positionCm", "CapabilityStandardPhysics2D.RigidBody");
        Fix64Vec2 previousPosition = obj.TryGetPropertyValue("previousPositionCm", out JsonNode? previousNode) && previousNode != null
            ? ParseVector2(previousNode, "CapabilityStandardPhysics2D.RigidBody.previousPositionCm")
            : position;
        float rotationRad = ReadFloat(obj, "rotationRad", "CapabilityStandardPhysics2D.RigidBody", 0f);
        Fix64Vec2 linearVelocity = ReadVector2(obj, "velocityCmPerSec", "CapabilityStandardPhysics2D.RigidBody", Fix64Vec2.Zero);
        float angularVelocity = ReadFloat(obj, "angularVelocityRadPerSec", "CapabilityStandardPhysics2D.RigidBody", 0f);
        float inverseMass = ReadRequiredFloat(obj, "inverseMass", "CapabilityStandardPhysics2D.RigidBody");
        float inverseInertia = ReadFloat(obj, "inverseInertia", "CapabilityStandardPhysics2D.RigidBody", 0f);

        var shapeStorage = context.Require<ShapeDataStorage2D>(ComponentAuthoringServiceKeys.Physics2DShapeStorage);
        Collider2D collider = BuildCollider(shapeStorage, GetRequiredObject(obj, "shape", "CapabilityStandardPhysics2D.RigidBody"));

        entity.Add(new Position2D { Value = position });
        entity.Add(new PreviousPosition2D { Value = previousPosition });
        entity.Add(new WorldPositionCm { Value = position });
        entity.Add(new PreviousWorldPositionCm { Value = previousPosition });
        entity.Add(new Rotation2D { Value = Fix64.FromFloat(rotationRad) });
        entity.Add(new Velocity2D
        {
            Linear = linearVelocity,
            Angular = Fix64.FromFloat(angularVelocity)
        });
        entity.Add(Mass2D.FromFloat(inverseMass, inverseInertia));
        entity.Add(collider);

        if (obj.TryGetPropertyValue("material", out JsonNode? materialNode) && materialNode != null)
        {
            entity.Add(ParseMaterial(materialNode));
        }

        if (obj.TryGetPropertyValue("forceCmPerSec2", out JsonNode? forceNode) && forceNode != null)
        {
            entity.Add(new ForceInput2D { Force = ParseVector2(forceNode, "CapabilityStandardPhysics2D.RigidBody.forceCmPerSec2") });
        }
    }

    private static void SetDampingField(Entity entity, JsonNode data, ComponentAuthoringContext context)
    {
        JsonObject obj = RequireObject(data, "CapabilityStandardPhysics2D.DampingField");
        ValidateProperties(obj, "CapabilityStandardPhysics2D.DampingField", "positionCm", "radiusCm", "dampingValue");
        Fix64Vec2 position = ReadRequiredVector2(obj, "positionCm", "CapabilityStandardPhysics2D.DampingField");

        entity.Add(new Position2D { Value = position });
        entity.Add(new PreviousPosition2D { Value = position });
        entity.Add(new WorldPositionCm { Value = position });
        entity.Add(new PreviousWorldPositionCm { Value = position });
        entity.Add(new DampingField
        {
            Radius = Fix64.FromFloat(ReadRequiredFloat(obj, "radiusCm", "CapabilityStandardPhysics2D.DampingField")),
            DampingValue = Fix64.FromFloat(ReadRequiredFloat(obj, "dampingValue", "CapabilityStandardPhysics2D.DampingField")),
        });
    }

    private static Collider2D BuildCollider(ShapeDataStorage2D shapeStorage, JsonObject shape)
    {
        string type = ReadRequiredString(shape, "type", "CapabilityStandardPhysics2D.RigidBody.shape");
        ColliderType2D colliderType = type switch
        {
            "Circle" => ColliderType2D.Circle,
            "Box" => ColliderType2D.Box,
            "Polygon" => ColliderType2D.Polygon,
            _ => throw new InvalidOperationException($"Unsupported CapabilityStandardPhysics2D.RigidBody.shape type '{type}'.")
        };

        int shapeDataIndex = colliderType switch
        {
            ColliderType2D.Circle => RegisterCircle(shapeStorage, shape),
            ColliderType2D.Box => RegisterBox(shapeStorage, shape),
            ColliderType2D.Polygon => RegisterPolygon(shapeStorage, shape),
            _ => throw new InvalidOperationException($"Unsupported collider type '{colliderType}'.")
        };

        return new Collider2D
        {
            Type = colliderType,
            ShapeDataIndex = shapeDataIndex
        };
    }

    private static int RegisterCircle(ShapeDataStorage2D shapeStorage, JsonObject shape)
    {
        ValidateProperties(shape, "CapabilityStandardPhysics2D.RigidBody.shape Circle", "type", "radiusCm", "localCenterCm");
        return shapeStorage.RegisterCircle(
            Fix64.FromFloat(ReadRequiredFloat(shape, "radiusCm", "CapabilityStandardPhysics2D.RigidBody.shape Circle")),
            ReadVector2(shape, "localCenterCm", "CapabilityStandardPhysics2D.RigidBody.shape Circle", Fix64Vec2.Zero));
    }

    private static int RegisterBox(ShapeDataStorage2D shapeStorage, JsonObject shape)
    {
        ValidateProperties(shape, "CapabilityStandardPhysics2D.RigidBody.shape Box", "type", "halfWidthCm", "halfHeightCm", "localCenterCm");
        return shapeStorage.RegisterBox(
            Fix64.FromFloat(ReadRequiredFloat(shape, "halfWidthCm", "CapabilityStandardPhysics2D.RigidBody.shape Box")),
            Fix64.FromFloat(ReadRequiredFloat(shape, "halfHeightCm", "CapabilityStandardPhysics2D.RigidBody.shape Box")),
            ReadVector2(shape, "localCenterCm", "CapabilityStandardPhysics2D.RigidBody.shape Box", Fix64Vec2.Zero));
    }

    private static int RegisterPolygon(ShapeDataStorage2D shapeStorage, JsonObject shape)
    {
        ValidateProperties(shape, "CapabilityStandardPhysics2D.RigidBody.shape Polygon", "type", "verticesCm");
        JsonNode verticesNode = RequireProperty(shape, "verticesCm", "CapabilityStandardPhysics2D.RigidBody.shape Polygon");
        if (verticesNode is not JsonArray vertices || vertices.Count < 3)
        {
            throw new InvalidOperationException("CapabilityStandardPhysics2D.RigidBody.shape Polygon requires at least 3 verticesCm entries.");
        }

        if (vertices.Count > 8)
        {
            throw new InvalidOperationException("CapabilityStandardPhysics2D.RigidBody.shape Polygon accepts at most 8 verticesCm entries.");
        }

        var parsed = new Fix64Vec2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            JsonNode? vertex = vertices[i];
            if (vertex == null)
            {
                throw new InvalidOperationException("CapabilityStandardPhysics2D.RigidBody.shape Polygon verticesCm cannot contain null entries.");
            }

            parsed[i] = ParseVector2(vertex, $"CapabilityStandardPhysics2D.RigidBody.shape Polygon.verticesCm[{i}]");
        }

        return shapeStorage.RegisterPolygon(parsed);
    }

    private static PhysicsMaterial2D ParseMaterial(JsonNode node)
    {
        JsonObject obj = RequireObject(node, "CapabilityStandardPhysics2D.RigidBody.material");
        ValidateProperties(obj, "CapabilityStandardPhysics2D.RigidBody.material", "friction", "restitution", "baseDamping");
        return new PhysicsMaterial2D
        {
            Friction = Fix64.FromFloat(ReadRequiredFloat(obj, "friction", "CapabilityStandardPhysics2D.RigidBody.material")),
            Restitution = Fix64.FromFloat(ReadRequiredFloat(obj, "restitution", "CapabilityStandardPhysics2D.RigidBody.material")),
            BaseDamping = Fix64.FromFloat(ReadRequiredFloat(obj, "baseDamping", "CapabilityStandardPhysics2D.RigidBody.material"))
        };
    }

    private static JsonObject GetRequiredObject(JsonObject obj, string name, string context)
    {
        JsonNode node = RequireProperty(obj, name, context);
        return RequireObject(node, $"{context}.{name}");
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

    private static Fix64Vec2 ReadVector2(JsonObject obj, string name, string context, Fix64Vec2 defaultValue)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node == null)
        {
            return defaultValue;
        }

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

    private static float ReadFloat(JsonObject obj, string name, string context, float defaultValue)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node == null)
        {
            return defaultValue;
        }

        if (node.GetValueKind() != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"{context}.{name} requires a numeric value.");
        }

        return node.GetValue<float>();
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
