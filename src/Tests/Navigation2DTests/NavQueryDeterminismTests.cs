using System;
using System.IO;
using NUnit.Framework;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Tests.Navigation2D
{
    /// <summary>
    /// Verifies that NavQueryService computations are deterministic across runs.
    /// After the float→Fix64 migration, all distance and tile-location calculations
    /// must produce identical results regardless of platform or run count.
    /// </summary>
    [TestFixture]
    public class NavQueryDeterminismTests
    {
        /// <summary>
        /// Helper: create a minimal NavTileStore with a stub that pre-loads tiles.
        /// For determinism testing, we only need TryProject and TryFindPath to exercise
        /// the Dist and LocateTile codepaths.
        /// </summary>
        private NavTileStore CreateStubStore(params NavTile[] tiles)
        {
            var dict = new System.Collections.Generic.Dictionary<NavTileId, byte[]>();
            foreach (var tile in tiles)
            {
                using var ms = new MemoryStream();
                NavTileBinary.Write(ms, tile);
                dict[tile.TileId] = ms.ToArray();
            }

            return new NavTileStore(id =>
            {
                if (dict.TryGetValue(id, out var data))
                    return new MemoryStream(data);
                throw new FileNotFoundException($"NavTile not found: {id}");
            });
        }

        private NavTile CreateSimpleTile(int chunkX, int chunkY, int originXcm, int originZcm)
        {
            // Create a tile with 2 triangles forming a simple square
            int[] vx = { 0, 100, 100, 0 };
            int[] vy = { 0, 0, 0, 0 };
            int[] vz = { 0, 0, 100, 100 };
            int[] triA = { 0, 0 };
            int[] triB = { 1, 2 };
            int[] triC = { 2, 3 };
            int[] n0 = { 1, 0 };
            int[] n1 = { -1, -1 };
            int[] n2 = { -1, -1 };

            return new NavTile(
                new NavTileId(chunkX, chunkY, 0),
                1,
                0UL,
                0UL,
                originXcm,
                originZcm,
                vx, vy, vz,
                triA, triB, triC,
                n0, n1, n2,
                Array.Empty<NavBorderPortal>());
        }

        [Test]
        public void TryProject_SameTile_DeterministicAcrossRuns()
        {
            var tile = CreateSimpleTile(0, 0, 0, 0);
            var store = CreateStubStore(tile);
            var service = new NavQueryService(store);

            // Run projection 100 times with same input
            bool firstResult = service.TryProject(50, 50, out var firstLoc);

            for (int i = 0; i < 100; i++)
            {
                bool result = service.TryProject(50, 50, out var loc);
                Assert.That(result, Is.EqualTo(firstResult), $"Run {i}: TryProject returned different bool");
                if (firstResult)
                {
                    Assert.That(loc.TriangleId, Is.EqualTo(firstLoc.TriangleId), $"Run {i}: Triangle mismatch");
                    Assert.That(loc.LocalXcm, Is.EqualTo(firstLoc.LocalXcm), $"Run {i}: LocalXcm mismatch");
                    Assert.That(loc.LocalZcm, Is.EqualTo(firstLoc.LocalZcm), $"Run {i}: LocalZcm mismatch");
                }
            }
        }

        [Test]
        public void TryFindPath_SameTile_DeterministicResult()
        {
            var tile = CreateSimpleTile(0, 0, 0, 0);
            var store = CreateStubStore(tile);
            var service = new NavQueryService(store);

            var firstResult = service.TryFindPath(10, 10, 90, 90);

            for (int i = 0; i < 50; i++)
            {
                var result = service.TryFindPath(10, 10, 90, 90);
                Assert.That(result.Status, Is.EqualTo(firstResult.Status), $"Run {i}: Status mismatch");
                Assert.That(result.PathXcm, Is.EqualTo(firstResult.PathXcm), $"Run {i}: PathXcm mismatch");
                Assert.That(result.PathZcm, Is.EqualTo(firstResult.PathZcm), $"Run {i}: PathZcm mismatch");
            }
        }

        [Test]
        public void Fix64Dist_MatchesFloatWithin1cm()
        {
            // Verify Fix64 distance is close to float distance (within 1cm tolerance)
            int ax = 100, az = 200, bx = 400, bz = 600;

            // Float version (old)
            float fdx = (bx - ax) / 100f;
            float fdz = (bz - az) / 100f;
            float floatDist = MathF.Sqrt(fdx * fdx + fdz * fdz);

            // Fix64 version (new)
            var dx = Fix64.FromInt(bx - ax);
            var dz = Fix64.FromInt(bz - az);
            var fix64Dist = Fix64Math.Sqrt(dx * dx + dz * dz);
            float fix64AsFloat = fix64Dist.ToFloat();

            // The Fix64 operates in cm, float in meters, so scale fix64 to meters for comparison
            float fix64InMeters = fix64AsFloat / 100f;
            float tolerance = 0.01f; // 1cm expressed in meters

            Assert.That(fix64InMeters, Is.EqualTo(floatDist).Within(tolerance),
                $"Fix64 dist ({fix64InMeters}m) should be within 1cm of float dist ({floatDist}m)");
        }

        [Test]
        public void Fix64Dist_Deterministic_SameInputSameOutput()
        {
            // Run the same distance calculation many times and verify identical raw values
            int ax = 12345, az = -6789, bx = 98765, bz = 43210;
            var dx = Fix64.FromInt(bx - ax);
            var dz = Fix64.FromInt(bz - az);
            var expected = Fix64Math.Sqrt(dx * dx + dz * dz);

            for (int i = 0; i < 1000; i++)
            {
                var dx2 = Fix64.FromInt(bx - ax);
                var dz2 = Fix64.FromInt(bz - az);
                var result = Fix64Math.Sqrt(dx2 * dx2 + dz2 * dz2);
                Assert.That(result.RawValue, Is.EqualTo(expected.RawValue),
                    $"Iteration {i}: Fix64 distance should be bit-identical");
            }
        }

        [Test]
        public void Fix64Dist_ModerateValues_Accurate()
        {
            // Test with moderate coordinate values (within single-tile scale ~30000cm)
            // Fix64 Q31.32 can handle values up to ~2 billion, but multiplication
            // of two large Fix64 values can overflow, so keep deltas reasonable.
            int ax = -15000, az = -15000, bx = 15000, bz = 15000;
            var dx = Fix64.FromInt(bx - ax);
            var dz = Fix64.FromInt(bz - az);
            var dist = Fix64Math.Sqrt(dx * dx + dz * dz);

            // Expected: sqrt((30000)^2 + (30000)^2) = 30000 * sqrt(2) ≈ 42426 cm
            float expected = 42426.4f;
            float actual = dist.ToFloat();
            float tolerance = 2f; // 2cm tolerance

            Assert.That(actual, Is.EqualTo(expected).Within(tolerance),
                $"Moderate distance: expected ~{expected}cm, got {actual}cm");
        }
    }
}
