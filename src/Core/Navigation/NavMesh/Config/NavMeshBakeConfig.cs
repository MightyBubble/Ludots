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

        /// <summary>
        /// 后台烘焙并发 worker 线程数：脏瓦片按 FIFO 由 N 个 worker 各烤一 tile，发布流式回游戏线程。
        /// 默认 1 保持既有单 worker 行为；突发脏区（如大地图全图重烤）调高可线性提升排干吞吐。
        /// </summary>
        public int BakeWorkerCount { get; set; } = 1;
    }
}
