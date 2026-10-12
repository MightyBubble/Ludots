using System.Collections.Generic;
using System.Text.Json.Serialization;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavMeshBakeConfig
    {
        public string Mode { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;

        [JsonConverter(typeof(NavMeshProfileCatalogConverter))]
        public List<NavMeshAgentProfileConfig> Profiles { get; set; } = new List<NavMeshAgentProfileConfig>();

        public List<NavLayerConfig> Layers { get; set; } = new List<NavLayerConfig>();

        [JsonConverter(typeof(NavMeshAreaCatalogConverter))]
        public List<NavMeshAreaConfig> Areas { get; set; } = new List<NavMeshAreaConfig>();

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

        /// <summary>
        /// 省略表示不限坡度。写出 0 或负数会被装载拒绝。
        /// Recast 烘焙必须有正的坡度上限，在烘焙时校验。
        /// </summary>
        public float? MaxSlopeDeg { get; set; }

        /// <summary>
        /// 这个能力档对应的体型 id，经 AgentProfileRegistry.Require 解析。
        /// Recast 烘焙用它做碰撞体。不经 Recast 烘焙的能力档也要写。
        /// </summary>
        public string AgentProfileId { get; set; }

        /// <summary>
        /// Recast column size in centimeters for a continuous-height source.
        /// Zero means the baker derives it from the agent radius.
        /// Crowd sessions require every capability profile they use to share one positive value;
        /// that value is the nav cell size.
        /// </summary>
        public int CellSizeCm { get; set; }

        public NavMeshJumpConfig Jump { get; set; }
    }

    public sealed class NavMeshJumpConfig
    {
        public int UpCm { get; set; }
        public int DownCm { get; set; }
        public int RangeNavCells { get; set; }
        public float Cost { get; set; }
    }

    public sealed class NavLayerConfig
    {
        public string Id { get; set; } = string.Empty;
        public int Layer { get; set; }
    }

    public sealed class NavMeshAreaConfig
    {
        public string Id { get; set; } = string.Empty;
        public int AreaId { get; set; }

        /// <summary>平面区域：几何 profile 的坡度上限不适用于这个区域。</summary>
        public bool SlopeFree { get; set; }
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

        /// <summary>
        /// 本图烘焙并装载瓦片库的能力档 id。数组顺序就是烘焙和装载顺序，不按键名重排。
        /// </summary>
        public List<string> NavProfiles { get; set; }

        /// <summary>
        /// 本图要校验代价行的区域 id。绑定了移动类型的地图必填，顺序不参与校验。
        /// </summary>
        public List<string> NavAreas { get; set; }

        /// <summary>有序绑定。数组下标就是 .navsurface 里的地形类型编号。</summary>
        public List<NavTerrainAreaBinding> TerrainAreas { get; set; }
    }

    public sealed class NavTerrainAreaBinding
    {
        public string TerrainId { get; set; } = string.Empty;
        public string AreaId { get; set; } = string.Empty;
    }
}
