using System;
using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.Terrain;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public static class NavBakeTerrainLoader
    {
        public static LogicTerrainField LoadVertexMapTerrain(IVirtualFileSystem vfs, string sourceUri)
        {
            if (vfs == null) throw new ArgumentNullException(nameof(vfs));
            if (string.IsNullOrWhiteSpace(sourceUri))
            {
                throw new ArgumentException("sourceUri is required.", nameof(sourceUri));
            }

            using var stream = vfs.GetStream(sourceUri);
            VertexMap map = VertexMapBinary.Read(stream);
            return new VertexMapLogicTerrainField(map);
        }
    }
}
