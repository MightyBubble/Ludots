using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Scoring;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiConfigLoader
    {
        private static readonly string[] ActionOrderPropertyNames =
        {
            "OrderPayloadKind",
            "OrderTypeKey",
            "OrderTypeId",
            "SubmitMode",
            "PlayerId"
        };

        private static readonly string[] TaskPropertyNames =
        {
            "id",
            "Kind",
            "OrderPayloadKind",
            "OrderTypeKey",
            "OrderTypeId",
            "AbilitySlotIndex",
            "SubmitMode",
            "PlayerId"
        };

        private static readonly string[] ActuatorPropertyNames =
        {
            "id"
        };

        private static readonly string[] DecisionPropertyNames =
        {
            "id",
            "TargetFilter",
            "Priority",
            "BaseScore",
            "Weight",
            "MomentumBonus",
            "MinDurationSteps",
            "Autocast",
            "OrdinaryAttack",
            "RequiresTarget",
            "KeepRunningUntilFinished",
            "ExplicitOrderOnly",
            "Flags",
            "Considerations",
            "Tasks"
        };

        private static readonly string[] ConsiderationPropertyNames =
        {
            "Input",
            "Normalization",
            "Curve",
            "Weight",
            "Aggregate"
        };

        private readonly ConfigPipeline _pipeline;
        private readonly AtomRegistry _atoms;
        private readonly AiConfigValidationContext? _validation;

        public AiConfigLoader(ConfigPipeline pipeline, AtomRegistry atoms, AiConfigValidationContext? validation = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _atoms = atoms ?? throw new ArgumentNullException(nameof(atoms));
            _validation = validation;
        }

        public AiCompiledRuntime LoadAndCompile(ConfigCatalog catalog, ConfigConflictReport? report = null)
        {
            var atomEntry = GetEntry(catalog, "AI/atoms.json", ConfigMergePolicy.ArrayById, idField: "id");
            var atomNode = Merge(in atomEntry, report);
            if (atomNode is JsonArray atomsArr)
            {
                for (int i = 0; i < atomsArr.Count; i++)
                {
                    string path = $"AI/atoms.json[{i}]";
                    if (atomsArr[i] is not JsonObject obj)
                    {
                        throw Fail(path, "Expected an object.");
                    }

                    string id = RequireString(obj, "id", path);
                    _atoms.GetOrAdd(id);
                }
            }

            var projectionRules = Array.Empty<WorldStateProjectionRule>();
            var projectionEntry = GetEntry(catalog, "AI/projection.json", ConfigMergePolicy.ArrayById, idField: "id");
            var projectionNode = Merge(in projectionEntry, report);
            if (projectionNode is JsonArray projArr)
            {
                var tmp = new List<WorldStateProjectionRule>(projArr.Count);
                for (int i = 0; i < projArr.Count; i++)
                {
                    string path = $"AI/projection.json[{i}]";
                    if (projArr[i] is not JsonObject obj)
                    {
                        throw Fail(path, "Expected an object.");
                    }

                    string id = ReadRecordId(obj, path);
                    int atomId = RequireAtomId(RequireString(obj, "Atom", path), $"AI/projection.json:{id}.Atom");
                    string op = RequireString(obj, "Op", path);
                    if (!TryParseProjectionOp(op, out var pOp))
                    {
                        throw Fail($"AI/projection.json:{id}.Op", $"Unsupported projection op '{op}'.");
                    }

                    int intKey = -1;
                    int intValue = 0;
                    int entityKey = -1;
                    switch (pOp)
                    {
                        case WorldStateProjectionOp.IntEquals:
                        case WorldStateProjectionOp.IntGreaterOrEqual:
                        case WorldStateProjectionOp.IntLessOrEqual:
                            intKey = RequireOrderBlackboardKey(obj, "IntKey", $"AI/projection.json:{id}.IntKey");
                            intValue = RequireInt(obj, "IntValue", $"AI/projection.json:{id}");
                            RejectProjectionField(obj, "EntityKey", $"AI/projection.json:{id}.EntityKey", op);
                            break;
                        case WorldStateProjectionOp.EntityIsNonNull:
                        case WorldStateProjectionOp.EntityIsNull:
                            entityKey = RequireOrderBlackboardKey(obj, "EntityKey", $"AI/projection.json:{id}.EntityKey");
                            RejectProjectionField(obj, "IntKey", $"AI/projection.json:{id}.IntKey", op);
                            RejectProjectionField(obj, "IntValue", $"AI/projection.json:{id}.IntValue", op);
                            break;
                        default:
                            throw Fail($"AI/projection.json:{id}.Op", $"Unsupported projection op '{op}'.");
                    }

                    tmp.Add(new WorldStateProjectionRule(atomId, pOp, intKey, intValue, entityKey));
                }
                projectionRules = tmp.ToArray();
            }

            var projectionTable = new WorldStateProjectionTable(projectionRules, atomCapacity: _atoms.Capacity);

            var goalDefs = Array.Empty<UtilityGoalPresetDefinition>();
            var utilityEntry = GetEntry(catalog, "AI/utility.json", ConfigMergePolicy.ArrayById, idField: "id");
            var utilityNode = Merge(in utilityEntry, report);
            if (utilityNode is JsonArray goalsArr)
            {
                var tmp = new List<UtilityGoalPresetDefinition>(goalsArr.Count);
                for (int i = 0; i < goalsArr.Count; i++)
                {
                    string path = $"AI/utility.json[{i}]";
                    if (goalsArr[i] is not JsonObject obj)
                    {
                        throw Fail(path, "Expected an object.");
                    }

                    string id = ReadRecordId(obj, path);
                    int goalPresetId = RequirePositiveInt(obj, "GoalPresetId", path);
                    int planningStrategyId = RequirePlanningStrategyId(obj, "PlanningStrategyId", path);
                    float weight = TryReadFloat(obj, "Weight", out float w) ? w : 1f;

                    var boolCons = Array.Empty<UtilityConsiderationBool256>();
                    if (obj.TryGetPropertyValue("Bool", out var boolNode) && boolNode is JsonArray boolArr)
                    {
                        var bc = new List<UtilityConsiderationBool256>(boolArr.Count);
                        for (int c = 0; c < boolArr.Count; c++)
                        {
                            string considerationPath = $"AI/utility.json:{id}.Bool[{c}]";
                            if (boolArr[c] is not JsonObject bObj)
                            {
                                throw Fail(considerationPath, "Expected an object.");
                            }

                            int atomId = RequireAtomId(RequireString(bObj, "Atom", considerationPath), $"{considerationPath}.Atom");
                            float ts = TryReadFloat(bObj, "TrueScore", out float tsv) ? tsv : 1f;
                            float fs = TryReadFloat(bObj, "FalseScore", out float fsv) ? fsv : 1f;
                            bc.Add(new UtilityConsiderationBool256(atomId, ts, fs));
                        }
                        boolCons = bc.ToArray();
                    }

                    tmp.Add(new UtilityGoalPresetDefinition(goalPresetId, planningStrategyId, weight, boolCons));
                }
                goalDefs = tmp.ToArray();
            }

            var goalSelector = UtilityGoalSelectorCompiled256.Compile(goalDefs);

            var goapActions = Array.Empty<ActionOpDefinition256>();
            var goapEntry = GetEntry(catalog, "AI/goap_actions.json", ConfigMergePolicy.ArrayById, idField: "id");
            var goapNode = Merge(in goapEntry, report);
            if (goapNode is JsonArray actArr)
            {
                var tmp = new List<ActionOpDefinition256>(actArr.Count);
                for (int i = 0; i < actArr.Count; i++)
                {
                    string path = $"AI/goap_actions.json[{i}]";
                    if (actArr[i] is not JsonObject obj)
                    {
                        throw Fail(path, "Expected an object.");
                    }

                    string id = ReadRecordId(obj, path);
                    int cost = TryReadInt(obj, "Cost", out int c) ? c : 1;

                    var pre = ReadCondition(obj, "Pre", _atoms, $"AI/goap_actions.json:{id}.Pre");
                    var post = ReadCondition(obj, "Post", _atoms, $"AI/goap_actions.json:{id}.Post");

                    var orderSpec = default(ActionOrderSpec);
                    var execKind = ActionExecutorKind.SubmitOrder;
                    if (obj.TryGetPropertyValue("Order", out var orderNode) && orderNode is JsonObject orderObj)
                    {
                        orderSpec = ReadOrderSpec(orderObj, $"AI/goap_actions.json:{id}.Order");
                    }
                    else
                    {
                        throw Fail($"AI/goap_actions.json:{id}.Order", "SubmitOrder action must declare an Order object.");
                    }

                    var bindings = Array.Empty<ActionBinding>();
                    if (obj.TryGetPropertyValue("Bindings", out var bindNode) && bindNode is JsonArray bindArr)
                    {
                        var btmp = new List<ActionBinding>(bindArr.Count);
                        for (int b = 0; b < bindArr.Count; b++)
                        {
                            string bindingPath = $"AI/goap_actions.json:{id}.Bindings[{b}]";
                            if (bindArr[b] is not JsonObject bObj)
                            {
                                throw Fail(bindingPath, "Expected an object.");
                            }

                            string op = RequireString(bObj, "Op", bindingPath);
                            if (IsBareOrderArgBinding(op))
                            {
                                throw Fail($"{bindingPath}.Op", $"Bare order arg binding '{op}' is outside the AI order contract. Use IntToAbilitySlot.");
                            }

                            if (!TryParseBindingOp(op, out var bop))
                            {
                                throw Fail($"{bindingPath}.Op", $"Unsupported binding op '{op}'.");
                            }

                            int sk = RequireOrderBlackboardKey(bObj, "SourceKey", $"{bindingPath}.SourceKey");
                            btmp.Add(new ActionBinding(bop, sk));
                        }
                        bindings = btmp.ToArray();
                    }

                    ValidateActionOrderBindings(in orderSpec, bindings, $"AI/goap_actions.json:{id}");

                    tmp.Add(new ActionOpDefinition256(
                        preMask: in pre.Mask,
                        preValues: in pre.Values,
                        postMask: in post.Mask,
                        postValues: in post.Values,
                        cost: cost,
                        executorKind: execKind,
                        orderSpec: in orderSpec,
                        bindings: bindings));
                }
                goapActions = tmp.ToArray();
            }

            var actionLibrary = ActionLibraryCompiled256.Compile(goapActions);

            var goapGoals = Array.Empty<GoapGoalPreset256>();
            var goapGoalEntry = GetEntry(catalog, "AI/goap_goals.json", ConfigMergePolicy.ArrayById, idField: "id");
            var goapGoalNode = Merge(in goapGoalEntry, report);
            if (goapGoalNode is JsonArray gArr)
            {
                var tmp = new List<GoapGoalPreset256>(gArr.Count);
                for (int i = 0; i < gArr.Count; i++)
                {
                    string path = $"AI/goap_goals.json[{i}]";
                    if (gArr[i] is not JsonObject obj)
                    {
                        throw Fail(path, "Expected an object.");
                    }

                    string id = ReadRecordId(obj, path);
                    int goalPresetId = RequirePositiveInt(obj, "GoalPresetId", path);
                    int hw = TryReadInt(obj, "HeuristicWeight", out int hwi) ? hwi : 1;
                    var cond = ReadCondition(obj, "Goal", _atoms, $"AI/goap_goals.json:{id}.Goal");
                    var goalCond = new WorldStateCondition256(in cond.Mask, in cond.Values);
                    tmp.Add(new GoapGoalPreset256(goalPresetId, in goalCond, hw));
                }
                goapGoals = tmp.ToArray();
            }

            var goapGoalTable = new GoapGoalTable256(goapGoals);

            var htnDomain = new HtnDomainCompiled256(Array.Empty<HtnCompoundTask>(), Array.Empty<HtnMethod256>(), Array.Empty<HtnSubtask>());
            var htnRoots = new HtnRootTable(Array.Empty<(int GoalPresetId, int RootTaskId)>());

            var htnEntry = GetEntry(catalog, "AI/htn_domain.json", ConfigMergePolicy.DeepObject, idField: "id");
            var htnNode = Merge(in htnEntry, report);
            if (htnNode is JsonObject htnObj)
            {
                if (htnObj.TryGetPropertyValue("Tasks", out var tNode) && tNode is JsonArray tArr
                    && htnObj.TryGetPropertyValue("Methods", out var mNode) && mNode is JsonArray mArr
                    && htnObj.TryGetPropertyValue("Subtasks", out var sNode) && sNode is JsonArray sArr)
                {
                    var tasks = new HtnCompoundTask[tArr.Count];
                    for (int i = 0; i < tArr.Count; i++)
                    {
                        string path = $"AI/htn_domain.json.Tasks[{i}]";
                        if (tArr[i] is not JsonObject o)
                        {
                            throw Fail(path, "Expected an object.");
                        }

                        int tid = RequireInt(o, "TaskId", path);
                        int fm = TryReadInt(o, "FirstMethod", out int x) ? x : 0;
                        int mc = TryReadInt(o, "MethodCount", out int y) ? y : 0;
                        if ((uint)tid >= (uint)tasks.Length)
                        {
                            throw Fail($"{path}.TaskId", $"Task id {tid} is outside task table length {tasks.Length}.");
                        }

                        tasks[tid] = new HtnCompoundTask(fm, mc);
                    }

                    var methods = new HtnMethod256[mArr.Count];
                    for (int i = 0; i < mArr.Count; i++)
                    {
                        string path = $"AI/htn_domain.json.Methods[{i}]";
                        if (mArr[i] is not JsonObject o)
                        {
                            throw Fail(path, "Expected an object.");
                        }

                        int mid = RequireInt(o, "MethodId", path);
                        int cost = TryReadInt(o, "Cost", out int cc) ? cc : 0;
                        int off = TryReadInt(o, "SubtaskOffset", out int so) ? so : 0;
                        int cnt = TryReadInt(o, "SubtaskCount", out int sc) ? sc : 0;
                        var cond = ReadCondition(o, "Condition", _atoms, $"{path}.Condition");
                        var cnd = new WorldStateCondition256(in cond.Mask, in cond.Values);
                        if ((uint)mid >= (uint)methods.Length)
                        {
                            throw Fail($"{path}.MethodId", $"Method id {mid} is outside method table length {methods.Length}.");
                        }

                        if (off < 0 || cnt < 0 || off + cnt > sArr.Count)
                        {
                            throw Fail(path, $"Method {mid} subtask range [{off}, {off + cnt}) exceeds subtask table length {sArr.Count}.");
                        }

                        methods[mid] = new HtnMethod256(in cnd, off, cnt, cost);
                    }

                    var subtasks = new HtnSubtask[sArr.Count];
                    for (int i = 0; i < sArr.Count; i++)
                    {
                        string path = $"AI/htn_domain.json.Subtasks[{i}]";
                        if (sArr[i] is not JsonObject o)
                        {
                            throw Fail(path, "Expected an object.");
                        }

                        int idx = RequireInt(o, "Index", path);
                        string kind = RequireString(o, "Kind", path);
                        int rid = RequireInt(o, "RefId", path);
                        var k = string.Equals(kind, "Compound", StringComparison.OrdinalIgnoreCase)
                            ? HtnSubtaskKind.Compound
                            : string.Equals(kind, "Action", StringComparison.OrdinalIgnoreCase)
                                ? HtnSubtaskKind.Action
                                : throw Fail($"{path}.Kind", $"Unsupported subtask kind '{kind}'.");

                        if ((uint)idx >= (uint)subtasks.Length)
                        {
                            throw Fail($"{path}.Index", $"Subtask index {idx} is outside subtask table length {subtasks.Length}.");
                        }

                        if (k == HtnSubtaskKind.Compound && (uint)rid >= (uint)tasks.Length)
                        {
                            throw Fail($"{path}.RefId", $"Subtask references unknown compound task id {rid}.");
                        }

                        if (k == HtnSubtaskKind.Action && (uint)rid >= (uint)goapActions.Length)
                        {
                            throw Fail($"{path}.RefId", $"Subtask references unknown action id {rid}.");
                        }

                        subtasks[idx] = new HtnSubtask(k, rid);
                    }

                    for (int i = 0; i < tasks.Length; i++)
                    {
                        ref readonly var task = ref tasks[i];
                        if (task.FirstMethod < 0 || task.MethodCount < 0 || task.FirstMethod + task.MethodCount > methods.Length)
                        {
                            throw Fail($"AI/htn_domain.json.Tasks[{i}]", $"Task method range [{task.FirstMethod}, {task.FirstMethod + task.MethodCount}) exceeds method table length {methods.Length}.");
                        }
                    }

                    htnDomain = new HtnDomainCompiled256(tasks, methods, subtasks);
                }

                if (htnObj.TryGetPropertyValue("Roots", out var rNode) && rNode is JsonArray rArr)
                {
                    var roots = new (int GoalPresetId, int RootTaskId)[rArr.Count];
                    int count = 0;
                    for (int i = 0; i < rArr.Count; i++)
                    {
                        string path = $"AI/htn_domain.json.Roots[{i}]";
                        if (rArr[i] is not JsonObject o)
                        {
                            throw Fail(path, "Expected an object.");
                        }

                        int gpid = RequirePositiveInt(o, "GoalPresetId", path);
                        int rid = RequireInt(o, "RootTaskId", path);
                        if (htnDomain.Tasks.Length > 0 && (uint)rid >= (uint)htnDomain.Tasks.Length)
                        {
                            throw Fail($"{path}.RootTaskId", $"Root references unknown task id {rid}.");
                        }

                        roots[count++] = (gpid, rid);
                    }
                    if (count != roots.Length) Array.Resize(ref roots, count);
                    htnRoots = new HtnRootTable(roots);
                }
            }

            var utilityRuntime = CompileUtilityRuntime(catalog, report);

            return new AiCompiledRuntime(_atoms, projectionTable, goalSelector, actionLibrary, goapGoalTable, htnDomain, htnRoots, utilityRuntime);
        }

        private UtilityAiCompiledRuntime CompileUtilityRuntime(ConfigCatalog catalog, ConfigConflictReport? report)
        {
            var targetFilterNode = Merge(GetEntry(catalog, "AI/target_filters.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var inputNode = Merge(GetEntry(catalog, "AI/inputs.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var normalizationNode = Merge(GetEntry(catalog, "AI/normalizations.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var curveNode = Merge(GetEntry(catalog, "AI/curves.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var taskNode = Merge(GetEntry(catalog, "AI/tasks.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var decisionNode = Merge(GetEntry(catalog, "AI/decisions.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var decisionMakerNode = Merge(GetEntry(catalog, "AI/decision_makers.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var profileNode = Merge(GetEntry(catalog, "AI/profiles.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var stanceNode = Merge(GetEntry(catalog, "AI/stances.json", ConfigMergePolicy.ArrayById, idField: "id"), report);
            var actuatorNode = Merge(GetEntry(catalog, "AI/actuators.json", ConfigMergePolicy.ArrayById, idField: "id"), report);

            bool hasUtilityConfig =
                HasArrayItems(targetFilterNode) ||
                HasArrayItems(inputNode) ||
                HasArrayItems(normalizationNode) ||
                HasArrayItems(curveNode) ||
                HasArrayItems(taskNode) ||
                HasArrayItems(decisionNode) ||
                HasArrayItems(decisionMakerNode) ||
                HasArrayItems(profileNode) ||
                HasArrayItems(stanceNode) ||
                HasArrayItems(actuatorNode);

            if (!hasUtilityConfig)
            {
                return UtilityAiCompiledRuntime.Empty;
            }

            if (_validation == null)
            {
                throw Fail("AI/utility", "Utility AI config requires AiConfigValidationContext.");
            }

            var targetFilters = new List<UtilityAiTargetFilterDefinition>();
            var targetFilterOps = new List<UtilityAiTargetFilterOpDefinition>();
            var targetFilterIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileTargetFilters(targetFilterNode, targetFilters, targetFilterOps, targetFilterIds);

            var actuators = new List<UtilityAiActuatorDefinition>();
            var actuatorIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileActuators(actuatorNode, actuators, actuatorIds);

            var inputs = new List<UtilityAiInputDefinition>();
            var inputIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileInputs(inputNode, inputs, inputIds, actuatorIds);

            var normalizations = new List<UtilityAiNormalizationDefinition>();
            var normalizationIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileNormalizations(normalizationNode, normalizations, normalizationIds);

            var curves = new List<UtilityAiCurveDefinition>();
            var curveIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileCurves(curveNode, curves, curveIds);

            var tasks = new List<UtilityAiTaskDefinition>();
            var taskIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileTasks(taskNode, tasks, taskIds);

            var stances = new List<UtilityAiStanceDefinition>();
            var stanceIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileStances(stanceNode, stances, stanceIds, targetFilterIds);

            var decisions = new List<UtilityAiDecisionDefinition>();
            var considerations = new List<UtilityAiConsiderationDefinition>();
            var decisionIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileDecisions(decisionNode, decisions, considerations, decisionIds, targetFilterIds, inputIds, normalizationIds, curveIds, taskIds);

            var decisionMakers = new List<UtilityAiDecisionMakerDefinition>();
            var decisionMakerIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileDecisionMakers(decisionMakerNode, decisionMakers, decisionMakerIds, decisionIds);

            var profiles = new List<UtilityAiProfileDefinition>();
            var profileIds = new Dictionary<string, int>(StringComparer.Ordinal);
            CompileProfiles(profileNode, profiles, profileIds, decisionMakerIds, stanceIds);

            if (profiles.Count == 0)
            {
                throw Fail("AI/profiles.json", "Utility AI config must declare at least one profile.");
            }

            return new UtilityAiCompiledRuntime(
                profiles.ToArray(),
                decisionMakers.ToArray(),
                decisions.ToArray(),
                considerations.ToArray(),
                targetFilters.ToArray(),
                targetFilterOps.ToArray(),
                inputs.ToArray(),
                normalizations.ToArray(),
                curves.ToArray(),
                tasks.ToArray(),
                stances.ToArray(),
                actuators.ToArray(),
                new UtilityAiAuthoringCatalog(profileIds, stanceIds, actuatorIds));
        }

        private void CompileTargetFilters(
            JsonNode? node,
            List<UtilityAiTargetFilterDefinition> filters,
            List<UtilityAiTargetFilterOpDefinition> ops,
            Dictionary<string, int> ids)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/target_filters.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireRecordId(obj, path);
                int offset = ops.Count;
                int maxResults = TryReadInt(obj, "MaxResults", out int authoredMax) ? authoredMax : 64;
                if (maxResults <= 0)
                {
                    throw Fail($"{path}.MaxResults", "MaxResults must be positive.");
                }

                if (obj.TryGetPropertyValue("Ops", out var opNode) && opNode is JsonArray opArr)
                {
                    for (int o = 0; o < opArr.Count; o++)
                    {
                        string opPath = $"AI/target_filters.json:{id}.Ops[{o}]";
                        JsonObject opObj = RequireObject(opArr[o], opPath);
                        string kind = RequireString(opObj, "Kind", opPath);
                        ops.Add(CompileTargetFilterOp(kind, opObj, opPath));
                    }
                }
                else
                {
                    throw Fail($"AI/target_filters.json:{id}.Ops", "Target filter must declare Ops.");
                }

                ids.Add(id, filters.Count);
                filters.Add(new UtilityAiTargetFilterDefinition(offset, ops.Count - offset, maxResults));
            }
        }

        private UtilityAiTargetFilterOpDefinition CompileTargetFilterOp(string kind, JsonObject obj, string path)
        {
            GameplayTagContainer tags = default;
            if (string.Equals(kind, "SourceSelf", StringComparison.OrdinalIgnoreCase))
            {
                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SourceSelf, 0, 0, RelationshipFilter.All, in tags);
            }

            if (string.Equals(kind, "SpatialRadius", StringComparison.OrdinalIgnoreCase))
            {
                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, RequirePositiveInt(obj, "RadiusCm", path), 0, RelationshipFilter.All, in tags);
            }

            if (string.Equals(kind, "Relationship", StringComparison.OrdinalIgnoreCase))
            {
                var relationship = RelationshipFilterUtil.Parse(RequireString(obj, "Value", path));
                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, relationship, in tags);
            }

            if (string.Equals(kind, "HasAllTags", StringComparison.OrdinalIgnoreCase))
            {
                tags = ReadTagMask(obj, "Tags", path);
                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.HasAllTags, 0, 0, RelationshipFilter.All, in tags);
            }

            if (string.Equals(kind, "HasNoneTags", StringComparison.OrdinalIgnoreCase))
            {
                tags = ReadTagMask(obj, "Tags", path);
                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.HasNoneTags, 0, 0, RelationshipFilter.All, in tags);
            }

            if (string.Equals(kind, "LayerAny", StringComparison.OrdinalIgnoreCase))
            {
                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.LayerAny, RequirePositiveInt(obj, "Mask", path), 0, RelationshipFilter.All, in tags);
            }

            if (string.Equals(kind, "DistanceMax", StringComparison.OrdinalIgnoreCase))
            {
                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.DistanceMax, RequirePositiveInt(obj, "MaxCm", path), 0, RelationshipFilter.All, in tags);
            }

            if (string.Equals(kind, "RecentAttacker", StringComparison.OrdinalIgnoreCase))
            {
                int ttl = TryReadInt(obj, "TtlSteps", out int authoredTtl) ? authoredTtl : 30;
                if (ttl <= 0)
                {
                    throw Fail($"{path}.TtlSteps", "TtlSteps must be positive.");
                }

                return new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.RecentAttacker, ttl, 0, RelationshipFilter.All, in tags);
            }

            throw Fail($"{path}.Kind", $"Unsupported target filter op '{kind}'.");
        }

        private void CompileInputs(
            JsonNode? node,
            List<UtilityAiInputDefinition> inputs,
            Dictionary<string, int> ids,
            Dictionary<string, int> actuatorIds)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/inputs.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireRecordId(obj, path);
                string kind = RequireString(obj, "Kind", path);
                UtilityAiInputKind parsedKind;
                int arg0 = 0;
                int graphId = 0;

                if (string.Equals(kind, "Constant", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiInputKind.Constant;
                    arg0 = TryReadInt(obj, "Value", out int value) ? value : 1;
                }
                else if (string.Equals(kind, "DistanceToTarget", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiInputKind.DistanceToTarget;
                }
                else if (string.Equals(kind, "TargetPriorityBucket", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiInputKind.TargetPriorityBucket;
                    arg0 = TryReadInt(obj, "DefaultPriority", out int priority) ? priority : 0;
                }
                else if (string.Equals(kind, "ActuatorReadiness01", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiInputKind.ActuatorReadiness01;
                    if (obj.ContainsKey("ActuatorId") || obj.ContainsKey("actuatorId"))
                    {
                        throw Fail($"{path}.ActuatorId", "Use Actuator with an AI/actuators.json id; numeric actuator ids are not authoring SSOT.");
                    }

                    arg0 = ResolveLocalId(
                        actuatorIds,
                        RequireString(obj, "Actuator", path),
                        $"{path}.Actuator",
                        "actuator");
                }
                else if (string.Equals(kind, "GraphScore", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiInputKind.GraphScore;
                    graphId = ResolveGraphReference(obj, path);
                }
                else if (string.Equals(kind, "TargetHasTag", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiInputKind.TargetHasTag;
                    arg0 = ResolveTag(RequireString(obj, "Tag", path), $"{path}.Tag");
                }
                else if (string.Equals(kind, "SourceHasTag", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiInputKind.SourceHasTag;
                    arg0 = ResolveTag(RequireString(obj, "Tag", path), $"{path}.Tag");
                }
                else if (string.Equals(kind, "InfluenceSample01", StringComparison.OrdinalIgnoreCase))
                {
                    throw Fail(
                        $"{path}.Kind",
                        "InfluenceSample01 is runtime-injected only (InfluenceFieldRegistry + field key table). " +
                        "AI/inputs.json authoring is not supported until influence projection is wired into the main loop.");
                }
                else
                {
                    throw Fail($"{path}.Kind", $"Unknown input kind '{kind}'.");
                }

                ids.Add(id, inputs.Count);
                inputs.Add(new UtilityAiInputDefinition(parsedKind, arg0, graphId));
            }
        }

        private static void CompileNormalizations(JsonNode? node, List<UtilityAiNormalizationDefinition> normalizations, Dictionary<string, int> ids)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/normalizations.json[{i}]";
                JsonObject obj = RequireObject(objNode: arr[i], path);
                string id = RequireRecordId(obj, path);
                string kind = RequireString(obj, "Kind", path);
                UtilityAiNormalizationKind parsedKind;
                if (string.Equals(kind, "Identity", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiNormalizationKind.Identity;
                }
                else if (string.Equals(kind, "Range", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiNormalizationKind.Range;
                }
                else if (string.Equals(kind, "RangeInverse", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiNormalizationKind.RangeInverse;
                }
                else
                {
                    throw Fail($"{path}.Kind", $"Unsupported normalization kind '{kind}'.");
                }

                float min = TryReadFloat(obj, "Min", out float authoredMin) ? authoredMin : 0f;
                float max = TryReadFloat(obj, "Max", out float authoredMax) ? authoredMax : 1f;
                if (parsedKind != UtilityAiNormalizationKind.Identity && max <= min)
                {
                    throw Fail(path, "Normalization Max must be greater than Min.");
                }

                ids.Add(id, normalizations.Count);
                normalizations.Add(new UtilityAiNormalizationDefinition(parsedKind, min, max));
            }
        }

        private static void CompileCurves(JsonNode? node, List<UtilityAiCurveDefinition> curves, Dictionary<string, int> ids)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/curves.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireRecordId(obj, path);
                string kind = RequireString(obj, "Kind", path);
                UtilityAiCurveKind parsedKind;
                if (string.Equals(kind, "Linear", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiCurveKind.Linear;
                }
                else if (string.Equals(kind, "Power", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiCurveKind.Power;
                }
                else if (string.Equals(kind, "Inverse", StringComparison.OrdinalIgnoreCase))
                {
                    parsedKind = UtilityAiCurveKind.Inverse;
                }
                else
                {
                    throw Fail($"{path}.Kind", $"Unknown curve kind '{kind}'.");
                }

                float exponent = TryReadFloat(obj, "Exponent", out float authoredExponent) ? authoredExponent : 1f;
                if (exponent <= 0f)
                {
                    throw Fail($"{path}.Exponent", "Exponent must be positive.");
                }

                ids.Add(id, curves.Count);
                curves.Add(new UtilityAiCurveDefinition(parsedKind, exponent));
            }
        }

        private void CompileTasks(JsonNode? node, List<UtilityAiTaskDefinition> tasks, Dictionary<string, int> ids)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/tasks.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                if (obj.ContainsKey("IntArg0"))
                {
                    throw Fail($"{path}.IntArg0", "Bare order int args are outside the Utility AI order contract. Use AbilitySlotIndex.");
                }

                if (obj.ContainsKey("IntArg1"))
                {
                    throw Fail($"{path}.IntArg1", "Bare order int args are outside the Utility AI order contract. Use AbilitySlotIndex.");
                }

                if (obj.ContainsKey("IntArg2"))
                {
                    throw Fail($"{path}.IntArg2", "Bare order int args are outside the Utility AI order contract. Use typed OrderPayloadKind fields.");
                }

                if (obj.ContainsKey("IntArg3"))
                {
                    throw Fail($"{path}.IntArg3", "Bare order int args are outside the Utility AI order contract. Use typed OrderPayloadKind fields.");
                }

                RequireKnownProperties(obj, path, TaskPropertyNames);
                string id = RequireRecordId(obj, path);
                string kind = RequireString(obj, "Kind", path);
                UtilityAiTaskKind parsedKind;
                if (string.Equals(kind, "SubmitOrder", StringComparison.Ordinal))
                {
                    parsedKind = UtilityAiTaskKind.SubmitOrder;
                }
                else if (string.Equals(kind, "Sequence", StringComparison.Ordinal) ||
                         string.Equals(kind, "Parallel", StringComparison.Ordinal) ||
                         string.Equals(kind, "ParallelComplete", StringComparison.Ordinal))
                {
                    throw Fail($"{path}.Kind", $"Task kind '{kind}' has no runtime executor. Use SubmitOrder for current Utility AI tasks.");
                }
                else
                {
                    throw Fail($"{path}.Kind", $"Unknown task kind '{kind}'.");
                }

                int orderTypeId = 0;
                AiOrderPayloadKind payloadKind = AiOrderPayloadKind.None;
                int abilitySlotIndex = TryReadInt(obj, "AbilitySlotIndex", out int slot) ? slot : -1;
                if (parsedKind == UtilityAiTaskKind.SubmitOrder)
                {
                    payloadKind = RequireOrderPayloadKind(obj, "OrderPayloadKind", path);
                    orderTypeId = ResolveOrderTypeReference(obj, path);
                    ValidateUtilityTaskPayload(payloadKind, abilitySlotIndex, path);
                }

                int submitMode = TryReadByte(obj, "SubmitMode", out byte sm) ? sm : (byte)OrderSubmitMode.Immediate;
                if (!Enum.IsDefined(typeof(OrderSubmitMode), (byte)submitMode))
                {
                    throw Fail($"{path}.SubmitMode", $"Unknown submit mode value {submitMode}.");
                }

                int playerId = TryReadInt(obj, "PlayerId", out int authoredPlayerId) ? authoredPlayerId : 0;
                ids.Add(id, tasks.Count);
                tasks.Add(new UtilityAiTaskDefinition(
                    parsedKind,
                    payloadKind,
                    orderTypeId,
                    abilitySlotIndex,
                    submitMode,
                    playerId));
            }
        }

        private void CompileStances(
            JsonNode? node,
            List<UtilityAiStanceDefinition> stances,
            Dictionary<string, int> ids,
            Dictionary<string, int> targetFilterIds)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/stances.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireRecordId(obj, path);
                int targetFilterId = TryReadString(obj, "TargetFilter", out string filter)
                    ? ResolveLocalId(targetFilterIds, filter, $"{path}.TargetFilter", "target filter")
                    : -1;
                ids.Add(id, stances.Count);
                stances.Add(new UtilityAiStanceDefinition(
                    stances.Count,
                    TryReadBool(obj, "AutoAcquire", out bool autoAcquire) && autoAcquire,
                    TryReadBool(obj, "Retaliate", out bool retaliate) && retaliate,
                    TryReadBool(obj, "AllowMoveChase", out bool allowMoveChase) && allowMoveChase,
                    targetFilterId));
            }
        }

        private void CompileActuators(
            JsonNode? node,
            List<UtilityAiActuatorDefinition> actuators,
            Dictionary<string, int> actuatorIds)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/actuators.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                RequireKnownProperties(obj, path, ActuatorPropertyNames);
                string id = RequireRecordId(obj, path);
                actuatorIds.Add(id, actuators.Count);
                actuators.Add(new UtilityAiActuatorDefinition(actuators.Count));
            }
        }

        private void CompileDecisions(
            JsonNode? node,
            List<UtilityAiDecisionDefinition> decisions,
            List<UtilityAiConsiderationDefinition> considerations,
            Dictionary<string, int> ids,
            Dictionary<string, int> targetFilterIds,
            Dictionary<string, int> inputIds,
            Dictionary<string, int> normalizationIds,
            Dictionary<string, int> curveIds,
            Dictionary<string, int> taskIds)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/decisions.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                RequireKnownProperties(obj, path, DecisionPropertyNames);
                string id = RequireRecordId(obj, path);
                int targetFilterId = ResolveLocalId(targetFilterIds, RequireString(obj, "TargetFilter", path), $"{path}.TargetFilter", "target filter");

                int considerationOffset = considerations.Count;
                if (obj.TryGetPropertyValue("Considerations", out var considerationNode) && considerationNode is JsonArray considerationArr)
                {
                    for (int c = 0; c < considerationArr.Count; c++)
                    {
                        string considerationPath = $"AI/decisions.json:{id}.Considerations[{c}]";
                        JsonObject cObj = RequireObject(considerationArr[c], considerationPath);
                        RequireKnownProperties(cObj, considerationPath, ConsiderationPropertyNames);
                        int inputId = ResolveLocalId(inputIds, RequireString(cObj, "Input", considerationPath), $"{considerationPath}.Input", "input");
                        int normalizationId = ResolveLocalId(normalizationIds, RequireString(cObj, "Normalization", considerationPath), $"{considerationPath}.Normalization", "normalization");
                        int curveId = ResolveLocalId(curveIds, RequireString(cObj, "Curve", considerationPath), $"{considerationPath}.Curve", "curve");
                        float weight = TryReadFloat(cObj, "Weight", out float authoredWeight) ? authoredWeight : 1f;
                        string aggregate = TryReadString(cObj, "Aggregate", out string authoredAggregate) ? authoredAggregate : "Multiply";
                        considerations.Add(new UtilityAiConsiderationDefinition(inputId, normalizationId, curveId, weight, ParseAggregateMode(aggregate, $"{considerationPath}.Aggregate")));
                    }
                }

                int taskOffset = ResolveTaskRange(obj, path, id, taskIds, out int taskCount);
                int priority = TryReadInt(obj, "Priority", out int authoredPriority) ? authoredPriority : 0;
                float baseScore = TryReadFloat(obj, "BaseScore", out float authoredBaseScore) ? authoredBaseScore : 1f;
                float weightDecision = TryReadFloat(obj, "Weight", out float authoredWeightDecision) ? authoredWeightDecision : 1f;
                float momentumBonus = TryReadFloat(obj, "MomentumBonus", out float authoredMomentum) ? authoredMomentum : 0f;
                int minDurationSteps = TryReadInt(obj, "MinDurationSteps", out int authoredMinDuration) ? authoredMinDuration : 0;
                UtilityAiDecisionFlags flags = ParseDecisionFlags(obj, path);

                ids.Add(id, decisions.Count);
                decisions.Add(new UtilityAiDecisionDefinition(
                    targetFilterId,
                    considerationOffset,
                    considerations.Count - considerationOffset,
                    taskOffset,
                    taskCount,
                    priority,
                    baseScore,
                    weightDecision,
                    momentumBonus,
                    minDurationSteps,
                    flags));
            }
        }

        private int ResolveTaskRange(JsonObject obj, string path, string id, Dictionary<string, int> taskIds, out int taskCount)
        {
            taskCount = 0;
            int first = -1;
            if (!obj.TryGetPropertyValue("Tasks", out var taskNode) || taskNode is not JsonArray taskArr)
            {
                throw Fail($"AI/decisions.json:{id}.Tasks", "Decision must declare Tasks.");
            }

            for (int t = 0; t < taskArr.Count; t++)
            {
                string taskKey = taskArr[t]?.ToString() ?? string.Empty;
                int resolved = ResolveLocalId(taskIds, taskKey, $"{path}.Tasks[{t}]", "task");
                if (first < 0)
                {
                    first = resolved;
                }
                else if (first + taskCount != resolved)
                {
                    throw Fail($"{path}.Tasks[{t}]", "Decision task references must resolve to a contiguous compiled task range.");
                }

                taskCount++;
            }

            if (taskCount <= 0)
            {
                throw Fail($"AI/decisions.json:{id}.Tasks", "Decision must reference at least one task.");
            }

            return first;
        }

        private void CompileDecisionMakers(
            JsonNode? node,
            List<UtilityAiDecisionMakerDefinition> decisionMakers,
            Dictionary<string, int> ids,
            Dictionary<string, int> decisionIds)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/decision_makers.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireRecordId(obj, path);
                int offset = ResolveDecisionRange(obj, path, id, decisionIds, out int count);
                string mode = TryReadString(obj, "SelectionMode", out string authoredMode) ? authoredMode : "UtilityScore";
                float switchMargin = TryReadFloat(obj, "SwitchMargin", out float authoredMargin) ? authoredMargin : 0f;
                ids.Add(id, decisionMakers.Count);
                decisionMakers.Add(new UtilityAiDecisionMakerDefinition(offset, count, ParseSelectionMode(mode, $"{path}.SelectionMode"), switchMargin));
            }
        }

        private int ResolveDecisionRange(JsonObject obj, string path, string id, Dictionary<string, int> decisionIds, out int count)
        {
            count = 0;
            int first = -1;
            if (!obj.TryGetPropertyValue("Decisions", out var decisionNode) || decisionNode is not JsonArray decisionArr)
            {
                throw Fail($"AI/decision_makers.json:{id}.Decisions", "Decision maker must declare Decisions.");
            }

            for (int d = 0; d < decisionArr.Count; d++)
            {
                string decisionKey = decisionArr[d]?.ToString() ?? string.Empty;
                int resolved = ResolveLocalId(decisionIds, decisionKey, $"{path}.Decisions[{d}]", "decision");
                if (first < 0)
                {
                    first = resolved;
                }
                else if (first + count != resolved)
                {
                    throw Fail($"{path}.Decisions[{d}]", "Decision maker references must resolve to a contiguous compiled decision range.");
                }

                count++;
            }

            if (count <= 0)
            {
                throw Fail($"AI/decision_makers.json:{id}.Decisions", "Decision maker must reference at least one decision.");
            }

            return first;
        }

        private void CompileProfiles(
            JsonNode? node,
            List<UtilityAiProfileDefinition> profiles,
            Dictionary<string, int> profileIds,
            Dictionary<string, int> decisionMakerIds,
            Dictionary<string, int> stanceIds)
        {
            if (node is not JsonArray arr) return;

            for (int i = 0; i < arr.Count; i++)
            {
                string path = $"AI/profiles.json[{i}]";
                JsonObject obj = RequireObject(arr[i], path);
                string id = RequireRecordId(obj, path);
                int offset = ResolveDecisionMakerRange(obj, path, decisionMakerIds, out int count);
                int interval = TryReadInt(obj, "DecisionIntervalSteps", out int authoredInterval) ? authoredInterval : 1;
                if (interval <= 0)
                {
                    throw Fail($"{path}.DecisionIntervalSteps", "DecisionIntervalSteps must be positive.");
                }

                int maxCandidates = TryReadInt(obj, "MaxCandidates", out int authoredMaxCandidates) ? authoredMaxCandidates : 64;
                if (maxCandidates <= 0)
                {
                    throw Fail($"{path}.MaxCandidates", "MaxCandidates must be positive.");
                }

                if (obj.ContainsKey("DefaultStanceId"))
                {
                    throw Fail($"{path}.DefaultStanceId", "Use DefaultStance with a stance key.");
                }

                int defaultStanceId = TryReadString(obj, "DefaultStance", out string stanceKey)
                    ? ResolveLocalId(stanceIds, stanceKey, $"{path}.DefaultStance", "stance")
                    : -1;

                profileIds.Add(id, profiles.Count);
                profiles.Add(new UtilityAiProfileDefinition(offset, count, interval, maxCandidates, defaultStanceId));
            }
        }

        private int ResolveDecisionMakerRange(JsonObject obj, string path, Dictionary<string, int> decisionMakerIds, out int count)
        {
            count = 0;
            int first = -1;
            if (!obj.TryGetPropertyValue("DecisionMakers", out var node) || node is not JsonArray arr)
            {
                throw Fail($"{path}.DecisionMakers", "Profile must declare DecisionMakers.");
            }

            for (int i = 0; i < arr.Count; i++)
            {
                string key = arr[i]?.ToString() ?? string.Empty;
                int resolved = ResolveLocalId(decisionMakerIds, key, $"{path}.DecisionMakers[{i}]", "decision maker");
                if (first < 0)
                {
                    first = resolved;
                }
                else if (first + count != resolved)
                {
                    throw Fail($"{path}.DecisionMakers[{i}]", "Profile decision maker references must resolve to a contiguous compiled range.");
                }

                count++;
            }

            if (count <= 0)
            {
                throw Fail($"{path}.DecisionMakers", "Profile must reference at least one decision maker.");
            }

            return first;
        }

        private int ResolveOrderTypeReference(JsonObject obj, string path)
        {
            if (_validation == null)
            {
                throw Fail(path, "Order references require AiConfigValidationContext with OrderTypeRegistry.");
            }

            int orderTypeId = 0;
            if (TryReadString(obj, "OrderTypeKey", out string orderTypeKey))
            {
                if (!_validation.OrderTypes.TryGetId(orderTypeKey, out orderTypeId) ||
                    orderTypeId <= 0 ||
                    !_validation.OrderTypes.IsRegistered(orderTypeId))
                {
                    throw Fail($"{path}.OrderTypeKey", $"References unknown order type key '{orderTypeKey}'.");
                }
            }

            if (TryReadInt(obj, "OrderTypeId", out int authoredOrderTypeId))
            {
                if (authoredOrderTypeId <= 0)
                {
                    throw Fail($"{path}.OrderTypeId", "OrderTypeId must be positive.");
                }

                if (!_validation.OrderTypes.IsRegistered(authoredOrderTypeId))
                {
                    throw Fail($"{path}.OrderTypeId", $"References unknown order type id {authoredOrderTypeId}.");
                }

                if (orderTypeId > 0 && orderTypeId != authoredOrderTypeId)
                {
                    throw Fail(path, $"OrderTypeKey resolved to {orderTypeId}, but OrderTypeId is {authoredOrderTypeId}.");
                }

                orderTypeId = authoredOrderTypeId;
            }

            if (orderTypeId <= 0)
            {
                throw Fail(path, "Task must declare OrderTypeKey or OrderTypeId.");
            }

            return orderTypeId;
        }

        private int ResolveGraphReference(JsonObject obj, string path)
        {
            int graphId = 0;
            if (TryReadString(obj, "GraphKey", out string graphKey))
            {
                graphId = GraphIdRegistry.GetId(graphKey);
                if (graphId <= 0)
                {
                    throw Fail($"{path}.GraphKey", $"References unknown graph key '{graphKey}'.");
                }
            }

            if (TryReadInt(obj, "GraphId", out int authoredGraphId))
            {
                if (authoredGraphId <= 0)
                {
                    throw Fail($"{path}.GraphId", "GraphId must be positive.");
                }

                if (graphId > 0 && graphId != authoredGraphId)
                {
                    throw Fail(path, $"GraphKey resolved to {graphId}, but GraphId is {authoredGraphId}.");
                }

                graphId = authoredGraphId;
            }

            if (graphId <= 0)
            {
                throw Fail(path, "GraphScore input must declare GraphKey or GraphId.");
            }

            if (_validation == null || _validation.Graphs == null)
            {
                throw Fail(path, "Graph references require AiConfigValidationContext with GraphProgramRegistry.");
            }

            try
            {
                GraphScoreEvaluator.RequireScoreProgram(_validation.Graphs, graphId, path);
            }
            catch (InvalidOperationException ex)
            {
                throw Fail(path, ex.Message);
            }

            return graphId;
        }

        private static int ResolveLocalId(Dictionary<string, int> ids, string key, string path, string kind)
        {
            if (string.IsNullOrWhiteSpace(key) || !ids.TryGetValue(key, out int id))
            {
                throw Fail(path, $"References unknown {kind} '{key}'.");
            }

            return id;
        }

        private static bool HasArrayItems(JsonNode? node)
        {
            return node is JsonArray arr && arr.Count > 0;
        }

        private static JsonObject RequireObject(JsonNode? objNode, string path)
        {
            if (objNode is not JsonObject obj)
            {
                throw Fail(path, "Expected an object.");
            }

            return obj;
        }

        private static string RequireRecordId(JsonObject obj, string path)
        {
            return RequireString(obj, "id", path);
        }

        private static GameplayTagContainer ReadTagMask(JsonObject obj, string key, string path)
        {
            var tags = new GameplayTagContainer();
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray arr)
            {
                throw Fail($"{path}.{key}", "Tag array is required.");
            }

            for (int i = 0; i < arr.Count; i++)
            {
                string tagName = arr[i]?.ToString() ?? string.Empty;
                int tagId = ResolveTag(tagName, $"{path}.{key}[{i}]");
                tags.AddTag(tagId);
            }

            return tags;
        }

        private static int ResolveTag(string tagName, string path)
        {
            int tagId = TagRegistry.GetId(tagName);
            if (tagId <= 0)
            {
                throw Fail(path, $"References unknown gameplay tag '{tagName}'.");
            }

            return tagId;
        }

        private static UtilityAiAggregateMode ParseAggregateMode(string value, string path)
        {
            if (string.Equals(value, "Multiply", StringComparison.OrdinalIgnoreCase)) return UtilityAiAggregateMode.Multiply;
            if (string.Equals(value, "WeightedSum", StringComparison.OrdinalIgnoreCase)) return UtilityAiAggregateMode.WeightedSum;
            if (string.Equals(value, "Veto", StringComparison.OrdinalIgnoreCase)) return UtilityAiAggregateMode.Veto;
            if (string.Equals(value, "PriorityBucket", StringComparison.OrdinalIgnoreCase)) return UtilityAiAggregateMode.PriorityBucket;
            throw Fail(path, $"Unsupported aggregate mode '{value}'.");
        }

        private static UtilityAiSelectionMode ParseSelectionMode(string value, string path)
        {
            if (string.Equals(value, "UtilityScore", StringComparison.OrdinalIgnoreCase)) return UtilityAiSelectionMode.UtilityScore;
            if (string.Equals(value, "FixedPriority", StringComparison.OrdinalIgnoreCase)) return UtilityAiSelectionMode.FixedPriority;
            throw Fail(path, $"Unsupported selection mode '{value}'.");
        }

        private static UtilityAiDecisionFlags ParseDecisionFlags(JsonObject obj, string path)
        {
            UtilityAiDecisionFlags flags = UtilityAiDecisionFlags.None;
            if (TryReadBool(obj, "Autocast", out bool autocast) && autocast) flags |= UtilityAiDecisionFlags.Autocast;
            if (TryReadBool(obj, "OrdinaryAttack", out bool ordinaryAttack) && ordinaryAttack) flags |= UtilityAiDecisionFlags.OrdinaryAttack;
            if (TryReadBool(obj, "RequiresTarget", out bool requiresTarget) && requiresTarget) flags |= UtilityAiDecisionFlags.RequiresTarget;
            if (TryReadBool(obj, "KeepRunningUntilFinished", out bool keepRunning) && keepRunning) flags |= UtilityAiDecisionFlags.KeepRunningUntilFinished;
            if (TryReadBool(obj, "ExplicitOrderOnly", out bool explicitOnly) && explicitOnly) flags |= UtilityAiDecisionFlags.ExplicitOrderOnly;

            if (obj.TryGetPropertyValue("Flags", out var node) && node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    string flag = arr[i]?.ToString() ?? string.Empty;
                    if (string.Equals(flag, "Autocast", StringComparison.OrdinalIgnoreCase)) flags |= UtilityAiDecisionFlags.Autocast;
                    else if (string.Equals(flag, "OrdinaryAttack", StringComparison.OrdinalIgnoreCase)) flags |= UtilityAiDecisionFlags.OrdinaryAttack;
                    else if (string.Equals(flag, "RequiresTarget", StringComparison.OrdinalIgnoreCase)) flags |= UtilityAiDecisionFlags.RequiresTarget;
                    else if (string.Equals(flag, "KeepRunningUntilFinished", StringComparison.OrdinalIgnoreCase)) flags |= UtilityAiDecisionFlags.KeepRunningUntilFinished;
                    else if (string.Equals(flag, "ExplicitOrderOnly", StringComparison.OrdinalIgnoreCase)) flags |= UtilityAiDecisionFlags.ExplicitOrderOnly;
                    else throw Fail($"{path}.Flags[{i}]", $"Unsupported decision flag '{flag}'.");
                }
            }

            return flags;
        }

        private ActionOrderSpec ReadOrderSpec(JsonObject orderObj, string path)
        {
            if (orderObj.ContainsKey("OrderTagId"))
            {
                throw Fail($"{path}.OrderTagId", "OrderTagId is outside the active AI order contract. Use OrderTypeKey or OrderTypeId.");
            }

            RequireKnownProperties(orderObj, path, ActionOrderPropertyNames);
            AiOrderPayloadKind payloadKind = RequireOrderPayloadKind(orderObj, "OrderPayloadKind", path);

            if (_validation == null)
            {
                throw Fail(path, "AI Order references require AiConfigValidationContext with OrderTypeRegistry.");
            }

            int orderTypeId = 0;
            if (TryReadString(orderObj, "OrderTypeKey", out string orderTypeKey))
            {
                if (!_validation.OrderTypes.TryGetId(orderTypeKey, out orderTypeId) ||
                    orderTypeId <= 0 ||
                    !_validation.OrderTypes.IsRegistered(orderTypeId))
                {
                    throw Fail($"{path}.OrderTypeKey", $"References unknown order type key '{orderTypeKey}'.");
                }
            }

            if (TryReadInt(orderObj, "OrderTypeId", out int authoredOrderTypeId))
            {
                if (authoredOrderTypeId <= 0)
                {
                    throw Fail($"{path}.OrderTypeId", "OrderTypeId must be positive.");
                }

                if (_validation != null && !_validation.OrderTypes.IsRegistered(authoredOrderTypeId))
                {
                    throw Fail($"{path}.OrderTypeId", $"References unknown order type id {authoredOrderTypeId}.");
                }

                if (orderTypeId > 0 && orderTypeId != authoredOrderTypeId)
                {
                    throw Fail(path, $"OrderTypeKey resolved to {orderTypeId}, but OrderTypeId is {authoredOrderTypeId}.");
                }

                orderTypeId = authoredOrderTypeId;
            }

            if (orderTypeId <= 0)
            {
                throw Fail(path, "Order must declare OrderTypeKey or OrderTypeId.");
            }

            byte submitModeByte = TryReadByte(orderObj, "SubmitMode", out byte sm)
                ? sm
                : (byte)OrderSubmitMode.Immediate;
            if (!Enum.IsDefined(typeof(OrderSubmitMode), submitModeByte))
            {
                throw Fail($"{path}.SubmitMode", $"Unsupported submit mode value {submitModeByte}.");
            }

            int playerId = TryReadInt(orderObj, "PlayerId", out int pid) ? pid : 0;
            return new ActionOrderSpec(payloadKind, orderTypeId, (OrderSubmitMode)submitModeByte, playerId);
        }

        private static AiOrderPayloadKind RequireOrderPayloadKind(JsonObject obj, string key, string path)
        {
            string value = RequireString(obj, key, path);
            if (string.Equals(value, "CastAbility", StringComparison.OrdinalIgnoreCase))
            {
                return AiOrderPayloadKind.CastAbility;
            }

            if (string.Equals(value, "MoveToWorldCm", StringComparison.OrdinalIgnoreCase))
            {
                return AiOrderPayloadKind.MoveToWorldCm;
            }

            if (string.Equals(value, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                return AiOrderPayloadKind.Stop;
            }

            if (string.Equals(value, "TargetEntity", StringComparison.OrdinalIgnoreCase))
            {
                return AiOrderPayloadKind.TargetEntity;
            }

            throw Fail($"{path}.{key}", $"Unsupported AI order payload kind '{value}'.");
        }

        private static void ValidateUtilityTaskPayload(AiOrderPayloadKind payloadKind, int abilitySlotIndex, string path)
        {
            switch (payloadKind)
            {
                case AiOrderPayloadKind.CastAbility:
                    if (abilitySlotIndex < 0)
                    {
                        throw Fail($"{path}.AbilitySlotIndex", "Utility AI SubmitOrder is a typed CastAbility order and requires AbilitySlotIndex.");
                    }
                    break;
                case AiOrderPayloadKind.TargetEntity:
                case AiOrderPayloadKind.MoveToWorldCm:
                case AiOrderPayloadKind.Stop:
                    if (abilitySlotIndex >= 0)
                    {
                        throw Fail($"{path}.AbilitySlotIndex", $"AbilitySlotIndex is only valid for CastAbility, not {payloadKind}.");
                    }
                    break;
                case AiOrderPayloadKind.None:
                default:
                    throw Fail($"{path}.OrderPayloadKind", $"Unsupported AI order payload kind '{payloadKind}'.");
            }
        }

        private static void ValidateActionOrderBindings(in ActionOrderSpec spec, IReadOnlyList<ActionBinding> bindings, string path)
        {
            bool hasAbilitySlot = false;
            bool hasTarget = false;
            bool hasTargetContext = false;
            bool hasMoveDestination = false;

            for (int i = 0; i < bindings.Count; i++)
            {
                switch (bindings[i].Op)
                {
                    case ActionBindingOp.IntToAbilitySlot:
                        hasAbilitySlot = true;
                        break;
                    case ActionBindingOp.EntityToTarget:
                        hasTarget = true;
                        break;
                    case ActionBindingOp.EntityToTargetContext:
                        hasTargetContext = true;
                        break;
                    case ActionBindingOp.EntityPositionToMoveDestination:
                        hasMoveDestination = true;
                        break;
                    default:
                        throw Fail($"{path}.Bindings[{i}].Op", $"Unsupported binding op '{bindings[i].Op}'.");
                }
            }

            switch (spec.PayloadKind)
            {
                case AiOrderPayloadKind.CastAbility:
                    if (!hasAbilitySlot)
                    {
                        throw Fail($"{path}.Bindings", "CastAbility order requires an IntToAbilitySlot binding.");
                    }
                    if (hasMoveDestination)
                    {
                        throw Fail($"{path}.Bindings", "EntityPositionToMoveDestination is not valid for CastAbility.");
                    }
                    break;
                case AiOrderPayloadKind.TargetEntity:
                    if (!hasTarget)
                    {
                        throw Fail($"{path}.Bindings", "TargetEntity order requires an EntityToTarget binding.");
                    }
                    if (hasAbilitySlot || hasTargetContext || hasMoveDestination)
                    {
                        throw Fail($"{path}.Bindings", "TargetEntity order only accepts EntityToTarget binding.");
                    }
                    break;
                case AiOrderPayloadKind.MoveToWorldCm:
                    if (!hasMoveDestination)
                    {
                        throw Fail($"{path}.Bindings", "MoveToWorldCm order requires an EntityPositionToMoveDestination binding.");
                    }
                    if (hasAbilitySlot || hasTarget || hasTargetContext)
                    {
                        throw Fail($"{path}.Bindings", "MoveToWorldCm order only accepts EntityPositionToMoveDestination binding.");
                    }
                    break;
                case AiOrderPayloadKind.Stop:
                    if (bindings.Count != 0)
                    {
                        throw Fail($"{path}.Bindings", "Stop order must not declare bindings.");
                    }
                    break;
                case AiOrderPayloadKind.None:
                default:
                    throw Fail($"{path}.Order.OrderPayloadKind", $"Unsupported AI order payload kind '{spec.PayloadKind}'.");
            }
        }

        private static void RequireKnownProperties(JsonObject obj, string path, IReadOnlyList<string> allowedProperties)
        {
            foreach (KeyValuePair<string, JsonNode?> kvp in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedProperties.Count; i++)
                {
                    if (string.Equals(kvp.Key, allowedProperties[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw Fail($"{path}.{kvp.Key}", $"Unexpected field '{kvp.Key}'. Use only: {string.Join(", ", allowedProperties)}.");
                }
            }
        }

        private JsonNode? Merge(in ConfigCatalogEntry entry, ConfigConflictReport? report)
        {
            if (report == null) return _pipeline.MergeFromCatalog(in entry);
            return _pipeline.MergeFromCatalog(in entry, report);
        }

        private static ConfigCatalogEntry GetEntry(ConfigCatalog catalog, string relativePath, ConfigMergePolicy policy, string idField)
        {
            if (catalog != null && catalog.TryGet(relativePath, out var e)) return e;
            return new ConfigCatalogEntry(relativePath, policy, idField);
        }

        private static (WorldStateBits256 Mask, WorldStateBits256 Values) ReadCondition(JsonObject obj, string propertyName, AtomRegistry atoms, string path)
        {
            var mask = new WorldStateBits256();
            var values = new WorldStateBits256();

            if (!obj.TryGetPropertyValue(propertyName, out var n) || n is not JsonObject c) return (mask, values);

            if (c.TryGetPropertyValue("Mask", out var mNode) && mNode is JsonArray maskArr)
            {
                for (int i = 0; i < maskArr.Count; i++)
                {
                    if (maskArr[i] == null)
                    {
                        throw Fail($"{path}.Mask[{i}]", "Atom id is required.");
                    }

                    int id = RequireAtomId(atoms, maskArr[i]!.ToString(), $"{path}.Mask[{i}]");
                    mask.SetBit(id, true);
                }
            }

            if (c.TryGetPropertyValue("Values", out var vNode) && vNode is JsonArray valArr)
            {
                for (int i = 0; i < valArr.Count; i++)
                {
                    if (valArr[i] == null)
                    {
                        throw Fail($"{path}.Values[{i}]", "Atom id is required.");
                    }

                    int id = RequireAtomId(atoms, valArr[i]!.ToString(), $"{path}.Values[{i}]");
                    values.SetBit(id, true);
                }
            }

            return (mask, values);
        }

        private int RequireAtomId(string atomName, string path)
        {
            return RequireAtomId(_atoms, atomName, path);
        }

        private static int RequireAtomId(AtomRegistry atoms, string atomName, string path)
        {
            if (string.IsNullOrWhiteSpace(atomName) || !atoms.TryGetId(atomName, out int atomId))
            {
                throw Fail(path, $"References unknown AI atom '{atomName}'. Declare it in AI/atoms.json.");
            }

            return atomId;
        }

        private static string ReadRecordId(JsonObject obj, string path)
        {
            return TryReadString(obj, "id", out string id) ? id : path;
        }

        private static int RequirePlanningStrategyId(JsonObject obj, string key, string path)
        {
            int value = RequireInt(obj, key, path);
            if (value != AIPlanningStrategyIds.None &&
                value != AIPlanningStrategyIds.Goap &&
                value != AIPlanningStrategyIds.Htn &&
                value != AIPlanningStrategyIds.DirectTask)
            {
                throw Fail($"{path}.{key}", $"Unknown planning strategy id {value}.");
            }

            return value;
        }

        private static int RequirePositiveInt(JsonObject obj, string key, string path)
        {
            int value = RequireInt(obj, key, path);
            if (value <= 0)
            {
                throw Fail($"{path}.{key}", "Value must be positive.");
            }

            return value;
        }

        private static int RequireOrderBlackboardKey(JsonObject obj, string key, string path)
        {
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null)
            {
                throw Fail(path, "Order blackboard key is required.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw Fail(path, $"Order blackboard key must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text))
                {
                    if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
                    {
                        throw Fail(path, "Order blackboard key must not contain leading or trailing whitespace.");
                    }

                    if (OrderBlackboardKeyRegistry.TryGetId(text, out int keyId))
                    {
                        return keyId;
                    }

                    throw Fail(path, $"References unknown order blackboard key '{text}'. Declare it in GAS/order_types.json orderBlackboardKeys or use a built-in key.");
                }
            }

            throw Fail(path, "Order blackboard key must be a non-empty semantic string.");
        }

        private static void RejectProjectionField(JsonObject obj, string key, string path, string op)
        {
            if (obj.ContainsKey(key))
            {
                throw Fail(path, $"Field is not valid for projection op '{op}'.");
            }
        }

        private static int RequireInt(JsonObject obj, string key, string path)
        {
            if (!TryReadInt(obj, key, out int value))
            {
                throw Fail($"{path}.{key}", "Integer value is required.");
            }

            return value;
        }

        private static string RequireString(JsonObject obj, string key, string path)
        {
            if (!TryReadString(obj, key, out string value))
            {
                throw Fail($"{path}.{key}", "Non-empty string value is required.");
            }

            return value;
        }

        private static InvalidOperationException Fail(string path, string message)
        {
            return new InvalidOperationException($"[AiConfigLoader] {path}: {message}");
        }

        private static bool TryParseProjectionOp(string op, out WorldStateProjectionOp result)
        {
            if (string.Equals(op, "IntEquals", StringComparison.OrdinalIgnoreCase)) { result = WorldStateProjectionOp.IntEquals; return true; }
            if (string.Equals(op, "IntGreaterOrEqual", StringComparison.OrdinalIgnoreCase)) { result = WorldStateProjectionOp.IntGreaterOrEqual; return true; }
            if (string.Equals(op, "IntLessOrEqual", StringComparison.OrdinalIgnoreCase)) { result = WorldStateProjectionOp.IntLessOrEqual; return true; }
            if (string.Equals(op, "EntityIsNonNull", StringComparison.OrdinalIgnoreCase)) { result = WorldStateProjectionOp.EntityIsNonNull; return true; }
            if (string.Equals(op, "EntityIsNull", StringComparison.OrdinalIgnoreCase)) { result = WorldStateProjectionOp.EntityIsNull; return true; }
            result = default;
            return false;
        }

        private static bool TryParseBindingOp(string op, out ActionBindingOp result)
        {
            if (string.Equals(op, "IntToAbilitySlot", StringComparison.Ordinal)) { result = ActionBindingOp.IntToAbilitySlot; return true; }
            if (string.Equals(op, "EntityToTarget", StringComparison.Ordinal)) { result = ActionBindingOp.EntityToTarget; return true; }
            if (string.Equals(op, "EntityToTargetContext", StringComparison.Ordinal)) { result = ActionBindingOp.EntityToTargetContext; return true; }
            if (string.Equals(op, "EntityPositionToMoveDestination", StringComparison.Ordinal)) { result = ActionBindingOp.EntityPositionToMoveDestination; return true; }
            result = default;
            return false;
        }

        private static bool IsBareOrderArgBinding(string op)
        {
            return string.Equals(op, "IntToOrderArg0", StringComparison.Ordinal) ||
                   string.Equals(op, "IntToOrderArg1", StringComparison.Ordinal) ||
                   string.Equals(op, "IntToOrderArg2", StringComparison.Ordinal) ||
                   string.Equals(op, "IntToOrderArg3", StringComparison.Ordinal);
        }

        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = string.Empty;
            if (obj.TryGetPropertyValue(key, out var node) && node != null)
            {
                value = node.ToString();
                return !string.IsNullOrWhiteSpace(value);
            }
            return false;
        }

        private static bool TryReadInt(JsonObject obj, string key, out int value)
        {
            value = default;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out int i)) { value = i; return true; }
                if (v.TryGetValue(out long l)) { value = (int)l; return true; }
                if (v.TryGetValue(out string? s) && int.TryParse(s, out int p)) { value = p; return true; }
            }
            return int.TryParse(node.ToString(), out value);
        }

        private static bool TryReadByte(JsonObject obj, string key, out byte value)
        {
            value = default;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out byte b)) { value = b; return true; }
                if (v.TryGetValue(out int i) && (uint)i <= 255u) { value = (byte)i; return true; }
                if (v.TryGetValue(out string? s) && byte.TryParse(s, out byte p)) { value = p; return true; }
            }
            return byte.TryParse(node.ToString(), out value);
        }

        private static bool TryReadBool(JsonObject obj, string key, out bool value)
        {
            value = default;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out bool b)) { value = b; return true; }
                if (v.TryGetValue(out string? s) && bool.TryParse(s, out bool p)) { value = p; return true; }
            }
            return bool.TryParse(node.ToString(), out value);
        }

        private static bool TryReadFloat(JsonObject obj, string key, out float value)
        {
            value = default;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out float f)) { value = f; return true; }
                if (v.TryGetValue(out double d)) { value = (float)d; return true; }
                if (v.TryGetValue(out string? s) && float.TryParse(s, out float p)) { value = p; return true; }
            }
            return float.TryParse(node.ToString(), out value);
        }
    }
}
