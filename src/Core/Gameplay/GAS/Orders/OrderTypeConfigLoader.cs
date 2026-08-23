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
            public string? Label { get; set; }
            public int? MaxQueueSize { get; set; }
            public string? SameTypePolicy { get; set; }
            public string? QueueFullPolicy { get; set; }
            public int? Priority { get; set; }
            public int? BufferWindowMs { get; set; }
            public int? PendingBufferWindowMs { get; set; }
            public bool? CanInterruptSelf { get; set; }
            public int? QueuedModeMaxSize { get; set; }
            public bool? AllowQueuedMode { get; set; }
            public bool? ClearQueueOnActivate { get; set; }
            public JsonNode? SpatialBlackboardKey { get; set; }
            public JsonNode? EntityBlackboardKey { get; set; }
            public JsonNode? IntArg0BlackboardKey { get; set; }
            public JsonNode? ValidationGraph { get; set; }
            public bool? InstantComplete { get; set; }
            public PersistentStoredTargetConfigJson? PersistentStoredTarget { get; set; }
        }

        public sealed class PersistentStoredTargetConfigJson
        {
            public string? TargetKindKey { get; set; }
            public string? TargetPositionKey { get; set; }
            public string? TargetEntityKey { get; set; }
            public string? HexQKey { get; set; }
            public string? HexRKey { get; set; }
        }

        public sealed class OrderRuleConfigJson
        {
            public string? OrderTypeKey { get; set; }
            public string[]? BlockedActiveOrderTypeKeys { get; set; }
            public string[]? InterruptsActiveOrderTypeKeys { get; set; }
        }

        private sealed class OrderTypesRootJson
        {
            public Dictionary<string, JsonNode?>? OrderBlackboardKeys { get; set; }
            public Dictionary<string, OrderTypeConfigJson>? OrderTypes { get; set; }
            public required Dictionary<string, OrderRuleConfigJson> OrderRules { get; set; }
        }

        public OrderTypeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public void RegisterBlackboardKeys(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/order_types.json")
        {
            OrderTypesRootJson root = LoadOrderTypesRoot(catalog, report, relativePath);
            RegisterConfiguredBlackboardKeys(root.OrderBlackboardKeys, relativePath);
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

            var root = LoadOrderTypesRoot(catalog, report, relativePath);
            RegisterConfiguredBlackboardKeys(root.OrderBlackboardKeys, relativePath);

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

            foreach (var kvp in root.OrderRules)
            {
                var config = kvp.Value ?? throw new InvalidOperationException($"Order rule '{kvp.Key}' in '{relativePath}' is null.");
                int orderTypeId = ResolveOrderTypeReference(config.OrderTypeKey, kvp.Key, relativePath, orderTypeRegistry);
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

            int maxQueueSize = RequireQueueSize(json.MaxQueueSize, key, path, "maxQueueSize");
            int queuedModeMaxSize = RequireQueueSize(json.QueuedModeMaxSize, key, path, "queuedModeMaxSize");

            return new OrderTypeConfig
            {
                Key = key,
                OrderTypeId = orderTypeId,
                Label = RequireString(json.Label, key, path, "label"),
                MaxQueueSize = maxQueueSize,
                SameTypePolicy = ParseSameTypePolicy(RequireString(json.SameTypePolicy, key, path, "sameTypePolicy")),
                QueueFullPolicy = ParseQueueFullPolicy(RequireString(json.QueueFullPolicy, key, path, "queueFullPolicy")),
                Priority = RequireInt(json.Priority, key, path, "priority"),
                BufferWindowMs = RequireInt(json.BufferWindowMs, key, path, "bufferWindowMs"),
                PendingBufferWindowMs = RequireInt(json.PendingBufferWindowMs, key, path, "pendingBufferWindowMs"),
                CanInterruptSelf = RequireBool(json.CanInterruptSelf, key, path, "canInterruptSelf"),
                QueuedModeMaxSize = queuedModeMaxSize,
                AllowQueuedMode = RequireBool(json.AllowQueuedMode, key, path, "allowQueuedMode"),
                ClearQueueOnActivate = RequireBool(json.ClearQueueOnActivate, key, path, "clearQueueOnActivate"),
                SpatialBlackboardKey = ResolveBlackboardKey(json.SpatialBlackboardKey, key, path, "spatialBlackboardKey"),
                EntityBlackboardKey = ResolveBlackboardKey(json.EntityBlackboardKey, key, path, "entityBlackboardKey"),
                IntArg0BlackboardKey = ResolveBlackboardKey(json.IntArg0BlackboardKey, key, path, "intArg0BlackboardKey"),
                ValidationGraphId = ResolveValidationGraph(json.ValidationGraph, key, path),
                InstantComplete = RequireBool(json.InstantComplete, key, path, "instantComplete"),
                PersistentStoredTargetKeys = ResolvePersistentStoredTarget(json.PersistentStoredTarget, json.InstantComplete, key, path),
            };
        }

        private static BlackboardStoredTargetKeys ResolvePersistentStoredTarget(
            PersistentStoredTargetConfigJson? json,
            bool? instantComplete,
            string key,
            string path)
        {
            bool isInstant = instantComplete == true;
            if (json == null)
            {
                if (isInstant)
                {
                    throw new InvalidOperationException(
                        $"Order type '{key}' in '{path}' with instantComplete=true must define persistentStoredTarget.");
                }

                return default;
            }

            if (!isInstant)
            {
                throw new InvalidOperationException(
                    $"Order type '{key}' in '{path}' defines persistentStoredTarget but instantComplete is not true.");
            }

            return new BlackboardStoredTargetKeys(
                ResolveBlackboardKeyFromString(RequireString(json.TargetKindKey, key, path, "persistentStoredTarget.targetKindKey"), key, path, "persistentStoredTarget.targetKindKey"),
                ResolveBlackboardKeyFromString(RequireString(json.TargetPositionKey, key, path, "persistentStoredTarget.targetPositionKey"), key, path, "persistentStoredTarget.targetPositionKey"),
                ResolveBlackboardKeyFromString(RequireString(json.TargetEntityKey, key, path, "persistentStoredTarget.targetEntityKey"), key, path, "persistentStoredTarget.targetEntityKey"),
                ResolveBlackboardKeyFromString(RequireString(json.HexQKey, key, path, "persistentStoredTarget.hexQKey"), key, path, "persistentStoredTarget.hexQKey"),
                ResolveBlackboardKeyFromString(RequireString(json.HexRKey, key, path, "persistentStoredTarget.hexRKey"), key, path, "persistentStoredTarget.hexRKey"));
        }

        private static int ResolveBlackboardKeyFromString(string text, string key, string path, string fieldName)
        {
            if (OrderBlackboardKeyRegistry.TryGetId(text, out int blackboardKey))
            {
                return blackboardKey;
            }

            throw new InvalidOperationException($"Order type '{key}' in '{path}' has unknown {fieldName} '{text}'.");
        }

        private static int RequireInt(int? value, string key, string path, string fieldName)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must explicitly define {fieldName}.");
            }

            return value.Value;
        }

        private static int RequireQueueSize(int? value, string key, string path, string fieldName)
        {
            int size = RequireInt(value, key, path, fieldName);
            if (size < 0 || size > Ludots.Core.Gameplay.GAS.Components.OrderBuffer.MAX_QUEUED_ORDERS)
            {
                throw new InvalidOperationException(
                    $"Order type '{key}' in '{path}' has {fieldName}={size}; expected 0..{Ludots.Core.Gameplay.GAS.Components.OrderBuffer.MAX_QUEUED_ORDERS}.");
            }

            return size;
        }

        private static bool RequireBool(bool? value, string key, string path, string fieldName)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must explicitly define {fieldName}.");
            }

            return value.Value;
        }

        private static string RequireString(string? value, string key, string path, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must explicitly define non-empty {fieldName}.");
            }

            return RequireCanonicalString(value, $"Order type '{key}' in '{path}' {fieldName}");
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

            semanticKey = RequireCanonicalString(semanticKey, $"Order type '{key}' in '{path}' orderTypeId semantic key");
            if (!string.Equals(semanticKey, key, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Order type '{key}' in '{path}' orderTypeId semantic key must exactly match the order type key.");
            }
        }

        private OrderTypesRootJson LoadOrderTypesRoot(
            ConfigCatalog catalog,
            ConfigConflictReport report,
            string relativePath)
        {
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

            if (root.OrderBlackboardKeys == null)
            {
                throw new InvalidOperationException($"'{relativePath}' must explicitly define orderBlackboardKeys, even when empty.");
            }

            if (root.OrderTypes == null || root.OrderTypes.Count == 0)
            {
                throw new InvalidOperationException($"'{relativePath}' must define a non-empty orderTypes object.");
            }

            if (root.OrderRules == null)
            {
                throw new InvalidOperationException($"'{relativePath}' must explicitly define orderRules, even when empty.");
            }

            return root;
        }

        private static void RegisterConfiguredBlackboardKeys(
            IReadOnlyDictionary<string, JsonNode?> configuredKeys,
            string path)
        {
            var keys = new List<string>(configuredKeys.Count);
            foreach (var kvp in configuredKeys)
            {
                string key = RequireConfiguredBlackboardKey(kvp.Key, path);
                RequireConfiguredBlackboardKeyDeclaration(kvp.Value, key, path);
                if (OrderBlackboardKeyRegistry.IsBuiltinKey(key))
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_GAS_ORDER_BLACKBOARD_BUILTIN_REDECLARED: order blackboard key '{key}' in '{path}' is built in and must only be referenced, not redeclared in orderBlackboardKeys.");
                }

                keys.Add(key);
            }

            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                OrderBlackboardKeyRegistry.Register(keys[i]);
            }
        }

        private static string RequireConfiguredBlackboardKey(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Order blackboard key in '{path}' must be a non-empty semantic string.");
            }

            return RequireCanonicalString(value, $"Order blackboard key in '{path}'");
        }

        private static void RequireConfiguredBlackboardKeyDeclaration(JsonNode? node, string key, string path)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Order blackboard key '{key}' in '{path}' must be declared with boolean true, not numeric id {numericId}.");
                }

                if (value.TryGetValue<bool>(out bool declared))
                {
                    if (declared)
                    {
                        return;
                    }

                    throw new InvalidOperationException(
                        $"Order blackboard key '{key}' in '{path}' must be declared with boolean true; remove the key when it is not used.");
                }
            }

            throw new InvalidOperationException(
                $"Order blackboard key '{key}' in '{path}' must be declared with boolean true.");
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

        private static int ResolveBlackboardKey(JsonNode? node, string key, string path, string fieldName)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must explicitly define {fieldName}.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException($"Order type '{key}' in '{path}' {fieldName} must be an exact semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string? text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException($"Order type '{key}' in '{path}' {fieldName} must be a non-empty semantic string.");
                    }

                    text = RequireCanonicalString(text, $"Order type '{key}' in '{path}' {fieldName}");
                    if (string.Equals(text, "none", StringComparison.Ordinal))
                    {
                        return -1;
                    }

                    if (OrderBlackboardKeyRegistry.TryGetId(text, out int blackboardKey))
                    {
                        return blackboardKey;
                    }

                    throw new InvalidOperationException($"Order type '{key}' in '{path}' has unknown {fieldName} '{text}'.");
                }
            }

            throw new InvalidOperationException($"Order type '{key}' in '{path}' {fieldName} must be an exact semantic string.");
        }

        private static int ResolveValidationGraph(JsonNode? graphNode, string key, string path)
        {
            if (graphNode == null)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must explicitly define validationGraph.");
            }

            if (graphNode is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Order type '{key}' in '{path}' validationGraph must be an exact semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string? graphName))
                {
                    if (string.IsNullOrWhiteSpace(graphName))
                    {
                        throw new InvalidOperationException($"Order type '{key}' in '{path}' validationGraph must be a non-empty semantic string.");
                    }

                    graphName = RequireCanonicalString(graphName, $"Order type '{key}' in '{path}' validationGraph");
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
            }

            throw new InvalidOperationException($"Order type '{key}' in '{path}' validationGraph must be an exact semantic string.");
        }

        private static unsafe OrderRuleSet ConvertToRuleSet(
            OrderRuleConfigJson json,
            string key,
            string path,
            OrderTypeRegistry orderTypeRegistry)
        {
            ResolveOrderTypeReference(json.OrderTypeKey, key, path, orderTypeRegistry);

            var result = new OrderRuleSet();
            Span<int> blocked = stackalloc int[OrderRuleSet.MAX_BLOCKED_ACTIVE_ORDER_TYPES];
            result.BlockedActiveCount = ResolveOrderTypeReferences(
                json.BlockedActiveOrderTypeKeys,
                "blockedActiveOrderTypeKeys",
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
                json.InterruptsActiveOrderTypeKeys,
                "interruptsActiveOrderTypeKeys",
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
            string[]? keys,
            string fieldName,
            string key,
            string path,
            OrderTypeRegistry orderTypeRegistry,
            Span<int> destination)
        {
            if (keys == null)
            {
                throw new InvalidOperationException($"Order rule '{key}' in '{path}' must explicitly define {fieldName}.");
            }

            int count = keys.Length;
            if (count > destination.Length)
            {
                throw new InvalidOperationException(
                    $"Order rule '{key}' in '{path}' references {count} order types, max {destination.Length}.");
            }

            int cursor = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                int orderTypeId = ResolveOrderTypeReference(keys[i], key, path, orderTypeRegistry);
                EnsureUniqueOrderRuleReference(destination.Slice(0, cursor), orderTypeId, key, path);
                destination[cursor++] = orderTypeId;
            }

            return cursor;
        }

        private static int ResolveOrderTypeReference(
            string? orderTypeKey,
            string key,
            string path,
            OrderTypeRegistry orderTypeRegistry)
        {
            if (string.IsNullOrWhiteSpace(orderTypeKey))
            {
                throw new InvalidOperationException(
                    $"Order rule '{key}' in '{path}' must reference a non-empty orderTypeKey.");
            }

            string resolvedKey = RequireCanonicalString(orderTypeKey, $"Order rule '{key}' in '{path}' orderTypeKey");
            if (!orderTypeRegistry.TryGetId(resolvedKey, out int orderTypeId) || orderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"Order rule '{key}' in '{path}' references unknown order type key '{resolvedKey}'.");
            }

            if (!orderTypeRegistry.IsRegistered(orderTypeId))
            {
                throw new InvalidOperationException($"Order rule '{key}' in '{path}' references unknown order type {orderTypeId}.");
            }

            return orderTypeId;
        }

        private static string RequireCanonicalString(string value, string context)
        {
            if (value.Length != value.Trim().Length)
            {
                throw new InvalidOperationException($"{context} must not contain leading or trailing whitespace.");
            }

            return value;
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
