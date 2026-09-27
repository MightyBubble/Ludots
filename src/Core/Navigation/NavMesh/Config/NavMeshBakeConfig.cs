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

        /// <summary>Per-map nav tile grids keyed by map id then board name (#1567: nav-owned home).</summary>
        public Dictionary<string, NavMapNavBoardsConfig> Maps { get; set; } = new Dictionary<string, NavMapNavBoardsConfig>();
        public NavRuntimeIncrementalConfig RuntimeIncremental { get; set; } = new NavRuntimeIncrementalConfig();
        public string TerrainFeed { get; set; } = NavBakeNames.TerrainFeedTriangles;

        public NavBakeMode ParsedMode => NavBakeNames.ParseMode(Mode, "NavMeshBakeConfig.mode");

        public NavBakeAlgorithmKind ParsedAlgorithm => NavBakeNames.ParseAlgorithm(Algorithm, "NavMeshBakeConfig.algorithm");

        public NavTerrainFeedKind ParsedTerrainFeed => NavBakeNames.ParseTerrainFeed(TerrainFeed, "NavMeshBakeConfig.terrainFeed");
    }

    public sealed class NavMeshAgentProfileConfig
    {
        public string Id { get; set; } = string.Empty;
        public int MaxClimbCm { get; set; }
        public float MaxSlopeDeg { get; set; }

        /// <summary>
        /// Recast column size in centimeters for a continuous-height source.
        /// Zero means the baker derives it from the agent radius.
        /// </summary>
        public int CellSizeCm { get; set; }
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

    public sealed class NavRuntimeIncrementalConfig
    {
        public int TileBudgetPerFixedTick { get; set; }
        public bool IncludeNeighborTiles { get; set; }
        public float HeightScaleMeters { get; set; }
        public float MinWalkableUpDot { get; set; }
        public int CliffHeightThreshold { get; set; }
    }

    public sealed class NavMapNavBoardsConfig
    {
        public Dictionary<string, NavTileGridConfig> Boards { get; set; } = new Dictionary<string, NavTileGridConfig>();
    }
}
