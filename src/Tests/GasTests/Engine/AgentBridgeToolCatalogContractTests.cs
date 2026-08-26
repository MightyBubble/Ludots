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
            Type toolInterface = typeof(IAgentTool);
            Type[] concreteTools = typeof(IAgentTool).Assembly
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && toolInterface.IsAssignableFrom(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToArray();

            Assert.That(concreteTools.Length, Is.EqualTo(BuiltinAgentTools.ExpectedNames.Count),
                "Update BuiltinAgentTools when adding/removing IAgentTool implementations.");

            using var engine = new Ludots.Core.Engine.GameEngine();
            var tools = new AgentToolRegistry();
            var runtime = new AgentBridgeRuntime(engine, tools);
            var time = new AgentTimeController();
            var recording = new RecordingController();
            var logRing = new AgentLogRingBackend();

            BuiltinAgentTools.RegisterAll(tools, runtime, time, recording, logRing);

            Assert.That(tools.Tools.Count, Is.EqualTo(BuiltinAgentTools.ExpectedNames.Count));

            var registeredNames = tools.Tools.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var expected = BuiltinAgentTools.ExpectedNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(registeredNames, Is.EqualTo(expected));

            var reflectedNames = new List<string>(concreteTools.Length);
            foreach (Type type in concreteTools)
            {
                IAgentTool instance = CreateProbeInstance(type, runtime, time, recording, logRing);
                reflectedNames.Add(instance.Name);
                Assert.That(tools.TryGet(instance.Name, out _), Is.True,
                    $"Concrete tool {type.Name} ({instance.Name}) is not registered by BuiltinAgentTools.");
            }

            Assert.That(
                reflectedNames.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(expected));
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
            BuiltinAgentTools.RegisterAll(tools, runtime, new AgentTimeController(), new RecordingController(), new AgentLogRingBackend());

            JsonArray catalog = tools.DescribeAll();
            Assert.That(catalog.Count, Is.EqualTo(BuiltinAgentTools.ExpectedNames.Count));
            foreach (JsonNode? node in catalog)
            {
                Assert.That(node?["name"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
                Assert.That(node?["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
            }
        }

        private static IAgentTool CreateProbeInstance(
            Type type,
            AgentBridgeRuntime runtime,
            AgentTimeController time,
            RecordingController recording,
            AgentLogRingBackend logRing)
        {
            if (type == typeof(SessionInfoTool)
                || type == typeof(InputStateTool)
                || type == typeof(InputInjectTool)
                || type == typeof(ScreenshotTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, runtime)!;
            }

            if (type == typeof(TimeGetTool) || type == typeof(TimeControlTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, time)!;
            }

            if (type == typeof(RecordingStartTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, recording, runtime)!;
            }

            if (type == typeof(RecordingStopTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, recording)!;
            }

            if (type == typeof(LogsTailTool))
            {
                return (IAgentTool)Activator.CreateInstance(type, logRing)!;
            }

            return (IAgentTool)Activator.CreateInstance(type)!;
        }
    }
}
