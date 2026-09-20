using System.Text.Json;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Tool;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class WalkabilityTextureExporterTests
{
    [Test]
    public void Rasterize_SyntheticNavTile_IsDeterministicAndPreservesAreaPalette()
    {
        NavTile tile = CreateTriangleTile(areaId: 7);
        var bounds = new WalkabilityTextureBounds(0, 0, 400, 400);

        byte[] first = WalkabilityTextureExporter.Rasterize(new[] { tile }, bounds, 4, 4);
        byte[] second = WalkabilityTextureExporter.Rasterize(new[] { tile }, bounds, 4, 4);
        WalkabilityAreaColor color = WalkabilityTextureExporter.GetAreaColor(7);

        Assert.That(second, Is.EqualTo(first));
        AssertPixel(first, 4, x: 0, y: 0, color, alpha: 255);
        AssertPixel(first, 4, x: 1, y: 0, default, alpha: 0);
        AssertPixel(first, 4, x: 1, y: 1, color, alpha: 255);
        AssertPixel(first, 4, x: 3, y: 3, color, alpha: 255);
    }

    [Test]
    public void ExportDirectory_WritesPngAndWorldBoundsSidecar()
    {
        string root = Path.Combine(Path.GetTempPath(), "ludots-nav-texture-" + Guid.NewGuid().ToString("N"));
        string tileDirectory = Path.Combine(root, "tiles");
        string outputPath = Path.Combine(root, "nav_walkability.png");
        Directory.CreateDirectory(tileDirectory);
        try
        {
            using (FileStream stream = File.Create(Path.Combine(tileDirectory, "navtile_0_0.ntil")))
            {
                NavTileBinary.Write(stream, CreateTriangleTile(areaId: 2));
            }

            WalkabilityTextureExportResult result = WalkabilityTextureExporter.ExportDirectory(
                tileDirectory,
                outputPath,
                width: 4,
                height: 4,
                explicitBounds: new WalkabilityTextureBounds(0, 0, 400, 400));

            byte[] png = File.ReadAllBytes(outputPath);
            using JsonDocument sidecar = JsonDocument.Parse(File.ReadAllText(outputPath + ".json"));
            Assert.That(png[..8], Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            Assert.That(result.TileCount, Is.EqualTo(1));
            Assert.That(result.TriangleCount, Is.EqualTo(1));
            Assert.That(sidecar.RootElement.GetProperty("boundsCm").GetProperty("maxX").GetInt32(), Is.EqualTo(400));
            Assert.That(
                sidecar.RootElement.GetProperty("contentHash").GetString(),
                Is.EqualTo(result.ContentHash));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static NavTile CreateTriangleTile(byte areaId)
    {
        return new NavTile(
            new NavTileId(0, 0),
            tileVersion: 1,
            buildConfigHash: 2,
            checksum: 0,
            originXcm: 0,
            originZcm: 0,
            vertexXcm: new[] { 0, 400, 0 },
            vertexYcm: new[] { 0, 0, 0 },
            vertexZcm: new[] { 0, 0, 400 },
            triA: new[] { 0 },
            triB: new[] { 1 },
            triC: new[] { 2 },
            n0: new[] { -1 },
            n1: new[] { -1 },
            n2: new[] { -1 },
            triAreaIds: new[] { areaId },
            portals: Array.Empty<NavBorderPortal>());
    }

    private static void AssertPixel(
        byte[] rgba,
        int width,
        int x,
        int y,
        WalkabilityAreaColor color,
        byte alpha)
    {
        int offset = ((y * width) + x) * 4;
        Assert.That(rgba[offset], Is.EqualTo(alpha == 0 ? 0 : color.R));
        Assert.That(rgba[offset + 1], Is.EqualTo(alpha == 0 ? 0 : color.G));
        Assert.That(rgba[offset + 2], Is.EqualTo(alpha == 0 ? 0 : color.B));
        Assert.That(rgba[offset + 3], Is.EqualTo(alpha));
    }
}
