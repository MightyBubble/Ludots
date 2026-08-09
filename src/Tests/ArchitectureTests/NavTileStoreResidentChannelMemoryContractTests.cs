using System;
using Ludots.Core.Navigation.NavMesh;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavTileStoreResidentChannelMemoryContractTests
    {
        [Test]
        public void NavTile_PreallocatedChannelPayloadBytes_MatchesComputeBankedChannelPayloadBytes()
        {
            const int vertexCapacity = 128;
            const int triangleCapacity = 256;
            const int portalCapacity = 32;

            NavTile tile = NavTile.CreateBanked(vertexCapacity, triangleCapacity, portalCapacity);
            long expected = NavTile.ComputeBankedChannelPayloadBytes(vertexCapacity, triangleCapacity, portalCapacity);

            Assert.That(tile.PreallocatedChannelPayloadBytes, Is.EqualTo(expected));
        }

        [Test]
        public void NavTileStore_PreallocatedResidentChannelPayloadBytes_EqualsCapacityTimesPerTileFormula()
        {
            const int residentTileCapacity = 16;
            const int vertexCapacity = 64;
            const int triangleCapacity = 128;
            const int portalCapacity = 24;

            var store = new NavTileStore(
                _ => throw new InvalidOperationException("Resident channel memory contract tests do not load tiles."),
                residentTileCapacity,
                vertexCapacity,
                triangleCapacity,
                portalCapacity);

            long perTile = NavTile.ComputeBankedChannelPayloadBytes(vertexCapacity, triangleCapacity, portalCapacity);
            long expected = checked((long)residentTileCapacity * perTile);

            Assert.That(store.PreallocatedResidentChannelPayloadBytes, Is.EqualTo(expected));
        }

        [Test]
        public void NavTileStore_PreallocatedResidentChannelPayloadBytes_ScalesWithResidentTileCapacity()
        {
            const int vertexCapacity = 32;
            const int triangleCapacity = 64;
            const int portalCapacity = 8;

            var small = CreateStore(residentTileCapacity: 4, vertexCapacity, triangleCapacity, portalCapacity);
            var large = CreateStore(residentTileCapacity: 12, vertexCapacity, triangleCapacity, portalCapacity);

            long perTile = NavTile.ComputeBankedChannelPayloadBytes(vertexCapacity, triangleCapacity, portalCapacity);
            Assert.That(small.PreallocatedResidentChannelPayloadBytes, Is.EqualTo(checked(4L * perTile)));
            Assert.That(large.PreallocatedResidentChannelPayloadBytes, Is.EqualTo(checked(12L * perTile)));
            Assert.That(
                large.PreallocatedResidentChannelPayloadBytes,
                Is.EqualTo(checked(small.PreallocatedResidentChannelPayloadBytes * 3L)));
        }

        [Test]
        public void NavTileStore_PreallocatedResidentChannelPayloadBytes_ScalesWithOutputVertexCapacity()
        {
            const int residentTileCapacity = 8;
            const int triangleCapacity = 64;
            const int portalCapacity = 8;

            var baseline = CreateStore(residentTileCapacity, outputVertexCapacity: 32, triangleCapacity, portalCapacity);
            var larger = CreateStore(residentTileCapacity, outputVertexCapacity: 96, triangleCapacity, portalCapacity);

            long delta = checked(
                (long)residentTileCapacity *
                (NavTile.ComputeBankedChannelPayloadBytes(96, triangleCapacity, portalCapacity) -
                 NavTile.ComputeBankedChannelPayloadBytes(32, triangleCapacity, portalCapacity)));

            Assert.That(
                larger.PreallocatedResidentChannelPayloadBytes - baseline.PreallocatedResidentChannelPayloadBytes,
                Is.EqualTo(delta));
            Assert.That(larger.PreallocatedResidentChannelPayloadBytes, Is.GreaterThan(baseline.PreallocatedResidentChannelPayloadBytes));
        }

        [Test]
        public void NavTileStore_PreallocatedResidentChannelPayloadBytes_ScalesWithOutputTriangleCapacity()
        {
            const int residentTileCapacity = 8;
            const int vertexCapacity = 32;
            const int portalCapacity = 8;

            var baseline = CreateStore(residentTileCapacity, vertexCapacity, outputTriangleCapacity: 64, portalCapacity);
            var larger = CreateStore(residentTileCapacity, vertexCapacity, outputTriangleCapacity: 192, portalCapacity);

            long delta = checked(
                (long)residentTileCapacity *
                (NavTile.ComputeBankedChannelPayloadBytes(vertexCapacity, 192, portalCapacity) -
                 NavTile.ComputeBankedChannelPayloadBytes(vertexCapacity, 64, portalCapacity)));

            Assert.That(
                larger.PreallocatedResidentChannelPayloadBytes - baseline.PreallocatedResidentChannelPayloadBytes,
                Is.EqualTo(delta));
            Assert.That(larger.PreallocatedResidentChannelPayloadBytes, Is.GreaterThan(baseline.PreallocatedResidentChannelPayloadBytes));
        }

        [Test]
        public void NavTileStore_PreallocatedResidentChannelPayloadBytes_ScalesWithOutputPortalCapacity()
        {
            const int residentTileCapacity = 8;
            const int vertexCapacity = 32;
            const int triangleCapacity = 64;

            var baseline = CreateStore(residentTileCapacity, vertexCapacity, triangleCapacity, outputPortalCapacity: 8);
            var larger = CreateStore(residentTileCapacity, vertexCapacity, triangleCapacity, outputPortalCapacity: 32);

            long delta = checked(
                (long)residentTileCapacity *
                (NavTile.ComputeBankedChannelPayloadBytes(vertexCapacity, triangleCapacity, 32) -
                 NavTile.ComputeBankedChannelPayloadBytes(vertexCapacity, triangleCapacity, 8)));

            Assert.That(
                larger.PreallocatedResidentChannelPayloadBytes - baseline.PreallocatedResidentChannelPayloadBytes,
                Is.EqualTo(delta));
            Assert.That(larger.PreallocatedResidentChannelPayloadBytes, Is.GreaterThan(baseline.PreallocatedResidentChannelPayloadBytes));
        }

        [Test]
        public void NavTileStore_PreallocatedResidentChannelPayloadBytes_DoesNotScaleWithWorldTileCount()
        {
            // Resident bank is sized by residentTileCapacity (window), not by 4096 world tiles.
            var window = CreateStore(residentTileCapacity: 64, outputVertexCapacity: 32, outputTriangleCapacity: 64, outputPortalCapacity: 8);
            long windowBytes = window.PreallocatedResidentChannelPayloadBytes;
            var sameWindow = CreateStore(residentTileCapacity: 64, outputVertexCapacity: 32, outputTriangleCapacity: 64, outputPortalCapacity: 8);
            Assert.That(sameWindow.PreallocatedResidentChannelPayloadBytes, Is.EqualTo(windowBytes));
            Assert.That(windowBytes, Is.EqualTo(checked(64L * NavTile.ComputeBankedChannelPayloadBytes(32, 64, 8))));
            Assert.That(windowBytes, Is.LessThan(checked(4096L * NavTile.ComputeBankedChannelPayloadBytes(32, 64, 8))));
        }

        private static NavTileStore CreateStore(
            int residentTileCapacity,
            int outputVertexCapacity,
            int outputTriangleCapacity,
            int outputPortalCapacity)
        {
            return new NavTileStore(
                _ => throw new InvalidOperationException("Resident channel memory contract tests do not load tiles."),
                residentTileCapacity,
                outputVertexCapacity,
                outputTriangleCapacity,
                outputPortalCapacity);
        }
    }
}
