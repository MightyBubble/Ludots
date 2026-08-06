using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Authoring;

public static class Physics2DTemplateAuthoring
{
    public static void RegisterRigidBody(string componentName, string modId)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new InvalidOperationException("Physics2D rigid body authoring requires a component name.");
        }

        Ludots.Core.Config.ComponentRegistry.Register(componentName, (entity, data, context) => SetRigidBody(entity, data, context, componentName), modId);
    }

    public static void RegisterDampingField(string componentName, string modId)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new InvalidOperationException("Physics2D damping field authoring requires a component name.");
        }

        Ludots.Core.Config.ComponentRegistry.Register(componentName, (entity, data, context) => SetDampingField(entity, data, componentName), modId);
    }

    private static void SetRigidBody(Entity entity, JsonNode data, ComponentAuthoringContext context, string componentName)
    {
        JsonObject obj = RequireObject(data, componentName);
        ValidateProperties(
            obj,
            componentName,
            "positionCm",
            "previousPositionCm",
            "rotationRad",
            "velocityCmPerSec",
            "angularVelocityRadPerSec",
            "bodyType",
            "inverseMass",
            "inverseInertia",
            "shape",
            "material",
            "forceCmPerSec2");

        Fix64Vec2 position = ReadRequiredVector2(obj, "positionCm", componentName);
        Fix64Vec2 previousPosition = obj.TryGetPropertyValue("previousPositionCm", out JsonNode? previousNode) && previousNode != null
            ? ParseVector2(previousNode, $"{componentName}.previousPositionCm")
            : position;
        float rotationRad = ReadFloat(obj, "rotationRad", componentName, 0f);
        Fix64Vec2 linearVelocity = ReadVector2(obj, "velocityCmPerSec", componentName, Fix64Vec2.Zero);
        float angularVelocity = ReadFloat(obj, "angularVelocityRadPerSec", componentName, 0f);
        Mass2D mass = ParseBodyMass(obj, componentName);
        if (mass.IsKinematic &&
            (obj.ContainsKey("velocityCmPerSec") || obj.ContainsKey("angularVelocityRadPerSec")))
        {
            throw new InvalidOperationException(
                $"{componentName} bodyType 'Kinematic' forbids authored velocity: Velocity2D is derived by physics from submitted target poses.");
        }

        var shapeStorage = context.Require<ShapeDataStorage2D>(ComponentAuthoringServiceKeys.Physics2DShapeStorage);
        Collider2D collider = BuildCollider(shapeStorage, GetRequiredObject(obj, "shape", componentName), componentName);

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
        entity.Add(mass);
        entity.Add(collider);

        if (obj.TryGetPropertyValue("material", out JsonNode? materialNode) && materialNode != null)
        {
            entity.Add(ParseMaterial(materialNode, componentName));
        }

        if (obj.TryGetPropertyValue("forceCmPerSec2", out JsonNode? forceNode) && forceNode != null)
        {
            if (mass.IsKinematic)
            {
                throw new InvalidOperationException(
                    $"{componentName}.forceCmPerSec2 is a contract error for bodyType 'Kinematic': kinematic bodies must be driven by target poses, never by forces.");
            }

            entity.Add(new ForceInput2D { Force = ParseVector2(forceNode, $"{componentName}.forceCmPerSec2") });
        }
    }

    private static Mass2D ParseBodyMass(JsonObject obj, string componentName)
    {
        float inverseInertia = ReadFloat(obj, "inverseInertia", componentName, 0f);
        if (!obj.TryGetPropertyValue("bodyType", out JsonNode? bodyTypeNode) || bodyTypeNode == null)
        {
            // No explicit bodyType keeps the pre-#732 contract: inverseMass alone decides static (0) vs dynamic (>0).
            return Mass2D.FromFloat(ReadRequiredFloat(obj, "inverseMass", componentName), inverseInertia);
        }

        string bodyType = bodyTypeNode.GetValueKind() == JsonValueKind.String
            ? bodyTypeNode.GetValue<string>()
            : throw new InvalidOperationException($"{componentName}.bodyType requires a string value.");
        float inverseMass = ReadFloat(obj, "inverseMass", componentName, 0f);
        switch (bodyType)
        {
            case "Static":
                if (inverseMass != 0f)
                {
                    throw new InvalidOperationException(
                        $"{componentName}.bodyType 'Static' requires inverseMass to be absent or 0, got {inverseMass}.");
                }

                return new Mass2D { InverseMass = Fix64.Zero, InverseInertia = Fix64.FromFloat(inverseInertia) };
            case "Dynamic":
                if (!(ReadRequiredFloat(obj, "inverseMass", componentName) > 0f))
                {
                    throw new InvalidOperationException(
                        $"{componentName}.bodyType 'Dynamic' requires explicit inverseMass > 0.");
                }

                return Mass2D.FromFloat(ReadRequiredFloat(obj, "inverseMass", componentName), inverseInertia);
            case "Kinematic":
                if (inverseMass != 0f)
                {
                    throw new InvalidOperationException(
                        $"{componentName}.bodyType 'Kinematic' requires inverseMass to be absent or 0, got {inverseMass}.");
                }

                if (inverseInertia != 0f)
                {
                    throw new InvalidOperationException(
                        $"{componentName}.bodyType 'Kinematic' requires inverseInertia to be absent or 0, got {inverseInertia}.");
                }

                return Mass2D.Kinematic;
            default:
                throw new InvalidOperationException(
                    $"{componentName}.bodyType has unsupported value '{bodyType}'. Allowed values: Static, Dynamic, Kinematic.");
        }
    }

    private static void SetDampingField(Entity entity, JsonNode data, string componentName)
    {
        JsonObject obj = RequireObject(data, componentName);
        ValidateProperties(obj, componentName, "positionCm", "radiusCm", "dampingValue");
        Fix64Vec2 position = ReadRequiredVector2(obj, "positionCm", componentName);

        entity.Add(new Position2D { Value = position });
        entity.Add(new PreviousPosition2D { Value = position });
        entity.Add(new WorldPositionCm { Value = position });
        entity.Add(new PreviousWorldPositionCm { Value = position });
        entity.Add(new DampingField
        {
            Radius = Fix64.FromFloat(ReadRequiredFloat(obj, "radiusCm", componentName)),
            DampingValue = Fix64.FromFloat(ReadRequiredFloat(obj, "dampingValue", componentName)),
        });
    }

    private static Collider2D BuildCollider(ShapeDataStorage2D shapeStorage, JsonObject shape, string componentName)
    {
        string context = $"{componentName}.shape";
        string type = ReadRequiredString(shape, "type", context);
        ColliderType2D colliderType = type switch
        {
            "Circle" => ColliderType2D.Circle,
            "Box" => ColliderType2D.Box,
            "Polygon" => ColliderType2D.Polygon,
            _ => throw new InvalidOperationException($"Unsupported {context} type '{type}'.")
        };

        int shapeDataIndex = colliderType switch
        {
            ColliderType2D.Circle => RegisterCircle(shapeStorage, shape, context),
            ColliderType2D.Box => RegisterBox(shapeStorage, shape, context),
            ColliderType2D.Polygon => RegisterPolygon(shapeStorage, shape, context),
            _ => throw new InvalidOperationException($"Unsupported collider type '{colliderType}'.")
        };

        return new Collider2D
        {
            Type = colliderType,
            ShapeDataIndex = shapeDataIndex
        };
    }

    private static int RegisterCircle(ShapeDataStorage2D shapeStorage, JsonObject shape, string context)
    {
        ValidateProperties(shape, $"{context} Circle", "type", "radiusCm", "localCenterCm");
        return shapeStorage.RegisterCircle(
            Fix64.FromFloat(ReadRequiredFloat(shape, "radiusCm", $"{context} Circle")),
            ReadVector2(shape, "localCenterCm", $"{context} Circle", Fix64Vec2.Zero));
    }

    private static int RegisterBox(ShapeDataStorage2D shapeStorage, JsonObject shape, string context)
    {
        ValidateProperties(shape, $"{context} Box", "type", "halfWidthCm", "halfHeightCm", "localCenterCm");
        return shapeStorage.RegisterBox(
            Fix64.FromFloat(ReadRequiredFloat(shape, "halfWidthCm", $"{context} Box")),
            Fix64.FromFloat(ReadRequiredFloat(shape, "halfHeightCm", $"{context} Box")),
            ReadVector2(shape, "localCenterCm", $"{context} Box", Fix64Vec2.Zero));
    }

    private static int RegisterPolygon(ShapeDataStorage2D shapeStorage, JsonObject shape, string context)
    {
        ValidateProperties(shape, $"{context} Polygon", "type", "verticesCm");
        JsonNode verticesNode = RequireProperty(shape, "verticesCm", $"{context} Polygon");
        if (verticesNode is not JsonArray vertices || vertices.Count < 3)
        {
            throw new InvalidOperationException($"{context} Polygon requires at least 3 verticesCm entries.");
        }

        if (vertices.Count > 8)
        {
            throw new InvalidOperationException($"{context} Polygon accepts at most 8 verticesCm entries.");
        }

        var parsed = new Fix64Vec2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            JsonNode? vertex = vertices[i];
            if (vertex == null)
            {
                throw new InvalidOperationException($"{context} Polygon verticesCm cannot contain null entries.");
            }

            parsed[i] = ParseVector2(vertex, $"{context} Polygon.verticesCm[{i}]");
        }

        return shapeStorage.RegisterPolygon(parsed);
    }

    private static PhysicsMaterial2D ParseMaterial(JsonNode node, string componentName)
    {
        JsonObject obj = RequireObject(node, $"{componentName}.material");
        ValidateProperties(obj, $"{componentName}.material", "friction", "restitution", "baseDamping");
        return new PhysicsMaterial2D
        {
            Friction = Fix64.FromFloat(ReadRequiredFloat(obj, "friction", $"{componentName}.material")),
            Restitution = Fix64.FromFloat(ReadRequiredFloat(obj, "restitution", $"{componentName}.material")),
            BaseDamping = Fix64.FromFloat(ReadRequiredFloat(obj, "baseDamping", $"{componentName}.material"))
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
