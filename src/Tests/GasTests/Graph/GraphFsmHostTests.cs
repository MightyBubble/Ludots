using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// GraphFsmHost sugar-lowering regression: one Script graph per agent, phase SSOT in
    /// MapVariableStore, one halt-only dispatch slice per think wave.
    /// Featured arena SSOT is HfsmWorld + AI/hfsm.json — this host is not author SSOT.
    /// </summary>
    [TestFixture]
    public sealed class GraphFsmHostTests
    {
        private const string FixtureGraphId = "tests.fsm.sentry.sugar";
        private const string PhaseVar = "sentry.phase";

        [Test]
        public void ThinkWave_AdvancesSentryPhaseCycle_ThroughGraphFsmHost()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out _);
            int graphId = RegisterSentrySugarFixture(programs);
            using var host = new GraphFsmHost(programs, graphId, capacity: 2, PhaseVar);
            int agent = host.AddAgent();
            var feed = new StaticDistanceFeed(100);

            Assert.That(host.Count, Is.EqualTo(1));
            Assert.That(host.GraphId, Is.EqualTo(graphId));
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0));

            GraphFsmThinkStats wave1 = host.ThinkWave(128, feed);
            Assert.That(wave1.Agents, Is.EqualTo(1));
            Assert.That(wave1.Halted, Is.EqualTo(1));
            Assert.That(host.PhaseOf(agent), Is.EqualTo(1));

            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(2));

            feed.DistanceCm = 99999;
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(3));
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0));
        }

        [Test]
        public void ResetAgent_ClearsPhaseAndRegisters()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out _);
            int graphId = RegisterSentrySugarFixture(programs);
            using var host = new GraphFsmHost(programs, graphId, capacity: 1, PhaseVar);
            int agent = host.AddAgent();
            host.ThinkWave(128, new StaticDistanceFeed(100));
            Assert.That(host.PhaseOf(agent), Is.EqualTo(1));

            using var world = Arch.Core.World.Create();
            Arch.Core.Entity poison = world.Create();
            host.SetEntityRegister(agent, 0, poison);
            Assert.That(host.EntityRegisterAt(agent, 0), Is.EqualTo(poison));

            host.ResetAgent(agent);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0));
            Assert.That(host.LastReturns[agent], Is.EqualTo(0));
            Assert.That(host.EntityRegisterAt(agent, 0), Is.EqualTo(Arch.Core.Entity.Null));
        }

        [Test]
        public void AddAgent_AtCapacity_FailsClosed()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out _);
            int graphId = RegisterSentrySugarFixture(programs);
            using var host = new GraphFsmHost(programs, graphId, capacity: 1, PhaseVar);
            host.AddAgent();
            Assert.That(() => host.AddAgent(), Throws.InvalidOperationException.With.Message.Contains("capacity"));
        }

        /// <summary>
        /// Compiles the former Graph.FSM.Sentry sugar shape as a test-only fixture.
        /// Must not reappear in production assets/GAS/graphs.json.
        /// </summary>
        internal static int RegisterSentrySugarFixture(GraphProgramRegistry programs)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var configCatalog = new ConfigCatalog();
            configCatalog.Add(new ConfigCatalogEntry(
                "Enums/enums.json",
                ConfigMergePolicy.ArrayById,
                "id",
                arrayAppendFields: new[] { "members" },
                allowEmpty: true));
            EnumCatalog enums = new EnumCatalogLoader(pipeline).Load(configCatalog);

            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(SentrySugarJson)!.AsObject();
            GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
                obj, FixtureGraphId, options, eventSchemas: null, enums);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Package.HasValue, Is.True);

            GraphProgramPackage pkg = compiled.Package!.Value;
            GraphProgramSymbolPatcher.Patch(pkg.Symbols, pkg.Program, new BootstrapMapVarResolver());

            int graphId = GraphIdRegistry.Register(FixtureGraphId);
            programs.Register(graphId, pkg.Program, GraphKind.Script, GraphInstructionSourceMap.Empty, pkg.Symbols);
            return graphId;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        private sealed class BootstrapMapVarResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw new InvalidOperationException(name);
            public int ResolveAttribute(string name) => throw new InvalidOperationException(name);
            public int ResolveEffectTemplate(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipType(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipReason(string name) => throw new InvalidOperationException(name);
            public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException(name);
            public int ResolveEntityTemplate(string name) => throw new InvalidOperationException(name);
        }

        private sealed class StaticDistanceFeed : IBehaviorTreeSensorFeed
        {
            public StaticDistanceFeed(int distanceCm) => DistanceCm = distanceCm;
            public int DistanceCm { get; set; }

            public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
            {
                ints[0] = DistanceCm;
            }
        }

        private const string SentrySugarJson = """
            {
              "id": "tests.fsm.sentry.sugar",
              "kind": "Script",
              "entry": "distIn",
              "nodes": [
                { "id": "distIn", "op": "MoveInt", "pinRegister": 1 },
                { "id": "fsm", "op": "FsmState", "enumType": "Ludots.SentryPhase", "stateVar": "sentry.phase" },
                { "id": "iAlertCm", "op": "ConstInt", "intValue": 500 },
                { "id": "iNear", "op": "CompareLtInt" },
                { "id": "iBranch", "op": "BranchBool" },
                { "id": "iAlertCode", "op": "ConstInt", "intValue": 1 },
                { "id": "iWrite", "op": "WriteMapVarInt", "var": "sentry.phase" },
                { "id": "iDone", "op": "HaltReturnInt" },
                { "id": "iHold", "op": "ConstInt", "intValue": 10 },
                { "id": "iHoldDone", "op": "HaltReturnInt" },
                { "id": "aCombatCode", "op": "ConstInt", "intValue": 2 },
                { "id": "aWrite", "op": "WriteMapVarInt", "var": "sentry.phase" },
                { "id": "aDone", "op": "HaltReturnInt" },
                { "id": "cCombatCm", "op": "ConstInt", "intValue": 200 },
                { "id": "cNear", "op": "CompareLtInt" },
                { "id": "cBranch", "op": "BranchBool" },
                { "id": "cHold", "op": "ConstInt", "intValue": 20 },
                { "id": "cHoldDone", "op": "HaltReturnInt" },
                { "id": "cRetreatCode", "op": "ConstInt", "intValue": 3 },
                { "id": "cWrite", "op": "WriteMapVarInt", "var": "sentry.phase" },
                { "id": "cDone", "op": "HaltReturnInt" },
                { "id": "rIdleCode", "op": "ConstInt", "intValue": 0 },
                { "id": "rWrite", "op": "WriteMapVarInt", "var": "sentry.phase" },
                { "id": "rDone", "op": "HaltReturnInt" },
                { "id": "dIdleCode", "op": "ConstInt", "intValue": 0 },
                { "id": "dWrite", "op": "WriteMapVarInt", "var": "sentry.phase" },
                { "id": "dDone", "op": "HaltReturnInt" }
              ],
              "controlEdges": [
                { "from": "distIn", "fromPort": "next", "to": "fsm" },
                { "from": "fsm", "fromPort": "case:Idle", "to": "iAlertCm" },
                { "from": "iAlertCm", "fromPort": "next", "to": "iNear" },
                { "from": "iNear", "fromPort": "next", "to": "iBranch" },
                { "from": "iBranch", "fromPort": "true", "to": "iAlertCode" },
                { "from": "iAlertCode", "fromPort": "next", "to": "iWrite" },
                { "from": "iWrite", "fromPort": "next", "to": "iDone" },
                { "from": "iBranch", "fromPort": "false", "to": "iHold" },
                { "from": "iHold", "fromPort": "next", "to": "iHoldDone" },
                { "from": "fsm", "fromPort": "case:Alert", "to": "aCombatCode" },
                { "from": "aCombatCode", "fromPort": "next", "to": "aWrite" },
                { "from": "aWrite", "fromPort": "next", "to": "aDone" },
                { "from": "fsm", "fromPort": "case:Combat", "to": "cCombatCm" },
                { "from": "cCombatCm", "fromPort": "next", "to": "cNear" },
                { "from": "cNear", "fromPort": "next", "to": "cBranch" },
                { "from": "cBranch", "fromPort": "true", "to": "cHold" },
                { "from": "cHold", "fromPort": "next", "to": "cHoldDone" },
                { "from": "cBranch", "fromPort": "false", "to": "cRetreatCode" },
                { "from": "cRetreatCode", "fromPort": "next", "to": "cWrite" },
                { "from": "cWrite", "fromPort": "next", "to": "cDone" },
                { "from": "fsm", "fromPort": "case:Retreat", "to": "rIdleCode" },
                { "from": "rIdleCode", "fromPort": "next", "to": "rWrite" },
                { "from": "rWrite", "fromPort": "next", "to": "rDone" },
                { "from": "fsm", "fromPort": "default", "to": "dIdleCode" },
                { "from": "dIdleCode", "fromPort": "next", "to": "dWrite" },
                { "from": "dWrite", "fromPort": "next", "to": "dDone" }
              ],
              "valueEdges": [
                { "from": "distIn", "fromPort": "value", "to": "iNear", "toPort": "a" },
                { "from": "iAlertCm", "fromPort": "value", "to": "iNear", "toPort": "b" },
                { "from": "iNear", "fromPort": "value", "to": "iBranch", "toPort": "condition" },
                { "from": "iAlertCode", "fromPort": "value", "to": "iWrite", "toPort": "value" },
                { "from": "iAlertCode", "fromPort": "value", "to": "iDone", "toPort": "value" },
                { "from": "iHold", "fromPort": "value", "to": "iHoldDone", "toPort": "value" },
                { "from": "aCombatCode", "fromPort": "value", "to": "aWrite", "toPort": "value" },
                { "from": "aCombatCode", "fromPort": "value", "to": "aDone", "toPort": "value" },
                { "from": "distIn", "fromPort": "value", "to": "cNear", "toPort": "a" },
                { "from": "cCombatCm", "fromPort": "value", "to": "cNear", "toPort": "b" },
                { "from": "cNear", "fromPort": "value", "to": "cBranch", "toPort": "condition" },
                { "from": "cHold", "fromPort": "value", "to": "cHoldDone", "toPort": "value" },
                { "from": "cRetreatCode", "fromPort": "value", "to": "cWrite", "toPort": "value" },
                { "from": "cRetreatCode", "fromPort": "value", "to": "cDone", "toPort": "value" },
                { "from": "rIdleCode", "fromPort": "value", "to": "rWrite", "toPort": "value" },
                { "from": "rIdleCode", "fromPort": "value", "to": "rDone", "toPort": "value" },
                { "from": "dIdleCode", "fromPort": "value", "to": "dWrite", "toPort": "value" },
                { "from": "dIdleCode", "fromPort": "value", "to": "dDone", "toPort": "value" }
              ]
            }
            """;
    }
}
