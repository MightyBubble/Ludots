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
        /// <c>InteractionContextInstance</c> at spawn (#1398 S2b, Case E 01/02). Trimmed
        /// non-empty when present; the profile id resolves against the installed context
        /// profiles (engine init fails fast on unknown ids, spawn fails closed on drift).
        /// </summary>
        [JsonPropertyName("initialInteractionContext")]
        public string? InitialInteractionContext { get; set; }

        /// <summary>
        /// 预置组合子实体（以本模板实体为父、按 localPose 相对落位）。
        /// 形状对齐 presenter 层 PresenterDefinition.Children 先例；spawn 走
        /// RuntimeEntitySpawnQueue 既有管线（map 装载走 EntityBuilder 同一物化路径）。
        /// </summary>
        [JsonPropertyName("children")]
        public List<EntityTemplateChild>? Children { get; set; }
    }

    public sealed class EntityTemplateChild
    {
        [JsonPropertyName("template")]
        public string Template { get; set; }

        [JsonPropertyName("localPose")]
        public EntityTemplateLocalPose? LocalPose { get; set; }

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
