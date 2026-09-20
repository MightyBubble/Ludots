using System;

namespace Ludots.Core.Presentation.Rendering
{
    /// <summary>
    /// Fail-fast validation for adapters that must publish NavMesh tile geometry from the formal presentation buffer.
    /// </summary>
    public static class NavMeshPresentationCapabilityValidator
    {
        public static void Require(PresentationAdapterCapabilities? capabilities)
        {
            if (capabilities == null ||
                (capabilities.Visuals & PresentationVisualCapabilities.NavMeshTileGeometry) == 0)
            {
                throw new InvalidOperationException(
                    "Presentation adapter must declare PresentationVisualCapabilities.NavMeshTileGeometry " +
                    "before NavMesh presentation can enable. Unsupported adapters fail fast (no silent disable).");
            }
        }
    }
}
