using System;
using Ludots.Core.Navigation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    internal sealed class NavContinuousHeightTerrainField : LogicTerrainField
    {
        private readonly LogicTerrainField _classification;
        private readonly IVisualHeightmap _heightmap;

        public NavContinuousHeightTerrainField(LogicTerrainField classification, IVisualHeightmap heightmap)
            : base(
                (classification ?? throw new ArgumentNullException(nameof(classification))).WidthCells,
                classification.HeightCells,
                classification.ChunkSizeCells)
        {
            _classification = classification;
            _heightmap = heightmap ?? throw new ArgumentNullException(nameof(heightmap));
        }

        public override LogicTerrainTopology Topology => _classification.Topology;

        public override int HorizontalStepCm => _classification.HorizontalStepCm;

        public override int VerticalStepCm => _classification.VerticalStepCm;

        public override LogicTerrainCell GetCell(int col, int row)
            => _classification.GetCell(col, row);

        public override bool TryGetCliffStraightenEdge(int col, int row, int edgeIndex, out bool value)
            => _classification.TryGetCliffStraightenEdge(col, row, edgeIndex, out value);

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
            => _classification.GetWorldPositionMeters(col, row, out xMeters, out zMeters);

        public override float GetHeightMeters(int col, int row, float heightScaleMeters)
        {
            GetWorldPositionMeters(col, row, out float xMeters, out float zMeters);
            float xCm = xMeters * 100f;
            float zCm = zMeters * 100f;
            if (!_heightmap.TrySampleHeightCm(xCm, zCm, out float heightCm))
            {
                throw new InvalidOperationException(
                    $"Continuous heightmap sampling failed at terrain cell ({col},{row}) world=({xCm:0.##},{zCm:0.##})cm.");
            }

            return heightCm / 100f;
        }
    }
}
