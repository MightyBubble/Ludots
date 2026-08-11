using System;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// Data-driven triangle-surface cold-compile settings owned only where no other SSOT exists.
    /// Shared halo must be explicitly authored and must be >= layered-span raster halo padding.
    /// </summary>
    public sealed class NavTriangleSurfaceConfig
    {
        public int HaloPaddingCm { get; set; }

        public void Validate(
            string path = "NavMeshBakeConfig.triangleSurface",
            NavLayeredSpanConfig? layeredSpan = null)
        {
            if (HaloPaddingCm < 0)
            {
                throw new InvalidOperationException($"{path}.haloPaddingCm must be >= 0.");
            }

            if (layeredSpan != null)
            {
                int layeredHaloPaddingCm = checked(layeredSpan.RasterHaloCells * layeredSpan.RasterCellSizeCm);
                if (HaloPaddingCm < layeredHaloPaddingCm)
                {
                    throw new InvalidOperationException(
                        $"{path}.haloPaddingCm ({HaloPaddingCm}) must be >= " +
                        $"layeredSpan.rasterHaloCells * rasterCellSizeCm ({layeredHaloPaddingCm}).");
                }
            }
        }
    }
}
