using System;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavBakePolicyContractTests
    {
        [Test]
        public void TerrainBoard_AllowsContinuousHeightAndBoardClassificationTogether()
        {
            var board = new BoardConfig
            {
                Name = "relief",
                SpatialType = "Grid",
                VisualHeightmapAsset = "terrain/relief.vhtm",
                NavBakePolicy = new NavBakePolicy
                {
                    HeightSource = NavBakeSourceKinds.ContinuousHeightmap,
                    ClassificationSource = NavBakeSourceKinds.BoardLogicTerrain,
                    StaticObstacleSource = NavBakeSourceKinds.MapEntities,
                    RuntimeObstacleSource = NavBakeSourceKinds.RuntimeEntities
                }
            };

            NavBakePolicy resolved = NavBakePolicyValidator.Require(board);

            Assert.That(resolved.HeightSource, Is.EqualTo(NavBakeSourceKinds.ContinuousHeightmap));
            Assert.That(resolved.ClassificationSource, Is.EqualTo(NavBakeSourceKinds.BoardLogicTerrain));
        }

        [Test]
        public void NodeGraph_RejectsAnyNavMeshRole()
        {
            var board = new BoardConfig
            {
                Name = "routes",
                SpatialType = "NodeGraph",
                NavBakePolicy = new NavBakePolicy
                {
                    HeightSource = NavBakeSourceKinds.BoardLogicTerrain,
                    ClassificationSource = NavBakeSourceKinds.BoardLogicTerrain,
                    StaticObstacleSource = NavBakeSourceKinds.None,
                    RuntimeObstacleSource = NavBakeSourceKinds.None
                }
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => NavBakePolicyValidator.Require(board))!;

            Assert.That(error.Message, Does.Contain("NodeGraph"));
        }

        [Test]
        public void MissingSelectedInput_FailsBeforeBake()
        {
            var board = new BoardConfig
            {
                Name = "relief",
                SpatialType = "Grid",
                VisualHeightmapAsset = "terrain/relief.vhtm",
                NavBakePolicy = new NavBakePolicy
                {
                    HeightSource = NavBakeSourceKinds.ContinuousHeightmap,
                    ClassificationSource = NavBakeSourceKinds.BoardLogicTerrain,
                    StaticObstacleSource = NavBakeSourceKinds.None,
                    RuntimeObstacleSource = NavBakeSourceKinds.None
                }
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => new NavBakeInput(
                    board,
                    new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                    continuousHeightmap: null,
                    staticObstacles: null,
                    runtimeStructuralObstacles: null))!;

            Assert.That(error.Message, Does.Contain("continuousHeightmap"));
        }

        [Test]
        public void RuntimeEntitySource_UsesEmptyBakeTimeSelection()
        {
            var board = new BoardConfig
            {
                Name = "runtime-obstacles",
                SpatialType = "Grid",
                VisualHeightmapAsset = "terrain/runtime-obstacles.vhtm",
                NavBakePolicy = new NavBakePolicy
                {
                    HeightSource = NavBakeSourceKinds.ContinuousHeightmap,
                    ClassificationSource = NavBakeSourceKinds.BoardLogicTerrain,
                    StaticObstacleSource = NavBakeSourceKinds.None,
                    RuntimeObstacleSource = NavBakeSourceKinds.RuntimeEntities
                }
            };

            Assert.DoesNotThrow(() => new NavBakeInput(
                board,
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                new FlatVisualHeightmap(),
                new NavObstacleSet(),
                new NavObstacleSet()));
        }
    }
}
