using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
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
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Config
{
    public delegate void ComponentSetter(Entity entity, JsonNode data);

    public static class ComponentRegistry
    {
        private static readonly Dictionary<string, ComponentSetter> _setters = new Dictionary<string, ComponentSetter>();
        private static readonly Dictionary<string, string> _registrationSource = new Dictionary<string, string>();
        private static RegistrationConflictReport _conflictReport;

        static ComponentRegistry()
        {
            Register<Position>("Position");
            Register<Velocity>("Velocity");
            Register<Health>("Health");
            Register<Name>("Name");
            Register<FacingDirection>("FacingDirection");
            Register("WorldPositionCm", SetWorldPositionCm);
            Register<Ludots.Core.Gameplay.Components.Team>("Team");
            Register<Ludots.Core.Gameplay.Components.PlayerOwner>("PlayerOwner");
            Register<Ludots.Core.Gameplay.Components.TeamIdentity>("TeamIdentity");
            Register<Ludots.Core.Gameplay.Components.TeamEntityRef>("TeamEntityRef");
            Register("EntityLayer", SetEntityLayer);
            Register("AttributeBuffer", SetAttributeBuffer);
            Register("AbilityStateBuffer", SetAbilityStateBuffer);
            Register("AbilityFormSetRef", SetAbilityFormSetRef);
            Register<ForceInput2D>("ForceInput2D");
            Register<GameplayTagContainer>("GameplayTagContainer");
            Register("OrderBuffer", SetOrderBuffer);
            Register<SelectionSelectableTag>("SelectionSelectableTag");
            Register("SelectionSelectableState", SetSelectionSelectableState);
            Register("NavActor", SetNavActor);
            Register("NavProfileRef", SetNavProfileRef);
            Register("NavCrowdProfileRef", SetNavCrowdProfileRef);
            Register("NavKnockbackPolicyRef", SetNavKnockbackPolicyRef);
            Register<BlackboardSpatialBuffer>("BlackboardSpatialBuffer");
            Register<BlackboardEntityBuffer>("BlackboardEntityBuffer");
            Register<BlackboardIntBuffer>("BlackboardIntBuffer");
            Register("AbilityExecAimSync", SetAbilityExecAimSync);
            Register<VisualTransform>("VisualTransform");
            Register("ManifestationObstacleIntent2D", SetManifestationObstacleIntent2D);
            Register("ManifestationObstaclePolygon2D", SetManifestationObstaclePolygon2D);
            Register("ManifestationMotion2D", SetManifestationMotion2D);
            Register("DestroyWhenParentExecutionEnds", SetDestroyWhenParentExecutionEnds);
        }

        public static void Register<T>(string name)
        {
            Register(name, (entity, json) =>
            {
                T component = json.Deserialize<T>(new JsonSerializerOptions { IncludeFields = true });
                entity.Add<T>(component);
            });
        }

        public static void SetConflictReport(RegistrationConflictReport report)
        {
            _conflictReport = report;
        }

        public static void Register(string name, ComponentSetter setter, string modId = null)
        {
#if DEBUG
            if (_setters.ContainsKey(name))
            {
                string existingMod = _registrationSource.TryGetValue(name, out var em) ? em : "(core)";
                string newMod = modId ?? "(core)";
                Log.Warn(in LogChannels.Config, $"Component '{name}' registered by '{existingMod}', overwritten by '{newMod}' (last-wins).");
                _conflictReport?.Add("ComponentRegistry", name, existingMod, newMod);
            }
#endif
            _setters[name] = setter;
            _registrationSource[name] = modId ?? "(core)";
        }

        public static void Apply(Entity entity, string componentName, JsonNode data)
        {
            if (data == null)
            {
                Log.Warn(in LogChannels.Config, $"Component '{componentName}' data is null, skipping.");
                return;
            }
            if (_setters.TryGetValue(componentName, out var setter))
            {
                setter(entity, data);
            }
            else
            {
                // Log warning: Unknown component
                Log.Warn(in LogChannels.Config, $"Unknown component '{componentName}'");
            }
        }

        private static void SetOrderBuffer(Entity entity, JsonNode data)
        {
            // OrderBuffer always starts empty; JSON data is ignored.
            entity.Add(OrderBuffer.CreateEmpty());
        }

        private static void SetSelectionSelectableState(Entity entity, JsonNode data)
        {
            var state = SelectionSelectableState.EnabledByDefault;
            if (data is JsonObject obj)
            {
                if ((obj.TryGetPropertyValue("IsEnabled", out var isEnabledNode) ||
                     obj.TryGetPropertyValue("isEnabled", out isEnabledNode)) &&
                    isEnabledNode != null)
                {
                    state.IsEnabled = ParseSelectionEnabled(isEnabledNode);
                }
            }
            else if (data != null)
            {
                state.IsEnabled = ParseSelectionEnabled(data);
            }

            entity.Add(state);
        }

        private static void SetNavActor(Entity entity, JsonNode data)
        {
            var actor = new NavActor
            {
                IsEnabled = 1,
                PhysicsMode = NavPhysicsMode.NavCrowdResolve,
                DefaultSolverMode = NavSolverMode.Hybrid,
            };
            if (data is JsonObject obj)
            {
                if (TryReadEnum(obj, out NavPhysicsMode physicsMode, "physicsMode", "PhysicsMode"))
                {
                    actor.PhysicsMode = physicsMode;
                }

                if (TryReadEnum(obj, out NavSolverMode solverMode, "defaultSolverMode", "DefaultSolverMode"))
                {
                    actor.DefaultSolverMode = solverMode;
                }

                if ((obj.TryGetPropertyValue("enabled", out var enabledNode) ||
                     obj.TryGetPropertyValue("Enabled", out enabledNode)) &&
                    enabledNode != null)
                {
                    actor.IsEnabled = ParseBooleanByte(enabledNode, defaultValue: 1);
                }
            }
            else if (data.GetValueKind() == JsonValueKind.True || data.GetValueKind() == JsonValueKind.False)
            {
                actor.IsEnabled = ParseBooleanByte(data, defaultValue: 1);
            }

            entity.Add(actor);
        }

        private static void SetNavProfileRef(Entity entity, JsonNode data)
        {
            entity.Add(new NavProfileRef
            {
                ProfileId = ResolveNavigationContractId(
                    data,
                    nameResolver: static catalog => catalog.NavProfileIds,
                    componentName: "NavProfileRef")
            });
        }

        private static void SetNavCrowdProfileRef(Entity entity, JsonNode data)
        {
            entity.Add(new NavCrowdProfileRef
            {
                ProfileId = ResolveNavigationContractId(
                    data,
                    nameResolver: static catalog => catalog.CrowdProfileIds,
                    componentName: "NavCrowdProfileRef")
            });
        }

        private static void SetNavKnockbackPolicyRef(Entity entity, JsonNode data)
        {
            entity.Add(new NavKnockbackPolicyRef
            {
                PolicyId = ResolveNavigationContractId(
                    data,
                    nameResolver: static catalog => catalog.KnockbackPolicyIds,
                    componentName: "NavKnockbackPolicyRef")
            });
        }

        private static void SetAbilityStateBuffer(Entity entity, JsonNode data)
        {
            var buffer = default(AbilityStateBuffer);
            if (data is JsonObject obj && obj.TryGetPropertyValue("abilityIds", out var idsNode) && idsNode is JsonArray arr)
            {
                for (int i = 0; i < arr.Count && buffer.Count < AbilityStateBuffer.CAPACITY; i++)
                {
                    var elem = arr[i];
                    if (elem == null) continue;

                    int id;
                    if (elem.GetValueKind() == JsonValueKind.String)
                    {
                        var abilityConfigId = elem.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(abilityConfigId))
                        {
                            id = 0;
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

                    if (id > 0) buffer.AddAbility(id);
                }
            }
            entity.Add(buffer);
        }

        private static byte ParseSelectionEnabled(JsonNode node)
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.Number => node.GetValue<int>() != 0 ? (byte)1 : (byte)0,
                _ => 0,
            };
        }

        private static void SetWorldPositionCm(Entity entity, JsonNode data)
        {
            int x = 0, y = 0;
            if (data is JsonObject obj)
            {
                // Support both "Value": {"X": ..., "Y": ...} and direct {"X": ..., "Y": ...}
                JsonNode valueNode = obj;
                if (obj.TryGetPropertyValue("Value", out var vNode) && vNode is JsonObject vObj)
                {
                    valueNode = vObj;
                }
                
                if (valueNode is JsonObject valObj)
                {
                    if (valObj.TryGetPropertyValue("X", out var xNode)) x = xNode?.GetValue<int>() ?? 0;
                    if (valObj.TryGetPropertyValue("Y", out var yNode)) y = yNode?.GetValue<int>() ?? 0;
                }
            }
            var fix64Pos = Fix64Vec2.FromInt(x, y);
            entity.Add(new WorldPositionCm { Value = fix64Pos });
            // Add the companion components required by interpolation, rendering, and culling.
            entity.Add(new PreviousWorldPositionCm { Value = fix64Pos });
            entity.Add(VisualTransform.Default);
            entity.Add(new CullState { IsVisible = true, LOD = LODLevel.High });
        }

        private static void SetAbilityFormSetRef(Entity entity, JsonNode data)
        {
            string? formSetName = null;
            if (data.GetValueKind() == JsonValueKind.String)
            {
                formSetName = data.GetValue<string>();
            }
            else if (data is JsonObject obj)
            {
                formSetName =
                    obj["formSetId"]?.GetValue<string>() ??
                    obj["FormSetId"]?.GetValue<string>();
            }

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
            uint category = 0;
            uint mask = uint.MaxValue; // default: interact with all layers
            if (data is JsonObject obj)
            {
                if (obj.TryGetPropertyValue("category", out var catNode) && catNode is JsonArray catArr)
                {
                    foreach (var item in catArr)
                    {
                        string name = item?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name))
                        {
                            int idx = LayerRegistry.GetIndex(name);
                            if (idx >= 0) category |= 1u << idx;
                        }
                    }
                }
                if (obj.TryGetPropertyValue("mask", out var maskNode) && maskNode is JsonArray maskArr)
                {
                    mask = 0;
                    foreach (var item in maskArr)
                    {
                        string name = item?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name))
                        {
                            int idx = LayerRegistry.GetIndex(name);
                            if (idx >= 0) mask |= 1u << idx;
                        }
                    }
                }
            }
            entity.Add(new Ludots.Core.Gameplay.Components.EntityLayer(category, mask));
        }

        private static unsafe void SetAttributeBuffer(Entity entity, JsonNode data)
        {
            var buffer = default(AttributeBuffer);
            if (data is not JsonObject obj)
            {
                entity.Add(buffer);
                return;
            }

            if (obj.TryGetPropertyValue("base", out var baseNode) && baseNode is JsonObject baseObj)
            {
                foreach (var kvp in baseObj)
                {
                    if (kvp.Value == null) continue;
                    float v = kvp.Value.GetValue<float>();
                    int attrId = AttributeRegistry.Register(kvp.Key);
                    buffer.SetBase(attrId, v);
                }
            }

            entity.Add(buffer);
        }

        private static void SetManifestationObstacleIntent2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D requires an object payload.");
            }

            var intent = new ManifestationObstacleIntent2D
            {
                Shape = ParseManifestationObstacleShape(obj["shape"]?.GetValue<string>() ?? obj["Shape"]?.GetValue<string>()),
                SinkPhysicsCollider = ParseBooleanByte(obj["sinkPhysicsCollider"] ?? obj["SinkPhysicsCollider"], defaultValue: 1),
                SinkNavigationObstacle = ParseBooleanByte(obj["sinkNavigationObstacle"] ?? obj["SinkNavigationObstacle"], defaultValue: 1),
                NavRadiusCm = ReadIntProperty(obj, "navRadiusCm", "NavRadiusCm"),
            };

            if (TryReadIntProperty(obj, out int radiusCm, "radiusCm", "RadiusCm"))
            {
                intent.RadiusCm = radiusCm;
            }

            if (TryReadIntProperty(obj, out int halfWidthCm, "halfWidthCm", "HalfWidthCm"))
            {
                intent.HalfWidthCm = halfWidthCm;
            }

            if (TryReadIntProperty(obj, out int halfHeightCm, "halfHeightCm", "HalfHeightCm"))
            {
                intent.HalfHeightCm = halfHeightCm;
            }

            if (TryReadPointProperty(obj, out var localOffset, "localOffsetCm", "LocalOffsetCm"))
            {
                intent.LocalOffsetXCm = localOffset.X;
                intent.LocalOffsetYCm = localOffset.Y;
            }
            else
            {
                intent.LocalOffsetXCm = ReadIntProperty(obj, "localOffsetXCm", "LocalOffsetXCm");
                intent.LocalOffsetYCm = ReadIntProperty(obj, "localOffsetYCm", "LocalOffsetYCm");
            }

            entity.Add(intent);
        }

        private static void SetManifestationObstaclePolygon2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationObstaclePolygon2D requires an object payload.");
            }

            JsonArray? vertices = obj["vertices"] as JsonArray ?? obj["Vertices"] as JsonArray;
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
                    throw new InvalidOperationException("ManifestationObstaclePolygon2D vertices entries must be objects with X/Y.");
                }

                polygon.SetVertex(i, new WorldCmInt2(
                    ReadIntProperty(pointObj, "x", "X"),
                    ReadIntProperty(pointObj, "y", "Y")));
            }

            entity.Add(polygon);
        }

        private static void SetAbilityExecAimSync(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityExecAimSync requires an object payload.");
            }

            entity.Add(new AbilityExecAimSync
            {
                AbilitySlot = ReadIntProperty(obj, "abilitySlot", "AbilitySlot"),
                SyncFacing = ParseBooleanByte(obj["syncFacing"] ?? obj["SyncFacing"], defaultValue: 0),
            });
        }

        private static void SetManifestationMotion2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationMotion2D requires an object payload.");
            }

            entity.Add(new ManifestationMotion2D
            {
                FollowParentPosition = ParseBooleanByte(obj["followParentPosition"] ?? obj["FollowParentPosition"], defaultValue: 1),
                FacingSource = ParseManifestationFacingSource(obj["facingSource"]?.GetValue<string>() ?? obj["FacingSource"]?.GetValue<string>()),
                SweepDegreesPerSecond = ReadFloatProperty(obj, "sweepDegreesPerSecond", "SweepDegreesPerSecond"),
                ForwardOffsetCm = ReadIntProperty(obj, "forwardOffsetCm", "ForwardOffsetCm"),
            });
        }

        private static void SetDestroyWhenParentExecutionEnds(Entity entity, JsonNode data)
        {
            entity.Add(new DestroyWhenParentExecutionEnds());
        }

        private static ManifestationObstacleShape2D ParseManifestationObstacleShape(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ManifestationObstacleShape2D.Circle;
            }

            return raw.Trim().ToLowerInvariant() switch
            {
                "circle" => ManifestationObstacleShape2D.Circle,
                "box" => ManifestationObstacleShape2D.Box,
                "polygon" => ManifestationObstacleShape2D.Polygon,
                _ => throw new InvalidOperationException($"Unsupported ManifestationObstacleIntent2D shape '{raw}'.")
            };
        }

        private static ManifestationFacingSource2D ParseManifestationFacingSource(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ManifestationFacingSource2D.None;
            }

            return raw.Trim().ToLowerInvariant() switch
            {
                "none" => ManifestationFacingSource2D.None,
                "sweepvelocity" => ManifestationFacingSource2D.SweepVelocity,
                "parentexecutiontarget" => ManifestationFacingSource2D.ParentExecutionTarget,
                _ => throw new InvalidOperationException($"Unsupported ManifestationMotion2D facingSource '{raw}'.")
            };
        }

        private static byte ParseBooleanByte(JsonNode? node, byte defaultValue)
        {
            if (node == null)
            {
                return defaultValue;
            }

            return node.GetValueKind() switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.Number => node.GetValue<int>() != 0 ? (byte)1 : (byte)0,
                _ => defaultValue,
            };
        }

        private static int ResolveNavigationContractId(
            JsonNode data,
            Func<Navigation2DContractCatalog, Ludots.Core.Registry.StringIntRegistry> nameResolver,
            string componentName)
        {
            if (data == null)
            {
                throw new InvalidOperationException($"{componentName} requires a non-null payload.");
            }

            if (data.GetValueKind() == JsonValueKind.Number)
            {
                return data.GetValue<int>();
            }

            string? name = null;
            if (data.GetValueKind() == JsonValueKind.String)
            {
                name = data.GetValue<string>();
            }
            else if (data is JsonObject obj)
            {
                name =
                    obj["id"]?.GetValue<string>() ??
                    obj["Id"]?.GetValue<string>() ??
                    obj["profile"]?.GetValue<string>() ??
                    obj["Profile"]?.GetValue<string>();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"{componentName} requires a non-empty profile id.");
            }

            var catalog = Navigation2DContractCatalogScope.RequireCurrent();
            int id = nameResolver(catalog).GetId(name.Trim());
            if (id <= 0)
            {
                throw new InvalidOperationException($"Unknown {componentName} id '{name}'.");
            }

            return id;
        }

        private static bool TryReadEnum<TEnum>(JsonObject obj, out TEnum value, params string[] names)
            where TEnum : struct
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (obj.TryGetPropertyValue(names[i], out var node) && node != null)
                {
                    if (node.GetValueKind() == JsonValueKind.String)
                    {
                        string? raw = node.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse(raw, ignoreCase: true, out value))
                        {
                            return true;
                        }
                    }
                    else if (node.GetValueKind() == JsonValueKind.Number)
                    {
                        value = (TEnum)Enum.ToObject(typeof(TEnum), node.GetValue<int>());
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static bool TryReadIntProperty(JsonObject obj, out int value, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (obj.TryGetPropertyValue(names[i], out var node) && node != null)
                {
                    value = node.GetValue<int>();
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static int ReadIntProperty(JsonObject obj, params string[] names)
        {
            return TryReadIntProperty(obj, out int value, names) ? value : 0;
        }

        private static float ReadFloatProperty(JsonObject obj, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (obj.TryGetPropertyValue(names[i], out var node) && node != null)
                {
                    return node.GetValue<float>();
                }
            }

            return 0f;
        }

        private static bool TryReadPointProperty(JsonObject obj, out WorldCmInt2 point, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (obj.TryGetPropertyValue(names[i], out var node) && node is JsonObject pointObj)
                {
                    point = new WorldCmInt2(
                        ReadIntProperty(pointObj, "x", "X"),
                        ReadIntProperty(pointObj, "y", "Y"));
                    return true;
                }
            }

            point = WorldCmInt2.Zero;
            return false;
        }
    }
}
