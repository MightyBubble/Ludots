using System.Text.Json.Nodes;
using Ludots.AgentBridge;
using NUnit.Framework;

namespace Ludots.Tests.Gas
{
    /// <summary>
    /// Bridge execution-model contracts (#1081 P0): loop-health diagnostics must
    /// distinguish a stalled game loop from a live one, and the input event
    /// ledger must record inject events with causal anchors (pump/tick) and a
    /// bounded ring. These run without any HTTP listener.
    /// </summary>
    public sealed class AgentBridgeExecutionModelContractTests
    {
        private static AgentBridgeRuntime CreateRuntime()
        {
            using var engine = new Ludots.Core.Engine.GameEngine();
            return new AgentBridgeRuntime(engine, new AgentToolRegistry());
        }

        [Test]
        public void LoopHealth_BeforeFirstPump_ReportsStalledWithNegativeAge()
        {
            AgentBridgeRuntime runtime = CreateRuntime();
            string message = runtime.DescribeLoopHealth(out JsonObject data);

            Assert.That(message, Does.Contain("not pumped"), message);
            Assert.That(data["loopAgeMs"]!.GetValue<double>(), Is.EqualTo(-1));
            Assert.That(data["pumpCount"]!.GetValue<long>(), Is.EqualTo(0));
            Assert.That(data["pendingRequests"], Is.Not.Null);
            Assert.That(data["lastTick"], Is.Not.Null);
        }

        [Test]
        public void LoopHealth_AfterPump_ReportsPumpingLoop()
        {
            AgentBridgeRuntime runtime = CreateRuntime();
            runtime.Pump();

            string message = runtime.DescribeLoopHealth(out JsonObject data);

            Assert.That(message, Does.Contain("pumping"), message);
            Assert.That(data["loopAgeMs"]!.GetValue<double>(), Is.GreaterThanOrEqualTo(0).And.LessThan(2000));
            Assert.That(data["pumpCount"]!.GetValue<long>(), Is.EqualTo(1));
        }

        [Test]
        public void InputEventLedger_RecordsEvents_And_CapsAtCapacity()
        {
            AgentBridgeRuntime runtime = CreateRuntime();
            runtime.Pump();

            for (int i = 0; i < 40; i++)
            {
                runtime.RecordInputEvent($"inj-{i}", "NavGate_ToggleGate", "press");
            }

            JsonArray ledger = runtime.InputEventLog();
            Assert.That(ledger.Count, Is.EqualTo(32), "ledger is a bounded ring");
            Assert.That(ledger[0]!["eventId"]!.GetValue<string>(), Is.EqualTo("inj-8"), "oldest entries dropped");
            Assert.That(ledger[^1]!["eventId"]!.GetValue<string>(), Is.EqualTo("inj-39"), "newest last");
            Assert.That(ledger[^1]!["pumpCount"]!.GetValue<long>(), Is.EqualTo(1), "events carry the pump anchor");
            Assert.That(ledger[^1]!["tick"], Is.Not.Null, "events carry the tick anchor");
        }

        [Test]
        public void InputEventLedger_ReturnsDeepClonedSnapshot()
        {
            AgentBridgeRuntime runtime = CreateRuntime();
            runtime.RecordInputEvent("inj-1", "SomeAction", "press");

            JsonArray snapshot = runtime.InputEventLog();
            snapshot[0]!["actionId"] = "TamperedAction";

            Assert.That(runtime.InputEventLog()[0]!["actionId"]!.GetValue<string>(), Is.EqualTo("SomeAction"));
        }
    }
}
