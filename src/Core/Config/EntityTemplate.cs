using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public class EntityTemplate : IIdentifiable
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

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

        // 切D 预留：资产级 relations 段（路径对 + 关系类型），本切只留形状不物化。
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
