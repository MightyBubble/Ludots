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
                !capabilities.Visuals.HasFlag(PresentationVisualCapabilities.NavMeshTileGeometry))
            {
                throw new InvalidOperationException(
                    "Presentation adapter must declare PresentationVisualCapabilities.NavMeshTileGeometry " +
                    "before Dynamic NavBake showcase NavMesh presentation can focus. " +
                    "Unsupported adapters must fail at map focus (no silent disable).");
            }
        }
    }
}
