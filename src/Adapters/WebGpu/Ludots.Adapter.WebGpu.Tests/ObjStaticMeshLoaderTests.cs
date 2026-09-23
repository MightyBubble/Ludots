using System.Text;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class ObjStaticMeshLoaderTests
{
    [Test]
    public void QuadIsTriangulatedIntoAWebGpuMesh()
    {
        using var stream = Utf8("""
            o marker
            v -1 0 -1
            v 1 0 -1
            v 1 0 1
            v -1 0 1
            f 1 4 3 2
            """);

        var mesh = ObjStaticMeshLoader.Load(stream, key: 17);

        Assert.That(mesh.Key, Is.EqualTo(17));
        Assert.That(mesh.Vertices.Length, Is.EqualTo(6));
        Assert.That(mesh.Indices.Length, Is.EqualTo(6));
        Assert.That(mesh.Vertices.Span[0].Normal.Y, Is.GreaterThan(0.99f));
    }

    [Test]
    public void NegativeIndicesAndExplicitNormalsAreAccepted()
    {
        using var stream = Utf8("""
            v 0 0 0
            v 1 0 0
            v 0 0 1
            vn 0 1 0
            f -3//1 -1//1 -2//1
            """);

        var mesh = ObjStaticMeshLoader.Load(stream, key: 3);

        Assert.That(mesh.Vertices.Length, Is.EqualTo(3));
        Assert.That(mesh.Vertices.Span[2].Normal.Y, Is.EqualTo(1f));
    }

    [Test]
    public void UnsupportedGeometryDirectiveFailsExplicitly()
    {
        using var stream = Utf8("""
            v 0 0 0
            v 1 0 0
            v 0 0 1
            l 1 2 3
            """);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => ObjStaticMeshLoader.Load(stream, key: 1))!;
        Assert.That(exception.Message, Does.Contain("unsupported directive 'l'"));
    }

    [Test]
    public void FormalSingleMaterialDeclarationIsAccepted()
    {
        using var stream = Utf8("""
            mtllib mass_navigation_materials.mtl
            o marker
            usemtl mass_navigation_surface
            v 0 0 0
            v 0 0 1
            v 1 0 0
            f 1 2 3
            """);

        var mesh = ObjStaticMeshLoader.Load(stream, key: 9);

        Assert.That(mesh.Indices.Length, Is.EqualTo(3));
    }

    [Test]
    public void MaterialSwitchFailsExplicitly()
    {
        using var stream = Utf8("""
            mtllib model.mtl
            usemtl first
            v 0 0 0
            v 0 0 1
            v 1 0 0
            f 1 2 3
            usemtl second
            """);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => ObjStaticMeshLoader.Load(stream, key: 1))!;
        Assert.That(exception.Message, Does.Contain("multiple OBJ materials are unsupported"));
    }

    [Test]
    public void UnknownRegisteredFormatFailsExplicitly()
    {
        using var stream = Utf8("mesh");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => WebGpuMeshAssetLoader.Load(stream, "Mod:assets/model.fbx", key: 1))!;
        Assert.That(exception.Message, Does.Contain("unsupported format '.fbx'"));
    }

    private static MemoryStream Utf8(string value) => new(Encoding.UTF8.GetBytes(value));
}
