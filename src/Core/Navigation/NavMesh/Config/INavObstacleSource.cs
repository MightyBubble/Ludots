using System.Collections.Generic;
using System.Text;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// Single obstacle-source contract for bake consumers.
    /// Cold/offline authoring uses <see cref="NavObstacleSet"/>; runtime uses fixed-capacity SoA snapshot.
    /// </summary>
    public interface INavObstacleSource
    {
        int ObstacleCount { get; }

        void ValidateForBake(IReadOnlyList<NavLayerConfig> layers, string pathPrefix);

        bool IsEnabled(int index);

        NavObstacleKind GetKind(int index);

        bool MatchesLayer(int index, string layerId);

        bool TryGetAreaId(int index, out byte areaId);

        void GetCircle(int index, out int centerXcm, out int centerZcm, out int radiusCm);

        int GetPolygonVertexCount(int index);

        void GetPolygonVertex(int index, int vertexIndex, out int xcm, out int zcm);

        /// <summary>
        /// Absolute world-centimetre vertical half-open interval [minYcm, maxYcm) with minYcm &lt; maxYcm.
        /// </summary>
        void GetVerticalRange(int index, out int minYcm, out int maxYcm);

        void AppendHash(int index, StringBuilder sb);
    }
}
