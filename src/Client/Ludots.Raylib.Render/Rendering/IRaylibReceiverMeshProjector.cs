using System.Numerics;
using Raylib_cs;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Re-draws GPU meshes that overlap a world-space AABB so a projected Decal can paint them.
    /// Visual-heightmap chunks are the first implementation; VertexMap and prop receivers bind the same contract.
    /// Fit must sample the bound receiver; implementations that cannot fit throw instead of leaving authored Y.
    /// </summary>
    public interface IRaylibReceiverMeshProjector
    {
        int DrawMeshesOverlappingAabbMeters(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            Material material);

        Vector3 FitYawedStampProjectorCenter(
            in Vector3 stampCenter,
            float yawRad,
            in Vector2 stampSizeMeters,
            int stableId);
    }
}
