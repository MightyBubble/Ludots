using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>地图上的桥只认结构模板 bridge；配置里没有它时开图就报错，不能把桥悄悄丢掉。</summary>
[TestFixture]
public class CrowdMapBridgeTemplateTests
{
    [Test]
    public void MapBridges_WithoutBridgeTemplate_Throw()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"]!.AsObject().Remove("bridge");
        var config = TestDefaults.Assemble(CrowdSimulationConfig.Load(json));
        var store = BuildStore(config);

        var ex = Assert.Throws<InvalidOperationException>(() => store.InstallMapBridges(new[] { Bridge(config) }));
        Assert.That(ex!.Message, Does.Contain("bridge"));
    }

    [Test]
    public void MapBridges_InstallWithBridgeTemplate()
    {
        var config = TestDefaults.Assemble();
        var store = BuildStore(config);
        int before = store.EntityIds.Count;

        store.InstallMapBridges(new[] { Bridge(config), Bridge(config) });

        Assert.That(store.EntityIds.Count, Is.EqualTo(before + 2));
        int last = store.EntityIds[^1];
        Assert.That(store.LiveTemplate(last).Id, Is.EqualTo("bridge"));
        Assert.That(store.FootprintOf(last).Shape, Is.EqualTo(CrowdStructureShape.Path));
    }

    private static LayeredPathRecord Bridge(CrowdSimulationRuntimeConfig config)
    {
        int cell = config.NavCellSizeCm;
        return new LayeredPathRecord(new CrowdSimulationBridgeSpan
        {
            X0Cm = cell / 2,
            Y0Cm = cell + cell / 2,
            X1Cm = 3 * cell + cell / 2,
            Y1Cm = cell + cell / 2,
            WidthCm = cell,
        }, "deck");
    }

    private static CrowdStructuresStore BuildStore(CrowdSimulationRuntimeConfig config)
    {
        int side = config.NavCellsPerSide;
        int n2 = side * side;
        var grid = new SurfaceGrid
        {
            NavCellsPerSide = side,
            NavCellSizeCm = config.NavCellSizeCm,
            TerrainType = new byte[n2],
            Area = new byte[n2],
            Blocked = new byte[n2],
            JumpCandidates = [],
        };
        return CrowdStructuresStore.Build(config, grid, []);
    }
}
