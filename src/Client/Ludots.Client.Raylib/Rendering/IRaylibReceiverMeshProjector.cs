using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    /// <summary>
    /// Re-draws GPU meshes that overlap a world-space AABB so a projected Decal can paint them.
    /// Visual-heightmap chunks are the first implementation; VertexMap and prop receivers bind the same contract.
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
    }
}
