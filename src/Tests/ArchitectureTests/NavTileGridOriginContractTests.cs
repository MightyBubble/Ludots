using System;
using System.IO;
using Ludots.Core.Navigation.NavMesh;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// 瓦片网格原点的回归锁。
///
/// 烘焙按地形世界位置切瓦片：板锚定居中世界时首个瓦片位于负半轴。
/// 运行时寻址若不减去网格原点，负半轴坐标会被钳到 chunk 0 而指向错误瓦片 —— 地图看起来
/// "有 navmesh 但寻不到路"，且错误只在本应落在正半轴的目标上出现。
/// </summary>
[TestFixture]
public sealed class NavTileGridOriginContractTests
{
    private const int TileWidthCm = 6_400;
    private const int TileHeightCm = 6_400;
    private const int GridOriginXcm = -12_800;
    private const int GridOriginYcm = -12_800;

    /// <summary>
    /// 居中世界的起点与目标都落在正半轴：不修原点时 cx = 世界/瓦片 会偏掉半个网格宽度。
    /// </summary>
    [Test]
    public void NegativeOriginGrid_ProjectsPositiveWorldPointsOntoCorrectTiles()
    {
        NavQueryService query = CreateQueryWithOrigin();

        // 世界 (0, 0) 相对网格原点偏移 (12800, 12800) -> chunk (2, 2)。
        Assert.That(query.TryProject(0, 0, out NavLocation center), Is.True,
            "the grid centre of a centred board must project onto a resident tile.");
        Assert.That(center.TileId, Is.EqualTo(new NavTileId(2, 2, 0)));

        // 靠负半轴一侧但仍在网格内：( -7000, -7000 ) -> chunk (0, 0)。
        Assert.That(query.TryProject(-7_000, -7_000, out NavLocation lowCorner), Is.True);
        Assert.That(lowCorner.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));

