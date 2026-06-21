using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiConfigLoader
    {
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

                    int intKey = TryReadInt(obj, "IntKey", out int ik) ? ik : -1;
                    int intValue = TryReadInt(obj, "IntValue", out int iv) ? iv : 0;
                    int entityKey = TryReadInt(obj, "EntityKey", out int ek) ? ek : -1;

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
                            if (!TryParseBindingOp(op, out var bop))
                            {
                                throw Fail($"{bindingPath}.Op", $"Unsupported binding op '{op}'.");
                            }

                            int sk = RequireInt(bObj, "SourceKey", bindingPath);
                            btmp.Add(new ActionBinding(bop, sk));
                        }
                        bindings = btmp.ToArray();
                    }

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

            return new AiCompiledRuntime(_atoms, projectionTable, goalSelector, actionLibrary, goapGoalTable, htnDomain, htnRoots);
        }

        private ActionOrderSpec ReadOrderSpec(JsonObject orderObj, string path)
        {
            if (orderObj.ContainsKey("OrderTagId"))
            {
                throw Fail($"{path}.OrderTagId", "OrderTagId is not a supported AI order contract field. Use OrderTypeKey or OrderTypeId.");
            }

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
            int abilityId = ResolveAbilityId(orderObj, path);
            return new ActionOrderSpec(orderTypeId, (OrderSubmitMode)submitModeByte, playerId, abilityId);
        }

        private int ResolveAbilityId(JsonObject orderObj, string path)
        {
            int abilityId = 0;
            if (TryReadString(orderObj, "AbilityKey", out string abilityKey))
            {
                if (_validation == null || _validation.Abilities == null)
                {
                    throw Fail($"{path}.AbilityKey", "AbilityKey requires AiConfigValidationContext with AbilityDefinitionRegistry.");
                }

                abilityId = AbilityIdRegistry.GetId(abilityKey);
                if (abilityId <= 0 || !_validation.Abilities.TryGet(abilityId, out _))
                {
                    throw Fail($"{path}.AbilityKey", $"References unknown ability key '{abilityKey}'.");
                }
            }

            if (TryReadInt(orderObj, "AbilityId", out int authoredAbilityId))
            {
                if (authoredAbilityId <= 0)
                {
                    throw Fail($"{path}.AbilityId", "AbilityId must be positive.");
                }

                if (_validation == null || _validation.Abilities == null)
                {
                    throw Fail($"{path}.AbilityId", "AbilityId requires AiConfigValidationContext with AbilityDefinitionRegistry.");
                }

                if (!_validation.Abilities.TryGet(authoredAbilityId, out _))
                {
                    throw Fail($"{path}.AbilityId", $"References unknown ability id {authoredAbilityId}.");
                }

                if (abilityId > 0 && abilityId != authoredAbilityId)
                {
                    throw Fail(path, $"AbilityKey resolved to {abilityId}, but AbilityId is {authoredAbilityId}.");
                }

                abilityId = authoredAbilityId;
            }

            return abilityId;
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
            if (string.Equals(op, "IntToOrderI0", StringComparison.OrdinalIgnoreCase)) { result = ActionBindingOp.IntToOrderI0; return true; }
            if (string.Equals(op, "IntToOrderI1", StringComparison.OrdinalIgnoreCase)) { result = ActionBindingOp.IntToOrderI1; return true; }
            if (string.Equals(op, "IntToOrderI2", StringComparison.OrdinalIgnoreCase)) { result = ActionBindingOp.IntToOrderI2; return true; }
            if (string.Equals(op, "IntToOrderI3", StringComparison.OrdinalIgnoreCase)) { result = ActionBindingOp.IntToOrderI3; return true; }
            if (string.Equals(op, "EntityToTarget", StringComparison.OrdinalIgnoreCase)) { result = ActionBindingOp.EntityToTarget; return true; }
            if (string.Equals(op, "EntityToTargetContext", StringComparison.OrdinalIgnoreCase)) { result = ActionBindingOp.EntityToTargetContext; return true; }
            result = default;
            return false;
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
                if (v.TryGetValue(out string s) && int.TryParse(s, out int p)) { value = p; return true; }
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
                if (v.TryGetValue(out string s) && byte.TryParse(s, out byte p)) { value = p; return true; }
            }
            return byte.TryParse(node.ToString(), out value);
        }

        private static bool TryReadFloat(JsonObject obj, string key, out float value)
        {
            value = default;
            if (!obj.TryGetPropertyValue(key, out var node) || node == null) return false;
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out float f)) { value = f; return true; }
                if (v.TryGetValue(out double d)) { value = (float)d; return true; }
                if (v.TryGetValue(out string s) && float.TryParse(s, out float p)) { value = p; return true; }
            }
            return float.TryParse(node.ToString(), out value);
        }
    }
}

