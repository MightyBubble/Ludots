using System;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;

namespace Ludots.Core.Scripting
{
    /// <summary>
    /// Machine-readable SSOT for "which named, typed parameters does an event carry".
    /// Built-in schemas are declared once here, reference <see cref="MapTriggerEventPayloadKeys"/>
    /// constants (never string copies), and are cross-checked at construction: every
    /// payload key constant must be either referenced by a schema, carried by a
    /// dynamic-name bridge family, or listed as pending with its owning slice.
    /// Mod custom events extend the same registry from Events/custom_events.json.
    /// </summary>
    public sealed class EventSchemaRegistry
    {
        /// <summary>
        /// Payload keys stuffed by dynamic-name bridge families (Gas.Event.* tag bridge,
        /// ability/effect moment bridge); per-name schemas are not enumerable at build time.
        /// </summary>
        private static readonly string[] DynamicBridgePayloadKeys =
        {
            MapTriggerEventPayloadKeys.SourceEntity,
            MapTriggerEventPayloadKeys.TargetEntity,
            MapTriggerEventPayloadKeys.TagId,
            MapTriggerEventPayloadKeys.Magnitude,
            MapTriggerEventPayloadKeys.AbilityId,
            MapTriggerEventPayloadKeys.EffectId,
            MapTriggerEventPayloadKeys.Moment,
        };

        /// <summary>
        /// Keys whose owning event is not yet schema-bound; each entry names the slice
        /// that will claim it. Shrinks to zero as PhaseChanged (#1113) and the float
        /// phase contract land.
        /// </summary>
        private static readonly (string Key, string Owner)[] PendingPayloadKeys =
        {
            (MapTriggerEventPayloadKeys.VarName, "PhaseChanged (#1113)"),
            (MapTriggerEventPayloadKeys.Phase, "PhaseChanged (#1113)"),
            (MapTriggerEventPayloadKeys.VarValueInt, "PhaseChanged (#1113)"),
            (MapTriggerEventPayloadKeys.VarValueFloat, "float phase slice of the map variable contract"),
        };

        private static readonly EventSchema[] BuiltinSchemas =
        {
            new(GameEvents.MapHeartbeat.Value, EventScope.Map, new EventParamSchema[]
            {
                new("heartbeatIndex", EventParamType.Int, MapTriggerEventPayloadKeys.HeartbeatIndex),
            }),
            new(GameEvents.EntitySpawned.Value, EventScope.Map, new EventParamSchema[]
            {
                new("sourceEntity", EventParamType.Entity, MapTriggerEventPayloadKeys.SourceEntity),
                new("sourceTeamId", EventParamType.Int, MapTriggerEventPayloadKeys.SourceTeamId),
            }),
            new(GameEvents.EntityDied.Value, EventScope.Map, new EventParamSchema[]
            {
                new("sourceEntity", EventParamType.Entity, MapTriggerEventPayloadKeys.SourceEntity),
                new("sourceTeamId", EventParamType.Int, MapTriggerEventPayloadKeys.SourceTeamId),
            }),
            new(GameEvents.EntityAliveCountChanged.Value, EventScope.Map, new EventParamSchema[]
            {
                new("sourceTeamId", EventParamType.Int, MapTriggerEventPayloadKeys.SourceTeamId),
                new("count", EventParamType.Int, MapTriggerEventPayloadKeys.Count),
                new("delta", EventParamType.Int, MapTriggerEventPayloadKeys.Delta),
            }),
            new(GameEvents.RegionEntered.Value, EventScope.Map, new EventParamSchema[]
            {
                new("sourceEntity", EventParamType.Entity, MapTriggerEventPayloadKeys.SourceEntity),
                new("regionId", EventParamType.String, MapTriggerEventPayloadKeys.RegionId),
            }),
            new(GameEvents.RegionExited.Value, EventScope.Map, new EventParamSchema[]
            {
                new("sourceEntity", EventParamType.Entity, MapTriggerEventPayloadKeys.SourceEntity),
                new("regionId", EventParamType.String, MapTriggerEventPayloadKeys.RegionId),
            }),
            new(GameEvents.InputActionFired.Value, EventScope.Map, new EventParamSchema[]
            {
                new("sourceEntity", EventParamType.Entity, MapTriggerEventPayloadKeys.SourceEntity),
                new("inputAction", EventParamType.String, MapTriggerEventPayloadKeys.InputAction),
                new("groundXCm", EventParamType.Float, MapTriggerEventPayloadKeys.GroundXCm),
                new("groundYCm", EventParamType.Float, MapTriggerEventPayloadKeys.GroundYCm),
                new("targetEntity", EventParamType.Entity, MapTriggerEventPayloadKeys.TargetEntity, Optional: true),
            }),
        };

