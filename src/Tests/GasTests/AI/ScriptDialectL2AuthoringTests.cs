using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Spatial;
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class ScriptDialectL2AuthoringTests
    {
        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
            AttributeRegistry.Clear();
        }

        [Test]
        public void 数据写的巡逻树加载后代理按树行动()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(
                out _,
                out GraphActionCatalog actions,
                out GraphBehaviorCatalog behavior);

            BehaviorTreeDefinition tree = behavior.RequireTree("bt.patrolChaseAttack");
            Assert.That(tree.NodeCount, Is.EqualTo(9));
            Assert.That(tree.Nodes[2].Leaf, Is.EqualTo(BehaviorTreeLeafBinding.ScriptSlice));
            Assert.That(tree.Nodes[2].GraphId, Is.EqualTo(actions.Require("bt.patrol")));

            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();
            for (int i = 0; i < 3; i++)
            {
                world.TickAll(programs, 32, sensors: null);
            }

            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
            Assert.That(world.LastScriptReturns[0], Is.EqualTo(0));
        }

        [Test]
        public void 叶子直接读目标血量不必先喂整数寄存器()
        {
            GraphIdRegistry.Clear();
            int healthId = AttributeRegistry.Register("Health");
            GraphControlFlowCompileResult compiled = CompileScript(
                """
                {
                  "kind": "Script",
                  "entry": "target",
                  "nodes": [
                    { "id": "target", "op": "LoadExplicitTarget" },
                    { "id": "hp", "op": "LoadAttribute", "attribute": "Health" },
                    { "id": "limit", "op": "ConstFloat", "floatValue": 50 },
                    { "id": "above", "op": "CompareGtFloat" },
                    { "id": "branch", "op": "BranchBool" },
                    { "id": "one", "op": "ConstInt", "intValue": 1 },
                    { "id": "zero", "op": "ConstInt", "intValue": 0 },
                    { "id": "haltHigh", "op": "HaltReturnInt" },
                    { "id": "haltLow", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "target", "fromPort": "next", "to": "hp" },
                    { "from": "hp", "fromPort": "next", "to": "limit" },
                    { "from": "limit", "fromPort": "next", "to": "above" },
                    { "from": "above", "fromPort": "next", "to": "branch" },
                    { "from": "branch", "fromPort": "true", "to": "one" },
                    { "from": "branch", "fromPort": "false", "to": "zero" },
                    { "from": "one", "fromPort": "next", "to": "haltHigh" },
                    { "from": "zero", "fromPort": "next", "to": "haltLow" }
                  ],
                  "valueEdges": [
                    { "from": "target", "fromPort": "value", "to": "hp", "toPort": "source" },
                    { "from": "hp", "fromPort": "value", "to": "above", "toPort": "a" },
                    { "from": "limit", "fromPort": "value", "to": "above", "toPort": "b" },
                    { "from": "above", "fromPort": "value", "to": "branch", "toPort": "condition" },
                    { "from": "one", "fromPort": "value", "to": "haltHigh", "toPort": "value" },
                    { "from": "zero", "fromPort": "value", "to": "haltLow", "toPort": "value" }
                  ]
                }
                """,
                "tests.s13.read-target-health");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            GraphInstruction[] program = compiled.Package!.Value.Program;
            GraphProgramSymbolPatcher.Patch(
                compiled.Package.Value.Symbols,
                program,
                new AttributeOnlyResolver());

            int graphId = GraphIdRegistry.Register("tests.s13.read-target-health");
            var programs = new GraphProgramRegistry();
            programs.Register(graphId, program, GraphKind.Script, GraphInstructionSourceMap.Empty, compiled.Package.Value.Symbols);

            using World ecs = World.Create();
            Entity target = ecs.Create(new AttributeBuffer());
            ecs.Get<AttributeBuffer>(target).SetCurrent(healthId, 80f);
            var api = new AttributeReadApi(ecs);

            var tree = new BehaviorTreeDefinition(
                "tests.s13.health-leaf",
                new[]
                {
                    new BehaviorTreeNode(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.ScriptSlice, graphId)
                },
                rootIndex: 0);
            var bt = new BehaviorTreeWorld(tree, 1);
            bt.AddAgent();
            bt.TickAll(programs, 32, sensors: null, ecs, default, target, api);

            Assert.That(bt.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
            Assert.That(bt.LastScriptReturns[0], Is.EqualTo(1), "80 > 50 should return 1 without writing I[0] from C#.");
        }

        [Test]
        public void ActionLib叶子经帧调用FuncLib纯函数()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(
                out GraphFunctionCatalog functions,
                out _);
            int calleeId = functions.Require("demo.const.seven").GraphId;

            GraphControlFlowCompileResult compiled = CompileScript(
                $$"""
                {
                  "kind": "Script",
                  "entry": "invoke",
                  "nodes": [
                    { "id": "invoke", "op": "InvokeScript", "graphId": {{calleeId}} },
                    { "id": "done", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "invoke", "fromPort": "next", "to": "done" }
                  ],
                  "valueEdges": [
                    { "from": "invoke", "fromPort": "value", "to": "done", "toPort": "value" }
                  ]
                }
                """,
                "tests.s13.action-invokes-func");

            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            int graphId = GraphIdRegistry.Register("tests.s13.action-invokes-func");
            programs.Register(graphId, compiled.Package!.Value.Program, GraphKind.Script);

            var tree = new BehaviorTreeDefinition(
                "tests.s13.func-leaf",
                new[]
                {
                    new BehaviorTreeNode(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.ScriptSlice, graphId)
                },
                rootIndex: 0);
            var bt = new BehaviorTreeWorld(tree, 1);
            bt.AddAgent();
            bt.TickAll(programs, 32, sensors: null);

            Assert.That(bt.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
            Assert.That(bt.LastScriptReturns[0], Is.EqualTo(7));
        }

        [Test]
        public void ActionLib十名是唯一清单()
        {
            _ = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions);
            var names = new HashSet<string>(actions.Names, StringComparer.Ordinal);
            Assert.That(names, Is.EquivalentTo(new[]
            {
                "bt.seeEnemy",
                "bt.inAttackRange",
                "bt.chase",
                "bt.attack",
                "bt.patrol",
                "hfsm.cond.alwaysTrue",
                "hfsm.combat.onEnter",
                "hfsm.combat.onTick",
                "hfsm.combat.onExit",
                "script.drinkUntilFull"
            }));
        }

        private static GraphControlFlowCompileResult CompileScript(string json, string graphId)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        }

        private sealed class AttributeOnlyResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw new InvalidOperationException(name);
            public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
            public int ResolveEffectTemplate(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipType(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipReason(string name) => throw new InvalidOperationException(name);
            public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException(name);
            public int ResolveEntityTemplate(string name) => throw new InvalidOperationException(name);
        }

        private sealed class AttributeReadApi : IGraphRuntimeApi
        {
            public void SpawnTemplate(int templateKeyId, Arch.Core.Entity source, float xCm, float yCm, bool hasPosition)
            {
            }

            private readonly World _world;

            public AttributeReadApi(World world) => _world = world;

            public bool TryGetGridPos(Entity entity, out IntVector2 gridPos)
            {
                gridPos = default;
                return false;
            }

            public bool HasTag(Entity entity, int tagId) => false;

            public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
            {
                if (_world.IsAlive(entity) && _world.Has<AttributeBuffer>(entity))
                {
                    value = _world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
                    return true;
                }

                value = 0f;
                return false;
            }

            public SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Entity> buffer) => default;
            public int GetTeamId(Entity entity) => 0;
            public uint GetEntityLayerCategory(Entity entity) => 0;
            public int GetRelationship(int teamA, int teamB) => GraphRelationship.Neutral;
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
            public void RemoveEffectTemplate(Entity target, int templateId) { }
            public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
            public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value) { }
            public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude) { }
            public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value) { value = 0f; return false; }
            public bool TryReadBlackboardInt(Entity entity, int keyId, out int value) { value = 0; return false; }
            public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value) { value = default; return false; }
            public void WriteBlackboardFloat(Entity entity, int keyId, float value) { }
            public void WriteBlackboardInt(Entity entity, int keyId, int value) { }
            public void WriteBlackboardEntity(Entity entity, int keyId, Entity value) { }
            public bool TryLoadConfigFloat(int keyId, out float value) { value = 0f; return false; }
            public bool TryLoadConfigInt(int keyId, out int value) { value = 0; return false; }
        }
    }
}
