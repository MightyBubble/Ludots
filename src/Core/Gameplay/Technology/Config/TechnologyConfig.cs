using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Technology.Config
{
    public sealed class TechnologyConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string? Scope { get; set; }
    }

    public sealed class TechnologyScopeConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
    }

    public sealed class TechnologyRequirementConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public TechnologyRequirementNodeConfig Root { get; set; } = new();
    }

    public sealed class TechnologyRequirementNodeConfig
    {
        public string Kind { get; set; } = string.Empty;
        public string? Scope { get; set; }
        public string? EntitySource { get; set; }
        public string? Technology { get; set; }
        public string? Graph { get; set; }
        public int Count { get; set; }
        public int Level { get; set; }
        public List<string>? Tags { get; set; }
        public List<TechnologyRequirementNodeConfig>? Children { get; set; }
        public TechnologyRequirementNodeConfig? Child { get; set; }
    }
}
