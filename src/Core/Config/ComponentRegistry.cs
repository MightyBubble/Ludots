using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Utils;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Layers;
using Ludots.Core.Modding;
using Ludots.Core.Physics;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;

namespace Ludots.Core.Config
{
    public delegate void ComponentSetter(Entity entity, JsonNode data);

    public static class ComponentRegistry
    {
        private static readonly Dictionary<string, ComponentSetter> _setters = new Dictionary<string, ComponentSetter>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ComponentType> _componentTypes = new Dictionary<string, ComponentType>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _registrationSource = new Dictionary<string, string>(StringComparer.Ordinal);
        private static RegistrationConflictReport _conflictReport;

        static ComponentRegistry()
        {
            Register<Position>("Position");
            Register<Velocity>("Velocity");
            Register<Health>("Health");
            Register<Name>("Name");
            Register<FacingDirection>("FacingDirection");
            Register("WorldPositionCm", SetWorldPositionCm);
            Register<SpatialPartitionExcluded>("SpatialPartitionExcluded");
            Register<Ludots.Core.Gameplay.Components.Team>("Team");
            Register<Ludots.Core.Gameplay.Components.PlayerOwner>("PlayerOwner");
            Register<Ludots.Core.Gameplay.Components.TeamIdentity>("TeamIdentity");
            Register<Ludots.Core.Gameplay.Components.PlayerIdentity>("PlayerIdentity");
            Register<Ludots.Core.Gameplay.Components.TeamEntityRef>("TeamEntityRef");
            Register("EntityLayer", SetEntityLayer, null, Component<Ludots.Core.Gameplay.Components.EntityLayer>.ComponentType);
            Register("AttributeBuffer", SetAttributeBuffer);
            Register("AbilityStateBuffer", SetAbilityStateBuffer);
            Register("AbilityFormSetRef", SetAbilityFormSetRef);
            Register<ForceInput2D>("ForceInput2D");
            Register<GameplayTagContainer>("GameplayTagContainer");
            Register<TagCountContainer>("TagCountContainer");
            Register<TimedTagBuffer>("TimedTagBuffer");
            Register("OrderBuffer", SetOrderBuffer, null, Component<OrderBuffer>.ComponentType);
            Register<SelectionSelectableTag>("SelectionSelectableTag");
            Register("SelectionSelectableState", SetSelectionSelectableState, null, Component<SelectionSelectableState>.ComponentType);
            Register<SelectionDragState>("SelectionDragState");
            Register("SpatialBounds", SetSpatialBounds);
            Register("SpatialBox3D", SetSpatialBox3D);
            Register("SpatialFootprint2D", SetSpatialFootprint2D);
            Register<BlackboardSpatialBuffer>("BlackboardSpatialBuffer");
            Register<BlackboardEntityBuffer>("BlackboardEntityBuffer");
            Register<BlackboardIntBuffer>("BlackboardIntBuffer");
            Register("AbilityExecAimSync", SetAbilityExecAimSync);
            Register<VisualTransform>("VisualTransform");
            Register<VisualHeightmapSampleState>("VisualHeightmapSampleState");
            Register("PresentationStaticTransform", SetPresentationStaticTransform);
            Register<PresentationStaticHeightPending>("PresentationStaticHeightPending");
            Register("ManifestationObstacleIntent2D", SetManifestationObstacleIntent2D);
            Register("ManifestationObstaclePolygon2D", SetManifestationObstaclePolygon2D);
            Register("CompoundObstacle2D", SetCompoundObstacle2D);
            Register("ManifestationMotion2D", SetManifestationMotion2D);
            Register("DestroyWhenParentExecutionEnds", SetDestroyWhenParentExecutionEnds);
        }

        public static void Register<T>(string name, string modId = null)
        {
            Register(name, (entity, json) =>
            {
                T component;
                try
                {
                    component = json.Deserialize<T>(StrictJsonOptions.CreateExact(includeFields: true))
                        ?? throw new InvalidOperationException($"Component '{name}' failed to deserialize.");
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Component '{name}' failed strict deserialization: {ex.Message}", ex);
                }

                entity.Add<T>(component);
            }, modId, Component<T>.ComponentType);
        }

        public static void SetConflictReport(RegistrationConflictReport report)
        {
            _conflictReport = report;
        }

        public static void Register(string name, ComponentSetter setter, string modId = null)
        {
            Register(name, setter, modId, componentType: null);
        }

