using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Progression.Config
{
    public sealed class ProgressionConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string? Scope { get; set; }
    }

    public sealed class ProgressionScopeConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string? MemberSource { get; set; }
        public string? Collection { get; set; }
        public string? RelationshipType { get; set; }
        public string? RelationshipDirection { get; set; }
    }

    public sealed class ProgressionRequirementConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public ProgressionRequirementNodeConfig Root { get; set; } = new();
    }

    public sealed class ProgressionRequirementNodeConfig
    {
        public string Kind { get; set; } = string.Empty;
        public string? Scope { get; set; }
        public string? EntitySource { get; set; }
        public string? Progression { get; set; }
        public string? Graph { get; set; }
        public int Count { get; set; }
        public int Level { get; set; }
        public List<string>? Tags { get; set; }
        public List<ProgressionRequirementNodeConfig>? Children { get; set; }
        public ProgressionRequirementNodeConfig? Child { get; set; }
    }
}
