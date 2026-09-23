using System.Numerics;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class GroundOverlayMeshCompilerTests
{
    [Test]
    public void AllFormalShapesCompileToIndexedTriangles()
    {
        GroundOverlayItem[] items =
        [
            Create(GroundOverlayShape.Circle),
            Create(GroundOverlayShape.Cone),
            Create(GroundOverlayShape.Line),
            Create(GroundOverlayShape.Ring),
        ];
        var compiler = new GroundOverlayMeshCompiler();

        var mesh = compiler.Compile(items);

        Assert.That(mesh.Vertices.Length, Is.GreaterThan(0));
        Assert.That(mesh.Indices.Length, Is.GreaterThan(0));
        Assert.That(mesh.Indices.Length % 3, Is.Zero);
        Assert.That(mesh.Vertices.Span[0].Position.Y, Is.EqualTo(2.025f).Within(0.0001f));
    }

    [Test]
    public void StableCompilationDoesNotAllocateAfterCapacityWarmup()
    {
        GroundOverlayItem[] items = [Create(GroundOverlayShape.Ring)];
        var compiler = new GroundOverlayMeshCompiler();
        compiler.Compile(items);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            compiler.Compile(items);
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void InvalidFormalShapeFailsExplicitly()
    {
        GroundOverlayItem[] items = [Create((GroundOverlayShape)255)];
        var compiler = new GroundOverlayMeshCompiler();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => compiler.Compile(items))!;
        Assert.That(exception.Message, Does.Contain("unknown shape value 255"));
    }

    private static GroundOverlayItem Create(GroundOverlayShape shape) => new()
    {
        StableId = 7,
        Shape = shape,
        Center = new Vector3(1f, 2f, 3f),
        Radius = 4f,
        InnerRadius = 2f,
        Angle = 0.6f,
        Rotation = 0.4f,
        Length = 5f,
        Width = 0.5f,
        FillColor = new Vector4(0.2f, 0.7f, 1f, 0.25f),
        BorderColor = new Vector4(0.5f, 0.9f, 1f, 0.8f),
        BorderWidth = 0.03f,
    };
}
