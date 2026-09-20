using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public class EntityTemplate : IIdentifiable
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// 装载期继承：引用另一模板 id。展开发生在跨 mod 同 id 合并之后、装载校验之前——
        /// components 按字段级深合并（子代字段胜，数组替换），children/TriggerGraphs 追加
        /// （TriggerGraphs 精确去重，同图双挂不是合法变体），onSpawnEffect/initialInteractionContext
        /// 子代非空才覆盖。未知父模板或继承环启动失败；展开后本字段清空，物化只消费展开结果。
        /// </summary>
        [JsonPropertyName("extends")]
        public string? Extends { get; set; }

        [JsonPropertyName("onSpawnEffect")]
        public string OnSpawnEffect { get; set; }

        // Map of ComponentName -> JsonObject Data
        [JsonPropertyName("components")]
        public Dictionary<string, JsonNode> Components { get; set; } = new Dictionary<string, JsonNode>();

        /// <summary>
        /// Entity-domain TriggerGraph mounts authored on the template (graph ids).
        /// Strict parsing (non-null array of trimmed non-empty strings) happens at
        /// template load; unknown graph names fail closed at mount time.
        /// </summary>
        [JsonPropertyName("TriggerGraphs")]
        public List<string>? TriggerGraphs { get; set; }

        /// <summary>
        /// Interaction context profile id mounted as the entity's base
        /// <c>InteractionContextInstance</c> at spawn. Trimmed
        /// non-empty when present; the profile id resolves against the installed context
        /// profiles (engine init fails fast on unknown ids, spawn fails closed on drift).
        /// </summary>
        [JsonPropertyName("initialInteractionContext")]
        public string? InitialInteractionContext { get; set; }

        /// <summary>
        /// 预置组合子实体（以本模板实体为父、按 localPose 相对落位）。
        /// 形状对齐 presenter 层 PresenterDefinition.Children 先例；spawn 走
        /// RuntimeEntitySpawnQueue 既有管线（map 装载走 EntityBuilder 同一物化路径）。
        /// 递归无上限：child 节点自身可再声明 children，且被引用模板自身的 children
        /// 也会展开（后者是 main 既有先例，两者同序叠加）。
        /// </summary>
        [JsonPropertyName("children")]
        public List<EntityTemplateChild>? Children { get; set; }

        // 资产轴 relations（模板内默认边）未实现，属切D：当前模板 JSON 里写 relations 会被
        // 反序列化静默丢弃——不要写，实例边用地图实体条目的 relations 段（#1554 已落）。

        /// <summary>
        /// 子树里是否存在带 localId 的可寻址节点（含被引用模板自身的 children 与内联 children）。
        /// 可寻址路径 = 实例根 instanceId + "." + localId 链；无实例根的子树没有命名空间可挂。
        /// 遍历序 = 声明序，仅做存在性判定（不依赖字典序）；装载期已验证 children 图无环。
        /// </summary>
        public static bool HasAddressableDescendant(
            IReadOnlyList<EntityTemplateChild>? children,
            DataRegistry<EntityTemplate> registry)
        {
            return HasAddressableDescendant(children, registry, new HashSet<string>(StringComparer.Ordinal));
        }

        private static bool HasAddressableDescendant(
            IReadOnlyList<EntityTemplateChild>? children,
            DataRegistry<EntityTemplate> registry,
            HashSet<string> visitedTemplates)
        {
            if (children == null)
            {
                return false;
            }

            for (int i = 0; i < children.Count; i++)
            {
                EntityTemplateChild child = children[i];
                if (child == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(child.LocalId))
                {
                    return true;
                }

                if (HasAddressableDescendant(child.Children, registry, visitedTemplates))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(child.Template) || !visitedTemplates.Add(child.Template))
                {
                    continue;
                }

                EntityTemplate? referenced = registry?.Get(child.Template);
                if (referenced != null && HasAddressableDescendant(referenced.Children, registry, visitedTemplates))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class EntityTemplateChild
    {
        /// <summary>
        /// 组内可寻址路径名（可选）。同一父 children 列表内必须唯一；装载期与祖先
        /// localId 链累积成实例根下的可寻址路径（供切D/切F 使用）。
        /// </summary>
        [JsonPropertyName("localId")]
        public string? LocalId { get; set; }

        [JsonPropertyName("template")]
        public string Template { get; set; }

        [JsonPropertyName("localPose")]
        public EntityTemplateLocalPose? LocalPose { get; set; }

        /// <summary>
        /// true（缺省）= 结构挂接子件，受 MovementParticipation 静态件禁令；
        /// false = 可动成员语义标记。本切只保留标记与禁令豁免，独立出生由切E 落地。
        /// </summary>
        [JsonPropertyName("attach")]
        public bool? Attach { get; set; }

        [JsonPropertyName("children")]
        public List<EntityTemplateChild>? Children { get; set; }

        [JsonPropertyName("overrides")]
        public Dictionary<string, JsonNode>? Overrides { get; set; }
    }

    public sealed class EntityTemplateLocalPose
    {
        [JsonPropertyName("offsetXCm")]
        public int? OffsetXCm { get; set; }

        [JsonPropertyName("offsetYCm")]
        public int? OffsetYCm { get; set; }

        [JsonPropertyName("facingDeg")]
        public int? FacingDeg { get; set; }

        [JsonPropertyName("inheritParentFacing")]
        public bool? InheritParentFacing { get; set; }

        [JsonPropertyName("offsetRotation")]
        public string? OffsetRotation { get; set; }
    }
}
