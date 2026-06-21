using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavMeshBakeConfig
    {
        public string Mode { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
        public List<NavMeshAgentProfileConfig> Profiles { get; set; } = new List<NavMeshAgentProfileConfig>();
        public List<NavLayerConfig> Layers { get; set; } = new List<NavLayerConfig>();
        public List<NavAreaCostConfig> Areas { get; set; } = new List<NavAreaCostConfig>();

        public NavBakeMode ParsedMode => NavBakeNames.ParseMode(Mode, "NavMeshBakeConfig.mode");

        public NavBakeAlgorithmKind ParsedAlgorithm => NavBakeNames.ParseAlgorithm(Algorithm, "NavMeshBakeConfig.algorithm");
    }

    public sealed class NavMeshAgentProfileConfig
    {
        public string Id { get; set; } = string.Empty;
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
