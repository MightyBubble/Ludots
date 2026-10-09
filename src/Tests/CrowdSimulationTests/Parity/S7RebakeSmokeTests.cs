using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S7 结构动态化内核冒烟(无真值,纯 C# 不变量):放置 / 拆除 / 寿命到期驱动增量重烘,
/// DB-03 增量 vs 全量一致性检查在每次 op 后必须通过;重烘 job 按 rebakeSlices 切片推进;
/// 同一脚本回放两次校验码逐位一致(RT-04 tick 驱动的确定性)。
/// </summary>
[TestFixture]
public class S7RebakeSmokeTests
{
    [Test]
    public void PlaceRemoveExpire_KeepsIncrementalNavConsistent()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.TruthNavFrozen = true; // F02:S7 冒烟走 S7 真值同款冻结口径(结构 op 会岔认知)
        session.VerifyIncrementalNav = true;

        // 脚本(与 S7 真值同构同尺寸:格距 62.5m,足迹中心落在格心):部署 → 行军 →
        // 建筑挡路 → 拆除 → 寿命路障(s7barrier 4 秒,simDt=1/3s → +12 tick 到期)→ 道路(仅代价)
        (int Tick, string Json)[] entries =
        {
            (0, """{"type":"spawnAt","xCm":480000,"yCm":560000,"count":120,"player":1,"unitType":0,"rIdx":0}"""),
            (2, """{"type":"order","xCm":1200000,"yCm":800000,"player":1,"shape":"box"}"""),
            (30, """{"type":"placeStructure","template":"building","xCm":503125,"yCm":565625,"sizeCm":14000}"""),
            (60, """{"type":"removeStructureAt","xCm":503125,"yCm":565625}"""),
            (90, """{"type":"placeStructure","template":"s7barrier","xCm":521875,"yCm":571875,"sizeCm":7000}"""),
            (120, """{"type":"placeStructure","template":"road","xCm":496875,"yCm":565625,"sizeCm":3000,"toXCm":521875,"toYCm":565625}"""),
        };
        var script = new List<CrowdCommand>();
        foreach (var (tick, json) in entries) script.Add(new CrowdCommand(tick, CrowdSimCommand.Parse(Cmd(json))));
        session.Commands.Schedule(session, script);

        int rebakes = 0;
        int dirtyTiles = 0;
        session.RebakeReported += r =>
        {
            rebakes++;
            dirtyTiles += r.Tiles;
        };
        var first = new List<string>();
        session.Advance(240, first);
        Assert.That(rebakes, Is.GreaterThanOrEqualTo(5), $"重烘报告次数 {rebakes}:建筑放置/拆除 + 路障放置/到期 + 道路");
        Assert.That(dirtyTiles, Is.GreaterThan(0), $"重烘焙到的脏 tile 累计 {dirtyTiles}:足迹必须真实盖住格心");
        // 建筑拆除后原阻挡格必须解封(该点后被道路覆盖,EntityAt 非空是对的,查阻挡栅格)
        int wallCell = 90 * session.Config.NavCellCount + 80;
        Assert.That(session.Structures!.Blocked[wallCell], Is.EqualTo(0), "建筑拆除后原阻挡格仍被标阻挡");

        // 回放:同一脚本、全新会话,校验码逐位一致
        var (runtime2, session2) = S4DeployTruthTests.BuildSession("s1337");
        using var service2 = new PathQueryService(session2.Navs, runtime2, 1, TimeSpan.FromSeconds(30), session2.ResolveNavContext);
        session2.EnableMovement(CrowdMovementKernel.Create(session2), new CrowdSimPlanner(session2, service2));
        session2.BlockOnDueReplies = true;
        session2.TruthNavFrozen = true; // F02:回放侧同款冻结
        session2.Commands.Schedule(session2, script);
        var second = new List<string>();
        session2.Advance(240, second);
        Assert.That(second, Is.EqualTo(first).AsCollection, "同脚本回放校验码不一致(结构 op 管线非确定)");
    }

    [Test]
    public void LayeredTemplatePlace_IsRejected()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        Assert.Throws<InvalidOperationException>(() =>
            Ludots.Core.CrowdSimulation.Structures.CrowdStructureOps.PlaceStructure(
                session, "bridge", Fix64Cm(500000), Fix64Cm(500000), Fix64Cm(400), Fix64Cm(600000), Fix64Cm(500000)));
    }

    private static System.Text.Json.Nodes.JsonNode Cmd(string json) =>
        JsonNode.Parse(json) ?? throw new InvalidOperationException("测试指令 JSON 解析为空");

    private static Fix64 Fix64Cm(int cm) => Fix64.FromInt(cm);
}
