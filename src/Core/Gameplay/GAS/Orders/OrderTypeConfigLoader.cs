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
            public int[] BlockedActiveOrderTypeIds { get; set; } = Array.Empty<int>();
            public int[] InterruptsActiveOrderTypeIds { get; set; } = Array.Empty<int>();
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
                if (!orderTypeRegistry.IsRegistered(config.OrderTypeId))
                {
                    throw new InvalidOperationException($"Order rule '{kvp.Key}' references unregistered order type {config.OrderTypeId}.");
                }

                var ruleSet = ConvertToRuleSet(config, kvp.Key, relativePath, orderTypeRegistry);
                orderRuleRegistry.Register(config.OrderTypeId, in ruleSet);
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
            if (json.OrderTypeId <= 0)
            {
                throw new InvalidOperationException($"Order rule '{key}' in '{path}' must define a positive orderTypeId.");
            }

            var result = new OrderRuleSet();
            result.BlockedActiveCount = ValidateOrderTypes(json.BlockedActiveOrderTypeIds, key, path, orderTypeRegistry, OrderRuleSet.MAX_BLOCKED_ACTIVE_ORDER_TYPES);
            for (int i = 0; i < result.BlockedActiveCount; i++)
            {
                result.BlockedActiveOrderTypeIds[i] = json.BlockedActiveOrderTypeIds[i];
            }

            result.InterruptsActiveCount = ValidateOrderTypes(json.InterruptsActiveOrderTypeIds, key, path, orderTypeRegistry, OrderRuleSet.MAX_INTERRUPTS_ACTIVE_ORDER_TYPES);
            for (int i = 0; i < result.InterruptsActiveCount; i++)
            {
                result.InterruptsActiveOrderTypeIds[i] = json.InterruptsActiveOrderTypeIds[i];
            }

            return result;
        }

        private static int ValidateOrderTypes(
            int[] source,
            string key,
            string path,
            OrderTypeRegistry orderTypeRegistry,
            int maxCount)
        {
            source ??= Array.Empty<int>();
            int count = Math.Min(source.Length, maxCount);
            for (int i = 0; i < count; i++)
            {
                int orderTypeId = source[i];
                if (orderTypeId <= 0)
                {
                    throw new InvalidOperationException($"Order rule '{key}' in '{path}' contains invalid order type id {orderTypeId}.");
                }

                if (!orderTypeRegistry.IsRegistered(orderTypeId))
                {
                    throw new InvalidOperationException($"Order rule '{key}' in '{path}' references unknown order type {orderTypeId}.");
                }
            }

            return count;
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
