using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

using CrowdSimulationTests.Parity;

/// <summary>
/// 真值会话单位走模板生成管线:模板速度与配置默认值差 1,单位必须取模板值。
/// 映射表对照 unitTypes 声明,不拿单位自己的键去证明自己。
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

        AssertMappingMatchesUnitTypeDeclarations(runtime, wiring);
        var bumpedSpeed = BumpTemplateSpeeds(runtime, wiring);

        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(0, CrowdSimCommand.Parse((JsonObject)JsonNode.Parse(
                """{"type":"spawn","count":64}""")!)),
        });
        int guard = 0;
        while (guard++ < 16 && !session.Step(out _)) { }
        Assert.That(session.Units.Count, Is.GreaterThan(0), "spawn 应已落地");

        var seenProfiles = new HashSet<string>();
        for (int i = 0; i < session.Units.Count; i++)
        {
            var entity = session.Units.EntityAt(i);
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            Assert.That(session.Groups.TryGet(state.GroupId, out var group), Is.True);
            int unitTypeIndex = IndexOfUnitType(runtime, group.LayerIdx);
            int radiusCm = runtime.RadiusClasses[group.RIdx];
            string expectedTemplateId = runtime.UnitTypes[unitTypeIndex].TemplatesByRadiusCm![radiusCm];
            var templateNode = (JsonObject)wiring.TemplateRegistry.Get(expectedTemplateId)!.Components["CrowdSimulationAgent"]!;

            Assert.That(session.World.Has<EntityTemplateKeyRef>(entity), Is.True);
            var keyRef = session.World.Get<EntityTemplateKeyRef>(entity);
            var (mappedId, mappedKey) = wiring.TemplatesByUnitTypeRadius[unitTypeIndex][group.RIdx];
            Assert.That(mappedId, Is.EqualTo(expectedTemplateId));
            Assert.That(keyRef.TemplateKeyId, Is.EqualTo(mappedKey));

            var agent = session.World.Get<CrowdSimulationAgent>(entity);
            Fix64 configSpeed = runtime.AgentTypes[group.LayerIdx].SpeedCmPerSecond;
            Fix64 templateSpeed = Fix64.FromDouble(templateNode["speedCmPerSecond"]!.GetValue<double>());
            Assert.That(bumpedSpeed[expectedTemplateId], Is.EqualTo(templateSpeed));
            Assert.That(templateSpeed, Is.Not.EqualTo(configSpeed),
                $"{expectedTemplateId} 的速度必须与配置默认值不同,否则测不出模板是否生效");
            Assert.That(agent.SpeedCmPerSecond, Is.EqualTo(templateSpeed),
                $"单位应取模板速度 {templateSpeed},而不是配置默认 {configSpeed}");
            Assert.That(agent.ProfileId, Is.EqualTo(templateNode["profileId"]!.GetValue<string>()));
            Assert.That(agent.RadiusCm, Is.EqualTo(Fix64.FromInt(templateNode["radiusCm"]!.GetValue<int>())));
            seenProfiles.Add(agent.ProfileId);
        }

        Assert.That(seenProfiles.Count, Is.GreaterThan(1), "64 单位应覆盖多种体型档案(部署按类型×体型散布)");
        TestContext.Out.WriteLine($"{session.Units.Count} 单位全部经模板管线,{seenProfiles.Count} 种档案,模板速度与配置默认值不同");
    }

    private static void AssertMappingMatchesUnitTypeDeclarations(
        CrowdSimulationRuntimeConfig runtime,
        CrowdSimPresentationWiring wiring)
    {
        for (int t = 0; t < runtime.UnitTypes.Count; t++)
        {
            RuntimeUnitType unitType = runtime.UnitTypes[t];
            Assert.That(unitType.TemplatesByRadiusCm, Is.Not.Null, $"{unitType.Id} 应声明 templates");
            foreach (var (radiusCm, templateId) in unitType.TemplatesByRadiusCm!)
            {
                int rIdx = CrowdDeployment.IndexOfRadiusClass(runtime.RadiusClasses, radiusCm);
                var (mappedId, _) = wiring.TemplatesByUnitTypeRadius[t][rIdx];
                Assert.That(mappedId, Is.EqualTo(templateId),
                    $"unitTypes.{unitType.Id}.templates[{radiusCm}] 应映射到 {templateId}");
                Assert.That(wiring.TemplateRegistry.Get(templateId), Is.Not.Null, templateId);
            }
        }
    }

    private static Dictionary<string, Fix64> BumpTemplateSpeeds(
        CrowdSimulationRuntimeConfig runtime,
        CrowdSimPresentationWiring wiring)
    {
        var bumped = new Dictionary<string, Fix64>(StringComparer.Ordinal);
        for (int t = 0; t < runtime.UnitTypes.Count; t++)
        {
            foreach (var (_, templateId) in runtime.UnitTypes[t].TemplatesByRadiusCm!)
            {
                if (bumped.ContainsKey(templateId)) continue;
                EntityTemplate template = wiring.TemplateRegistry.Get(templateId)!;
                var agent = (JsonObject)template.Components["CrowdSimulationAgent"]!;
                // 写真按 double 读速度;写成 int 节点会在装配时无法转成 double。
                double speed = agent["speedCmPerSecond"]!.GetValue<double>();
                double distinct = speed + 1;
                agent["speedCmPerSecond"] = distinct;
                bumped[templateId] = Fix64.FromDouble(distinct);
            }
        }

        return bumped;
    }

    private static int IndexOfUnitType(CrowdSimulationRuntimeConfig runtime, int agentTypeIndex)
    {
        for (int i = 0; i < runtime.UnitTypes.Count; i++)
        {
            if (runtime.UnitTypes[i].AgentTypeIndex == agentTypeIndex) return i;
        }

        throw new AssertionException($"没有 agentTypeIndex={agentTypeIndex} 的 unitType");
    }
}
