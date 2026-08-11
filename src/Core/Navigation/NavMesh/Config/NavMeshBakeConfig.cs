using System;
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
        public NavRuntimeIncrementalConfig RuntimeIncremental { get; set; } = new NavRuntimeIncrementalConfig();
        public NavLayeredSpanConfig LayeredSpan { get; set; } = null!;
        public NavTriangleSurfaceConfig TriangleSurface { get; set; } = null!;
        public NavRecastConfig Recast { get; set; } = null!;

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

    public sealed class NavRuntimeIncrementalConfig
    {
        public int TileBudgetPerFixedTick { get; set; }
        public bool IncludeNeighborTiles { get; set; }
        public float HeightScaleMeters { get; set; }
        public float MinWalkableUpDot { get; set; }
        public int CliffHeightThreshold { get; set; }
        public int TrackedStructuralEntityCapacity { get; set; }
        public int ObstaclePrimitiveCapacity { get; set; }
        public int PolygonVertexCapacity { get; set; }

        /// <summary>Fixed dirty-tile ring capacity. Exhaustion fails naming this owner.</summary>
        public int DirtyTileCapacity { get; set; }

        /// <summary>Fixed staged bake-entry capacity (tiles × layers × profiles in one generation).</summary>
        public int StagedEntryCapacity { get; set; }

        /// <summary>Fixed published-receipt capacity for one generation commit.</summary>
        public int PublishedTileCapacity { get; set; }

        /// <summary>Fixed atomic store-group capacity (layer × profile stores in one commit).</summary>
        public int StoreGroupCapacity { get; set; }

        /// <summary>Fixed resident tile slots per NavTileStore.</summary>
        public int ResidentTileCapacity { get; set; }

        /// <summary>Per output-bank tile vertex channel capacity.</summary>
        public int OutputVertexCapacity { get; set; }

        /// <summary>Per output-bank tile triangle channel capacity.</summary>
        public int OutputTriangleCapacity { get; set; }

        /// <summary>Per output-bank tile portal channel capacity.</summary>
        public int OutputPortalCapacity { get; set; }

        /// <summary>Initial resident window origin chunk X (inclusive). Out-of-world fails at bootstrap.</summary>
        public int InitialResidentChunkX { get; set; }

        /// <summary>Initial resident window origin chunk Z (inclusive). Out-of-world fails at bootstrap.</summary>
        public int InitialResidentChunkZ { get; set; }

        /// <summary>Initial resident window width in chunks. Must be &gt; 0.</summary>
        public int InitialResidentWidthChunks { get; set; }

        /// <summary>Initial resident window height in chunks. Must be &gt; 0.</summary>
        public int InitialResidentHeightChunks { get; set; }

        public int InitialResidentTileCount
        {
            get
            {
                if (InitialResidentWidthChunks <= 0 || InitialResidentHeightChunks <= 0)
                {
                    throw new InvalidOperationException(
                        "NavMeshBakeConfig.runtimeIncremental.initialResidentWidthChunks and initialResidentHeightChunks must be > 0.");
                }

                return checked(InitialResidentWidthChunks * InitialResidentHeightChunks);
            }
        }
    }
}