        private readonly Dictionary<string, EventSchema> _schemas;

        public EventSchemaRegistry()
        {
            _schemas = new Dictionary<string, EventSchema>(StringComparer.Ordinal);
            for (int i = 0; i < BuiltinSchemas.Length; i++)
            {
                _schemas.Add(BuiltinSchemas[i].EventName, BuiltinSchemas[i]);
            }

            AssertNoOrphanPayloadKeys();
        }

        public IReadOnlyCollection<EventSchema> All => _schemas.Values;

        public bool TryGet(string eventName, out EventSchema schema)
        {
            return _schemas.TryGetValue(eventName, out schema!);
        }

        /// <summary>
        /// Registers a mod-declared event schema. Fail closed: names must not collide with
        /// any existing schema, and payload keys must be dot-namespaced outside the reserved
        /// MapTrigger namespace (built-in keys are never legal custom parameter keys).
        /// </summary>
        public void RegisterCustom(EventSchema schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            if (string.IsNullOrWhiteSpace(schema.EventName))
            {
                throw new InvalidOperationException("Custom event schema requires a non-empty event name.");
            }

            if (_schemas.ContainsKey(schema.EventName))
            {
                throw new InvalidOperationException(
                    $"Custom event schema '{schema.EventName}' collides with an existing schema.");
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < schema.Params.Count; i++)
            {
                EventParamSchema param = schema.Params[i];
                if (string.IsNullOrWhiteSpace(param.Name) || !seenNames.Add(param.Name))
                {
                    throw new InvalidOperationException(
                        $"Custom event schema '{schema.EventName}' params[{i}] needs a unique non-empty name.");
                }

                if (!seenKeys.Add(param.PayloadKey))
                {
                    throw new InvalidOperationException(
                        $"Custom event schema '{schema.EventName}' declares payload key '{param.PayloadKey}' twice.");
                }

                if (IsReservedPayloadKey(param.PayloadKey))
                {
                    throw new InvalidOperationException(
                        $"Custom event schema '{schema.EventName}' payload key '{param.PayloadKey}' must not use the " +
                        "reserved 'MapTrigger.' namespace; declare a mod-namespaced key instead.");
                }

                if (!IsNamespacedPayloadKey(param.PayloadKey, out string keyError))
                {
                    throw new InvalidOperationException(
                        $"Custom event schema '{schema.EventName}' payload key '{param.PayloadKey}' is invalid: {keyError}");
                }
            }

            _schemas.Add(schema.EventName, schema);
        }

        /// <summary>
        /// Fire-time contract check: every declared parameter must be present (unless
        /// optional) with the declared type, and no undeclared MapTrigger.* key may ride
        /// the context. Events without a schema entry are not validated yet.
        /// </summary>
        public void ValidateFirePayload(EventKey eventKey, ScriptContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!TryGet(eventKey.Value, out EventSchema schema))
            {
                return;
            }

