using System;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// Data-driven Recast raster cell sizes owned by Core.
    /// Every operational value is explicit; no defaults or radius-derived fallback.
    /// </summary>
    public sealed class NavRecastConfig
    {
        public int RasterCellSizeCm { get; set; }
        public int RasterCellHeightCm { get; set; }

        public void Validate(string path = "NavMeshBakeConfig.recast")
        {
            RequirePositive(RasterCellSizeCm, nameof(RasterCellSizeCm), path);
            RequirePositive(RasterCellHeightCm, nameof(RasterCellHeightCm), path);
        }

        private static void RequirePositive(int value, string field, string path)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException($"{path}.{field} must be > 0.");
            }
        }
    }
}
