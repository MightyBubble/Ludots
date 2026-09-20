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
        public List<RelationshipReasonConfig> Reasons { get; set; } = new();
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
        public RelationshipTypeTemplateConfig? Template { get; set; }
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


    public sealed class RelationshipReasonConfig : IIdentifiable
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
