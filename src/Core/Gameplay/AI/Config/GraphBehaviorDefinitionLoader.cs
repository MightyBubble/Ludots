using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class GraphBehaviorDefinitionLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly GraphActionCatalog? _actions;

        public GraphBehaviorDefinitionLoader(ConfigPipeline pipeline, GraphActionCatalog? actions)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _actions = actions;
        }

        public GraphBehaviorCatalog Load(ConfigCatalog catalog, ConfigConflictReport? report = null)
        {
            var result = new GraphBehaviorCatalog();
            LoadTrees(catalog, report, result);
            LoadHfsms(catalog, report, result);
            return result;
        }

        private void LoadTrees(ConfigCatalog catalog, ConfigConflictReport? report, GraphBehaviorCatalog result)
        {
            var entry = GetEntry(catalog, "AI/behavior_trees.json");
            var fragments = _pipeline.CollectFragmentsWithSources(entry.RelativePath);
            if (fragments.Count == 0)
            {
                return;
            }

            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                result.RegisterTree(CompileTree(merged[i].Id, merged[i].Node));
            }
        }

        private void LoadHfsms(ConfigCatalog catalog, ConfigConflictReport? report, GraphBehaviorCatalog result)
        {
            var entry = GetEntry(catalog, "AI/hfsm.json");
            var fragments = _pipeline.CollectFragmentsWithSources(entry.RelativePath);
            if (fragments.Count == 0)
            {
                return;
            }

            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                result.RegisterHfsm(CompileHfsm(merged[i].Id, merged[i].Node));
            }
        }

        private BehaviorTreeDefinition CompileTree(string id, JsonObject obj)
        {
            string rootId = RequireString(obj, "root", $"AI/behavior_trees.json:{id}");
            if (obj["nodes"] is not JsonArray nodesArr || nodesArr.Count == 0)
            {
                throw Fail($"AI/behavior_trees.json:{id}.nodes", "Tree requires a non-empty nodes array.");
            }

            var authored = new List<AuthoredTreeNode>(nodesArr.Count);
            var byId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < nodesArr.Count; i++)
            {
                string path = $"AI/behavior_trees.json:{id}.nodes[{i}]";
                if (nodesArr[i] is not JsonObject nodeObj)
                {
                    throw Fail(path, "Expected an object.");
                }

                string nodeId = RequireString(nodeObj, "id", path);
                if (!byId.TryAdd(nodeId, authored.Count))
                {
                    throw Fail($"{path}.id", $"Duplicate node id '{nodeId}'.");
                }

                authored.Add(new AuthoredTreeNode(
                    nodeId,
                    ParseTreeKind(RequireString(nodeObj, "kind", path), path),
                    ReadStringArray(nodeObj, "children"),
                    ParseLeaf(nodeObj, path),
                    ReadOptionalString(nodeObj, "action")));
            }

            if (!byId.ContainsKey(rootId))
            {
                throw Fail($"AI/behavior_trees.json:{id}.root", $"Unknown root '{rootId}'.");
            }

            var packed = PackTree(id, authored, byId, rootId);
            return new BehaviorTreeDefinition(id, packed, rootIndex: 0);
        }

        private BehaviorTreeNode[] PackTree(
            string treeId,
            List<AuthoredTreeNode> authored,
            Dictionary<string, int> byId,
            string rootId)
        {
            var packed = new List<BehaviorTreeNode>(authored.Count);
            var childStarts = new int[authored.Count];
            var indexOfAuthored = new int[authored.Count];
            Array.Fill(indexOfAuthored, -1);

            var queue = new Queue<int>();
            Place(authored, byId, packed, indexOfAuthored, rootId);
            queue.Enqueue(byId[rootId]);

            while (queue.Count > 0)
            {
                int authoredIndex = queue.Dequeue();
                AuthoredTreeNode node = authored[authoredIndex];
                int childStart = packed.Count;
                childStarts[authoredIndex] = childStart;
                for (int i = 0; i < node.Children.Length; i++)
                {
                    string childId = node.Children[i];
                    if (!byId.ContainsKey(childId))
                    {
                        throw Fail(
                            $"AI/behavior_trees.json:{treeId}.{node.Id}.children",
                            $"Unknown child '{childId}'.");
                    }

                    if (indexOfAuthored[byId[childId]] >= 0)
                    {
                        throw Fail(
                            $"AI/behavior_trees.json:{treeId}.{node.Id}.children",
                            $"Child '{childId}' is referenced by more than one parent.");
                    }

                    Place(authored, byId, packed, indexOfAuthored, childId);
                    queue.Enqueue(byId[childId]);
                }
            }

            if (packed.Count != authored.Count)
            {
                throw Fail($"AI/behavior_trees.json:{treeId}", "Unreachable nodes are not allowed.");
            }

            var nodes = new BehaviorTreeNode[packed.Count];
            for (int a = 0; a < authored.Count; a++)
            {
                AuthoredTreeNode src = authored[a];
                int packedIndex = indexOfAuthored[a];
                int graphId = 0;
                if (src.Leaf == BehaviorTreeLeafBinding.ScriptSlice)
                {
                    graphId = RequireAction(
                        src.Action,
                        GraphActionHost.BehaviorTree,
                        $"AI/behavior_trees.json:{treeId}.{src.Id}.action");
                }
                else if (!string.IsNullOrWhiteSpace(src.Action))
                {
                    throw Fail($"AI/behavior_trees.json:{treeId}.{src.Id}.action", "action is only valid on ScriptSlice leaves.");
                }

                nodes[packedIndex] = new BehaviorTreeNode(
                    src.Kind,
                    childStarts[a],
                    src.Children.Length,
                    src.Leaf,
                    graphId);
            }

            return nodes;
        }

        private static void Place(
            List<AuthoredTreeNode> authored,
            Dictionary<string, int> byId,
            List<BehaviorTreeNode> packed,
            int[] indexOfAuthored,
            string id)
        {
            int authoredIndex = byId[id];
            indexOfAuthored[authoredIndex] = packed.Count;
            packed.Add(default);
        }

        private HfsmDefinition CompileHfsm(string id, JsonObject obj)
        {
            string rootId = RequireString(obj, "root", $"AI/hfsm.json:{id}");
            if (obj["states"] is not JsonArray statesArr || statesArr.Count == 0)
            {
                throw Fail($"AI/hfsm.json:{id}.states", "HFSM requires a non-empty states array.");
            }

            var authored = new List<AuthoredHfsmState>(statesArr.Count);
            var byId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < statesArr.Count; i++)
            {
                string path = $"AI/hfsm.json:{id}.states[{i}]";
                if (statesArr[i] is not JsonObject stateObj)
                {
                    throw Fail(path, "Expected an object.");
                }

                string stateId = RequireString(stateObj, "id", path);
                if (!byId.TryAdd(stateId, authored.Count))
                {
                    throw Fail($"{path}.id", $"Duplicate state id '{stateId}'.");
                }

                authored.Add(new AuthoredHfsmState(
                    stateId,
                    ParseStateKind(RequireString(stateObj, "kind", path), path),
                    ReadStringArray(stateObj, "children"),
                    ReadOptionalString(stateObj, "defaultChild"),
                    ReadOptionalString(stateObj, "onEnter"),
                    ReadOptionalString(stateObj, "onTick"),
                    ReadOptionalString(stateObj, "onExit")));
            }

            if (!byId.ContainsKey(rootId))
            {
                throw Fail($"AI/hfsm.json:{id}.root", $"Unknown root '{rootId}'.");
            }

            HfsmState[] states = PackHfsm(id, authored, byId, rootId, out int[] packedOfAuthored);
            HfsmTransition[] transitions = ReadTransitions(id, obj, byId, packedOfAuthored);
            return new HfsmDefinition(id, states, rootIndex: 0, transitions);
        }

        private HfsmState[] PackHfsm(
            string hfsmId,
            List<AuthoredHfsmState> authored,
            Dictionary<string, int> byId,
            string rootId,
            out int[] packedOfAuthored)
        {
            var packed = new List<int>(authored.Count);
            packedOfAuthored = new int[authored.Count];
            Array.Fill(packedOfAuthored, -1);
            int[] indexOfAuthored = packedOfAuthored;
            var childStarts = new int[authored.Count];
            var parents = new int[authored.Count];
            Array.Fill(parents, -1);

            var queue = new Queue<int>();
            PlaceHfsm(byId, packed, indexOfAuthored, rootId);
            queue.Enqueue(byId[rootId]);

            while (queue.Count > 0)
            {
                int authoredIndex = queue.Dequeue();
                AuthoredHfsmState state = authored[authoredIndex];
                int childStart = packed.Count;
                childStarts[authoredIndex] = childStart;
                for (int i = 0; i < state.Children.Length; i++)
                {
                    string childId = state.Children[i];
                    if (!byId.ContainsKey(childId))
                    {
                        throw Fail($"AI/hfsm.json:{hfsmId}.{state.Id}.children", $"Unknown child '{childId}'.");
                    }

                    int childAuthored = byId[childId];
                    if (indexOfAuthored[childAuthored] >= 0)
                    {
                        throw Fail($"AI/hfsm.json:{hfsmId}.{state.Id}.children", $"Child '{childId}' has more than one parent.");
                    }

                    parents[childAuthored] = authoredIndex;
                    PlaceHfsm(byId, packed, indexOfAuthored, childId);
                    queue.Enqueue(childAuthored);
                }
            }

            if (packed.Count != authored.Count)
            {
                throw Fail($"AI/hfsm.json:{hfsmId}", "Unreachable states are not allowed.");
            }

            var states = new HfsmState[packed.Count];
            for (int a = 0; a < authored.Count; a++)
            {
                AuthoredHfsmState src = authored[a];
                int packedIndex = indexOfAuthored[a];
                int defaultChild = 0;
                if (src.Kind == HfsmStateKind.Compound)
                {
                    if (string.IsNullOrWhiteSpace(src.DefaultChild) || !byId.TryGetValue(src.DefaultChild, out int defaultAuthored))
                    {
                        throw Fail($"AI/hfsm.json:{hfsmId}.{src.Id}.defaultChild", "Compound state requires a known defaultChild.");
                    }

                    defaultChild = indexOfAuthored[defaultAuthored];
                }

                int parentPacked = parents[a] < 0 ? -1 : indexOfAuthored[parents[a]];
                states[packedIndex] = new HfsmState(
                    src.Kind,
                    parentPacked,
                    childStarts[a],
                    src.Children.Length,
                    defaultChild,
                    ResolveOptionalAction(src.OnEnter, $"AI/hfsm.json:{hfsmId}.{src.Id}.onEnter"),
                    ResolveOptionalAction(src.OnTick, $"AI/hfsm.json:{hfsmId}.{src.Id}.onTick"),
                    ResolveOptionalAction(src.OnExit, $"AI/hfsm.json:{hfsmId}.{src.Id}.onExit"));
            }

            return states;
        }

        private static void PlaceHfsm(
            Dictionary<string, int> byId,
            List<int> packed,
            int[] indexOfAuthored,
            string id)
        {
            int authoredIndex = byId[id];
            indexOfAuthored[authoredIndex] = packed.Count;
            packed.Add(authoredIndex);
        }

        private HfsmTransition[] ReadTransitions(
            string id,
            JsonObject obj,
            Dictionary<string, int> byId,
            int[] packedOfAuthored)
        {
            if (obj["transitions"] is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<HfsmTransition>();
            }

            var transitions = new HfsmTransition[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/hfsm.json:{id}.transitions[{i}]";
                if (arr[i] is not JsonObject tr)
                {
                    throw Fail(path, "Expected an object.");
                }

                string fromId = RequireString(tr, "from", path);
                string toId = RequireString(tr, "to", path);
                if (!byId.TryGetValue(fromId, out int fromAuthored) || !byId.TryGetValue(toId, out int toAuthored))
                {
                    throw Fail(path, $"Unknown from/to state '{fromId}' -> '{toId}'.");
                }

                int priority = 0;
                if (tr.TryGetPropertyValue("priority", out JsonNode? pNode) && pNode is JsonValue pVal)
                {
                    priority = pVal.GetValue<int>();
                }

                transitions[i] = new HfsmTransition(
                    packedOfAuthored[fromAuthored],
                    packedOfAuthored[toAuthored],
                    ParsePredicate(RequireString(tr, "predicate", path), path),
                    priority,
                    ResolveOptionalAction(ReadOptionalString(tr, "condition"), $"{path}.condition"));
            }

            return transitions;
        }

        private int RequireAction(string? name, GraphActionHost expectedHost, string path)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw Fail(path, "ScriptSlice / lifecycle binding requires an ActionLib name.");
            }

            if (_actions == null)
            {
                throw Fail(path, "ActionLib catalog is required to resolve behavior bindings.");
            }

            return _actions.Require(name, expectedHost);
        }

        private int ResolveOptionalAction(string? name, string path)
            => string.IsNullOrWhiteSpace(name) ? 0 : RequireAction(name, GraphActionHost.Hfsm, path);

        private static ConfigCatalogEntry GetEntry(ConfigCatalog catalog, string relativePath)
        {
            if (catalog != null && catalog.TryGet(relativePath, out ConfigCatalogEntry entry))
            {
                return entry;
            }

            return new ConfigCatalogEntry(relativePath, ConfigMergePolicy.ArrayById, "id");
        }

        private static BehaviorTreeNodeKind ParseTreeKind(string text, string path)
        {
            if (!Enum.TryParse(text, ignoreCase: false, out BehaviorTreeNodeKind kind) ||
                kind == BehaviorTreeNodeKind.None ||
                !Enum.IsDefined(typeof(BehaviorTreeNodeKind), kind))
            {
                throw Fail($"{path}.kind", $"Unsupported node kind '{text}'.");
            }

            return kind;
        }

        private static BehaviorTreeLeafBinding ParseLeaf(JsonObject obj, string path)
        {
            string? text = ReadOptionalString(obj, "leaf");
            if (string.IsNullOrWhiteSpace(text))
            {
                return BehaviorTreeLeafBinding.None;
            }

            if (!Enum.TryParse(text, ignoreCase: false, out BehaviorTreeLeafBinding leaf) ||
                !Enum.IsDefined(typeof(BehaviorTreeLeafBinding), leaf))
            {
                throw Fail($"{path}.leaf", $"Unsupported leaf '{text}'.");
            }

            return leaf;
        }

        private static HfsmStateKind ParseStateKind(string text, string path)
        {
            if (!Enum.TryParse(text, ignoreCase: false, out HfsmStateKind kind) ||
                !Enum.IsDefined(typeof(HfsmStateKind), kind))
            {
                throw Fail($"{path}.kind", $"Unsupported state kind '{text}'.");
            }

            return kind;
        }

        private static HfsmTransitionPredicate ParsePredicate(string text, string path)
        {
            if (!Enum.TryParse(text, ignoreCase: false, out HfsmTransitionPredicate predicate) ||
                !Enum.IsDefined(typeof(HfsmTransitionPredicate), predicate))
            {
                throw Fail($"{path}.predicate", $"Unsupported predicate '{text}'.");
            }

            return predicate;
        }

        private static string[] ReadStringArray(JsonObject obj, string property)
        {
            if (!obj.TryGetPropertyValue(property, out JsonNode? node) || node is not JsonArray arr)
            {
                return Array.Empty<string>();
            }

            var values = new string[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                values[i] = arr[i]?.GetValue<string>()
                    ?? throw Fail(property, "Array entries must be strings.");
            }

            return values;
        }

        private static string RequireString(JsonObject obj, string property, string path)
        {
            string? text = ReadOptionalString(obj, property);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw Fail($"{path}.{property}", "Non-empty string is required.");
            }

            return text;
        }

        private static string? ReadOptionalString(JsonObject obj, string property)
        {
            if (!obj.TryGetPropertyValue(property, out JsonNode? node) || node is not JsonValue value)
            {
                return null;
            }

            string? text = value.GetValue<string>();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static InvalidOperationException Fail(string path, string message)
            => new($"[GraphBehaviorDefinitionLoader] {path}: {message}");

        private readonly struct AuthoredTreeNode
        {
            public AuthoredTreeNode(
                string id,
                BehaviorTreeNodeKind kind,
                string[] children,
                BehaviorTreeLeafBinding leaf,
                string? action)
            {
                Id = id;
                Kind = kind;
                Children = children;
                Leaf = leaf;
                Action = action;
            }

            public string Id { get; }
            public BehaviorTreeNodeKind Kind { get; }
            public string[] Children { get; }
            public BehaviorTreeLeafBinding Leaf { get; }
            public string? Action { get; }
        }

        private readonly struct AuthoredHfsmState
        {
            public AuthoredHfsmState(
                string id,
                HfsmStateKind kind,
                string[] children,
                string? defaultChild,
                string? onEnter,
                string? onTick,
                string? onExit)
            {
                Id = id;
                Kind = kind;
                Children = children;
                DefaultChild = defaultChild;
                OnEnter = onEnter;
                OnTick = onTick;
                OnExit = onExit;
            }

            public string Id { get; }
            public HfsmStateKind Kind { get; }
            public string[] Children { get; }
            public string? DefaultChild { get; }
            public string? OnEnter { get; }
            public string? OnTick { get; }
            public string? OnExit { get; }
        }
    }
}
