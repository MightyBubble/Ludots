using System;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavTriangleSurfaceContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly NavTriangleSurfaceFlags SolidOnly = NavTriangleSurfaceFlags.Solid;

        [Test]
        public void NavTriangleSurface_OverlappingXzAtDifferentHeights_BothRetainedAndAddressable()
        {
            // Same XZ footprint, different Y - layered geometry must remain two triangles.
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 10, 20, 10, 10, 20, 10 },
                vertexYcm: new[] { 0, 0, 0, 500, 500, 500 },
                vertexZcm: new[] { 10, 10, 20, 10, 10, 20 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 1, 2 },
                triStableIds: new[] { 100, 200 },
                triFlags: new[] { FloorFlags, FloorFlags });

            Assert.That(surface.TriangleCount, Is.EqualTo(2));
            Assert.That(surface.VertexYcm[0], Is.EqualTo(0));
            Assert.That(surface.VertexYcm[3], Is.EqualTo(500));
            Assert.That(surface.TriStableIds[0], Is.EqualTo(100));
            Assert.That(surface.TriStableIds[1], Is.EqualTo(200));
            Assert.That(surface.TriAreaIds[0], Is.EqualTo((byte)1));
            Assert.That(surface.TriAreaIds[1], Is.EqualTo((byte)2));
            Assert.That(surface.TriFlags[0], Is.EqualTo(FloorFlags));
            Assert.That(surface.TriFlags[1], Is.EqualTo(FloorFlags));

            var grid = new NavTriangleSurfaceTileGrid(
                originXcm: 0,
                originZcm: 0,
                tileWidthCm: 100,
                tileHeightCm: 100,
                tileCountX: 1,
                tileCountZ: 1,
                haloPaddingCm: 0);
            var index = NavTriangleSurfaceTileIndex.Build(surface, grid);

            ReadOnlySpan<int> tileTris = index.GetTriangleIndices(0, 0);
            Assert.That(tileTris.Length, Is.EqualTo(2));
            Assert.That(tileTris[0], Is.EqualTo(0));
            Assert.That(tileTris[1], Is.EqualTo(1));
            Assert.That(surface.VertexYcm[surface.TriA[tileTris[0]]], Is.EqualTo(0));
            Assert.That(surface.VertexYcm[surface.TriA[tileTris[1]]], Is.EqualTo(500));
        }

        [Test]
        public void NavTriangleSurface_BoundaryAndHaloAssignment_ReachesAllExpectedTiles()
        {
            // Triangle sits near the shared edge of tiles (0,0) and (1,0).
            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 90, 99, 90 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 10, 10, 20 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });

            var withoutHalo = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 2, 2, haloPaddingCm: 0));
            Assert.That(ToArray(withoutHalo.GetTriangleIndices(0, 0)), Is.EqualTo(new[] { 0 }));
            Assert.That(ToArray(withoutHalo.GetTriangleIndices(1, 0)), Is.Empty);
            Assert.That(ToArray(withoutHalo.GetTriangleIndices(0, 1)), Is.Empty);
            Assert.That(ToArray(withoutHalo.GetTriangleIndices(1, 1)), Is.Empty);

            // Halo 10 expands max X from 99 to 109, so tile (1,0) is also overlapped.
            var withHalo = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 2, 2, haloPaddingCm: 10));
            Assert.That(ToArray(withHalo.GetTriangleIndices(0, 0)), Is.EqualTo(new[] { 0 }));
            Assert.That(ToArray(withHalo.GetTriangleIndices(1, 0)), Is.EqualTo(new[] { 0 }));
            Assert.That(ToArray(withHalo.GetTriangleIndices(0, 1)), Is.Empty);
            Assert.That(ToArray(withHalo.GetTriangleIndices(1, 1)), Is.Empty);

            // Exact tile-boundary max X=100 without halo still reaches the next tile (closed AABB vs half-open tiles).
            var onBoundary = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 80, 100, 80 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 10, 10, 20 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 7 },
                triFlags: new[] { FloorFlags });
            var boundaryIndex = NavTriangleSurfaceTileIndex.Build(
                onBoundary,
                new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 2, 1, haloPaddingCm: 0));
            Assert.That(ToArray(boundaryIndex.GetTriangleIndices(0, 0)), Is.EqualTo(new[] { 0 }));
            Assert.That(ToArray(boundaryIndex.GetTriangleIndices(1, 0)), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void NavTriangleSurface_StableIdOrdering_IsDeterministicAcrossSourceInputOrder()
        {
            var first = BuildSurface(
                vertexXcm: new[] { 10, 20, 10, 30, 40, 30, 50, 60, 50 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 10, 10, 20, 10, 10, 20, 10, 10, 20 },
                triA: new[] { 0, 3, 6 },
                triB: new[] { 1, 4, 7 },
                triC: new[] { 2, 5, 8 },
                triAreaIds: new byte[] { 1, 1, 1 },
                triStableIds: new[] { 30, 10, 20 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags });

            var second = BuildSurface(
                vertexXcm: new[] { 50, 60, 50, 10, 20, 10, 30, 40, 30 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 10, 10, 20, 10, 10, 20, 10, 10, 20 },
                triA: new[] { 0, 3, 6 },
                triB: new[] { 1, 4, 7 },
                triC: new[] { 2, 5, 8 },
                triAreaIds: new byte[] { 1, 1, 1 },
                triStableIds: new[] { 20, 30, 10 },
                triFlags: new[] { FloorFlags, FloorFlags, FloorFlags });

            var grid = new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 1, 1, haloPaddingCm: 0);
            var indexA = NavTriangleSurfaceTileIndex.Build(first, grid);
            var indexB = NavTriangleSurfaceTileIndex.Build(second, grid);

            int[] stableOrderA = StableIdsInTile(indexA, 0, 0);
            int[] stableOrderB = StableIdsInTile(indexB, 0, 0);
            Assert.That(stableOrderA, Is.EqualTo(new[] { 10, 20, 30 }));
            Assert.That(stableOrderB, Is.EqualTo(new[] { 10, 20, 30 }));
            Assert.That(stableOrderA, Is.EqualTo(stableOrderB));
        }

        [Test]
        public void NavTriangleSurface_Snapshot_DefensivelyCopiesCallerChannels()
        {
            var vertexXcm = new[] { 10, 20, 10 };
            var vertexYcm = new[] { 0, 0, 0 };
            var vertexZcm = new[] { 10, 10, 20 };
            var triA = new[] { 0 };
            var triB = new[] { 1 };
            var triC = new[] { 2 };
            var triAreaIds = new byte[] { 9 };
            var triStableIds = new[] { 42 };
            var triFlags = new[] { FloorFlags };

            var surface = new NavTriangleSurfaceSnapshot(
                vertexXcm,
                vertexYcm,
                vertexZcm,
                triA,
                triB,
                triC,
                triAreaIds,
                triStableIds,
                triFlags);

            vertexXcm[0] = 999;
            vertexYcm[1] = 999;
            vertexZcm[2] = 999;
            triA[0] = 2;
            triB[0] = 0;
            triC[0] = 1;
            triAreaIds[0] = 255;
            triStableIds[0] = 777;
            triFlags[0] = SolidOnly;

            Assert.That(surface.VertexXcm[0], Is.EqualTo(10));
            Assert.That(surface.VertexYcm[1], Is.EqualTo(0));
            Assert.That(surface.VertexZcm[2], Is.EqualTo(20));
            Assert.That(surface.TriA[0], Is.EqualTo(0));
            Assert.That(surface.TriB[0], Is.EqualTo(1));
            Assert.That(surface.TriC[0], Is.EqualTo(2));
            Assert.That(surface.TriAreaIds[0], Is.EqualTo((byte)9));
            Assert.That(surface.TriStableIds[0], Is.EqualTo(42));
            Assert.That(surface.TriFlags[0], Is.EqualTo(FloorFlags));
        }

        [Test]
        public void NavTriangleSurface_TriFlags_RejectLengthMismatchZeroUnknownAndWalkWithoutSolid()
        {
            Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 11 },
                triFlags: Array.Empty<NavTriangleSurfaceFlags>()));

            var zeroEx = Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 11 },
                triFlags: new[] { (NavTriangleSurfaceFlags)0 }));
            Assert.That(zeroEx!.Message, Does.Contain("Triangle 0"));
            Assert.That(zeroEx.Message, Does.Contain("stable id 11"));
            Assert.That(zeroEx.Message, Does.Contain("zero"));
            Assert.That(zeroEx.Message, Does.Contain("triFlags"));
            Assert.That(zeroEx.ParamName, Is.EqualTo("triFlags"));

            var unknownEx = Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 22 },
                triFlags: new[] { (NavTriangleSurfaceFlags)0x04 }));
            Assert.That(unknownEx!.Message, Does.Contain("Triangle 0"));
            Assert.That(unknownEx.Message, Does.Contain("stable id 22"));
            Assert.That(unknownEx.Message, Does.Contain("unknown"));
            Assert.That(unknownEx.Message, Does.Contain("triFlags"));
            Assert.That(unknownEx.ParamName, Is.EqualTo("triFlags"));

            var walkOnlyEx = Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 33 },
                triFlags: new[] { NavTriangleSurfaceFlags.WalkCandidate }));
            Assert.That(walkOnlyEx!.Message, Does.Contain("Triangle 0"));
            Assert.That(walkOnlyEx.Message, Does.Contain("stable id 33"));
            Assert.That(walkOnlyEx.Message, Does.Contain("WalkCandidate"));
            Assert.That(walkOnlyEx.Message, Does.Contain("Solid"));
            Assert.That(walkOnlyEx.Message, Does.Contain("triFlags"));
            Assert.That(walkOnlyEx.ParamName, Is.EqualTo("triFlags"));

            // Solid|unknown must still reject as unknown bits (no silent masking).
            var solidPlusUnknown = Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 44 },
                triFlags: new[] { SolidOnly | (NavTriangleSurfaceFlags)0x08 }));
            Assert.That(solidPlusUnknown!.Message, Does.Contain("unknown"));
            Assert.That(solidPlusUnknown.ParamName, Is.EqualTo("triFlags"));
        }

        [Test]
        public void NavTriangleSurface_StableIds_RejectNegative_AllowZero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { -1 },
                triFlags: new[] { FloorFlags }));

            var withZero = new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 0 },
                triFlags: new[] { FloorFlags });
            Assert.That(withZero.TriStableIds[0], Is.EqualTo(0));
        }

        [Test]
        public void NavTriangleSurface_TriangleOutsideDeclaredGrid_FailsFast()
        {
            var surface = BuildSurface(
                vertexXcm: new[] { 1000, 1010, 1000 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 1000, 1000, 1010 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 55 },
                triFlags: new[] { FloorFlags });

            var ex = Assert.Throws<ArgumentException>(() => NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 2, 2, haloPaddingCm: 0)));
            Assert.That(ex!.Message, Does.Contain("Triangle index 0"));
            Assert.That(ex.Message, Does.Contain("stable id 55"));
        }

        [Test]
        public void NavTriangleSurface_InvalidChannelsIndicesDuplicateIdsAndTileConfig_FailFast()
        {
            Assert.Throws<ArgumentNullException>(() => new NavTriangleSurfaceSnapshot(
                null!, new[] { 0 }, new[] { 0 }, new[] { 0 }, new[] { 0 }, new[] { 0 }, new byte[] { 0 }, new[] { 0 }, new[] { FloorFlags }));

            Assert.Throws<ArgumentNullException>(() => new NavTriangleSurfaceSnapshot(
                new[] { 0 }, new[] { 0 }, new[] { 0 }, new[] { 0 }, new[] { 0 }, new[] { 0 }, new byte[] { 0 }, new[] { 0 }, null!));

            Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 2 },
                vertexYcm: new[] { 0, 1 },
                vertexZcm: new[] { 0, 1, 2 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags }));

            Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 2 },
                vertexYcm: new[] { 0, 1, 2 },
                vertexZcm: new[] { 0, 1, 2 },
                triA: new[] { 0, 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags }));

            Assert.Throws<ArgumentOutOfRangeException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 2 },
                vertexYcm: new[] { 0, 1, 2 },
                vertexZcm: new[] { 0, 1, 2 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 99 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags }));

            Assert.Throws<ArgumentException>(() => new NavTriangleSurfaceSnapshot(
                vertexXcm: new[] { 0, 1, 2, 3, 4, 5 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 0, 0, 0, 0 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 7, 7 },
                triFlags: new[] { FloorFlags, FloorFlags }));

            Assert.Throws<ArgumentOutOfRangeException>(() => new NavTriangleSurfaceTileGrid(0, 0, 0, 100, 1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 0, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 1, 1, -1));
            Assert.Throws<OverflowException>(() => new NavTriangleSurfaceTileGrid(0, 0, int.MaxValue, 100, 3, 1, 0));

            var surface = BuildSurface(
                vertexXcm: new[] { 0, 1, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 1 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 0 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => index.GetTriangleIndices(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => index.GetTriangleIndices(new NavBakeTileCoord(0, 1)));
        }

        [Test]
        public void NavTriangleSurface_WarmedTileLookup_AllocatesExactlyZeroBytes()
        {
            var surface = BuildSurface(
                vertexXcm: new[] { 10, 20, 10, 110, 120, 110 },
                vertexYcm: new[] { 0, 0, 0, 0, 0, 0 },
                vertexZcm: new[] { 10, 10, 20, 10, 10, 20 },
                triA: new[] { 0, 3 },
                triB: new[] { 1, 4 },
                triC: new[] { 2, 5 },
                triAreaIds: new byte[] { 0, 0 },
                triStableIds: new[] { 1, 2 },
                triFlags: new[] { FloorFlags, FloorFlags });
            var index = NavTriangleSurfaceTileIndex.Build(
                surface,
                new NavTriangleSurfaceTileGrid(0, 0, 100, 100, 2, 1, haloPaddingCm: 5));

            // Warmup
            _ = index.GetTriangleIndices(0, 0).Length;
            _ = index.GetTriangleIndices(1, 0).Length;
            _ = index.GetTriangleIndices(new NavBakeTileCoord(0, 0)).Length;

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                ReadOnlySpan<int> a = index.GetTriangleIndices(0, 0);
                ReadOnlySpan<int> b = index.GetTriangleIndices(1, 0);
                ReadOnlySpan<int> c = index.GetTriangleIndices(new NavBakeTileCoord(0, 0));
                if (a.Length + b.Length + c.Length < 0)
                {
                    throw new InvalidOperationException("Unreachable guard to keep spans live.");
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), $"Warmed tile lookup allocated {allocated} bytes.");
        }

        private static NavTriangleSurfaceSnapshot BuildSurface(
            int[] vertexXcm,
            int[] vertexYcm,
            int[] vertexZcm,
            int[] triA,
            int[] triB,
            int[] triC,
            byte[] triAreaIds,
            int[] triStableIds,
            NavTriangleSurfaceFlags[] triFlags)
            => new(
                vertexXcm,
                vertexYcm,
                vertexZcm,
                triA,
                triB,
                triC,
                triAreaIds,
                triStableIds,
                triFlags);

        private static int[] ToArray(ReadOnlySpan<int> span)
        {
            var result = new int[span.Length];
            span.CopyTo(result);
            return result;
        }

        private static int[] StableIdsInTile(NavTriangleSurfaceTileIndex index, int tileX, int tileZ)
        {
            ReadOnlySpan<int> tris = index.GetTriangleIndices(tileX, tileZ);
            ReadOnlySpan<int> stableIds = index.Surface.TriStableIds;
            var result = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                result[i] = stableIds[tris[i]];
            }

            return result;
        }
    }
}
