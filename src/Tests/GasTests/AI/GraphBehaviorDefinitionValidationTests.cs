using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.AI
{
    [TestFixture]
    public sealed class GraphBehaviorDefinitionValidationTests
    {
        private static readonly IReadOnlyList<(string Json, string Error)> BehaviorTreeFailures =
            new (string, string)[]
            {
                (
                    """
                    [{
                      "id": "bt.unknown-child",
                      "root": "root",
                      "nodes": [{ "id": "root", "kind": "Sequence", "children": ["missing"] }]
                    }]
                    """,
                    "Unknown child 'missing'"),
                (
                    """
                    [{
                      "id": "bt.multiple-parents",
                      "root": "root",
                      "nodes": [
                        { "id": "root", "kind": "Sequence", "children": ["left", "right"] },
                        { "id": "left", "kind": "Sequence", "children": ["leaf"] },
                        { "id": "right", "kind": "Sequence", "children": ["leaf"] },
                        { "id": "leaf", "kind": "Action", "leaf": "ScriptSlice", "action": "bt.action" }
                      ]
                    }]
                    """,
                    "referenced by more than one parent"),
                (
                    """
                    [{
                      "id": "bt.unknown-action",
                      "root": "root",
                      "nodes": [{ "id": "root", "kind": "Action", "leaf": "ScriptSlice", "action": "missing" }]
                    }]
                    """,
                    "Graph action 'missing' is not registered"),
                (
                    """
                    [{
                      "id": "bt.wrong-host",
                      "root": "root",
                      "nodes": [{ "id": "root", "kind": "Action", "leaf": "ScriptSlice", "action": "hfsm.action" }]
                    }]
                    """,
                    "'BehaviorTree' is required"),
            };

        private static readonly IReadOnlyList<(string Json, string Error)> HfsmFailures =
            new (string, string)[]
            {
                (
                    """
                    [{
                      "id": "hfsm.unknown-transition-state",
                      "root": "idle",
                      "states": [{ "id": "idle", "kind": "Leaf" }],
                      "transitions": [{ "from": "idle", "to": "missing", "predicate": "Always" }]
                    }]
                    """,
                    "Unknown from/to state 'idle' -> 'missing'"),
                (
                    """
                    [{
                      "id": "hfsm.unknown-action",
                      "root": "idle",
                      "states": [{ "id": "idle", "kind": "Leaf", "onTick": "missing" }]
                    }]
                    """,
                    "Graph action 'missing' is not registered"),
                (
                    """
                    [{
                      "id": "hfsm.wrong-host",
                      "root": "idle",
                      "states": [{ "id": "idle", "kind": "Leaf", "onTick": "bt.action" }]
                    }]
                    """,
                    "'Hfsm' is required"),
            };

        [Test]
        public void ValidateBehaviorTrees_UsesProductionTopologyAndActionRules()
        {
            GraphActionCatalog actions = CreateActions();

            foreach ((string json, string expectedError) in BehaviorTreeFailures)
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    GraphBehaviorDefinitionLoader.ValidateBehaviorTrees(ParseArray(json), actions))!;
                Assert.That(error.Message, Does.Contain(expectedError));
            }
        }

        [Test]
        public void ValidateHfsms_UsesProductionTopologyAndActionRules()
        {
            GraphActionCatalog actions = CreateActions();

            foreach ((string json, string expectedError) in HfsmFailures)
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    GraphBehaviorDefinitionLoader.ValidateHfsms(ParseArray(json), actions))!;
                Assert.That(error.Message, Does.Contain(expectedError));
            }
        }

        [Test]
        public void ValidateBehaviorDefinitions_AcceptsValidAndEmptyCatalogs()
        {
            GraphActionCatalog actions = CreateActions();
            JsonArray trees = ParseArray(
                """
                [{
                  "id": "bt.valid",
                  "root": "root",
                  "nodes": [{ "id": "root", "kind": "Action", "leaf": "ScriptSlice", "action": "bt.action" }]
                }]
                """);
            JsonArray hfsms = ParseArray(
                """
                [{
                  "id": "hfsm.valid",
                  "root": "idle",
                  "states": [{ "id": "idle", "kind": "Leaf", "onTick": "hfsm.action" }],
                  "transitions": [{ "from": "idle", "to": "idle", "predicate": "Always", "priority": 10 }]
                }]
                """);

            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateBehaviorTrees(trees, actions));
            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateHfsms(hfsms, actions));
            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateBehaviorTrees(new JsonArray(), actions));
            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateHfsms(new JsonArray(), actions));
        }

        private static GraphActionCatalog CreateActions()
        {
            var actions = new GraphActionCatalog();
            actions.Register("bt.action", 1, GraphKind.Script, GraphActionHost.BehaviorTree);
            actions.Register("hfsm.action", 2, GraphKind.Script, GraphActionHost.Hfsm);
            return actions;
        }

        private static JsonArray ParseArray(string json)
            => JsonNode.Parse(json)?.AsArray() ?? throw new InvalidOperationException("Expected JSON array.");
    }
}
