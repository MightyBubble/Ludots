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
        public RelationshipTypeTemplateConfig? Template { get; set; }
    }

    /// <summary>
    /// Engine behavior a relationship type opts into. The catalog must bind every non-None role to
    /// exactly one type; the engine resolves type ids by role and never by type name.
    /// </summary>
    public enum RelationshipRole
    {
        None = 0,
        /// <summary>source owns target; a target has at most one direct owner and inherits PlayerOwner from its root owner.</summary>
        Ownership,
        /// <summary>source is a member of target; a target carrying TeamIdentity projects Team onto the source.</summary>
        Membership,
        /// <summary>source may command target (and target's owned subtree when target is a player rep) in addition to what it owns.</summary>
        ControlGrant,
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
