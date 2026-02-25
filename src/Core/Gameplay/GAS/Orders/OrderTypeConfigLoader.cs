using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// JSON configuration root for order types.
    /// </summary>
    public class OrderTypesConfigRoot
    {
        /// <summary>
        /// Map of order type key to configuration.
        /// </summary>
        public Dictionary<string, OrderTypeConfigJson> OrderTypes { get; set; } = new();

        /// <summary>
        /// Tag rule definitions for order conflicts.
        /// </summary>
        public Dictionary<string, TagRuleConfigJson> TagRules { get; set; } = new();
    }

    /// <summary>
    /// JSON representation of order type config.
    /// </summary>
    public class OrderTypeConfigJson
    {
        public int OrderTagId { get; set; }
        public string Label { get; set; } = string.Empty;
        public int MaxQueueSize { get; set; } = 3;
        public string SameTypePolicy { get; set; } = "Queue";
        public string QueueFullPolicy { get; set; } = "DropOldest";
        public int Priority { get; set; } = 100;
        public int BufferWindowMs { get; set; } = 500;
        public bool CanInterruptSelf { get; set; } = false;
        public int OrderStateTagId { get; set; }
        public int QueuedModeMaxSize { get; set; } = 16;
        public bool AllowQueuedMode { get; set; } = true;
        public bool ClearQueueOnActivate { get; set; } = true;
    }

    /// <summary>
    /// JSON representation of tag rule config.
    /// </summary>
    public class TagRuleConfigJson
    {
        public int TagId { get; set; }
        public int[] RequiredTags { get; set; } = Array.Empty<int>();
        public int[] BlockedTags { get; set; } = Array.Empty<int>();
        public int[] AttachedTags { get; set; } = Array.Empty<int>();
        public int[] RemovedTags { get; set; } = Array.Empty<int>();
        public int[] DisabledIfTags { get; set; } = Array.Empty<int>();
        public int[] RemoveIfTags { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// Loader for order type configurations.
    /// </summary>
    public sealed class OrderTypeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public OrderTypeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        /// <summary>
        /// Load order type configurations from ConfigPipeline.
        /// </summary>
        public void Load(
            OrderTypeRegistry orderTypeRegistry,
            TagRuleRegistry tagRuleRegistry,
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/order_types.json")
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null) return;

            var root = mergedObject.Deserialize<OrderTypesConfigRoot>(JsonOptions);
            if (root == null) return;

            foreach (var (key, json) in root.OrderTypes)
            {
                var config = ConvertToConfig(json, key);
                orderTypeRegistry.Register(config);
            }

            foreach (var (key, json) in root.TagRules)
            {
                var ruleSet = ConvertToRuleSet(json);
                tagRuleRegistry.Register(json.TagId, ruleSet);
            }
        }

        /// <summary>
        /// Register default order types and tag rules.
        /// </summary>
        public static void RegisterDefaults(
            OrderTypeRegistry orderTypeRegistry,
            TagRuleRegistry tagRuleRegistry)
        {
            orderTypeRegistry.Register(new OrderTypeConfig
            {
                OrderTagId = OrderStateTags.Active_CastAbility,
                Label = "castAbility",
                MaxQueueSize = 3,
                SameTypePolicy = SameTypePolicy.Queue,
                QueueFullPolicy = QueueFullPolicy.DropOldest,
                Priority = 100,
                BufferWindowMs = 500,
                CanInterruptSelf = false,
                OrderStateTagId = OrderStateTags.Active_CastAbility,
                QueuedModeMaxSize = 8,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Cast_TargetPosition,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex
            });

            orderTypeRegistry.Register(new OrderTypeConfig
            {
                OrderTagId = OrderStateTags.Active_AttackTarget,
                Label = "attackTarget",
                MaxQueueSize = 1,
                SameTypePolicy = SameTypePolicy.Replace,
                QueueFullPolicy = QueueFullPolicy.DropOldest,
                Priority = 75,
                BufferWindowMs = 300,
                CanInterruptSelf = true,
                OrderStateTagId = OrderStateTags.Active_AttackTarget,
                QueuedModeMaxSize = 8,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Attack_MovePosition,
                EntityBlackboardKey = OrderBlackboardKeys.Attack_TargetEntity,
                IntArg0BlackboardKey = -1
            });

            orderTypeRegistry.Register(new OrderTypeConfig
            {
                OrderTagId = OrderStateTags.Active_Stop,
                Label = "stop",
                MaxQueueSize = 1,
                SameTypePolicy = SameTypePolicy.Replace,
                QueueFullPolicy = QueueFullPolicy.DropOldest,
                Priority = 200,
                BufferWindowMs = 0,
                CanInterruptSelf = false,
                OrderStateTagId = OrderStateTags.Active_Stop,
                QueuedModeMaxSize = 1,
                AllowQueuedMode = false,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });

            RegisterCastAbilityRules(tagRuleRegistry);
            RegisterStopRules(tagRuleRegistry);
        }

        private static unsafe void RegisterCastAbilityRules(TagRuleRegistry registry)
        {
            var ruleSet = new TagRuleSet();
            ruleSet.AttachedTags[0] = OrderStateTags.State_HasActive;
            ruleSet.AttachedCount = 1;
            registry.Register(OrderStateTags.Active_CastAbility, ruleSet);
        }

        private static unsafe void RegisterStopRules(TagRuleRegistry registry)
        {
            var ruleSet = new TagRuleSet();
            ruleSet.RemovedTags[0] = OrderStateTags.Active_CastAbility;
            ruleSet.RemovedTags[1] = OrderStateTags.Active_AttackTarget;
            ruleSet.RemovedCount = 2;
            ruleSet.AttachedTags[0] = OrderStateTags.State_HasActive;
            ruleSet.AttachedCount = 1;
            registry.Register(OrderStateTags.Active_Stop, ruleSet);
        }

        private static OrderTypeConfig ConvertToConfig(OrderTypeConfigJson json, string key)
        {
            return new OrderTypeConfig
            {
                OrderTagId = json.OrderTagId,
                Label = string.IsNullOrEmpty(json.Label) ? key : json.Label,
                MaxQueueSize = json.MaxQueueSize,
                SameTypePolicy = ParseSameTypePolicy(json.SameTypePolicy),
                QueueFullPolicy = ParseQueueFullPolicy(json.QueueFullPolicy),
                Priority = json.Priority,
                BufferWindowMs = json.BufferWindowMs,
                CanInterruptSelf = json.CanInterruptSelf,
                OrderStateTagId = json.OrderStateTagId,
                QueuedModeMaxSize = json.QueuedModeMaxSize,
                AllowQueuedMode = json.AllowQueuedMode,
                ClearQueueOnActivate = json.ClearQueueOnActivate
            };
        }

        private static unsafe TagRuleSet ConvertToRuleSet(TagRuleConfigJson json)
        {
            var ruleSet = new TagRuleSet();

            CopyTags(json.RequiredTags, ruleSet.RequiredTags, TagRuleSet.MAX_REQUIRED_TAGS, out ruleSet.RequiredCount);
            CopyTags(json.BlockedTags, ruleSet.BlockedTags, TagRuleSet.MAX_BLOCKED_TAGS, out ruleSet.BlockedCount);
            CopyTags(json.AttachedTags, ruleSet.AttachedTags, TagRuleSet.MAX_ATTACHED_TAGS, out ruleSet.AttachedCount);
            CopyTags(json.RemovedTags, ruleSet.RemovedTags, TagRuleSet.MAX_REMOVED_TAGS, out ruleSet.RemovedCount);
            CopyTags(json.DisabledIfTags, ruleSet.DisabledIfTags, TagRuleSet.MAX_DISABLED_IF_TAGS, out ruleSet.DisabledIfCount);
            CopyTags(json.RemoveIfTags, ruleSet.RemoveIfTags, TagRuleSet.MAX_REMOVE_IF_TAGS, out ruleSet.RemoveIfCount);

            return ruleSet;
        }

        private static unsafe void CopyTags(int[] source, int* dest, int maxCount, out int count)
        {
            count = Math.Min(source.Length, maxCount);
            for (int i = 0; i < count; i++)
            {
                dest[i] = source[i];
            }
        }

        private static SameTypePolicy ParseSameTypePolicy(string value)
        {
            return value?.ToLowerInvariant() switch
            {
                "queue" => SameTypePolicy.Queue,
                "replace" => SameTypePolicy.Replace,
                "ignore" => SameTypePolicy.Ignore,
                _ => SameTypePolicy.Queue
            };
        }

        private static QueueFullPolicy ParseQueueFullPolicy(string value)
        {
            return value?.ToLowerInvariant() switch
            {
                "dropoldest" => QueueFullPolicy.DropOldest,
                "rejectnew" => QueueFullPolicy.RejectNew,
                _ => QueueFullPolicy.DropOldest
            };
        }
    }
}
