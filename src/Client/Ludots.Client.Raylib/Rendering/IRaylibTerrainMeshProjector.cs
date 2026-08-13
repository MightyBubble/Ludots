using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    /// <summary>
    /// Re-draws cached terrain GPU meshes that overlap a world-space AABB.
    /// Used by clip-volume projected Decals (same triangles as the opaque heightfield pass).
    /// </summary>
    public interface IRaylibTerrainMeshProjector
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