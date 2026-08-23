using System;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Tool;
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

        [Test]
        public void GridCellSize_IsRequiredBySharedToolResolver()
        {
            var board = new BoardConfig
            {
                Name = "missing-cell-size",
                SpatialType = "Grid",
                GridCellSizeCm = 0
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ToolMapConfigResolver.RequireGridCellSizeCm(board))!;

            Assert.That(error.Message, Does.Contain("GridCellSizeCm"));
        }

        [Test]
        public void BoardResolver_RequiresAnExactNavigationBoard()
        {
            var map = new Ludots.Core.Config.MapConfig
            {
                Id = "multi-board",
                Boards =
                {
                    new BoardConfig { Name = "logic", SpatialType = "Grid", NavigationEnabled = true },
                    new BoardConfig { Name = "routes", SpatialType = "NodeGraph", NavigationEnabled = false }
                }
            };

            Assert.Throws<InvalidOperationException>(() => ToolMapConfigResolver.ResolveBoardByName(map, "missing"));
            Assert.Throws<InvalidOperationException>(() => ToolMapConfigResolver.ResolveBoardByName(map, "routes"));
            Assert.That(ToolMapConfigResolver.ResolveBoardByName(map, "logic").Name, Is.EqualTo("logic"));
        }

        [Test]
        public void BakeContext_RejectsPolicyInstanceThatWasNotValidatedByInput()
        {
            var board = new BoardConfig
            {
                Name = "logic",
                SpatialType = "Grid",
                NavBakePolicy = NavBakePolicy.ForBoardLogicTerrain()
            };
            var terrain = new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4);
            var input = new NavBakeInput(board, terrain, null, new NavObstacleSet(), null);
            var context = new NavBakeContext
            {
                SourceUri = "Core:test",
                Input = input,
                Policy = input.Policy.Clone(),
                Terrain = terrain,
                Obstacles = new NavObstacleSet()
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.Validate())!;

            Assert.That(error.Message, Does.Contain("validated by").IgnoreCase);
        }
    }
}
