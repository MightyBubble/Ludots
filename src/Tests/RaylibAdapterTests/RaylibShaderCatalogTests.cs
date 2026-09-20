using NUnit.Framework;
using Ludots.Raylib.Render;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibShaderCatalogTests
{
    [Test]
    public void RegisterInstancing_RejectsEmptyShaderKey()
    {
        var catalog = new RaylibShaderCatalog();

        Assert.Throws<ArgumentException>(() => catalog.RegisterInstancing("", null!));
        Assert.Throws<ArgumentException>(() => catalog.RegisterInstancing("   ", null!));
    }

    [Test]
    public void RegisterInstancing_RejectsDuplicateShaderKey()
    {
        var catalog = new RaylibShaderCatalog();
        catalog.RegisterInstancing("lit.custom", null!);

        var ex = Assert.Throws<InvalidOperationException>(() => catalog.RegisterInstancing("lit.custom", null!));
        Assert.That(ex!.Message, Does.Contain("already registered"));
        Assert.That(ex.Message, Does.Contain("lit.custom"));
    }

    [Test]
    public void RequireInstancing_ThrowsForUnregisteredShaderKey()
    {
        var catalog = new RaylibShaderCatalog();

        var ex = Assert.Throws<InvalidOperationException>(() => catalog.RequireInstancing("lit.missing"));
        Assert.That(ex!.Message, Does.Contain("lit.missing"));
    }

    [Test]
    public void RequireInstancing_ReturnsRegisteredShader()
    {
        var catalog = new RaylibShaderCatalog();
        catalog.RegisterInstancing("lit.custom", null!);

        // RaylibLaneShader 构造函数私有且接线依赖 GL，注册表语义用 null 值占位验证存取闭环。
        Assert.That(catalog.RequireInstancing("lit.custom"), Is.Null);
        Assert.That(catalog.InstancingShaders.Count(), Is.EqualTo(1));
    }
}
