using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Spawning;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

using CrowdSimulationTests.Parity;

/// <summary>
/// 真值会话单位走模板生成管线的集成门:S4DeployTruthTests.BuildSession 就是真值测试的
/// 实际装配(经 VFS → ConfigPipeline → MapLoader.LoadTemplates 装载链取模板),
/// 生成单位后逐单位断言模板键引用可解析、呈现预置件在位、组件值与模板写真逐位一致
/// ——证明"值同源"落在实体事实上,不是装配口径的口头声明。
/// </summary>
public class TemplatePipelineIntegrationTests
{
    [Test]
    public void TruthSessionUnitsGoThroughTemplatePipeline()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        var wiring = session.Presentation;
        Assert.That(wiring.TemplateRegistry.Get("crowd_simulation.unit.infantry.r100"), Is.Not.Null,
            "模板注册表应装到 CrowdSimulationDemoContentMod 的 Entities/templates.json(经 MapLoader.LoadTemplates 装载链)");

        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(0, CrowdSimCommand.Parse((JsonObject)JsonNode.Parse(
                """{"type":"spawn","count":64}""")!)),
        });
        int guard = 0;
        while (guard++ < 16 && !session.Step(out _)) { }
        Assert.That(session.Units.Count, Is.GreaterThan(0), "spawn 应已落地");

        // 模板键 → 模板 id 反查表(验证单位的键引用解析回真实模板)
        var keyToTemplate = new Dictionary<int, string>();
        foreach (var row in wiring.TemplatesByUnitTypeRadius)
        {
            foreach (var (templateId, keyId) in row) keyToTemplate[keyId] = templateId;
        }

        var seenProfiles = new HashSet<string>();
        for (int i = 0; i < session.Units.Count; i++)
        {
            var entity = session.Units.EntityAt(i);
            uint handle = session.Units.HandleAt(i);

            Assert.That(session.World.Has<EntityTemplateKeyRef>(entity), Is.True, $"单位 {handle:x} 缺模板键引用");
            var keyRef = session.World.Get<EntityTemplateKeyRef>(entity);
            Assert.That(keyToTemplate.TryGetValue(keyRef.TemplateKeyId, out var templateId), Is.True,
                $"单位 {handle:x} 模板键 {keyRef.TemplateKeyId} 不在映射表");

            // 值同源:定点数来自模板写真;字符串 id 留在模板,实体上是生成时解析的下标。
            var agent = session.World.Get<CrowdSimulationAgent>(entity);
            var templateNode = wiring.TemplateRegistry.Get(templateId)!.Components["CrowdSimulationAgent"]!;
            string profileId = templateNode["profileId"]!.GetValue<string>();
            int expectedProfile = -1;
            int expectedAgent = -1;
            for (int p = 0; p < runtime.Profiles.Count; p++)
            {
                if (runtime.Profiles[p].Id == profileId)
                {
                    expectedProfile = p;
                    expectedAgent = runtime.Profiles[p].AgentTypeIndex;
                    break;
                }
            }

            Assert.That(expectedProfile, Is.GreaterThanOrEqualTo(0), $"模板档案 \"{profileId}\" 不在运行时表");
            Assert.That(agent.ProfileIndex, Is.EqualTo(expectedProfile),
                $"单位 {handle:x} 档案下标应在生成时从模板 profileId 解析");
            Assert.That(agent.AgentTypeIndex, Is.EqualTo(expectedAgent),
                $"单位 {handle:x} 移动类型下标应在生成时从体型 navProfileId 解析");
            Assert.That(agent.PersonalRadiusCm, Is.EqualTo(runtime.Profiles[expectedProfile].PersonalRadiusCm),
                $"单位 {handle:x} 个人半径应等于体型半径 × avoidanceRadiusScale");
            Assert.That(agent.PushPriority, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(templateNode["pushPriority"]!.GetValue<int>())),
                $"单位 {handle:x} 推挤优先级应来自模板写真");
            Assert.That(agent.SpeedCmPerSec, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(templateNode["speedCmPerSec"]!.GetValue<int>())),
                $"单位 {handle:x} 速度应来自模板写真");
            seenProfiles.Add(profileId);
        }

        Assert.That(seenProfiles.Count, Is.GreaterThan(1), "64 单位应覆盖多种体型档案(部署按类型×体型散布)");
        TestContext.Out.WriteLine($"{session.Units.Count} 单位全部经模板管线,{seenProfiles.Count} 种档案,模板写真值逐位一致");

        for (int t = 0; t < runtime.UnitTypes.Count; t++)
        {
            var row = wiring.TemplatesByUnitTypeRadius[t];
            for (int r = 0; r < row.Count; r++)
            {
                var (mappedId, keyId) = row[r];
                Assert.That(wiring.TemplateRegistry.Get(mappedId), Is.Not.Null,
                    $"unitType={runtime.UnitTypes[t].Id} radiusClass={r} 映射到未知模板 {mappedId}");
                Assert.That(keyToTemplate[keyId], Is.EqualTo(mappedId),
                    $"unitType={runtime.UnitTypes[t].Id} radiusClass={r} 模板键与表不一致");
            }
        }
    }

    [Test]
    public void DifferentiatedTemplates_PerRadiusValuesMatchEntityNotSharedDemoDefault()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var wiring = session.Presentation;
        string templateR100 = wiring.TemplatesByUnitTypeRadius[0][0].TemplateId;
        string templateR200 = wiring.TemplatesByUnitTypeRadius[0][1].TemplateId;
        var node100 = wiring.TemplateRegistry.Get(templateR100)!.Components["CrowdSimulationAgent"]!;
        var node200 = wiring.TemplateRegistry.Get(templateR200)!.Components["CrowdSimulationAgent"]!;
        int speed100 = node100["speedCmPerSec"]!.GetValue<int>();
        int speed200 = node200["speedCmPerSec"]!.GetValue<int>();
        int push100 = node100["pushPriority"]!.GetValue<int>();
        int push200 = node200["pushPriority"]!.GetValue<int>();
        Assert.That(push200, Is.Not.EqualTo(push100), "相邻半径级模板应有可区分的写真值");

        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = 1,
            XCm = 480000,
            YCm = 560000,
            Count = 1,
            UnitType = 0,
            RIdx = 0,
        }, CrowdSimCommands.Exec);
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = 1,
            XCm = 481000,
            YCm = 560000,
            Count = 1,
            UnitType = 0,
            RIdx = 1,
        }, CrowdSimCommands.Exec);

        var agent100 = session.World.Get<CrowdSimulationAgent>(session.Units.EntityAt(0));
        var agent200 = session.World.Get<CrowdSimulationAgent>(session.Units.EntityAt(1));
        Assert.That(agent100.SpeedCmPerSec, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(speed100)));
        Assert.That(agent200.SpeedCmPerSec, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(speed200)));
        Assert.That(agent100.PushPriority, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(push100)));
        Assert.That(agent200.PushPriority, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(push200)));
        Assert.That(agent100.PushPriority, Is.Not.EqualTo(agent200.PushPriority));
    }

}
