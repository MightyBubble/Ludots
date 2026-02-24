using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavMeshBakeConfig
    {
        public List<NavAgentProfileConfig> Profiles { get; set; } = new List<NavAgentProfileConfig>();
        public List<NavLayerConfig> Layers { get; set; } = new List<NavLayerConfig>();
        public List<NavAreaCostConfig> Areas { get; set; } = new List<NavAreaCostConfig>();
    }

    public sealed class NavAgentProfileConfig
    {
        public string Id { get; set; } = string.Empty;
        public int RadiusCm { get; set; }
        public int HeightCm { get; set; }
        public int MaxClimbCm { get; set; }
        public float MaxSlopeDeg { get; set; }
    }

    public sealed class NavLayerConfig
    {
        public string Id { get; set; } = string.Empty;
        public int Layer { get; set; }
    }

    public sealed class NavAreaCostConfig
    {
        public string Id { get; set; } = string.Empty;
        public int AreaId { get; set; }
        public float Cost { get; set; } = 1f;
    }
}
