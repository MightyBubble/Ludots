using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Relationships.Config
{
    public sealed class RelationshipCatalogConfig
    {
        public List<RelationshipTypeConfig> Types { get; set; } = new();
        public List<RelationshipMetricConfig> Metrics { get; set; } = new();
        public List<RelationshipFlagConfig> Flags { get; set; } = new();
        public List<RelationshipBandConfig> Bands { get; set; } = new();
        public List<RelationshipReasonConfig> Reasons { get; set; } = new();
        public List<RelationshipCallbackConfig> Callbacks { get; set; } = new();
        public List<RelationshipSynergyConfig> Synergies { get; set; } = new();
    }

    public sealed class RelationshipTypeConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public bool IsSymmetric { get; set; }
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

    public sealed class RelationshipBandConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string MetricId { get; set; } = string.Empty;
        public string FlagId { get; set; } = string.Empty;
        public short Threshold { get; set; }
        public string Comparison { get; set; } = nameof(RelationshipBandComparison.GreaterOrEqual);
    }

    public sealed class RelationshipReasonConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
    }

    public sealed class RelationshipCallbackConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string MetricId { get; set; } = string.Empty;
        public int? MinimumValue { get; set; }
        public int? MaximumValue { get; set; }
        public string EventKey { get; set; } = string.Empty;
        public string ExitEventKey { get; set; } = string.Empty;
        public List<string> AddTagsToSource { get; set; } = new();
        public List<string> AddTagsToTarget { get; set; } = new();
        public List<string> AddTagsToSourceTeam { get; set; } = new();
        public List<string> AddTagsToTargetTeam { get; set; } = new();
        public List<string> RemoveTagsFromSource { get; set; } = new();
        public List<string> RemoveTagsFromTarget { get; set; } = new();
        public List<string> RemoveTagsFromSourceTeam { get; set; } = new();
        public List<string> RemoveTagsFromTargetTeam { get; set; } = new();
    }

    public sealed class RelationshipSynergyConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public List<string> RequireAllTags { get; set; } = new();
        public int MinimumCount { get; set; } = 1;
        public List<string> ApplyTagsToTeam { get; set; } = new();
        public string EventKey { get; set; } = string.Empty;
    }
}
