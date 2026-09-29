using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Gameplay.Relationships.Config
{
    public sealed class RelationshipCatalogConfig
    {
        public List<RelationshipTypeConfig> Types { get; set; } = new();
        public List<RelationshipMetricConfig> Metrics { get; set; } = new();
        public List<RelationshipFlagConfig> Flags { get; set; } = new();
        public List<RelationshipKnowledgeGrantConfig> KnowledgeGrants { get; set; } = new();
        public DomainStanceConfig? Stance { get; set; }
    }

    /// <summary>
    /// Data-declared domain stance keys (RFC-0065 DEC-3). Stance names exist only in JSON;
    /// they are resolved to relationship type ids at install time.
    /// </summary>
    public sealed class DomainStanceConfig
    {
        public List<string> StanceTypes { get; set; } = new();
        public string SameDomainStance { get; set; } = string.Empty;
        public string SameTeamStance { get; set; } = string.Empty;
        public string DefaultStance { get; set; } = string.Empty;
    }

    public sealed class RelationshipTypeConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public bool IsSymmetric { get; set; }
        public RelationshipRole Role { get; set; }
        public RelationshipTypeRulesConfig? Rules { get; set; }
        public RelationshipTypeTemplateConfig? Template { get; set; }
    }

    /// <summary>
    /// Which relationship type the engine reads when it answers a control-plane question. A role carries
    /// no link constraints of its own; uniqueness, exclusion and cycles are declared in <see cref="RelationshipTypeRulesConfig"/>.
    /// </summary>
    public enum RelationshipRole
    {
        None = 0,
        /// <summary>PlayerOwner is projected from the root source reached by walking incoming edges of this type.</summary>
        Ownership,
        /// <summary>Team is projected onto the source from a target carrying TeamIdentity.</summary>
        Membership,
        /// <summary>Control domains add the targets of this type to what the source owns.</summary>
        ControlGrant,
    }

    /// <summary>
    /// Link constraints for one relationship type, enforced on every link of that type regardless of caller.
    /// Counterpart of the tag rule set: <c>blockedAny</c> / <c>removed</c> name other relationship types between the same pair.
    /// </summary>
    public sealed class RelationshipTypeRulesConfig
    {
        /// <summary>Most links of this type a target may receive; 0 = unlimited.</summary>
        public int MaxIncoming { get; set; }
        /// <summary>Most links of this type a source may hold; 0 = unlimited.</summary>
        public int MaxOutgoing { get; set; }
        /// <summary>Required whenever a maximum is set.</summary>
        public RelationshipCapacityPolicy OnFull { get; set; }
        /// <summary>Reject a link that would close a cycle along this type.</summary>
        public bool Acyclic { get; set; }
        /// <summary>Reject the link when the same source → target pair already has any of these types.</summary>
        public List<string> BlockedAny { get; set; } = new();
        /// <summary>Remove these types from the same source → target pair when the link is made.</summary>
        public List<string> Removed { get; set; } = new();
    }

    public enum RelationshipCapacityPolicy
    {
        None = 0,
        /// <summary>The new link fails loudly; existing links stay.</summary>
        Reject,
        /// <summary>The existing link is removed and the new one takes its place; only valid with a maximum of 1.</summary>
        Replace,
    }

    /// <summary>
    /// Birth components for materialized relationship entities of a type; same component-dictionary
    /// authoring shape as <c>EntityTemplate.Components</c>, resolved through the ComponentRegistry chain.
    /// </summary>
    public sealed class RelationshipTypeTemplateConfig
    {
        public Dictionary<string, JsonNode> Components { get; set; } = new();
    }

    public sealed class RelationshipMetricConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public short MinValue { get; set; } = -100;
        public short MaxValue { get; set; } = 100;
        public short DefaultValue { get; set; }
    }

    public sealed class RelationshipFlagConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
    }





    public sealed class RelationshipKnowledgeGrantConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string CollectionKey { get; set; } = string.Empty;
        public KnowledgePresence Presence { get; set; }
        public KnowledgePositionAccess Position { get; set; }
        public List<int> AttributeIds { get; set; } = new();
        public List<string> Attributes { get; set; } = new();
        public List<int> RelationshipTypeIds { get; set; } = new();
        public List<string> RelationshipTypes { get; set; } = new();
        public List<int> TagIds { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public int ObservedTick { get; set; }
        public int ExpiryTick { get; set; }
        public int ConfidencePermille { get; set; } = 1000;
    }
}
