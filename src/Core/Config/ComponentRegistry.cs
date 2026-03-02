using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Layers;
using Ludots.Core.Modding;
using Ludots.Core.Physics;
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
            Register("WorldPositionCm", SetWorldPositionCm);
            Register<Ludots.Core.Gameplay.Components.Team>("Team");
            Register<Ludots.Core.Gameplay.Components.PlayerOwner>("PlayerOwner");
            Register<Ludots.Core.Gameplay.Components.TeamIdentity>("TeamIdentity");
            Register<Ludots.Core.Gameplay.Components.TeamEntityRef>("TeamEntityRef");
            Register("EntityLayer", SetEntityLayer);
            Register("AttributeBuffer", SetAttributeBuffer);
            Register("AbilityStateBuffer", SetAbilityStateBuffer);
            Register<ForceInput2D>("ForceInput2D");
            Register<GameplayTagContainer>("GameplayTagContainer");
            Register("OrderBuffer", SetOrderBuffer);
            Register<BlackboardSpatialBuffer>("BlackboardSpatialBuffer");
            Register<BlackboardEntityBuffer>("BlackboardEntityBuffer");
            Register<BlackboardIntBuffer>("BlackboardIntBuffer");
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
            // OrderBuffer always starts empty — JSON data is ignored (no serializable state)
            entity.Add(OrderBuffer.CreateEmpty());
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
                    if (elem.GetValueKind() == System.Text.Json.JsonValueKind.String)
                    {
                        string tag = elem.GetValue<string>();
                        id = string.IsNullOrWhiteSpace(tag) ? 0 : Ludots.Core.Gameplay.GAS.Registry.TagRegistry.Register(tag);
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
            // 自动添加插值、渲染、剔除所需的伴生组件
            entity.Add(new PreviousWorldPositionCm { Value = fix64Pos });
            entity.Add(VisualTransform.Default);
            entity.Add(new CullState { IsVisible = true, LOD = LODLevel.High });
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
    }
}
