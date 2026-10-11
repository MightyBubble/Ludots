using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 演示 Mod 调试脚本装载冒烟:逐一加载 crowd_simulation 全部 showcase mod 的
/// CrowdSimulationDebug.json——tick 读取、指令边界解析(CrowdSimCommand.Parse)、升序合同、
/// 入队校验(tick 升序 / 玩家号表外 / 迷雾面在无 Fog 会话 / area 恰一形状 / 字段类型)
/// 全过即绿。玩家号与迷雾面是否合法取决于演示自己的地图,会话按同目录 game.json 的
/// startupMapId 建立。脚本与 game.json 经 csproj 内容链接进 assets/debug(通配,新增演示自动纳入)。
/// </summary>
public sealed class DemoScriptSmokeTests
{
    [Test]
    public void AllShowcaseDebugScripts_LoadAndEnqueueValidate()
    {
        string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "assets", "debug");
        var files = Directory.EnumerateFiles(root, "CrowdSimulationDebug.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.That(files, Is.Not.Empty, "assets/debug 下未发现任何 CrowdSimulationDebug.json(内容链接失效或演示资产缺失)");
        TestContext.Out.WriteLine($"发现 {files.Count} 份演示调试脚本");

        foreach (string file in files)
        {
            string modDir = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)[0];
            var debug = JsonNode.Parse(File.ReadAllText(file))!;
            if (debug["session"]?["script"] is not JsonArray script || script.Count == 0)
            {
                TestContext.Out.WriteLine($"{modDir}: 无 session.script,跳过");
                continue;
            }

            string seed = DemoSeedOf(file);
            var (runtime, session) = S4DeployTruthTests.BuildSession(seed);
            using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
            session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));

            var commands = script
                .Select(e => new CrowdCommand(e!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(e!["cmd"]!)))
                .ToArray();
            Assert.DoesNotThrow(() => session.Commands.Schedule(session, commands),
                $"{modDir}: 脚本装载/入队校验失败(升序/玩家号/迷雾面/恰一形状/字段类型)");
            Assert.That(session.Commands.PendingCount, Is.EqualTo(commands.Length), $"{modDir}: 入队数不一致");
            TestContext.Out.WriteLine($"{modDir}: 种子 {seed}, {commands.Length} 条指令装载入队通过");
        }
    }

    private const string CrowdMapPrefix = "crowd_simulation_";

    private static string DemoSeedOf(string scriptPath)
    {
        string gamePath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "game.json");
        if (!File.Exists(gamePath))
            throw new InvalidOperationException($"{scriptPath}: 同目录缺少 game.json,无法确定演示地图");
        string? mapId = JsonNode.Parse(File.ReadAllText(gamePath))!["startupMapId"]?.GetValue<string>();
        if (mapId is null || !mapId.StartsWith(CrowdMapPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"{gamePath}: startupMapId={mapId ?? "<缺失>"} 不是 {CrowdMapPrefix}<种子> 地图");
        string seed = mapId.Substring(CrowdMapPrefix.Length);
        if (!Directory.Exists(S1SurfaceTruthTests.SeedDir(seed)))
            throw new InvalidOperationException($"{gamePath}: 地图 {mapId} 没有对应的种子资产 {S1SurfaceTruthTests.SeedDir(seed)}");
        return seed;
    }
}
