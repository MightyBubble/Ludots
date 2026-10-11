using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>寿命到期查询：各仓只报自己的到期 id，按 id 升序。</summary>
[TestFixture]
public class CrowdStructuresDueExpiriesTests
{
    [Test]
    public void DueExpiries_EachStoreReportsOnlyItsOwnIds()
    {
        var config = TestDefaults.Assemble();
        var a = BuildStore(config);
        var b = BuildStore(config);
        int tpl = a.TemplateIndexOf("building");
        Assert.That(tpl, Is.GreaterThanOrEqualTo(0));
        var fp = new CrowdStructureFootprint
        {
            Shape = CrowdStructureShape.Rect,
            X = Fix64.FromInt(3000),
            Y = Fix64.FromInt(3000),
            Hx = Fix64.FromInt(200),
            Hy = Fix64.FromInt(200),
        };
        var (idA1, _) = a.PlaceEntity(tpl, fp, 4);
        var (idA2, _) = a.PlaceEntity(tpl, fp, 6);
        var (idB, _) = b.PlaceEntity(tpl, fp, 6);

        Assert.That(Collect(a, 3), Is.Empty);
        Assert.That(Collect(b, 3), Is.Empty);
        Assert.That(Collect(a, 4), Is.EqualTo(new[] { idA1 }).AsCollection);
        Assert.That(Collect(b, 4), Is.Empty);
        Assert.That(Collect(a, 6), Is.EqualTo(new[] { idA1, idA2 }).AsCollection);
        Assert.That(Collect(b, 6), Is.EqualTo(new[] { idB }).AsCollection);
        Assert.That(Collect(a, 6), Is.EqualTo(new[] { idA1, idA2 }).AsCollection);
        Assert.That(Collect(b, 0), Is.Empty);

        var empty = BuildStore(config);
        ReadOnlySpan<int> heldA = a.DueExpiries(6);
        Assert.That(b.DueExpiries(6).ToArray(), Is.EqualTo(new[] { idB }).AsCollection);
        Assert.That(empty.DueExpiries(6).IsEmpty, Is.True);
        Assert.That(heldA.ToArray(), Is.EqualTo(new[] { idA1, idA2 }).AsCollection, "别的仓查询不得改写本仓已交出的跨度");
    }

    private static List<int> Collect(CrowdStructuresStore store, int tick)
    {
        var ids = new List<int>();
        foreach (int id in store.DueExpiries(tick)) ids.Add(id);
        return ids;
    }

    private static CrowdStructuresStore BuildStore(CrowdSimulationRuntimeConfig config)
    {
        int n = config.NavCellCount;
        int n2 = n * n;
        var grid = new SurfaceGrid
        {
            CellCount = n,
            CellSizeCm = config.NavCellSizeCm,
            TerrainType = new byte[n2],
            Area = new byte[n2],
            Blocked = new byte[n2],
            JumpCandidates = [],
        };
        return CrowdStructuresStore.Build(config, grid, []);
    }
}
