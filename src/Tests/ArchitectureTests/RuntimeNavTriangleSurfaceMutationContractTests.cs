using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class RuntimeNavTriangleSurfaceMutationContractTests
    {
        [Test]
        public void RuntimeNavTriangleSurfaceService_Publish_BumpsGenerationAndRejectsNull()
        {
            NavTriangleSurfaceTileIndex first = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            var service = new RuntimeNavTriangleSurfaceService(first);
            Assert.That(service.ContentGeneration, Is.EqualTo(1UL));
            Assert.That(service.Published.Surface.TriangleCount, Is.EqualTo(8));

            NavTriangleSurfaceTileIndex second = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            service.Publish(second);
            Assert.That(service.ContentGeneration, Is.EqualTo(2UL));
            Assert.That(service.Published, Is.SameAs(second));

            Assert.Throws<ArgumentNullException>(() => service.Publish(null!));
        }

        [Test]
        public void TerrainBrush_Block_RemovesWalkableCellsAndDirtyAabbCoversAffectedTilesOnly()
        {
            NavTriangleSurfaceTileIndex source = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            Assert.That(source.Surface.TriangleCount, Is.EqualTo(8));

            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 250,
                centerZcm: 250,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Block,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex blocked = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out WorldAabbCm dirty);
            Assert.That(blocked.Surface.TriangleCount, Is.GreaterThan(0));
            Assert.That(blocked.Surface.TriangleCount, Is.Not.EqualTo(source.Surface.TriangleCount));
            Assert.That(dirty.Left, Is.EqualTo(200));
            Assert.That(dirty.Top, Is.EqualTo(200));
            Assert.That(dirty.Right, Is.EqualTo(300));
            Assert.That(dirty.Bottom, Is.EqualTo(300));

            WorldAabbCm restoreDirty = NavTriangleSurfaceTerrainBrush.ComputeChangedTileAabb(blocked, source);
            Assert.That(restoreDirty.Left, Is.EqualTo(0));
            Assert.That(restoreDirty.Top, Is.EqualTo(0));
            Assert.That(restoreDirty.Right, Is.EqualTo(800));
            Assert.That(restoreDirty.Bottom, Is.EqualTo(800));
        }

        [Test]
        public void TerrainBrush_Raise_ChangesVertexHeightInsideBrush()
        {
            NavTriangleSurfaceTileIndex source = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 150,
                centerZcm: 150,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Raise,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 3,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex raised = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out _);
            bool sawRaised = false;
            ReadOnlySpan<int> vy = raised.Surface.VertexYcm;
            for (int i = 0; i < vy.Length; i++)
            {
                if (vy[i] == 300)
                {
                    sawRaised = true;
                    break;
                }
            }

            Assert.That(sawRaised, Is.True, "Raised brush must emit vertices at height level 3 (300cm).");
        }

        [Test]
        public void TerrainBrush_BlockThenRepublishBeforeImage_RestoresTriangleCount()
        {
            NavTriangleSurfaceTileIndex source = CompileFlat(8, 8, chunkSize: 4, halo: 100);
            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 250,
                centerZcm: 250,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Block,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex blocked = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out _);
            var service = new RuntimeNavTriangleSurfaceService(source);
            service.Publish(blocked);
            Assert.That(service.ContentGeneration, Is.EqualTo(2UL));
            service.Publish(source);
            Assert.That(service.Published.Surface.TriangleCount, Is.EqualTo(source.Surface.TriangleCount));
            Assert.That(service.ContentGeneration, Is.EqualTo(3UL));
        }

        [Test]
        public void TerrainBrush_GroundBandEdit_PreservesStackedBridgeTrianglesExactly()
        {
            NavTriangleSurfaceTileIndex source = CreateGroundWithBridge();
            var spec = new NavTriangleSurfaceTerrainBrushSpec(
                centerXcm: 200,
                centerZcm: 200,
                halfExtentCm: 50,
                kind: NavTriangleSurfaceTerrainBrushKind.Block,
                cellSizeCm: 100,
                heightScaleMeters: 1f,
                baseHeightLevel: 0,
                raiseHeightLevel: 2,
                targetMinYcm: -10,
                targetMaxYcm: 10);

            NavTriangleSurfaceTileIndex edited = NavTriangleSurfaceTerrainBrush.Apply(source, spec, out WorldAabbCm dirty);

            Assert.That(dirty, Is.EqualTo(new WorldAabbCm(100, 100, 200, 200)));
            AssertTriangleCoordinatesEqualByStableId(source.Surface, edited.Surface, stableId: 20);
            AssertTriangleCoordinatesEqualByStableId(source.Surface, edited.Surface, stableId: 21);
        }

        private static NavTriangleSurfaceTileIndex CompileFlat(int widthCells, int heightCells, int chunkSize, int halo)
        {
            var terrain = new FlatGridLogicTerrainField(widthCells, heightCells, cellSizeCm: 100, chunkSizeCells: chunkSize);
            var build = new NavBuildConfig(1f, 0.6f, 1);
            return LogicTerrainTriangleSurfaceCompiler.Compile(terrain, build, halo);
        }

        private static NavTriangleSurfaceTileIndex CreateGroundWithBridge()
        {
            NavTriangleSurfaceFlags walk = NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;
            var snapshot = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 400, 400, 0, 100, 300, 300, 100 },
                vertexYcm: new[] { 0, 0, 0, 0, 500, 500, 500, 500 },
                vertexZcm: new[] { 0, 0, 400, 400, 100, 100, 300, 300 },
                triA: new[] { 0, 0, 4, 4 },
                triB: new[] { 1, 2, 5, 6 },
                triC: new[] { 2, 3, 6, 7 },
                triAreaIds: new byte[] { 0, 0, 0, 0 },
                triStableIds: new[] { 10, 11, 20, 21 },
                triFlags: new[] { walk, walk, walk, walk });
            return NavTriangleSurfaceTileIndex.Build(
                snapshot,
                new NavTriangleSurfaceTileGrid(0, 0, 400, 400, 1, 1, haloPaddingCm: 0));
        }

        private static void AssertTriangleCoordinatesEqualByStableId(
            NavTriangleSurfaceSnapshot expected,
            NavTriangleSurfaceSnapshot actual,
            int stableId)
        {
            int expectedIndex = FindTriangleByStableId(expected, stableId);
            int actualIndex = FindTriangleByStableId(actual, stableId);
            Assert.That(actualIndex, Is.GreaterThanOrEqualTo(0), $"Triangle stable id {stableId} must survive the ground edit.");
            AssertVertexEqual(expected, expected.TriA[expectedIndex], actual, actual.TriA[actualIndex]);
            AssertVertexEqual(expected, expected.TriB[expectedIndex], actual, actual.TriB[actualIndex]);
            AssertVertexEqual(expected, expected.TriC[expectedIndex], actual, actual.TriC[actualIndex]);
        }

        private static int FindTriangleByStableId(NavTriangleSurfaceSnapshot surface, int stableId)
        {
            for (int i = 0; i < surface.TriangleCount; i++)
            {
                if (surface.TriStableIds[i] == stableId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AssertVertexEqual(
            NavTriangleSurfaceSnapshot expected,
            int expectedVertex,
            NavTriangleSurfaceSnapshot actual,
            int actualVertex)
        {
            Assert.That(actual.VertexXcm[actualVertex], Is.EqualTo(expected.VertexXcm[expectedVertex]));
            Assert.That(actual.VertexYcm[actualVertex], Is.EqualTo(expected.VertexYcm[expectedVertex]));
            Assert.That(actual.VertexZcm[actualVertex], Is.EqualTo(expected.VertexZcm[expectedVertex]));
        }
    }
}
