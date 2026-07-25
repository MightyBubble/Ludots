using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class ResolvedTerrainPresentationTests
    {
        [Test]
        public void Resolve_BoardTerrainReturnsTheNamedBoardsVertexMap()
        {
            MapSession session = CreateSession(new TerrainPresentationBindingConfig
            {
                Source = TerrainPresentationSource.BoardTerrain,
                BoardName = "battle",
            });
            var expected = new VertexMap();
            session.AddBoard(CreateBoard("other", new VertexMap()));
            session.AddBoard(CreateBoard("battle", expected));

            ResolvedTerrainPresentation? resolved = ResolvedTerrainPresentation.Resolve(session);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.Not.Null);
                Assert.That(resolved!.Source, Is.EqualTo(TerrainPresentationSource.BoardTerrain));
                Assert.That(resolved.BoardName, Is.EqualTo("battle"));
                Assert.That(resolved.BoardTerrain, Is.SameAs(expected));
                Assert.That(resolved.VisualHeightmap, Is.Null);
            });
        }

        [Test]
        public void Resolve_BoardTerrainFailsWhenTheNamedBoardIsMissingOrHasNoTerrain()
        {
            MapSession missingBoard = CreateSession(new TerrainPresentationBindingConfig
            {
                Source = TerrainPresentationSource.BoardTerrain,
                BoardName = "battle",
            });
            Assert.That(
                () => ResolvedTerrainPresentation.Resolve(missingBoard),
                Throws.InvalidOperationException.With.Message.Contains("not part of the map session"));

            MapSession missingTerrain = CreateSession(new TerrainPresentationBindingConfig
            {
                Source = TerrainPresentationSource.BoardTerrain,
                BoardName = "battle",
            });
            missingTerrain.AddBoard(CreateBoard("battle", vertexMap: null));
            Assert.That(
                () => ResolvedTerrainPresentation.Resolve(missingTerrain),
                Throws.InvalidOperationException.With.Message.Contains("no loaded VertexMap"));
        }

        [Test]
        public void Resolve_VisualHeightmapRejectsBoardNameAndRequiresLoadedHeightmap()
        {
            MapSession conflicting = CreateSession(new TerrainPresentationBindingConfig
            {
                Source = TerrainPresentationSource.VisualHeightmap,
                BoardName = "default",
            });
            conflicting.VisualHeightmap = new FlatVisualHeightmap();
            Assert.That(
                () => ResolvedTerrainPresentation.Resolve(conflicting),
                Throws.InvalidOperationException.With.Message.Contains("must not also declare BoardName"));

            MapSession missing = CreateSession(new TerrainPresentationBindingConfig
            {
                Source = TerrainPresentationSource.VisualHeightmap,
            });
            Assert.That(
                () => ResolvedTerrainPresentation.Resolve(missing),
                Throws.InvalidOperationException.With.Message.Contains("no visual heightmap was loaded"));
        }

        private static MapSession CreateSession(TerrainPresentationBindingConfig binding)
        {
            return new MapSession(
                new MapId("terrain-presentation-test"),
                new MapConfig
                {
                    Id = "terrain-presentation-test",
                    TerrainPresentation = binding,
                });
        }

        private static GridBoard CreateBoard(string name, VertexMap? vertexMap)
        {
            return new GridBoard(
                new BoardId(name),
                name,
                new BoardConfig
                {
                    Name = name,
                    WidthInMacroTiles = 1,
                    HeightInMacroTiles = 1,
                })
            {
                VertexMap = vertexMap!,
            };
        }
    }
}
