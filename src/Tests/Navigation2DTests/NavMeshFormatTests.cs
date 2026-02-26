using System.IO;
using Ludots.Core.Navigation.NavMesh;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    public class NavMeshFormatTests
    {
        [Test]
        public void NavTileBinary_PreservesTriAreaIds()
        {
            var tile = new NavTile(
                new NavTileId(0, 0, 0),
                tileVersion: 1,
                buildConfigHash: 123UL,
                checksum: 0UL,
                originXcm: 0,
                originZcm: 0,
                vertexXcm: new[] { 0, 100, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                n0: new[] { -1 },
                n1: new[] { -1 },
                n2: new[] { -1 },
                triAreaIds: new byte[] { 5 },
                portals: System.Array.Empty<NavBorderPortal>());

            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tile);
            ms.Position = 0;
            var read = NavTileBinary.Read(ms);

            Assert.That(read.TriangleCount, Is.EqualTo(1));
            Assert.That(read.TriAreaIds[0], Is.EqualTo((byte)5));
        }

        [Test]
        public void NavQueryService_UsesLayerWhenLocatingTiles()
        {
            var tile = new NavTile(
                new NavTileId(0, 0, 2),
                tileVersion: 1,
                buildConfigHash: 123UL,
                checksum: 0UL,
                originXcm: 0,
                originZcm: 0,
                vertexXcm: new[] { 0, 100, 0 },
                vertexYcm: new[] { 0, 0, 0 },
                vertexZcm: new[] { 0, 0, 100 },
                triA: new[] { 0 },
                triB: new[] { 1 },
                triC: new[] { 2 },
                n0: new[] { -1 },
                n1: new[] { -1 },
                n2: new[] { -1 },
                triAreaIds: new byte[] { 0 },
                portals: System.Array.Empty<NavBorderPortal>());

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                NavTileBinary.Write(ms, tile);
                bytes = ms.ToArray();
            }

            var store = new NavTileStore(id =>
            {
                Assert.That(id.Layer, Is.EqualTo(2));
                return new MemoryStream(bytes, writable: false);
            });

            var query = new NavQueryService(store, layer: 2, areaCosts: NavAreaCostTable.CreateDefault());
            Assert.That(query.TryProject(0, 0, out var loc), Is.True);
            Assert.That(loc.TileId.Layer, Is.EqualTo(2));
        }
    }
}
