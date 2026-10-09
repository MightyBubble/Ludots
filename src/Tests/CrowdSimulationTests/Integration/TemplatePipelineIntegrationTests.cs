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
/// 实际装配(经 L44 改造后经 VFS→ConfigPipeline→MapLoader.LoadTemplates 装载链取模板),
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
            "模板注册表应装到 CrowdSimulationMod 的 Entities/templates.json(经 MapLoader.LoadTemplates 装载链)");

        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(0, CrowdSimCommand.Parse((JsonObject)JsonNode.Parse(
                """{"type":"spawn","count":64}""")!)),
        });
        string? hash = null;
        int guard = 0;
        while (hash == null && guard++ < 16) hash = session.Step();
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

            // 模板生成管线的逐实体证据:模板键引用可解析、呈现稳定 id 已分配、
            // 呈现同步所需的既往位组件在位(无头直建路径不会挂这些)
            Assert.That(session.World.Has<EntityTemplateKeyRef>(entity), Is.True, $"单位 {handle:x} 缺模板键引用");
            var keyRef = session.World.Get<EntityTemplateKeyRef>(entity);
            Assert.That(keyToTemplate.TryGetValue(keyRef.TemplateKeyId, out var templateId), Is.True,
                $"单位 {handle:x} 模板键 {keyRef.TemplateKeyId} 不在映射表");
            Assert.That(session.World.Has<Ludots.Core.Presentation.Components.PresentationStableId>(entity), Is.True,
                $"单位 {handle:x} 缺呈现稳定 id");
            Assert.That(session.World.Get<Ludots.Core.Presentation.Components.PresentationStableId>(entity).Value, Is.GreaterThan(0),
                $"单位 {handle:x} 呈现稳定 id 未分配");
            Assert.That(session.World.Has<PreviousWorldPositionCm>(entity), Is.True,
                $"单位 {handle:x} 缺 PreviousWorldPositionCm(呈现同步查询要求)");

            // 值同源:单位组件值与模板写真一致(证明模板值在生效,不是配置回落值)
            var agent = session.World.Get<CrowdSimulationAgent>(entity);
            var templateNode = wiring.TemplateRegistry.Get(templateId)!.Components["CrowdSimulationAgent"]!;
            Assert.That(agent.ProfileId, Is.EqualTo(templateNode["profileId"]!.GetValue<string>()),
                $"单位 {handle:x} profileId 应来自模板写真");
            Assert.That(agent.RadiusCm, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(templateNode["radiusCm"]!.GetValue<int>())),
                $"单位 {handle:x} 半径应来自模板写真");
            Assert.That(agent.SpeedCmPerSecond, Is.EqualTo(Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(templateNode["speedCmPerSecond"]!.GetValue<int>())),
                $"单位 {handle:x} 速度应来自模板写真");
            seenProfiles.Add(agent.ProfileId);
        }

        Assert.That(seenProfiles.Count, Is.GreaterThan(1), "64 单位应覆盖多种体型档案(部署按类型×体型散布)");
        TestContext.Out.WriteLine($"{session.Units.Count} 单位全部经模板管线,{seenProfiles.Count} 种档案,模板写真值逐位一致");
    }

}