        // 靠正半轴一侧：( 12_000, 12_000 ) -> chunk (3, 3)。
        Assert.That(query.TryProject(12_000, 12_000, out NavLocation highCorner), Is.True);
        Assert.That(highCorner.TileId, Is.EqualTo(new NavTileId(3, 3, 0)));
    }

    /// <summary>
    /// 同样的世界坐标，在缺省（原点 0）网格与显式原点网格上必须落到不同瓦片：
    /// 这就是"忘了传原点"与"传对原点"的可观测差别。
    /// </summary>
    [Test]
    public void MissingOrigin_ShiftsProjectionByHalfExtent()
    {
        NavQueryService withOrigin = CreateQueryWithOrigin();
        NavQueryService withoutOrigin = CreateQueryWithoutOrigin();

        Assert.That(withOrigin.TryProject(0, 0, out NavLocation correct), Is.True);
        Assert.That(withoutOrigin.TryProject(0, 0, out NavLocation naive), Is.True);

        Assert.That(correct.TileId, Is.EqualTo(new NavTileId(2, 2, 0)));
        Assert.That(naive.TileId, Is.EqualTo(new NavTileId(0, 0, 0)),
            "原点缺省 0 时同一个世界点落到网格另一角，这就是需要显式原点的原因。");
        Assert.That(naive.TileId, Is.Not.EqualTo(correct.TileId));
    }

    /// <summary>
    /// 跨瓦片寻路必须能穿过负半轴：起点在网格左下、目标在右下，路径应连通。
    /// </summary>
    [Test]
    public void NegativeOriginGrid_PathsAcrossTheGrid()
    {
        NavQueryService query = CreateQueryWithOrigin();

        NavPathResult path = query.TryFindPath(-7_000, -7_000, 12_000, 12_000);

        Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok), "a centred grid must be walkable end to end.");
        Assert.That(path.PathXcm.Length, Is.GreaterThan(0));
        Assert.That(path.PathXcm[0], Is.EqualTo(-7_000));
        Assert.That(path.PathXcm[path.PathXcm.Length - 1], Is.EqualTo(12_000));
    }

    /// <summary>
    /// 缺省原点的公开构造仍必须可用（旧地图从世界 0 起），且与显式传 0 行为一致。
    /// </summary>
    [Test]
    public void DefaultConstructor_BehavesLikeExplicitZeroOrigin()
    {
        NavTileStore store = CreateZeroOriginResidentStore();
        var implicitZero = new NavQueryService(store, layer: 0, areaCosts: null!, TileWidthCm, TileHeightCm);
        var explicitZero = new NavQueryService(store, layer: 0, areaCosts: null!, TileWidthCm, TileHeightCm, gridOriginXcm: 0, gridOriginYcm: 0);

        Assert.That(implicitZero.TryProject(0, 0, out NavLocation a), Is.True);
        Assert.That(explicitZero.TryProject(0, 0, out NavLocation b), Is.True);
        Assert.That(a.TileId, Is.EqualTo(b.TileId), "缺省原点必须等价于显式传 0。");
        Assert.That(a.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));

        var registry = new NavQueryServiceRegistry(
            new System.Collections.Generic.Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
            TileWidthCm,
            TileHeightCm);
        Assert.That(registry.GridOriginXcm, Is.Zero);
        Assert.That(registry.GridOriginYcm, Is.Zero);
    }

    /// <summary>
    /// Registry 把原点原样交给查询服务：两者必须一致，否则运行时会用错网格。
    /// </summary>
    [Test]
    public void Registry_ForwardsGridOriginToCreatedQueries()
    {
        var stores = new System.Collections.Generic.Dictionary<NavQueryServiceKey, NavTileStore>
        {
            [new NavQueryServiceKey(0, 0)] = CreateResidentStore()
        };
        var registry = new NavQueryServiceRegistry(stores, TileWidthCm, TileHeightCm, GridOriginXcm, GridOriginYcm);

        Assert.That(registry.GridOriginXcm, Is.EqualTo(GridOriginXcm));
        Assert.That(registry.GridOriginYcm, Is.EqualTo(GridOriginYcm));
        Assert.That(registry.TryCreateQuery(layer: 0, profile: 0, areaCosts: null!, out NavQueryService query), Is.True);
        Assert.That(query.TryProject(0, 0, out NavLocation loc), Is.True);
        Assert.That(loc.TileId, Is.EqualTo(new NavTileId(2, 2, 0)),
            "注册表建出的查询必须带上同一个网格原点。");
    }

    private static NavQueryService CreateQueryWithOrigin()
    {
        return new NavQueryService(
            CreateResidentStore(),
            layer: 0,
            areaCosts: null!,
            TileWidthCm,
            TileHeightCm,
            GridOriginXcm,
            GridOriginYcm);
    }

    private static NavQueryService CreateQueryWithoutOrigin()
    {
        return new NavQueryService(CreateResidentStore(), layer: 0, areaCosts: null!, TileWidthCm, TileHeightCm);
    }

    /// <summary>把 4x4 的平地瓦片驻留在内存里，origin 按传入网格原点偏移。</summary>
    private static NavTileStore CreateResidentStore()
    {
        return CreateStore(gridOriginXcm: GridOriginXcm, gridOriginYcm: GridOriginYcm);
    }

    private static NavTileStore CreateZeroOriginResidentStore()
    {
        return CreateStore(gridOriginXcm: 0, gridOriginYcm: 0);
    }

    private static NavTileStore CreateStore(int gridOriginXcm, int gridOriginYcm)
    {
        var store = new NavTileStore(_ => throw new FileNotFoundException("all tiles are resident"));
        for (int cy = 0; cy < 4; cy++)
        {
            for (int cx = 0; cx < 4; cx++)
            {
                NavTile flat = DefaultGridNavTileFactory.CreateFlatTile(
                    cx, cy, layer: 0, tileVersion: 1, TileWidthCm, TileHeightCm, tileWidthCells: 64, tileHeightCells: 64);
                store.Replace(WithOrigin(flat, cx, cy, gridOriginXcm, gridOriginYcm));
            }
        }

        return store;
    }

    /// <summary>
    /// 重新锚定瓦片到居中网格：几何不变，只把 origin 换成网格原点加 chunk 偏移，
    /// 模拟烘焙在居中板上产出的产物。
    /// </summary>
    private static NavTile WithOrigin(NavTile flat, int chunkX, int chunkY, int gridOriginXcm, int gridOriginYcm)
    {
        return new NavTile(
            flat.TileId,
            flat.TileVersion,
            flat.BuildConfigHash,
            flat.Checksum,
            gridOriginXcm + (chunkX * TileWidthCm),
            gridOriginYcm + (chunkY * TileHeightCm),
            flat.VertexXcm,
            flat.VertexYcm,
            flat.VertexZcm,
            flat.TriA,
            flat.TriB,
            flat.TriC,
            flat.N0,
            flat.N1,
            flat.N2,
            flat.Portals);
    }
}