        private static void Register(string name, ComponentSetter setter, string modId, ComponentType? componentType)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("ComponentRegistry registration requires a non-empty component name.");
            }

            if (setter == null)
            {
                throw new InvalidOperationException($"ComponentRegistry registration '{name}' requires a setter.");
            }

            if (_setters.TryGetValue(name, out var existingSetter))
            {
                string existingMod = _registrationSource.TryGetValue(name, out var em) ? em : "(core)";
                string newMod = modId ?? "(core)";
                if (IsSameRegistration(name, existingSetter, setter, componentType, existingMod, newMod))
                {
                    return;
                }

                _conflictReport?.Add("ComponentRegistry", name, existingMod, newMod);
                throw new InvalidOperationException(
                    $"Component '{name}' already registered by '{existingMod}', cannot register duplicate from '{newMod}'.");
            }

            _setters[name] = setter;
            if (componentType.HasValue)
            {
                _componentTypes[name] = componentType.Value;
            }
            else
            {
                _componentTypes.Remove(name);
            }

            _registrationSource[name] = modId ?? "(core)";
        }

        private static bool IsSameRegistration(
            string name,
            ComponentSetter existingSetter,
            ComponentSetter newSetter,
            ComponentType? newComponentType,
            string existingMod,
            string newMod)
        {
            if (!string.Equals(existingMod, newMod, StringComparison.Ordinal))
            {
                return false;
            }

            bool hasExistingType = _componentTypes.TryGetValue(name, out var existingType);
            if (hasExistingType || newComponentType.HasValue)
            {
                return hasExistingType &&
                    newComponentType.HasValue &&
                    existingType.Equals(newComponentType.Value);
            }

            return existingSetter.Equals(newSetter);
        }

        public static int UnregisterSource(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new InvalidOperationException("ComponentRegistry unregister requires a non-empty mod id.");
            }

            if (string.Equals(modId, "(core)", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ComponentRegistry cannot unregister core component authoring.");
            }

            var names = new List<string>();
            foreach (var kvp in _registrationSource)
            {
                if (string.Equals(kvp.Value, modId, StringComparison.Ordinal))
                {
                    names.Add(kvp.Key);
                }
            }

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                _setters.Remove(name);
                _componentTypes.Remove(name);
                _registrationSource.Remove(name);
            }

            return names.Count;
        }

        public static bool TryGetComponentType(string componentName, out ComponentType componentType)
        {
            return _componentTypes.TryGetValue(componentName, out componentType);
        }

        public static IReadOnlyDictionary<string, ComponentType> GetRegisteredComponentTypes()
        {
            var ordered = new SortedDictionary<string, ComponentType>(_componentTypes, StringComparer.Ordinal);
            return new ReadOnlyDictionary<string, ComponentType>(ordered);
        }

        public static void Apply(Entity entity, string componentName, JsonNode data)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                throw new InvalidOperationException("ComponentRegistry requires a non-empty component name.");
            }

            if (data == null)
            {
                throw new InvalidOperationException($"Component '{componentName}' requires non-null data.");
            }

            if (_setters.TryGetValue(componentName, out var setter))
            {
                setter(entity, data);
                return;
            }

            throw new InvalidOperationException($"Unknown component '{componentName}'.");
        }

        private static void SetOrderBuffer(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "OrderBuffer");
            entity.Add(OrderBuffer.CreateEmpty());
        }

        private static void SetSelectionSelectableState(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("SelectionSelectableState requires an object payload.");
            }

            ValidateProperties(obj, "SelectionSelectableState", "IsEnabled");
            JsonNode isEnabledNode = RequireProperty(obj, "IsEnabled", "SelectionSelectableState");
            byte enabled = ParseSelectionEnabled(isEnabledNode, "SelectionSelectableState.IsEnabled");
            entity.Add(new SelectionSelectableState { IsEnabled = enabled });
        }

        private static void SetSpatialBounds(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("SpatialBounds requires an object payload.");
            }
            ValidateProperties(obj, "SpatialBounds", "kind", "localCenterCm", "localCenterXCm", "localCenterYCm", "localCenterZCm");

            string kindRaw = RequireStringProperty(obj, "kind", "SpatialBounds");

            var bounds = new SpatialBounds
            {
                Kind = ParseSpatialBoundsKind(kindRaw),
            };

            bool hasLocalCenter = obj.TryGetPropertyValue("localCenterCm", out _);
            bool hasSplitLocalCenter =
                obj.TryGetPropertyValue("localCenterXCm", out _) ||
                obj.TryGetPropertyValue("localCenterZCm", out _);
            if (hasLocalCenter && hasSplitLocalCenter)
            {
                throw new InvalidOperationException("SpatialBounds must author either localCenterCm or localCenterXCm/localCenterZCm, not both.");
            }

            if (TryReadPointProperty(obj, out var localCenter, "localCenterCm", "SpatialBounds.localCenterCm"))
            {
                bounds.LocalCenterXCm = localCenter.X;
                bounds.LocalCenterZCm = localCenter.Y;
            }
            else
            {
                bounds.LocalCenterXCm = ReadIntProperty(obj, "localCenterXCm", "SpatialBounds");
                bounds.LocalCenterZCm = ReadIntProperty(obj, "localCenterZCm", "SpatialBounds");
            }

            bounds.LocalCenterYCm = ReadIntProperty(obj, "localCenterYCm", "SpatialBounds");
            entity.Add(bounds);
        }

        private static void SetSpatialBox3D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("SpatialBox3D requires an object payload.");
            }
            ValidateProperties(obj, "SpatialBox3D", "halfSizeXCm", "halfSizeYCm", "halfSizeZCm");

            entity.Add(new SpatialBox3D
            {
                HalfSizeXCm = ReadIntProperty(obj, "halfSizeXCm", "SpatialBox3D"),
                HalfSizeYCm = ReadIntProperty(obj, "halfSizeYCm", "SpatialBox3D"),
                HalfSizeZCm = ReadIntProperty(obj, "halfSizeZCm", "SpatialBox3D"),
            });
        }

        private static void SetSpatialFootprint2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("SpatialFootprint2D requires an object payload.");
            }
            ValidateProperties(obj, "SpatialFootprint2D", "vertices", "polygons");

            JsonArray? polygons = obj["polygons"] as JsonArray;
            JsonArray? vertices = obj["vertices"] as JsonArray;

            var footprint = new SpatialFootprint2D();
            if (polygons != null)
            {
                if (polygons.Count == 0 || polygons.Count > SpatialFootprint2D.MaxPolygons)
                {
                    throw new InvalidOperationException($"SpatialFootprint2D polygons count must be between 1 and {SpatialFootprint2D.MaxPolygons}.");
                }

                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    if (polygons[polygonIndex] is not JsonArray polygonVertices)
                    {
                        throw new InvalidOperationException("SpatialFootprint2D polygons entries must be vertex arrays.");
                    }

                    SetFootprintPolygon(ref footprint, polygonIndex, polygonVertices);
                }
            }
            else if (vertices != null)
            {
                SetFootprintPolygon(ref footprint, 0, vertices);
            }
            else
            {
                throw new InvalidOperationException("SpatialFootprint2D requires either 'vertices' or 'polygons'.");
            }

            entity.Add(footprint);
        }

        private static void SetAbilityStateBuffer(Entity entity, JsonNode data)
        {
            var buffer = default(AbilityStateBuffer);
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityStateBuffer requires an object payload.");
            }
            ValidateProperties(obj, "AbilityStateBuffer", "abilityIds");

            if (obj.TryGetPropertyValue("abilityIds", out var idsNode))
            {
                if (idsNode is not JsonArray arr)
                {
                    throw new InvalidOperationException("AbilityStateBuffer.abilityIds requires an array.");
                }

                if (arr.Count > AbilityStateBuffer.CAPACITY)
                {
                    throw new InvalidOperationException(
                        $"AbilityStateBuffer.abilityIds accepts at most {AbilityStateBuffer.CAPACITY} entries.");
                }

                for (int i = 0; i < arr.Count; i++)
                {
                    var elem = arr[i];
                    if (elem == null)
                    {
                        throw new InvalidOperationException($"AbilityStateBuffer.abilityIds[{i}] requires a non-null value.");
                    }

                    int id;
                    if (elem.GetValueKind() == JsonValueKind.String)
                    {
                        var abilityConfigId = elem.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(abilityConfigId))
                        {
                            throw new InvalidOperationException($"AbilityStateBuffer.abilityIds[{i}] requires a non-empty ability id.");
                        }
                        else
                        {
                            id = Ludots.Core.Gameplay.GAS.Registry.AbilityIdRegistry.GetId(abilityConfigId);
                            if (id <= 0)
                            {
                                throw new InvalidOperationException($"Unknown ability id '{abilityConfigId}' in AbilityStateBuffer config.");
                            }
                        }
                    }
                    else
                    {
                        id = elem.GetValue<int>();
                    }

                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"AbilityStateBuffer.abilityIds[{i}] resolved to invalid id '{id}'.");
                    }

                    buffer.AddAbility(id);
                }
            }
            entity.Add(buffer);
        }

        private static byte ParseSelectionEnabled(JsonNode node, string context)
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => throw new InvalidOperationException($"{context} requires a boolean enabled value."),
            };
        }

        private static SpatialBoundsKind ParseSpatialBoundsKind(string value)
        {
            return value switch
            {
                "Point" => SpatialBoundsKind.Point,
                "Footprint2D" => SpatialBoundsKind.Footprint2D,
                "Box3D" => SpatialBoundsKind.Box3D,
                _ => throw new InvalidOperationException($"Unsupported SpatialBounds kind '{value}'. Expected Point, Footprint2D, or Box3D."),
            };
        }

        private static void SetFootprintPolygon(ref SpatialFootprint2D footprint, int polygonIndex, JsonArray vertices)
        {
            if (vertices.Count < 3 || vertices.Count > SpatialFootprint2D.MaxVerticesPerPolygon)
            {
                throw new InvalidOperationException(
                    $"SpatialFootprint2D polygon vertex count must be between 3 and {SpatialFootprint2D.MaxVerticesPerPolygon}.");
            }

            footprint.SetPolygonVertexCount(polygonIndex, vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i] is not JsonObject pointObj)
                {
                    throw new InvalidOperationException("SpatialFootprint2D vertices entries must be objects with x/y.");
                }

                footprint.SetVertex(
                    polygonIndex,
                    i,
                    new WorldCmInt2(
                        ReadIntProperty(pointObj, "x", "SpatialFootprint2D vertex"),
                        ReadIntProperty(pointObj, "y", "SpatialFootprint2D vertex")));
            }
        }

        private static void SetWorldPositionCm(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("WorldPositionCm requires an object payload.");
            }
            ValidateProperties(obj, "WorldPositionCm", "Value");

            JsonNode valueNode = RequireProperty(obj, "Value", "WorldPositionCm");
            if (valueNode is not JsonObject valueObj)
            {
                throw new InvalidOperationException("WorldPositionCm.Value requires an object payload.");
            }
            ValidateProperties(valueObj, "WorldPositionCm.Value", "X", "Y");

            int x = ReadIntProperty(valueObj, "X", "WorldPositionCm.Value");
            int y = ReadIntProperty(valueObj, "Y", "WorldPositionCm.Value");
            var fix64Pos = Fix64Vec2.FromInt(x, y);
            entity.Add(new WorldPositionCm { Value = fix64Pos });
            // Add the companion components required by interpolation, rendering, and culling.
            entity.Add(new PreviousWorldPositionCm { Value = fix64Pos });
            entity.Add(VisualTransform.Default);
            entity.Add(new CullState { IsVisible = false, LOD = LODLevel.Low });
        }

        private static void SetPresentationStaticTransform(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "PresentationStaticTransform");
            entity.Add(new PresentationStaticTransform());
            entity.Add(new PresentationStaticVisualPending());
            entity.Add(new PresentationStaticCullPending());
        }

        private static void SetAbilityFormSetRef(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityFormSetRef requires an object payload.");
            }

            ValidateProperties(obj, "AbilityFormSetRef", "formSetId");
            string formSetName = RequireStringProperty(obj, "formSetId", "AbilityFormSetRef");
            if (string.IsNullOrWhiteSpace(formSetName))
            {
                throw new InvalidOperationException("AbilityFormSetRef requires a non-empty formSetId.");
            }

            int formSetId = AbilityFormSetIdRegistry.GetId(formSetName);
            if (formSetId <= 0)
            {
                throw new InvalidOperationException($"Unknown ability form set id '{formSetName}'.");
            }

            entity.Add(new AbilityFormSetRef { FormSetId = formSetId });
            if (!entity.Has<AbilityFormSlotBuffer>())
            {
                entity.Add(new AbilityFormSlotBuffer());
            }
        }

        private static void SetEntityLayer(Entity entity, JsonNode data)
        {
            LayerMask layerMask = EntityLayerAuthoring.ReadLayerMask(data, "EntityLayer component");
            entity.Add(new Ludots.Core.Gameplay.Components.EntityLayer(layerMask));
        }

        private static unsafe void SetAttributeBuffer(Entity entity, JsonNode data)
        {
            var buffer = default(AttributeBuffer);
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AttributeBuffer requires an object payload.");
            }
            ValidateProperties(obj, "AttributeBuffer", "base", "current");

            if (obj.TryGetPropertyValue("base", out var baseNode))
            {
                if (baseNode is not JsonObject baseObj)
                {
                    throw new InvalidOperationException("AttributeBuffer.base requires an object payload.");
                }

                foreach (var kvp in baseObj)
                {
                    if (kvp.Value == null)
                    {
                        throw new InvalidOperationException($"AttributeBuffer.base.{kvp.Key} requires a non-null numeric value.");
                    }

                    float v = kvp.Value.GetValue<float>();
                    int attrId = AttributeRegistry.Register(kvp.Key);
                    buffer.SetBase(attrId, v);
                }
            }

            if (obj.TryGetPropertyValue("current", out var currentNode))
            {
                if (currentNode is not JsonObject currentObj)
                {
                    throw new InvalidOperationException("AttributeBuffer.current requires an object payload.");
                }

                foreach (var kvp in currentObj)
                {
                    if (kvp.Value == null)
                    {
                        throw new InvalidOperationException($"AttributeBuffer.current.{kvp.Key} requires a non-null numeric value.");
                    }

                    float v = kvp.Value.GetValue<float>();
                    int attrId = AttributeRegistry.Register(kvp.Key);
                    buffer.SetCurrent(attrId, v);
                }
            }

            var snapshot = default(AttributeLastSnapshot);
            ulong definedMask = buffer.DefinedMask;
            while (definedMask != 0UL)
            {
                int attributeId = System.Numerics.BitOperations.TrailingZeroCount(definedMask);
                definedMask &= definedMask - 1UL;
                snapshot.Values[attributeId] = buffer.GetCurrent(attributeId);
            }

            entity.Add(buffer);
            entity.Add(snapshot);
        }

        private static void SetManifestationObstacleIntent2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D requires an object payload.");
            }
            ValidateProperties(
                obj,
                "ManifestationObstacleIntent2D",
                "shape",
                "sinkPhysicsCollider",
                "sinkNavigationObstacle",
                "navRadiusCm",
                "radiusCm",
                "halfWidthCm",
                "halfHeightCm",
                "localOffsetCm",
                "localOffsetXCm",
                "localOffsetYCm");

            string shapeRaw = RequireStringProperty(obj, "shape", "ManifestationObstacleIntent2D");
            var intent = new ManifestationObstacleIntent2D
            {
                Shape = ParseManifestationObstacleShape(shapeRaw),
                SinkPhysicsCollider = ParseBooleanByte(RequireProperty(obj, "sinkPhysicsCollider", "ManifestationObstacleIntent2D"), "ManifestationObstacleIntent2D.sinkPhysicsCollider"),
                SinkNavigationObstacle = ParseBooleanByte(RequireProperty(obj, "sinkNavigationObstacle", "ManifestationObstacleIntent2D"), "ManifestationObstacleIntent2D.sinkNavigationObstacle"),
                NavRadiusCm = ReadIntProperty(obj, "navRadiusCm", "ManifestationObstacleIntent2D"),
            };

            if (intent.SinkPhysicsCollider == 0 && intent.SinkNavigationObstacle == 0)
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D requires at least one sink intent.");
            }

            if (intent.Shape == ManifestationObstacleShape2D.Circle)
            {
                RequireAbsentProperties(obj, "ManifestationObstacleIntent2D Circle", "halfWidthCm", "halfHeightCm");
                intent.RadiusCm = ReadIntProperty(obj, "radiusCm", "ManifestationObstacleIntent2D");
            }
            else if (intent.Shape == ManifestationObstacleShape2D.Box)
            {
                RequireAbsentProperties(obj, "ManifestationObstacleIntent2D Box", "radiusCm");
                intent.HalfWidthCm = ReadIntProperty(obj, "halfWidthCm", "ManifestationObstacleIntent2D");
                intent.HalfHeightCm = ReadIntProperty(obj, "halfHeightCm", "ManifestationObstacleIntent2D");
            }
            else
            {
                RequireAbsentProperties(obj, "ManifestationObstacleIntent2D Polygon", "radiusCm", "halfWidthCm", "halfHeightCm");
            }

            bool hasLocalOffset = obj.TryGetPropertyValue("localOffsetCm", out _);
            bool hasSplitLocalOffset =
                obj.TryGetPropertyValue("localOffsetXCm", out _) ||
                obj.TryGetPropertyValue("localOffsetYCm", out _);
            if (hasLocalOffset && hasSplitLocalOffset)
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D must author either localOffsetCm or localOffsetXCm/localOffsetYCm, not both.");
            }

            if (TryReadPointProperty(obj, out var localOffset, "localOffsetCm", "ManifestationObstacleIntent2D.localOffsetCm"))
            {
                intent.LocalOffsetXCm = localOffset.X;
                intent.LocalOffsetYCm = localOffset.Y;
            }
            else
            {
                intent.LocalOffsetXCm = ReadIntProperty(obj, "localOffsetXCm", "ManifestationObstacleIntent2D");
                intent.LocalOffsetYCm = ReadIntProperty(obj, "localOffsetYCm", "ManifestationObstacleIntent2D");
            }

            entity.Add(intent);
        }

        private static void SetManifestationObstaclePolygon2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationObstaclePolygon2D requires an object payload.");
            }
            ValidateProperties(obj, "ManifestationObstaclePolygon2D", "vertices");

            JsonArray? vertices = obj["vertices"] as JsonArray;
            if (vertices == null)
            {
                throw new InvalidOperationException("ManifestationObstaclePolygon2D requires a vertices array.");
            }

            if (vertices.Count < 3 || vertices.Count > ManifestationObstaclePolygon2D.MaxVertices)
            {
                throw new InvalidOperationException($"ManifestationObstaclePolygon2D vertices count must be between 3 and {ManifestationObstaclePolygon2D.MaxVertices}.");
            }

            var polygon = new ManifestationObstaclePolygon2D
            {
                VertexCount = (byte)vertices.Count,
            };

            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i] is not JsonObject pointObj)
                {
                    throw new InvalidOperationException("ManifestationObstaclePolygon2D vertices entries must be objects with x/y.");
                }

                polygon.SetVertex(i, new WorldCmInt2(
                    ReadIntProperty(pointObj, "x", "ManifestationObstaclePolygon2D vertex"),
                    ReadIntProperty(pointObj, "y", "ManifestationObstaclePolygon2D vertex")));
            }

            entity.Add(polygon);
        }

        private static void SetCompoundObstacle2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("CompoundObstacle2D requires an object payload.");
            }
            ValidateProperties(obj, "CompoundObstacle2D", "sinkPhysicsCollider", "sinkNavigationObstacle", "pieces");

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = ParseBooleanByte(RequireProperty(obj, "sinkPhysicsCollider", "CompoundObstacle2D"), "CompoundObstacle2D.sinkPhysicsCollider"),
                SinkNavigationObstacle = ParseBooleanByte(RequireProperty(obj, "sinkNavigationObstacle", "CompoundObstacle2D"), "CompoundObstacle2D.sinkNavigationObstacle"),
            };

            if (obstacle.SinkPhysicsCollider == 0 && obstacle.SinkNavigationObstacle == 0)
            {
                throw new InvalidOperationException("CompoundObstacle2D requires at least one sink intent.");
            }

            if (RequireProperty(obj, "pieces", "CompoundObstacle2D") is not JsonArray pieces)
            {
                throw new InvalidOperationException("CompoundObstacle2D.pieces requires an array.");
            }

            if (pieces.Count == 0 || pieces.Count > CompoundObstacle2D.MaxPieces)
            {
                throw new InvalidOperationException($"CompoundObstacle2D pieces count must be between 1 and {CompoundObstacle2D.MaxPieces}.");
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] is not JsonObject pieceObj)
                {
                    throw new InvalidOperationException($"CompoundObstacle2D.pieces[{i}] requires an object payload.");
                }

                SetCompoundObstaclePiece(ref obstacle, i, pieceObj);
            }

            entity.Add(obstacle);
        }

        private static void SetCompoundObstaclePiece(ref CompoundObstacle2D obstacle, int pieceIndex, JsonObject obj)
        {
            string context = $"CompoundObstacle2D.pieces[{pieceIndex}]";
            ValidateProperties(
                obj,
                context,
                "shape",
                "navRadiusCm",
                "radiusCm",
                "halfWidthCm",
                "halfHeightCm",
                "localOffsetCm",
                "localOffsetXCm",
                "localOffsetYCm",
                "vertices");

            ManifestationObstacleShape2D shape = ParseManifestationObstacleShape(
                RequireStringProperty(obj, "shape", context),
                context);

            int radiusCm = 0;
            int halfWidthCm = 0;
            int halfHeightCm = 0;
            if (shape == ManifestationObstacleShape2D.Circle)
            {
                RequireAbsentProperties(obj, $"{context} Circle", "halfWidthCm", "halfHeightCm", "vertices");
                radiusCm = ReadIntProperty(obj, "radiusCm", context);
            }
            else if (shape == ManifestationObstacleShape2D.Box)
            {
                RequireAbsentProperties(obj, $"{context} Box", "radiusCm", "vertices");
                halfWidthCm = ReadIntProperty(obj, "halfWidthCm", context);
                halfHeightCm = ReadIntProperty(obj, "halfHeightCm", context);
            }
            else
            {
                RequireAbsentProperties(obj, $"{context} Polygon", "radiusCm", "halfWidthCm", "halfHeightCm");
            }

            bool hasLocalOffset = obj.TryGetPropertyValue("localOffsetCm", out _);
            bool hasSplitLocalOffset =
                obj.TryGetPropertyValue("localOffsetXCm", out _) ||
                obj.TryGetPropertyValue("localOffsetYCm", out _);
            if (hasLocalOffset && hasSplitLocalOffset)
            {
                throw new InvalidOperationException($"{context} must author either localOffsetCm or localOffsetXCm/localOffsetYCm, not both.");
            }

            int localOffsetXCm;
            int localOffsetYCm;
            if (TryReadPointProperty(obj, out var localOffset, "localOffsetCm", $"{context}.localOffsetCm"))
            {
                localOffsetXCm = localOffset.X;
                localOffsetYCm = localOffset.Y;
            }
            else
            {
                localOffsetXCm = ReadIntProperty(obj, "localOffsetXCm", context);
                localOffsetYCm = ReadIntProperty(obj, "localOffsetYCm", context);
            }

            int navRadiusCm = ReadIntProperty(obj, "navRadiusCm", context);
            obstacle.SetPiece(
                pieceIndex,
                shape,
                radiusCm,
                halfWidthCm,
                halfHeightCm,
                localOffsetXCm,
                localOffsetYCm,
                navRadiusCm);

            if (shape == ManifestationObstacleShape2D.Polygon)
            {
                if (RequireProperty(obj, "vertices", context) is not JsonArray vertices)
                {
                    throw new InvalidOperationException($"{context}.vertices requires an array.");
                }

                if (vertices.Count < 3 || vertices.Count > CompoundObstacle2D.MaxVerticesPerPolygon)
                {
                    throw new InvalidOperationException(
                        $"{context}.vertices count must be between 3 and {CompoundObstacle2D.MaxVerticesPerPolygon}.");
                }

                obstacle.SetPolygonVertexCount(pieceIndex, vertices.Count);
                for (int i = 0; i < vertices.Count; i++)
                {
                    if (vertices[i] is not JsonObject pointObj)
                    {
                        throw new InvalidOperationException($"{context}.vertices entries must be objects with x/y.");
                    }
                    ValidateProperties(pointObj, $"{context}.vertices[{i}]", "x", "y");

                    obstacle.SetVertex(
                        pieceIndex,
                        i,
                        new WorldCmInt2(
                            ReadIntProperty(pointObj, "x", $"{context}.vertices[{i}]"),
                            ReadIntProperty(pointObj, "y", $"{context}.vertices[{i}]")));
                }
            }
        }

        private static void SetAbilityExecAimSync(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityExecAimSync requires an object payload.");
            }
            ValidateProperties(obj, "AbilityExecAimSync", "abilitySlot", "syncFacing");

            entity.Add(new AbilityExecAimSync
            {
                AbilitySlot = ReadIntProperty(obj, "abilitySlot", "AbilityExecAimSync"),
                SyncFacing = ParseBooleanByte(RequireProperty(obj, "syncFacing", "AbilityExecAimSync"), "AbilityExecAimSync.syncFacing"),
            });
        }

        private static void SetManifestationMotion2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationMotion2D requires an object payload.");
            }
            ValidateProperties(obj, "ManifestationMotion2D", "followParentPosition", "facingSource", "sweepDegreesPerSecond", "forwardOffsetCm");

            entity.Add(new ManifestationMotion2D
            {
                FollowParentPosition = ParseBooleanByte(RequireProperty(obj, "followParentPosition", "ManifestationMotion2D"), "ManifestationMotion2D.followParentPosition"),
                FacingSource = ParseManifestationFacingSource(RequireStringProperty(obj, "facingSource", "ManifestationMotion2D")),
                SweepDegreesPerSecond = ReadFloatProperty(obj, "sweepDegreesPerSecond", "ManifestationMotion2D"),
                ForwardOffsetCm = ReadIntProperty(obj, "forwardOffsetCm", "ManifestationMotion2D"),
            });
        }

        private static void SetDestroyWhenParentExecutionEnds(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "DestroyWhenParentExecutionEnds");
            entity.Add(new DestroyWhenParentExecutionEnds());
        }

        private static ManifestationObstacleShape2D ParseManifestationObstacleShape(string? raw)
        {
            return ParseManifestationObstacleShape(raw, "ManifestationObstacleIntent2D");
        }

        private static ManifestationObstacleShape2D ParseManifestationObstacleShape(string? raw, string context)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"{context} requires a non-empty shape.");
            }

            return raw switch
            {
                "Circle" => ManifestationObstacleShape2D.Circle,
                "Box" => ManifestationObstacleShape2D.Box,
                "Polygon" => ManifestationObstacleShape2D.Polygon,
                _ => throw new InvalidOperationException($"Unsupported {context} shape '{raw}'.")
            };
        }

        private static ManifestationFacingSource2D ParseManifestationFacingSource(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("ManifestationMotion2D requires a non-empty facingSource.");
            }

            return raw switch
            {
                "None" => ManifestationFacingSource2D.None,
                "SweepVelocity" => ManifestationFacingSource2D.SweepVelocity,
                "ParentExecutionTarget" => ManifestationFacingSource2D.ParentExecutionTarget,
                _ => throw new InvalidOperationException($"Unsupported ManifestationMotion2D facingSource '{raw}'.")
            };
        }

        private static byte ParseBooleanByte(JsonNode node, string context)
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => throw new InvalidOperationException($"{context} requires a boolean value."),
            };
        }

        private static bool TryReadIntProperty(JsonObject obj, out int value, string name)
        {
            if (!obj.TryGetPropertyValue(name, out var node))
            {
                value = 0;
                return false;
            }

            if (node == null)
            {
                throw new InvalidOperationException($"Property '{name}' requires a non-null integer value.");
            }

            if (node.GetValueKind() == JsonValueKind.Null)
            {
                throw new InvalidOperationException($"Property '{name}' requires a non-null integer value.");
            }

            if (node.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"Property '{name}' requires an integer value.");
            }

            value = node.GetValue<int>();
            return true;
        }

        private static void RequireEmptyObject(JsonNode data, string context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an empty object payload.");
            }

            if (obj.Count != 0)
            {
                throw new InvalidOperationException($"{context} does not accept authored fields.");
            }
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

        private static void RequireAbsentProperties(JsonObject obj, string context, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (obj.ContainsKey(names[i]))
                {
                    throw new InvalidOperationException($"{context} must not author '{names[i]}'.");
                }
            }
        }

        private static int ReadIntProperty(JsonObject obj, string name, string context)
        {
            if (TryReadIntProperty(obj, out int value, name))
            {
                return value;
            }

            throw new InvalidOperationException($"{context} requires explicit '{name}'.");
        }

        private static bool TryReadPointProperty(JsonObject obj, out WorldCmInt2 point, string name, string context)
        {
            if (!obj.TryGetPropertyValue(name, out var node))
            {
                point = WorldCmInt2.Zero;
                return false;
            }

            if (node is not JsonObject pointObj)
            {
                throw new InvalidOperationException($"{context} requires an object payload.");
            }

            ValidateProperties(pointObj, context, "x", "y");
            point = new WorldCmInt2(
                ReadIntProperty(pointObj, "x", context),
                ReadIntProperty(pointObj, "y", context));
            return true;
        }

        private static float ReadFloatProperty(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
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

        private static string RequireStringProperty(JsonObject obj, string name, string context)
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
    }
}
