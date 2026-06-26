using System;
using Ludots.Core.Modding;
using Ludots.Core.Map.Fields;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public static class NavBakeTerrainLoader
    {
        public static LogicTerrainField LoadLogicTerrain(IVirtualFileSystem vfs, string sourceUri)
        {
            if (vfs == null) throw new ArgumentNullException(nameof(vfs));
            if (string.IsNullOrWhiteSpace(sourceUri))
            {
                throw new ArgumentException("sourceUri is required.", nameof(sourceUri));
            }
            if (!sourceUri.EndsWith(".ltrn", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Nav bake terrain source must be .ltrn LogicTerrain. Legacy terrain requires explicit one-way import: {sourceUri}");
            }

            using var stream = vfs.GetStream(sourceUri);
            return LogicTerrainBinary.Read(stream);
        }
    }
}
