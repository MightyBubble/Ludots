using System.Numerics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.FlowField;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    public sealed class Navigation2DFlowCrowdFieldTests
    {
        private const int CellSizeCm = 100;
        private const int TileSizeCells = 64;

        [Test]
        public void CrowdFlow_DenseOpposingLane_IncreasesPotentialAndCreatesLateralBias()
        {
            var streaming = new Navigation2DFlowStreamingConfig
            {
                Enabled = true,
                ActivationRadiusTiles = 0,
                MaxActiveTilesPerFlow = 1,
                UnloadGraceTicks = 1,
                MaxPotentialCells = 512f,
            };
            var crowd = new Navigation2DFlowCrowdConfig
            {
                Enabled = true,
                Density = new Navigation2DFlowCrowdDensityConfig
                {
                    Min = 0.2f,
                    Max = 1.2f,
                    Exponent = 0.3f,
                },
                Speed = new Navigation2DFlowCrowdSpeedConfig
                {
                    MinFactor = 0.15f,
                    MaxFactor = 1f,
                    FlowVelocityScaleCmPerSec = 600f,
                },
                Cost = new Navigation2DFlowCrowdCostConfig
                {
                    DistanceWeight = 0.2f,
                    TimeWeight = 0.8f,
                    DiscomfortWeight = 0f,
                    NormalizeDistanceAndTimeWeights = true,
                },
            };

            var surface = new CrowdSurface2D(Fix64.FromInt(CellSizeCm), TileSizeCells, initialTileCapacity: 4);
            using var flow = new CrowdFlow2D(surface, streaming, crowd, hasLoadedTileSource: false, initialTileCapacity: 4, initialFrontierCapacity: 4096);

            flow.SetGoalPoint(CellCenterCm(0, 0), Fix64.Zero);
            flow.BeginDemandFrame(1);
            flow.AddDemandPoint(CellCenterCm(4, 0));
            flow.PrepareFrame();

            surface.ClearObstacleField();
            surface.ClearCrowdFields();
            for (int repeat = 0; repeat < 12; repeat++)
            {
                for (int cellX = 1; cellX <= 4; cellX++)
                {
                    surface.SplatDensity(CellCenterCm(cellX, 0).ToVector2(), new Vector2(900f, 0f), crowd.Density.Exponent, createTilesIfMissing: false);
                }
            }

            surface.NormalizeAverageVelocityField();
            flow.MarkCrowdFieldsDirty();
            flow.Step(65536);

            Assert.That(surface.TryGetDensityCell(3, 0, out float density), Is.True);
            Assert.That(density, Is.GreaterThan(1f));
            Assert.That(surface.TryGetAverageVelocityCell(3, 0, out Vector2 averageVelocity), Is.True);
            Assert.That(averageVelocity.X, Is.GreaterThan(100f));

            Assert.That(flow.TryGetPotentialAtCell(4, 0, out float congestedPotential), Is.True);
            Assert.That(flow.TryGetPotentialAtCell(4, 1, out float sideLanePotential), Is.True);
            Assert.That(sideLanePotential, Is.LessThan(congestedPotential));

            Assert.That(flow.TrySampleDesiredVelocityCm(CellCenterCm(4, 0), Fix64.FromInt(800), out Fix64Vec2 desiredVelocity), Is.True);
            Vector2 desired = desiredVelocity.ToVector2();
            Assert.That(desired.X, Is.LessThan(-10f));
            Assert.That(System.MathF.Abs(desired.Y), Is.GreaterThan(10f));
        }

        private static Fix64Vec2 CellCenterCm(int cellX, int cellY)
        {
            return Fix64Vec2.FromInt(cellX * CellSizeCm + (CellSizeCm / 2), cellY * CellSizeCm + (CellSizeCm / 2));
        }
    }
}
