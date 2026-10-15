using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 演示 Mod 调试脚本装载冒烟:逐一按 crowd_simulation 每个自带 CrowdSimulationDebug.json 的 mod
/// 的加载序合并配置后解析——tick 读取、演示脚本结构校验(无成员 order 留到执行 tick 再补成员，
/// select 记录会被拒绝)、升序合同、玩家号表外 / 迷雾面 / area 恰一形状 / 字段类型
/// 脚本按运行时同一规则拼出(session.scriptAsset 引用的行在前,session.script 在后)。全过即绿。玩家号与迷雾面是否合法取决于演示自己的地图，会话按合并后 game.json 的
/// startupMapId 建立。新增演示自动纳入。
/// </summary>
public sealed class DemoScriptSmokeTests
{
    [Test]
    public void AllShowcaseDebugScripts_LoadAndEnqueueValidate()
    {
        var mods = ModConfigChain.ModsShipping("CrowdSimulationDebug.json", Path.Combine("showcases", "crowd_simulation"));
        Assert.That(mods, Is.Not.Empty, "mods/showcases/crowd_simulation 下未发现任何 CrowdSimulationDebug.json");
        TestContext.Out.WriteLine($"发现 {mods.Count} 份演示调试脚本");

        foreach (string modId in mods)
        {
            var chain = ModConfigChain.Open(modId);
            var debug = CrowdSimulationDebugConfig.Load(chain.Merged("CrowdSimulationDebug.json"));
            var rows = CrowdSimulationDebugConfig.SessionScript(debug.Session, chain.OpenAsset);
            if (rows is not { Length: > 0 })
            {
                TestContext.Out.WriteLine($"{modId}: 无会话脚本，跳过");
                continue;
            }

            if (ScriptsRunByOwnAcceptance.TryGetValue(modId, out string? coveredBy))
            {
                TestContext.Out.WriteLine($"{modId}: 地图不是种子图，脚本由 {coveredBy} 整段跑过");
                continue;
            }

            string seed = DemoSeedOf(chain);
            var (runtime, session) = S4DeployTruthTests.BuildSession(seed);
            using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
            session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));

            var demo = CrowdDemoScript.Load(CrowdSimulationRuntime.ComposeDemoScript(0, rows));
            Assert.DoesNotThrow(() => demo.ValidateStructure(session),
                $"{modId}: 脚本结构校验失败(升序/玩家号/迷雾面/恰一形状/字段类型)");
            TestContext.Out.WriteLine($"{modId}: 种子 {seed}, {demo.Count} 条演示指令结构校验通过");
        }
    }

    private const string CrowdMapPrefix = "crowd_simulation_";

    // 这里的会话只能从种子资产建。地图不是种子图的演示，必须有自己的验收测试按真实进图路径跑完整段脚本，并在此登记。
    private static readonly Dictionary<string, string> ScriptsRunByOwnAcceptance = new(StringComparer.Ordinal)
    {
        ["CrowdSimulationMinimalMod"] = "CrowdSimulationTests.Authoring.CrowdSimulationMinimalModTests",
    };

    private static string DemoSeedOf(ModConfigChain chain)
    {
        string mapId = chain.StartupMapId();
        if (!mapId.StartsWith(CrowdMapPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"{chain.ModId}: startupMapId={mapId} 不是 {CrowdMapPrefix}<种子> 地图");
        string seed = mapId.Substring(CrowdMapPrefix.Length);
        if (!Directory.Exists(S1SurfaceTruthTests.SeedDir(seed)))
            throw new InvalidOperationException($"{chain.ModId}: 地图 {mapId} 没有对应的种子资产 {S1SurfaceTruthTests.SeedDir(seed)}");
        return seed;
    }
}
