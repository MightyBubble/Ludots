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
                      "id": "bt.condition-not-in-funclib",
                      "root": "root",
                      "nodes": [{ "id": "root", "kind": "Condition", "leaf": "ScriptSlice", "action": "bt.action" }]
                    }]
                    """,
                    "Graph function 'bt.action' is not registered in FuncLib"),
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
                      "id": "hfsm.condition-not-in-funclib",
                      "root": "root",
                      "states": [
                        { "id": "root", "kind": "Compound", "children": ["idle", "alert"], "defaultChild": "idle" },
                        { "id": "idle", "kind": "Leaf" },
                        { "id": "alert", "kind": "Leaf" }
                      ],
                      "transitions": [{ "from": "idle", "to": "alert", "predicate": "Always", "condition": "hfsm.action" }]
                    }]
                    """,
                    "Graph function 'hfsm.action' is not registered in FuncLib"),
            };

        [Test]
        public void ValidateBehaviorTrees_UsesProductionTopologyAndActionRules()
        {
            GraphActionCatalog actions = CreateActions();
            GraphFunctionCatalog functions = CreateFunctions();

            foreach ((string json, string expectedError) in BehaviorTreeFailures)
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    GraphBehaviorDefinitionLoader.ValidateBehaviorTrees(ParseArray(json), actions, functions))!;
                Assert.That(error.Message, Does.Contain(expectedError));
            }
        }

        [Test]
        public void ValidateHfsms_UsesProductionTopologyAndActionRules()
        {
            GraphActionCatalog actions = CreateActions();
            GraphFunctionCatalog functions = CreateFunctions();

            foreach ((string json, string expectedError) in HfsmFailures)
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    GraphBehaviorDefinitionLoader.ValidateHfsms(ParseArray(json), actions, functions))!;
                Assert.That(error.Message, Does.Contain(expectedError));
            }
        }

        [Test]
        public void ValidateBehaviorDefinitions_AcceptsValidAndEmptyCatalogs()
        {
            GraphActionCatalog actions = CreateActions();
            GraphFunctionCatalog functions = CreateFunctions();
            JsonArray trees = ParseArray(
                """
                [{
                  "id": "bt.valid",
                  "root": "root",
                  "nodes": [
                    { "id": "root", "kind": "Sequence", "children": ["see", "act"] },
                    { "id": "see", "kind": "Condition", "leaf": "ScriptSlice", "action": "bt.cond" },
                    { "id": "act", "kind": "Action", "leaf": "ScriptSlice", "action": "bt.action" }
                  ]
                }]
                """);
            JsonArray hfsms = ParseArray(
                """
                [{
                  "id": "hfsm.valid",
                  "root": "root",
                  "states": [
                    { "id": "root", "kind": "Compound", "children": ["idle", "alert"], "defaultChild": "idle" },
                    { "id": "idle", "kind": "Leaf", "onTick": "hfsm.action" },
                    { "id": "alert", "kind": "Leaf" }
                  ],
                  "transitions": [
                    { "from": "idle", "to": "alert", "predicate": "Always", "condition": "hfsm.cond", "priority": 10 },
                    { "from": "alert", "to": "idle", "predicate": "Always" }
                  ]
                }]
                """);

            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateBehaviorTrees(trees, actions, functions));
            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateHfsms(hfsms, actions, functions));
            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateBehaviorTrees(new JsonArray(), actions, functions));
            Assert.DoesNotThrow(() => GraphBehaviorDefinitionLoader.ValidateHfsms(new JsonArray(), actions, functions));
        }

        private static GraphActionCatalog CreateActions()
        {
            var actions = new GraphActionCatalog();
            actions.Register("bt.action", 1, GraphKind.Script);
            actions.Register("hfsm.action", 2, GraphKind.Script);
            return actions;
        }

        private static GraphFunctionCatalog CreateFunctions()
        {
            var functions = new GraphFunctionCatalog();
            functions.Register("bt.cond", 3, GraphKind.Script);
            functions.Register("hfsm.cond", 4, GraphKind.Script);
            return functions;
        }

        private static JsonArray ParseArray(string json)
            => JsonNode.Parse(json)?.AsArray() ?? throw new InvalidOperationException("Expected JSON array.");
    }
}
