using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>
/// 小队站上桥面层时，在视野外放一栋建筑生成认知变体：认知变体等于自身认知场的全量烘焙，
/// 分区摘要带单位层号分区和导航分区；再跑 100 步随机操作，不变量和导航一致性仍成立。
/// </summary>
[TestFixture]
public sealed class LayerUatTests
{
    private const int Count = 20;
    private const int MaxTicks = 3000;
    private const int FuzzOps = 100;
    private const long FuzzSeed = 7;
    private const int BuildingCells = 11;

    [Test]
    public void DeckSquad_BeliefVariant_DigestAndFuzz_CoverTheDeckLayer()
    {
        using var scene = CrowdUatKit.Scene();
        var session = scene.Session;
        CrowdUatBridgeRoute? route = null;
        foreach (int id in CrowdUatBridges.LayeredIds(session))
        {
            route = CrowdUatBridges.Route(session, scene.Nav, id);
            if (route != null) break;
        }

        Assert.That(route, Is.Not.Null, "地图上没有可测试的桥");
        CrowdUatKit.SpawnAt(session, route!.A.X, route.A.Y, Count, rIdx: 0);
        CrowdUatKit.Order(session, route.B.X, route.B.Y);
        for (int t = 0; t < MaxTicks && DeckCount(session) == 0; t++) CrowdUatKit.Step(session);
        int onDeck = DeckCount(session);
        Assert.That(onDeck, Is.GreaterThan(0), "单位未上桥面层");

        Fix64 w = scene.WorldCm;
        Fix64 far = route.A.X < w / 2 ? w * 9 / 10 : w / 10;
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "building",
            XCm = (int)far.ToLong(),
            YCm = (int)far.ToLong(),
            SizeCm = BuildingCells * scene.CellCm,
        }, CrowdSimCommands.Exec);
        CrowdUatKit.Step(session, 2);
        int variants = session.Beliefs!.VariantCount();
        int deckAtCheck = DeckCount(session);
        Assert.That(variants, Is.GreaterThan(0), "视野外的建筑没有生成认知变体");
        Assert.That(deckAtCheck, Is.GreaterThan(0), "核对认知变体时已没有单位站在桥面层");
        CrowdSimulationValidator.ValidateNav(session);

        var partitions = new List<CrowdSimPartition>();
        CrowdSimPartitions.Copy(session, partitions);
        bool level = false;
        int navSections = 0;
        foreach (var p in partitions)
        {
            if (p.Name == "units.level") level = true;
            if (p.Name.StartsWith("nav.", StringComparison.Ordinal)) navSections++;
        }

        Assert.That(level, Is.True, "状态摘要缺少 units.level 分区");
        Assert.That(navSections, Is.GreaterThan(0), "状态摘要缺少导航分区");

        var fuzz = CrowdFuzzRunner.Run(session, RegressCatalog().CreateFuzzPlan(FuzzSeed, FuzzOps));
        TestContext.Out.WriteLine(
            $"[uat-layers] onDeck={onDeck} deckAtCheck={deckAtCheck} variants={variants} navSections={navSections} fuzzOps={(fuzz.Passed ? FuzzOps : fuzz.Step)}");
        Assert.That(fuzz.Passed, Is.True, $"Fuzz 第 {fuzz.Step} 步 ({fuzz.OpId}) 失败：{fuzz.Error}");
        CrowdSimulationValidator.ValidateSim(session);
        CrowdSimulationValidator.ValidateNav(session);
    }

    private static int DeckCount(CrowdSimSession session)
    {
        int n = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (CrowdUatKit.StateAt(session, i).Level != 0) n++;
        }

        return n;
    }

    private static CrowdSimRegressCatalog RegressCatalog()
    {
        string relative = Path.Combine(
            "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod", "assets", "CrowdSimulation", "regress", "catalog.json");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return CrowdSimRegressCatalog.Parse(JsonNode.Parse(File.ReadAllText(path))!.AsObject());
        }

        throw new InvalidOperationException("找不到回归场景表。");
    }
}
