using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public sealed class OrderTypeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };

        public sealed class OrderTypeConfigJson
        {
            public JsonNode? OrderTypeId { get; set; }
            public string Label { get; set; } = string.Empty;
            public int MaxQueueSize { get; set; } = 3;
            public string SameTypePolicy { get; set; } = "Queue";
            public string QueueFullPolicy { get; set; } = "DropOldest";
            public int Priority { get; set; } = 100;
            public int BufferWindowMs { get; set; } = 500;
            public int PendingBufferWindowMs { get; set; } = 400;
            public bool CanInterruptSelf { get; set; }
            public int QueuedModeMaxSize { get; set; } = 16;
            public bool AllowQueuedMode { get; set; } = true;
            public bool ClearQueueOnActivate { get; set; } = true;
            public JsonNode? SpatialBlackboardKey { get; set; }
            public JsonNode? EntityBlackboardKey { get; set; }
            public JsonNode? IntArg0BlackboardKey { get; set; }
            public JsonNode? ValidationGraphId { get; set; }
            public string ValidationGraph { get; set; } = string.Empty;
        }

        public sealed class OrderRuleConfigJson
        {
            public int OrderTypeId { get; set; }
            public string OrderTypeKey { get; set; } = string.Empty;
            public int[] BlockedActiveOrderTypeIds { get; set; } = Array.Empty<int>();
            public int[] InterruptsActiveOrderTypeIds { get; set; } = Array.Empty<int>();
            public string[] BlockedActiveOrderTypeKeys { get; set; } = Array.Empty<string>();
            public string[] InterruptsActiveOrderTypeKeys { get; set; } = Array.Empty<string>();
        }

        private sealed class OrderTypesRootJson
        {
            public Dictionary<string, OrderTypeConfigJson> OrderTypes { get; set; } = new();
            public Dictionary<string, OrderRuleConfigJson> OrderRules { get; set; } = new();
        }

        public OrderTypeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public void Load(
            OrderTypeRegistry orderTypeRegistry,
            OrderRuleRegistry orderRuleRegistry,
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/order_types.json")
        {
            if (orderTypeRegistry == null) throw new ArgumentNullException(nameof(orderTypeRegistry));
            if (orderRuleRegistry == null) throw new ArgumentNullException(nameof(orderRuleRegistry));

            orderTypeRegistry.Clear();
            orderRuleRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var root = mergedObject.Deserialize<OrderTypesRootJson>(JsonOptions);
            if (root == null)
            {
                throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            }

            if (root.OrderTypes == null || root.OrderTypes.Count == 0)
            {
                throw new InvalidOperationException($"'{relativePath}' must define a non-empty orderTypes object.");
            }

            var assignedIds = new HashSet<int>();
            var orderTypeEntries = new List<KeyValuePair<string, OrderTypeConfigJson>>(root.OrderTypes);
            var semanticOrderTypeKeys = new List<string>();
            var resolvedOrderTypeIds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kvp in orderTypeEntries)
            {
                ReserveAuthoredOrderTypeId(
                    kvp.Value?.OrderTypeId,
                    kvp.Key,
                    relativePath,
                    assignedIds,
                    semanticOrderTypeKeys,
                    resolvedOrderTypeIds);
            }

            semanticOrderTypeKeys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < semanticOrderTypeKeys.Count; i++)
            {
                string key = semanticOrderTypeKeys[i];
                int orderTypeId = AssignNextFreeRuntimeOrderTypeId(key, relativePath, assignedIds);
                resolvedOrderTypeIds[key] = orderTypeId;
            }

            foreach (var kvp in orderTypeEntries)
            {
                var config = ConvertToConfig(kvp.Value, kvp.Key, relativePath, resolvedOrderTypeIds);
                orderTypeRegistry.Register(config);
            }

            if (root.OrderRules == null)
            {
                return;
            }

            foreach (var kvp in root.OrderRules)
            {
                var config = kvp.Value ?? throw new InvalidOperationException($"Order rule '{kvp.Key}' in '{relativePath}' is null.");
                int orderTypeId = ResolveOrderTypeReference(config.OrderTypeId, config.OrderTypeKey, kvp.Key, relativePath, orderTypeRegistry);
                if (!orderTypeRegistry.IsRegistered(orderTypeId))
                {
                    throw new InvalidOperationException($"Order rule '{kvp.Key}' references unregistered order type {orderTypeId}.");
                }

                var ruleSet = ConvertToRuleSet(config, kvp.Key, relativePath, orderTypeRegistry);
                orderRuleRegistry.Register(orderTypeId, in ruleSet);
            }
        }

        private static OrderTypeConfig ConvertToConfig(
            OrderTypeConfigJson json,
            string key,
            string path,
            IReadOnlyDictionary<string, int> resolvedOrderTypeIds)
        {
            if (json == null)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' is null.");
            }

            int orderTypeId = ResolveOrderTypeId(json.OrderTypeId, key, path, resolvedOrderTypeIds);
            if (orderTypeId <= 0)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must define a positive orderTypeId.");
            }

            return new OrderTypeConfig
            {
                Key = key,
                OrderTypeId = orderTypeId,
                Label = string.IsNullOrWhiteSpace(json.Label) ? key : json.Label,
                MaxQueueSize = json.MaxQueueSize,
                SameTypePolicy = ParseSameTypePolicy(json.SameTypePolicy),
                QueueFullPolicy = ParseQueueFullPolicy(json.QueueFullPolicy),
                Priority = json.Priority,
                BufferWindowMs = json.BufferWindowMs,
                PendingBufferWindowMs = json.PendingBufferWindowMs,
                CanInterruptSelf = json.CanInterruptSelf,
                QueuedModeMaxSize = json.QueuedModeMaxSize,
                AllowQueuedMode = json.AllowQueuedMode,
                ClearQueueOnActivate = json.ClearQueueOnActivate,
                SpatialBlackboardKey = ResolveBlackboardKey(json.SpatialBlackboardKey, OrderBlackboardKeys.Generic_TargetPosition, key, path, nameof(json.SpatialBlackboardKey)),
                EntityBlackboardKey = ResolveBlackboardKey(json.EntityBlackboardKey, OrderBlackboardKeys.Generic_TargetEntity, key, path, nameof(json.EntityBlackboardKey)),
                IntArg0BlackboardKey = ResolveBlackboardKey(json.IntArg0BlackboardKey, -1, key, path, nameof(json.IntArg0BlackboardKey)),
                ValidationGraphId = ResolveValidationGraph(json.ValidationGraphId, json.ValidationGraph, key, path)
            };
        }

        private static int ResolveOrderTypeId(
            JsonNode? node,
            string key,
            string path,
            IReadOnlyDictionary<string, int> resolvedOrderTypeIds)
        {
            int id;
            if (node == null)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must explicitly define orderTypeId.");
            }

            if (node is JsonValue value && value.TryGetValue<int>(out id))
            {
                if (id <= 0)
                {
                    throw new InvalidOperationException($"Order type '{key}' in '{path}' must define a positive orderTypeId.");
                }

                if (id >= OrderTypeRegistry.MaxOrderTypes)
                {
                    throw new InvalidOperationException($"Order type '{key}' in '{path}' uses orderTypeId {id}, max is {OrderTypeRegistry.MaxOrderTypes - 1}.");
                }

                return id;
            }

            if (node is JsonValue textValue && textValue.TryGetValue<string>(out string? text))
            {
                ValidateSemanticOrderTypeId(text, key, path);
                if (resolvedOrderTypeIds.TryGetValue(key, out int semanticId))
                {
                    return semanticId;
                }

                throw new InvalidOperationException($"Order type '{key}' in '{path}' semantic orderTypeId was not resolved.");
            }

            throw new InvalidOperationException($"Order type '{key}' in '{path}' orderTypeId must be an int or exact semantic key string.");
        }

        private static void ReserveAuthoredOrderTypeId(
            JsonNode? node,
            string key,
            string path,
            HashSet<int> assignedIds,
            List<string> semanticOrderTypeKeys,
            Dictionary<string, int> resolvedOrderTypeIds)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must explicitly define orderTypeId.");
            }

            if (node is not JsonValue value)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' orderTypeId must be an int or exact semantic key string.");
            }

            if (value.TryGetValue<string>(out string? semanticKey))
            {
                ValidateSemanticOrderTypeId(semanticKey, key, path);
                semanticOrderTypeKeys.Add(key);
                return;
            }

            if (!value.TryGetValue<int>(out int id))
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' orderTypeId must be an int or exact semantic key string.");
            }

            if (id <= 0)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must define a positive orderTypeId.");
            }

            if (id >= OrderTypeRegistry.MaxOrderTypes)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' uses orderTypeId {id}, max is {OrderTypeRegistry.MaxOrderTypes - 1}.");
            }

            if (!assignedIds.Add(id))
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' duplicates orderTypeId {id}.");
            }

            resolvedOrderTypeIds[key] = id;
        }

        private static void ValidateSemanticOrderTypeId(string? semanticKey, string key, string path)
        {
            if (string.IsNullOrWhiteSpace(semanticKey))
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' orderTypeId semantic key must be non-empty.");
            }

            semanticKey = semanticKey.Trim();
            if (!string.Equals(semanticKey, key, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Order type '{key}' in '{path}' orderTypeId semantic key must exactly match the order type key.");
            }
        }

        private static int AssignNextFreeRuntimeOrderTypeId(string key, string path, HashSet<int> assignedIds)
        {
            for (int id = 1; id < OrderTypeRegistry.MaxOrderTypes; id++)
            {
                if (assignedIds.Add(id))
                {
                    return id;
                }
            }

            throw new InvalidOperationException($"Order type '{key}' in '{path}' cannot resolve a free runtime orderTypeId.");
        }

        private static int ResolveBlackboardKey(JsonNode? node, int defaultValue, string key, string path, string fieldName)
        {
            if (node == null)
            {
                return defaultValue;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    return numericId;
                }

                if (value.TryGetValue<string>(out string? text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException($"Order type '{key}' in '{path}' {fieldName} must be a non-empty semantic string.");
                    }

                    text = text.Trim();
                    if (string.Equals(text, "none", StringComparison.Ordinal))
                    {
                        return -1;
                    }

                    return text switch
                    {
                        "Cast.SlotIndex" => OrderBlackboardKeys.Cast_SlotIndex,
                        "Cast.TargetEntity" => OrderBlackboardKeys.Cast_TargetEntity,
                        "Cast.TargetPosition" => OrderBlackboardKeys.Cast_TargetPosition,
                        "Cast.AbilityId" => OrderBlackboardKeys.Cast_AbilityId,
                        "Attack.TargetEntity" => OrderBlackboardKeys.Attack_TargetEntity,
                        "Attack.MovePosition" => OrderBlackboardKeys.Attack_MovePosition,
                        "Attack.IsAttackMove" => OrderBlackboardKeys.Attack_IsAttackMove,
                        "Stop.Type" => OrderBlackboardKeys.Stop_Type,
                        "Hold.Active" => OrderBlackboardKeys.Hold_Active,
                        "Patrol.Waypoints" => OrderBlackboardKeys.Patrol_Waypoints,
                        "Patrol.CurrentIndex" => OrderBlackboardKeys.Patrol_CurrentIndex,
                        "Patrol.Direction" => OrderBlackboardKeys.Patrol_Direction,
                        "Generic.TargetEntity" => OrderBlackboardKeys.Generic_TargetEntity,
                        "Generic.TargetPosition" => OrderBlackboardKeys.Generic_TargetPosition,
                        "Generic.IntParam" => OrderBlackboardKeys.Generic_IntParam,
                        "Generic.FloatParam" => OrderBlackboardKeys.Generic_FloatParam,
                        _ => throw new InvalidOperationException($"Order type '{key}' in '{path}' has unknown {fieldName} '{text}'.")
                    };
                }
            }

            throw new InvalidOperationException($"Order type '{key}' in '{path}' {fieldName} must be an int or semantic string.");
        }

        private static int ResolveValidationGraph(JsonNode? idNode, string graphName, string key, string path)
        {
            bool hasId = idNode != null;
            bool hasGraph = !string.IsNullOrWhiteSpace(graphName);
            if (hasId && hasGraph)
            {
                throw new InvalidOperationException(
                    $"Order type '{key}' in '{path}' must define either validationGraphId or validationGraph, not both.");
            }

            if (hasGraph)
            {
                graphName = graphName.Trim();
                if (string.Equals(graphName, "none", StringComparison.Ordinal))
                {
                    return 0;
                }

                int graphId = GraphIdRegistry.GetId(graphName);
                if (graphId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Order type '{key}' in '{path}' validationGraph references unknown graph '{graphName}'.");
                }

                return graphId;
            }

            if (idNode == null)
            {
                return 0;
            }

            if (idNode is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    if (numericId < 0)
                    {
                        throw new InvalidOperationException($"Order type '{key}' in '{path}' validationGraphId must be non-negative.");
                    }

                    return numericId;
                }

                if (value.TryGetValue<string>(out string? text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException($"Order type '{key}' in '{path}' validationGraphId must be an int or 'none'.");
                    }

                    text = text.Trim();
                    if (string.Equals(text, "none", StringComparison.Ordinal))
                    {
                        return 0;
                    }
                }
            }

            throw new InvalidOperationException($"Order type '{key}' in '{path}' validationGraphId must be a non-negative int.");
        }

        private static unsafe OrderRuleSet ConvertToRuleSet(
            OrderRuleConfigJson json,
            string key,
            string path,
            OrderTypeRegistry orderTypeRegistry)
        {
            ResolveOrderTypeReference(json.OrderTypeId, json.OrderTypeKey, key, path, orderTypeRegistry);

            var result = new OrderRuleSet();
            Span<int> blocked = stackalloc int[OrderRuleSet.MAX_BLOCKED_ACTIVE_ORDER_TYPES];
            result.BlockedActiveCount = ResolveOrderTypeReferences(
                json.BlockedActiveOrderTypeIds,
                json.BlockedActiveOrderTypeKeys,
                key,
                path,
                orderTypeRegistry,
                blocked);
            for (int i = 0; i < result.BlockedActiveCount; i++)
            {
                result.BlockedActiveOrderTypeIds[i] = blocked[i];
            }

            Span<int> interrupts = stackalloc int[OrderRuleSet.MAX_INTERRUPTS_ACTIVE_ORDER_TYPES];
            result.InterruptsActiveCount = ResolveOrderTypeReferences(
                json.InterruptsActiveOrderTypeIds,
                json.InterruptsActiveOrderTypeKeys,
                key,
                path,
                orderTypeRegistry,
                interrupts);
            for (int i = 0; i < result.InterruptsActiveCount; i++)
            {
                result.InterruptsActiveOrderTypeIds[i] = interrupts[i];
            }

            return result;
        }

        private static int ResolveOrderTypeReferences(
            int[] ids,
            string[] keys,
            string key,
            string path,
            OrderTypeRegistry orderTypeRegistry,
            Span<int> destination)
        {
            ids ??= Array.Empty<int>();
            keys ??= Array.Empty<string>();
            int count = ids.Length + keys.Length;
            if (count > destination.Length)
            {
                throw new InvalidOperationException(
                    $"Order rule '{key}' in '{path}' references {count} order types, max {destination.Length}.");
            }

            int cursor = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                int orderTypeId = ResolveOrderTypeReference(ids[i], string.Empty, key, path, orderTypeRegistry);
                EnsureUniqueOrderRuleReference(destination.Slice(0, cursor), orderTypeId, key, path);
                destination[cursor++] = orderTypeId;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                int orderTypeId = ResolveOrderTypeReference(0, keys[i], key, path, orderTypeRegistry);
                EnsureUniqueOrderRuleReference(destination.Slice(0, cursor), orderTypeId, key, path);
                destination[cursor++] = orderTypeId;
            }

            return cursor;
        }

        private static int ResolveOrderTypeReference(
            int orderTypeId,
            string orderTypeKey,
            string key,
            string path,
            OrderTypeRegistry orderTypeRegistry)
        {
            bool hasId = orderTypeId > 0;
            bool hasKey = !string.IsNullOrWhiteSpace(orderTypeKey);
            if (hasId == hasKey)
            {
                throw new InvalidOperationException(
                    $"Order rule '{key}' in '{path}' must reference exactly one order type id or key.");
            }

            if (hasKey)
            {
                if (!orderTypeRegistry.TryGetId(orderTypeKey, out orderTypeId) || orderTypeId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Order rule '{key}' in '{path}' references unknown order type key '{orderTypeKey}'.");
                }
            }

            if (!orderTypeRegistry.IsRegistered(orderTypeId))
            {
                throw new InvalidOperationException($"Order rule '{key}' in '{path}' references unknown order type {orderTypeId}.");
            }

            return orderTypeId;
        }

        private static void EnsureUniqueOrderRuleReference(ReadOnlySpan<int> existing, int orderTypeId, string key, string path)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] == orderTypeId)
                {
                    throw new InvalidOperationException(
                        $"Order rule '{key}' in '{path}' references duplicate order type {orderTypeId}.");
                }
            }
        }

        private static SameTypePolicy ParseSameTypePolicy(string value)
        {
            return value switch
            {
                "Queue" => SameTypePolicy.Queue,
                "Replace" => SameTypePolicy.Replace,
                "Ignore" => SameTypePolicy.Ignore,
                _ => throw new InvalidOperationException($"Unknown SameTypePolicy '{value}'."),
            };
        }

        private static QueueFullPolicy ParseQueueFullPolicy(string value)
        {
            return value switch
            {
                "DropOldest" => QueueFullPolicy.DropOldest,
                "RejectNew" => QueueFullPolicy.RejectNew,
                _ => throw new InvalidOperationException($"Unknown QueueFullPolicy '{value}'."),
            };
        }
    }
}
