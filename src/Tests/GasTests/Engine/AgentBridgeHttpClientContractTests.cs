using System.Text.Json.Nodes;
using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using NUnit.Framework;

namespace Ludots.Tests.Gas
{
    /// <summary>
    /// HTTP surface smoke: /health /tools /rpc stay aligned with BuiltinAgentTools
    /// so CLI / MCP / Inspector share one live catalog (#1056 P0-1 / P2 skeleton).
    /// </summary>
    public sealed class AgentBridgeHttpClientContractTests
    {
        [Test]
        public async Task HttpCatalog_MatchesBuiltinRegistration_AndRpcRoundTrips()
        {
            using var engine = new Ludots.Core.Engine.GameEngine();
            var tools = new AgentToolRegistry();
            var runtime = new AgentBridgeRuntime(engine, tools);
            BuiltinAgentTools.RegisterAll(
                tools,
                runtime,
                new AgentTimeController(),
                new RecordingController(),
                new AgentLogRingBackend());

            string discovery = Path.Combine(Path.GetTempPath(), "ludots-agent-bridge-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(discovery);

            var config = new AgentBridgeConfig
            {
                Enabled = true,
                RequestedPort = 47800 + Random.Shared.Next(0, 100),
            };

            using var server = new AgentBridgeHttpServer(runtime, config, discovery);
            server.Start();
            string baseUrl = $"http://127.0.0.1:{server.Port}";

            try
            {
                using var client = new AgentBridgeRpcClient(baseUrl);
                JsonObject health = await client.GetHealthAsync();
                Assert.That(health["ok"]!.GetValue<bool>(), Is.True);

                JsonArray catalog = await client.ListToolsAsync();
                Assert.That(catalog.Count, Is.EqualTo(BuiltinAgentTools.ExpectedNames.Count));

                var names = catalog
                    .Select(n => n?["name"]?.GetValue<string>())
                    .Where(n => n != null)
                    .Cast<string>()
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToArray();
                Assert.That(
                    names,
                    Is.EqualTo(BuiltinAgentTools.ExpectedNames.OrderBy(n => n, StringComparer.Ordinal).ToArray()));

                JsonObject listRpc = await client.CallAsync("ludots.tools.list");
                Assert.That(listRpc["error"], Is.Null);
                Assert.That(listRpc["result"]?["tools"] is JsonArray arr && arr.Count == names.Length, Is.True);
            }
            finally
            {
                try { Directory.Delete(discovery, recursive: true); } catch { /* temp cleanup */ }
            }
        }
    }
}
