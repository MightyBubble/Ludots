using System;
using System.IO;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibTerrainRendererTests
{
    [Test]
    public void ReceiverSurface_ImplementsReceiverMeshProjectorContract()
    {
        using var renderer = new RaylibTerrainRenderer();
        Assert.That(renderer, Is.InstanceOf<IRaylibReceiverMeshProjector>());
    }

    [Test]
    public void ReceiverSurface_AabbDrawRejectsNonFiniteBoundsBeforeTouchingGpu()
    {
        using var renderer = new RaylibTerrainRenderer();
        Assert.That(
            () => renderer.DrawMeshesOverlappingAabbMeters(
                float.NaN, 0f, 0f, 10f, 10f, 10f, default(Raylib_cs.Material)),
            Throws.ArgumentException.With.Message.Contains("finite"));
        Assert.That(
            () => renderer.DrawMeshesOverlappingAabbMeters(
                0f, 0f, 0f, 10f, float.PositiveInfinity, 10f, default(Raylib_cs.Material)),
            Throws.ArgumentException.With.Message.Contains("finite"));
    }

    [Test]
    public void ReceiverSurface_AabbDrawRejectsInvertedBoundsBeforeTouchingGpu()
    {
        using var renderer = new RaylibTerrainRenderer();
        Assert.That(
            () => renderer.DrawMeshesOverlappingAabbMeters(
                10f, 0f, 0f, 0f, 10f, 10f, default(Raylib_cs.Material)),
            Throws.ArgumentException.With.Message.Contains("min must be <= max"));
    }

    [Test]
    public void ReceiverSurface_FitWithoutStampSourceThrowsInsteadOfUsingAuthoredY()
    {
        using var renderer = new RaylibTerrainRenderer();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => renderer.FitYawedStampProjectorCenter(
                new Vector3(1f, 9f, 2f),
                0f,
                new Vector2(3.8f, 4.2f),
                71))!;
        Assert.That(ex.Message, Does.Contain(nameof(RaylibTerrainRenderer.BindStampHeightSampleSource)));
    }

    [Test]
    public void ComputeTerrainAabbMeters_TracksBuiltMeshVertexExtents()
    {
        var buffer = new ChunkMeshWriteBuffer();
        Span<Vector3> vertices = stackalloc Vector3[]
        {
            new(2f, 4f, 6f),
            new(-1f, 8f, 3f),
            new(0.5f, 5f, -2f),
        };
        buffer.EnsureAdditionalVertices(vertices.Length);
        foreach (Vector3 vertex in vertices)
        {
            buffer.AppendVertex(vertex, Vector3.UnitY, Vector4.One);
        }

        RaylibTerrainRenderer.ComputeTerrainAabbMeters(
            buffer,
            out float minX,
            out float minY,
            out float minZ,
            out float maxX,
            out float maxY,
            out float maxZ);

        Assert.That(minX, Is.EqualTo(-1f));
        Assert.That(minY, Is.EqualTo(4f));
        Assert.That(minZ, Is.EqualTo(-2f));
        Assert.That(maxX, Is.EqualTo(2f));
        Assert.That(maxY, Is.EqualTo(8f));
        Assert.That(maxZ, Is.EqualTo(6f));
    }

        [Test]
        public void HostLoop_BindsMapLaneReceiverProjector_AndFeedsBothTerrainLanes()
        {
            string repoRoot = FindRepoRoot();
            string hostSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Raylib",
                "Ludots.Adapter.Raylib",
                "RaylibHostLoop.cs"));
            string frameRendererSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Adapters",
                "Raylib",
                "Ludots.Adapter.Raylib",
                "Rendering",
                "RaylibFrameRenderer.cs"));

            Assert.That(hostSource, Does.Contain("new MapLaneReceiverMeshProjector(engine, continuousHeightmapRenderer, terrainRenderer, primitiveRenderer.StaticMeshReceiverProjector)"));
            Assert.That(frameRendererSource, Does.Contain("_continuousHeightmapRenderer.BindStampHeightSampleSource(continuousHeightmap)"));
            Assert.That(frameRendererSource, Does.Contain("_terrainRenderer.BindStampHeightSampleSource(continuousHeightmap)"));
            Assert.That(
                hostSource.IndexOf("primitiveRenderer.BindReceiverMeshProjector(continuousHeightmapRenderer)", StringComparison.Ordinal),
                Is.EqualTo(-1),
                "Decal receiver must follow the focused map lane, not hard-bind the visual heightmap renderer.");
        }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Directory.GetParent(current)!.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }
}