            for (int i = 0; i < schema.Params.Count; i++)
            {
                EventParamSchema param = schema.Params[i];
                if (!context.Contains(param.PayloadKey))
                {
                    if (param.Optional)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"EVENT.SCHEMA.MissingParam: firing '{schema.EventName}' without declared parameter " +
                        $"'{param.Name}' (payload key '{param.PayloadKey}').");
                }

                object raw = context.Get<object>(param.PayloadKey);
                if (!MatchesParamType(param.Type, raw))
                {
                    throw new InvalidOperationException(
                        $"EVENT.SCHEMA.ParamTypeMismatch: firing '{schema.EventName}' parameter '{param.Name}' " +
                        $"(payload key '{param.PayloadKey}') expects {param.Type} but carried '{raw?.GetType().Name ?? "null"}'.");
                }
            }

            foreach (KeyValuePair<string, object> entry in context.EnumerateStringEntries())
            {
                if (IsReservedPayloadKey(entry.Key) && !schema.DeclaresPayloadKey(entry.Key))
                {
                    throw new InvalidOperationException(
                        $"EVENT.SCHEMA.UndeclaredPayloadKey: firing '{schema.EventName}' carries payload key " +
                        $"'{entry.Key}' that its schema does not declare.");
                }
            }
        }

        internal static bool IsReservedPayloadKey(string payloadKey)
        {
            return payloadKey.StartsWith("MapTrigger.", StringComparison.Ordinal);
        }

        internal static bool IsNamespacedPayloadKey(string payloadKey, out string error)
        {
            error = string.Empty;
            int dot = payloadKey.IndexOf('.');
            if (dot <= 0 || dot == payloadKey.Length - 1)
            {
                error = "keys need a non-empty 'Prefix.Name' shape (mod namespace first).";
                return false;
            }

            return true;
        }

        private static bool MatchesParamType(EventParamType type, object value)
        {
            return type switch
            {
                EventParamType.Entity => value is Entity,
                EventParamType.Int => value is int,
                EventParamType.Float => value is float,
                EventParamType.String => value is string,
                _ => false,
            };
        }

        /// <summary>
        /// Cross-check against <see cref="MapTriggerEventPayloadKeys"/>: every constant must
        /// be referenced by a built-in schema, belong to a dynamic bridge family, or be
        /// pending with a named owner. A new key that is none of these fails engine start.
        /// </summary>
        private static void AssertNoOrphanPayloadKeys()
        {
            var accounted = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < BuiltinSchemas.Length; i++)
            {
                EventSchema schema = BuiltinSchemas[i];
                for (int p = 0; p < schema.Params.Count; p++)
                {
                    accounted.Add(schema.Params[p].PayloadKey);
                }
            }

            foreach (string key in DynamicBridgePayloadKeys)
            {
                accounted.Add(key);
            }

            foreach ((string key, string owner) in PendingPayloadKeys)
            {
                accounted.Add(key);
            }

            foreach (FieldInfo field in typeof(MapTriggerEventPayloadKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetRawConstantValue() is not string constant)
                {
                    continue;
                }

                if (!accounted.Contains(constant))
                {
                    throw new InvalidOperationException(
                        $"EVENT.SCHEMA.OrphanPayloadKey: '{constant}' ({field.Name}) is declared in " +
                        $"{nameof(MapTriggerEventPayloadKeys)} but no built-in schema references it, no dynamic bridge " +
                        "family carries it, and no pending entry claims it.");
                }
            }

            foreach ((string key, string owner) in PendingPayloadKeys)
            {
                for (int i = 0; i < BuiltinSchemas.Length; i++)
                {
                    if (BuiltinSchemas[i].DeclaresPayloadKey(key))
                    {
                        throw new InvalidOperationException(
                            $"EVENT.SCHEMA.StalePendingKey: '{key}' is claimed pending for '{owner}' but built-in " +
                            $"schema '{BuiltinSchemas[i].EventName}' already declares it; drop the pending entry.");
                    }
                }
            }
        }
    }
}
