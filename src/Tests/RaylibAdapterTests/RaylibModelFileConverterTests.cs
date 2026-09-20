using NUnit.Framework;
using Ludots.Raylib.Render;

namespace Ludots.Tests.RaylibAdapter;

/// <summary>
/// #1050 回归锁：native raylib 5.5 的 OBJ 装载分支对无 texcoord/normal 索引的面片
/// （`f v` 形态）AccessViolation。该形态的 OBJ 必须经 Assimp 转换器产出合法 GLB，
/// 引擎内不允许再出现直连 Rl.LoadModel 的 OBJ 路径。
/// </summary>
[TestFixture]
public sealed class RaylibModelFileConverterTests : IDisposable
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LudotsModelConverterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void BareVertexOnlyFacesObj_ConvertsToValidGlb()
    {
        // mass_navigation 资产的实际形态：无 vt/vn，面片纯顶点索引——native 装载即崩。
        string obj = Path.Combine(_tempDir, "bare.obj");
        File.WriteAllText(obj,
            "v -1 -1 -1\nv 1 -1 -1\nv 1 1 -1\nv -1 1 -1\nv -1 -1 1\nv 1 -1 1\nv 1 1 1\nv -1 1 1\n" +
            "f 1 2 3 4\nf 5 6 7 8\n");

        string glb = RaylibModelFileConverter.ConvertToCachedGlb(obj);
        AssertGlb(glb);
    }

    [Test]
    public void TexturedObjWithMtl_ConvertsToValidGlb()
    {
        string obj = Path.Combine(_tempDir, "textured.obj");
        string mtl = Path.Combine(_tempDir, "shared.mtl");
        File.WriteAllText(obj,
            "mtllib shared.mtl\nusemtl surface\n" +
            "v -1 -1 -1\nv 1 -1 -1\nv 1 1 -1\nv -1 1 -1\n" +
            "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n" +
            "vn 0 0 -1\n" +
            "f 1/1/1 2/2/1 3/3/1 4/4/1\n");
        File.WriteAllText(mtl,
            "newmtl surface\nKd 0.8 0.6 0.4\nKs 0.1 0.1 0.1\nNs 32\n");

        string glb = RaylibModelFileConverter.ConvertToCachedGlb(obj);
        AssertGlb(glb);
    }

    [Test]
    public void Conversion_IsCachedBySourceStamp()
    {
        string obj = Path.Combine(_tempDir, "cached.obj");
        File.WriteAllText(obj, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");

        string first = RaylibModelFileConverter.ConvertToCachedGlb(obj);
        string second = RaylibModelFileConverter.ConvertToCachedGlb(obj);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(File.Exists(first), Is.True);
    }

    [Test]
    public void ZeroMeshSource_FailsLoud()
    {
        string obj = Path.Combine(_tempDir, "empty.obj");
        File.WriteAllText(obj, "# no geometry at all\n");

        Assert.That(
            () => RaylibModelFileConverter.ConvertToCachedGlb(obj),
            Throws.InvalidOperationException.With.Message.Contains("无网格"));
    }

    [Test]
    public void UnsupportedExtension_PrepareNativeLoadableFailsLoud()
    {
        string path = Path.Combine(_tempDir, "model.usdz");
        File.WriteAllText(path, "not a model");

        Assert.That(
            () => RaylibModelFileLoader.PrepareNativeLoadable(path),
            Throws.InvalidOperationException.With.Message.Contains("不支持的模型格式"));
    }

    [Test]
    public void NativeGltf_PassesThroughUntouched()
    {
        string glb = Path.Combine(_tempDir, "direct.glb");
        File.WriteAllBytes(glb, new byte[] { 0x67, 0x6c, 0x54, 0x46, 0x02, 0x00, 0x00, 0x00 });

        Assert.That(RaylibModelFileLoader.PrepareNativeLoadable(glb), Is.EqualTo(glb));
    }

    private static void AssertGlb(string path)
    {
        Assert.That(File.Exists(path), Is.True, $"GLB 产物应存在：{path}");
        using FileStream stream = File.OpenRead(path);
        var header = new byte[12];
        int read = stream.Read(header, 0, header.Length);
        Assert.That(read, Is.EqualTo(12));
        Assert.That(header[0..4], Is.EqualTo(new byte[] { 0x67, 0x6c, 0x54, 0x46 }), "GLB magic 'glTF'");
        Assert.That(header[4] | (header[5] << 8), Is.EqualTo(2), "GLB container version 2");
        Assert.That(new FileInfo(path).Length, Is.GreaterThan(12), "GLB 应有 JSON chunk");
    }
}
