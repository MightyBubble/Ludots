using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanProvenanceContractTests
    {
        private static readonly NavTriangleSurfaceFlags FloorFlags =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private static readonly LayeredSpanWalkabilitySpec DefaultWalk = new(
            agentHeightCm: 180,
            minWalkableUpDotQ1M: 500_000,
            sameSurfaceToleranceCm: 0);

        [Test]
        public void Provenance_SameCountRerasterize_RejectsStaleWalkabilitySheetsLinksAndRadius_ClearsOutputs()
        {
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            NavTriangleSurfaceSnapshot floorY0 = FloorAtY(0);
            NavTriangleSurfaceSnapshot floorY50 = FloorAtY(50);
            int[] indices = { 0 };

            var raw = new LayeredSpanScratch(2, 16);
            var walk = new LayeredSpanWalkabilityScratch(2, 16, 16);
            var sheets = new LayeredSpanSurfaceSheetScratch(2, 16);
            var links = new LayeredSpanWalkLinkScratch(16, 32);
            var radius = new LayeredSpanRadiusFieldScratch(16, 16, 32);
            var regions = new LayeredSpanRegionScratch(16, 16);
            var linkSpec = new LayeredSpanWalkLinkSpec(0);

            LayeredSpanRasterizer.Rasterize(floorY0, indices, in grid, raw);
            LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, walk);
            LayeredSpanSurfaceSheetAssigner.Assign(floorY0, raw, in grid, in DefaultWalk, sheets);
            LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
            LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, radius);
            LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, regions);

            Assert.That(raw.HasPublishedContent, Is.True);
            Assert.That(walk.HasPublishedContent, Is.True);
            Assert.That(sheets.HasPublishedContent, Is.True);
            Assert.That(links.HasPublishedContent, Is.True);
            Assert.That(radius.HasPublishedContent, Is.True);
            Assert.That(regions.HasPublishedContent, Is.True);

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;
            int walkableCount = walk.WalkableSpanCount;
            ulong rawGen1 = raw.ContentGeneration;
            Assert.That(columnCount, Is.EqualTo(2));
            Assert.That(spanCount, Is.GreaterThan(0));
            Assert.That(walkableCount, Is.GreaterThan(0));
            Assert.That(raw.SpanMaxYcm[0], Is.EqualTo(0));

            // Snapshot stale outputs that keep identical counts after rerasterize.
            var staleWalk = walk;
            var staleSheets = sheets;
            var staleLinks = links;
            var staleRadius = radius;
            ulong staleWalkGen = staleWalk.ContentGeneration;
            ulong staleSheetsGen = staleSheets.ContentGeneration;
            ulong staleLinksGen = staleLinks.ContentGeneration;
            ulong staleRadiusGen = staleRadius.ContentGeneration;

            LayeredSpanRasterizer.Rasterize(floorY50, indices, in grid, raw);
            Assert.That(raw.ContentGeneration, Is.EqualTo(rawGen1 + 1));
            Assert.That(raw.ColumnCount, Is.EqualTo(columnCount));
            Assert.That(raw.SpanCount, Is.EqualTo(spanCount));
            Assert.That(raw.SpanMaxYcm[0], Is.EqualTo(50));
            Assert.That(staleWalk.ColumnCount, Is.EqualTo(columnCount));
            Assert.That(staleWalk.ClassifiedSpanCount, Is.EqualTo(spanCount));
            Assert.That(staleWalk.WalkableSpanCount, Is.EqualTo(walkableCount));
            Assert.That(staleSheets.ColumnCount, Is.EqualTo(columnCount));
            Assert.That(staleSheets.SpanCount, Is.EqualTo(spanCount));
            Assert.That(staleLinks.WalkableSpanCount, Is.EqualTo(walkableCount));
            Assert.That(staleRadius.SpanCount, Is.EqualTo(spanCount));
            Assert.That(staleWalk.WasBuiltFrom(raw), Is.False);
            Assert.That(staleSheets.WasBuiltFrom(raw), Is.False);
            Assert.That(staleLinks.WasBuiltFrom(raw, staleWalk), Is.False);
            Assert.That(staleRadius.WasBuiltFrom(raw, staleWalk, staleSheets, staleLinks), Is.False);

            var freshWalk = new LayeredSpanWalkabilityScratch(2, 16, 16);
            var freshSheets = new LayeredSpanSurfaceSheetScratch(2, 16);
            var freshLinks = new LayeredSpanWalkLinkScratch(16, 32);
            var freshRadius = new LayeredSpanRadiusFieldScratch(16, 16, 32);
            LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, freshWalk);
            LayeredSpanSurfaceSheetAssigner.Assign(floorY50, raw, in grid, in DefaultWalk, freshSheets);
            LayeredSpanWalkLinkBuilder.Build(raw, freshWalk, in grid, in linkSpec, freshLinks);
            LayeredSpanRadiusFieldBuilder.Build(raw, freshWalk, freshSheets, freshLinks, in grid, freshRadius);
            Assert.That(freshWalk.WalkableSpanCount, Is.EqualTo(walkableCount));
            Assert.That(ReferenceEquals(freshWalk, staleWalk), Is.False);
            Assert.That(ReferenceEquals(freshSheets, staleSheets), Is.False);
            Assert.That(ReferenceEquals(freshLinks, staleLinks), Is.False);
            Assert.That(ReferenceEquals(freshRadius, staleRadius), Is.False);
            Assert.That(freshWalk.WasBuiltFrom(raw), Is.True);
            Assert.That(freshSheets.WasBuiltFrom(raw), Is.True);
            Assert.That(freshLinks.WasBuiltFrom(raw, freshWalk), Is.True);
            Assert.That(freshRadius.WasBuiltFrom(raw, freshWalk, freshSheets, freshLinks), Is.True);
            Assert.That(staleWalk.ContentGeneration, Is.EqualTo(staleWalkGen));
            Assert.That(staleSheets.ContentGeneration, Is.EqualTo(staleSheetsGen));
            Assert.That(staleLinks.ContentGeneration, Is.EqualTo(staleLinksGen));
            Assert.That(staleRadius.ContentGeneration, Is.EqualTo(staleRadiusGen));

            var linksOut = new LayeredSpanWalkLinkScratch(16, 32);
            LayeredSpanWalkLinkBuilder.Build(raw, freshWalk, in grid, in linkSpec, linksOut);
            Assert.That(linksOut.LinkCount, Is.GreaterThan(0));
            Assert.That(linksOut.HasPublishedContent, Is.True);
            var exWalk = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkLinkBuilder.Build(raw, staleWalk, in grid, in linkSpec, linksOut));
            Assert.That(exWalk!.Message, Does.Contain("walkability output that matches"));
            Assert.That(exWalk.Message, Does.Contain("identity and content generation"));
            Assert.That(linksOut.WalkableSpanCount, Is.EqualTo(0));
            Assert.That(linksOut.LinkCount, Is.EqualTo(0));
            Assert.That(linksOut.HasPublishedContent, Is.False);

            var radiusOut = new LayeredSpanRadiusFieldScratch(16, 16, 32);
            LayeredSpanRadiusFieldBuilder.Build(raw, freshWalk, freshSheets, freshLinks, in grid, radiusOut);
            Assert.That(radiusOut.HasPublishedContent, Is.True);
            var exRadiusSheets = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(raw, freshWalk, staleSheets, freshLinks, in grid, radiusOut));
            Assert.That(exRadiusSheets!.Message, Does.Contain("surface-sheet output that matches"));
            Assert.That(radiusOut.HasPublishedContent, Is.False);

            LayeredSpanRadiusFieldBuilder.Build(raw, freshWalk, freshSheets, freshLinks, in grid, radiusOut);
            LayeredSpanRegionBuilder.Build(raw, freshWalk, freshSheets, freshLinks, radiusOut, agentRadiusCm: 0, regions);
            Assert.That(regions.RegionCount, Is.GreaterThan(0));

            var exSheets = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    raw, freshWalk, staleSheets, freshLinks, radiusOut, agentRadiusCm: 0, regions));
            Assert.That(exSheets!.Message, Does.Contain("surface-sheet output that matches"));
            Assert.That(exSheets.Message, Does.Contain("identity and content generation"));
            Assert.That(regions.SpanCount, Is.EqualTo(0));
            Assert.That(regions.RegionCount, Is.EqualTo(0));
            Assert.That(regions.HasPublishedContent, Is.False);

            LayeredSpanRegionBuilder.Build(raw, freshWalk, freshSheets, freshLinks, radiusOut, agentRadiusCm: 0, regions);
            Assert.That(regions.RegionCount, Is.GreaterThan(0));

            var exLinks = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    raw, freshWalk, freshSheets, staleLinks, radiusOut, agentRadiusCm: 0, regions));
            Assert.That(exLinks!.Message, Does.Contain("walk-link output that matches"));
            Assert.That(exLinks.Message, Does.Contain("identity and content generation"));
            Assert.That(regions.SpanCount, Is.EqualTo(0));
            Assert.That(regions.RegionCount, Is.EqualTo(0));
            Assert.That(regions.HasPublishedContent, Is.False);

            LayeredSpanRegionBuilder.Build(raw, freshWalk, freshSheets, freshLinks, radiusOut, agentRadiusCm: 0, regions);
            Assert.That(regions.RegionCount, Is.GreaterThan(0));

            var exRadius = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    raw, freshWalk, freshSheets, freshLinks, staleRadius, agentRadiusCm: 0, regions));
            Assert.That(exRadius!.Message, Does.Contain("radius-field output that matches"));
            Assert.That(exRadius.Message, Does.Contain("identity and content generation"));
            Assert.That(regions.SpanCount, Is.EqualTo(0));
            Assert.That(regions.RegionCount, Is.EqualTo(0));
            Assert.That(regions.HasPublishedContent, Is.False);
        }

        [Test]
        public void Provenance_SameCountFromAnotherRawScratchInstance_Rejected()
        {
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            NavTriangleSurfaceSnapshot floor = FloorAtY(0);
            int[] indices = { 0 };
            var linkSpec = new LayeredSpanWalkLinkSpec(0);

            var rawA = new LayeredSpanScratch(2, 16);
            var walkA = new LayeredSpanWalkabilityScratch(2, 16, 16);
            var sheetsA = new LayeredSpanSurfaceSheetScratch(2, 16);
            var linksA = new LayeredSpanWalkLinkScratch(16, 32);
            var radiusA = new LayeredSpanRadiusFieldScratch(16, 16, 32);
            LayeredSpanRasterizer.Rasterize(floor, indices, in grid, rawA);
            LayeredSpanWalkabilityClassifier.Classify(rawA, in DefaultWalk, walkA);
            LayeredSpanSurfaceSheetAssigner.Assign(floor, rawA, in grid, in DefaultWalk, sheetsA);
            LayeredSpanWalkLinkBuilder.Build(rawA, walkA, in grid, in linkSpec, linksA);
            LayeredSpanRadiusFieldBuilder.Build(rawA, walkA, sheetsA, linksA, in grid, radiusA);

            var rawB = new LayeredSpanScratch(2, 16);
            var walkB = new LayeredSpanWalkabilityScratch(2, 16, 16);
            var sheetsB = new LayeredSpanSurfaceSheetScratch(2, 16);
            var linksB = new LayeredSpanWalkLinkScratch(16, 32);
            var radiusB = new LayeredSpanRadiusFieldScratch(16, 16, 32);
            LayeredSpanRasterizer.Rasterize(floor, indices, in grid, rawB);
            LayeredSpanWalkabilityClassifier.Classify(rawB, in DefaultWalk, walkB);
            LayeredSpanSurfaceSheetAssigner.Assign(floor, rawB, in grid, in DefaultWalk, sheetsB);
            LayeredSpanWalkLinkBuilder.Build(rawB, walkB, in grid, in linkSpec, linksB);
            LayeredSpanRadiusFieldBuilder.Build(rawB, walkB, sheetsB, linksB, in grid, radiusB);

            Assert.That(rawA.ColumnCount, Is.EqualTo(rawB.ColumnCount));
            Assert.That(rawA.SpanCount, Is.EqualTo(rawB.SpanCount));
            Assert.That(walkA.WalkableSpanCount, Is.EqualTo(walkB.WalkableSpanCount));
            Assert.That(ReferenceEquals(rawA, rawB), Is.False);
            Assert.That(walkB.WasBuiltFrom(rawA), Is.False);
            Assert.That(sheetsB.WasBuiltFrom(rawA), Is.False);
            Assert.That(linksB.WasBuiltFrom(rawA, walkA), Is.False);
            Assert.That(radiusB.WasBuiltFrom(rawA, walkA, sheetsA, linksA), Is.False);

            var linksOut = new LayeredSpanWalkLinkScratch(16, 32);
            LayeredSpanWalkLinkBuilder.Build(rawA, walkA, in grid, in linkSpec, linksOut);
            var exWalk = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanWalkLinkBuilder.Build(rawA, walkB, in grid, in linkSpec, linksOut));
            Assert.That(exWalk!.Message, Does.Contain("identity and content generation"));
            Assert.That(linksOut.HasPublishedContent, Is.False);
            Assert.That(linksOut.LinkCount, Is.EqualTo(0));

            var radiusOut = new LayeredSpanRadiusFieldScratch(16, 16, 32);
            LayeredSpanRadiusFieldBuilder.Build(rawA, walkA, sheetsA, linksA, in grid, radiusOut);
            var exRadius = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRadiusFieldBuilder.Build(rawA, walkA, sheetsB, linksA, in grid, radiusOut));
            Assert.That(exRadius!.Message, Does.Contain("surface-sheet output that matches"));
            Assert.That(radiusOut.HasPublishedContent, Is.False);

            var regions = new LayeredSpanRegionScratch(16, 16);
            LayeredSpanRegionBuilder.Build(rawA, walkA, sheetsA, linksA, radiusA, agentRadiusCm: 0, regions);
            Assert.That(regions.RegionCount, Is.GreaterThan(0));

            var exSheets = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    rawA, walkA, sheetsB, linksA, radiusA, agentRadiusCm: 0, regions));
            Assert.That(exSheets!.Message, Does.Contain("surface-sheet output that matches"));
            Assert.That(regions.HasPublishedContent, Is.False);

            LayeredSpanRegionBuilder.Build(rawA, walkA, sheetsA, linksA, radiusA, agentRadiusCm: 0, regions);
            var exLinks = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    rawA, walkA, sheetsA, linksB, radiusA, agentRadiusCm: 0, regions));
            Assert.That(exLinks!.Message, Does.Contain("walk-link output that matches"));
            Assert.That(regions.HasPublishedContent, Is.False);

            LayeredSpanRegionBuilder.Build(rawA, walkA, sheetsA, linksA, radiusA, agentRadiusCm: 0, regions);
            var exStaleRadius = Assert.Throws<InvalidOperationException>(
                () => LayeredSpanRegionBuilder.Build(
                    rawA, walkA, sheetsA, linksA, radiusB, agentRadiusCm: 0, regions));
            Assert.That(exStaleRadius!.Message, Does.Contain("radius-field output that matches"));
            Assert.That(regions.HasPublishedContent, Is.False);
        }

        [Test]
        public void Provenance_WarmedValidationPath_AllocatesExactlyZeroBytes()
        {
            var grid = new LayeredSpanRasterGridSpec(0, 0, 100, 2, 1);
            NavTriangleSurfaceSnapshot floor = FloorAtY(0);
            int[] indices = { 0 };
            var linkSpec = new LayeredSpanWalkLinkSpec(0);

            var raw = new LayeredSpanScratch(2, 16);
            var walk = new LayeredSpanWalkabilityScratch(2, 16, 16);
            var sheets = new LayeredSpanSurfaceSheetScratch(2, 16);
            var links = new LayeredSpanWalkLinkScratch(16, 32);
            var radius = new LayeredSpanRadiusFieldScratch(16, 16, 32);
            var regions = new LayeredSpanRegionScratch(16, 16);

            for (int i = 0; i < 64; i++)
            {
                LayeredSpanRasterizer.Rasterize(floor, indices, in grid, raw);
                LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, walk);
                LayeredSpanSurfaceSheetAssigner.Assign(floor, raw, in grid, in DefaultWalk, sheets);
                LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
                LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, radius);
                LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, regions);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                LayeredSpanRasterizer.Rasterize(floor, indices, in grid, raw);
                LayeredSpanWalkabilityClassifier.Classify(raw, in DefaultWalk, walk);
                LayeredSpanSurfaceSheetAssigner.Assign(floor, raw, in grid, in DefaultWalk, sheets);
                LayeredSpanWalkLinkBuilder.Build(raw, walk, in grid, in linkSpec, links);
                LayeredSpanRadiusFieldBuilder.Build(raw, walk, sheets, links, in grid, radius);
                LayeredSpanRegionBuilder.Build(raw, walk, sheets, links, radius, agentRadiusCm: 0, regions);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocated,
                Is.EqualTo(0),
                $"Warmed layered-span provenance validation path allocated {allocated} bytes.");
            Assert.That(raw.HasPublishedContent, Is.True);
            Assert.That(walk.WasBuiltFrom(raw), Is.True);
            Assert.That(sheets.WasBuiltFrom(raw), Is.True);
            Assert.That(links.WasBuiltFrom(raw, walk), Is.True);
            Assert.That(radius.WasBuiltFrom(raw, walk, sheets, links), Is.True);
            Assert.That(regions.HasPublishedContent, Is.True);
        }

        private static NavTriangleSurfaceSnapshot FloorAtY(int yCm)
            => new(
                vertexXcm: new[] { 0, 200, 0 },
                vertexYcm: new[] { yCm, yCm, yCm },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                triAreaIds: new byte[] { 1 },
                triStableIds: new[] { 1 },
                triFlags: new[] { FloorFlags });
    }
}
