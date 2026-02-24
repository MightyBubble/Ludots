using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public sealed class GraphConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = "Effect";
        public string Entry { get; set; } = string.Empty;
        public List<GraphNodeConfig> Nodes { get; set; } = new();
    }

    public sealed class GraphNodeConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public string? Next { get; set; }
        public List<string> Inputs { get; set; } = new();

        public float FloatValue { get; set; }
        public int IntValue { get; set; }
        public bool BoolValue { get; set; }

        public string? Tag { get; set; }
        public string? Attribute { get; set; }
        public string? EffectTemplate { get; set; }

        public float Radius { get; set; }
        public int Limit { get; set; }
        public string? Sort { get; set; }
    }
}

