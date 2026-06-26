using System;
using Ludots.Core.Map.Fields;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public sealed class NavObstacleAreaProjectionSource : ILogicTerrainAreaSource
    {
        private readonly NavObstacleSet _obstacles;
        private readonly string _layerId;

        public NavObstacleAreaProjectionSource(NavObstacleSet obstacles, string layerId)
        {
            _obstacles = obstacles ?? throw new ArgumentNullException(nameof(obstacles));
            if (string.IsNullOrWhiteSpace(layerId) ||
                !string.Equals(layerId.Trim(), layerId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Area projection requires a non-empty trimmed nav layer id.", nameof(layerId));
            }

            _layerId = layerId;
        }

        public bool TryGetAreaId(int col, int row, int worldXCm, int worldYCm, out byte areaId)
            => NavObstacleGeometry.TryResolveAreaIdAtPoint(worldXCm, worldYCm, _obstacles, _layerId, out areaId);
    }
}
