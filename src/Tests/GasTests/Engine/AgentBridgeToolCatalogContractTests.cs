using System.Text.Json.Nodes;
using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using NUnit.Framework;

namespace Ludots.Tests.Gas
{
    /// <summary>
    /// #1056 P0-1: every concrete IAgentTool in the AgentBridge assembly must be
    /// registered through BuiltinAgentTools (HTTP / MCP / CLI / Inspector share one catalog).
    /// </summary>
    public sealed class AgentBridgeToolCatalogContractTests
    {
        [Test]
        public void BuiltinCatalog_RegistersEveryConcreteIAgentTool_ExactlyOnce()
        {
            using var engine = new Ludots.Core.Engine.GameEngine();
            var tools = new AgentToolRegistry();
            var runtime = new AgentBridgeRuntime(engine, tools);
            var time = new AgentTimeController();
            var recording = new RecordingController();
            var logRing = new AgentLogRingBackend();

            BuiltinAgentTools.RegisterAll(tools, runtime, time, recording, logRing);

            (Type Type, string Name)[] concreteTools =
                AgentBridgeToolCatalogProbe.GetConcreteTools(runtime, time, recording, logRing);
            foreach ((Type type, string name) in concreteTools)
            {
                Assert.That(tools.TryGet(name, out _), Is.True,
                    $"Concrete tool {type.Name} ({name}) is not registered by BuiltinAgentTools.");
            }

            string[] expected = concreteTools
                .Select(tool => tool.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            Assert.That(expected, Is.Unique,
                "two concrete IAgentTool implementations share one wire name; the registry would shadow one of them");

            string[] registeredNames = tools.Tools.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(registeredNames, Is.EqualTo(expected),
                "BuiltinAgentTools must expose exactly the concrete IAgentTool set: no missing tool, no extra registration.");
        }

        [Test]
        public void EndpointResolver_RejectsInvalidUrl_FailLoud()
        {
            Assert.Throws<InvalidOperationException>(() => AgentBridgeEndpoint.Resolve("not-a-url"));
        }

        [Test]
        public void EndpointResolver_PrefersExplicitUrl()
        {
            string url = AgentBridgeEndpoint.Resolve("http://127.0.0.1:48000/", discoveryPath: "/tmp/does-not-exist");
            Assert.That(url, Is.EqualTo("http://127.0.0.1:48000"));
        }

        [Test]
        public void DescribeAll_EmitsSchemaForEveryRegisteredTool()
        {
            using var engine = new Ludots.Core.Engine.GameEngine();
            var tools = new AgentToolRegistry();
            var runtime = new AgentBridgeRuntime(engine, tools);
            var time = new AgentTimeController();
            var recording = new RecordingController();
            var logRing = new AgentLogRingBackend();
            BuiltinAgentTools.RegisterAll(tools, runtime, time, recording, logRing);
            string[] expected = AgentBridgeToolCatalogProbe.GetConcreteToolNames(runtime, time, recording, logRing);

            JsonArray catalog = tools.DescribeAll();
            Assert.That(catalog.Count, Is.EqualTo(expected.Length));

            var describedNames = new List<string>(catalog.Count);
            foreach (JsonNode? node in catalog)
            {
                Assert.That(node?["name"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
                Assert.That(node?["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
                describedNames.Add(node!["name"]!.GetValue<string>());
            }

            describedNames.Sort(StringComparer.Ordinal);
            Assert.That(describedNames, Is.EqualTo(expected),
                "the self-describing catalog must advertise exactly the concrete IAgentTool set");
        }
    }
}
