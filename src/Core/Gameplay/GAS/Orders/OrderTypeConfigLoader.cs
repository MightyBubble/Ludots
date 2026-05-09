using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public sealed class OrderTypeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public sealed class OrderTypeConfigJson
        {
            public int OrderTypeId { get; set; }
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
            public int SpatialBlackboardKey { get; set; } = OrderBlackboardKeys.Generic_TargetPosition;
            public int EntityBlackboardKey { get; set; } = OrderBlackboardKeys.Generic_TargetEntity;
            public int IntArg0BlackboardKey { get; set; } = -1;
            public int ValidationGraphId { get; set; }
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

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.DeepObject);
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

            foreach (var kvp in root.OrderTypes)
            {
                var config = ConvertToConfig(kvp.Value, kvp.Key, relativePath);
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

        private static OrderTypeConfig ConvertToConfig(OrderTypeConfigJson json, string key, string path)
        {
            if (json == null)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' is null.");
            }

            if (json.OrderTypeId <= 0)
            {
                throw new InvalidOperationException($"Order type '{key}' in '{path}' must define a positive orderTypeId.");
            }

            return new OrderTypeConfig
            {
                Key = key,
                OrderTypeId = json.OrderTypeId,
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
                SpatialBlackboardKey = json.SpatialBlackboardKey,
                EntityBlackboardKey = json.EntityBlackboardKey,
                IntArg0BlackboardKey = json.IntArg0BlackboardKey,
                ValidationGraphId = json.ValidationGraphId
            };
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
            return value?.ToLowerInvariant() switch
            {
                "queue" => SameTypePolicy.Queue,
                "replace" => SameTypePolicy.Replace,
                "ignore" => SameTypePolicy.Ignore,
                _ => throw new InvalidOperationException($"Unknown SameTypePolicy '{value}'."),
            };
        }

        private static QueueFullPolicy ParseQueueFullPolicy(string value)
        {
            return value?.ToLowerInvariant() switch
            {
                "dropoldest" => QueueFullPolicy.DropOldest,
                "rejectnew" => QueueFullPolicy.RejectNew,
                _ => throw new InvalidOperationException($"Unknown QueueFullPolicy '{value}'."),
            };
        }
    }
}
