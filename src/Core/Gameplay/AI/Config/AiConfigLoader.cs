using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly AtomRegistry _atoms;

        public AiConfigLoader(ConfigPipeline pipeline, AtomRegistry atoms)
        {
            _pipeline = pipeline;
            _atoms = atoms;
        }

        public AiCompiledRuntime LoadAndCompile(ConfigCatalog catalog, ConfigConflictReport? report = null)
        {
            var atomEntry = GetEntry(catalog, "AI/atoms.json", ConfigMergePolicy.ArrayById, idField: "Id");
            var atomNode = Merge(in atomEntry, report);
            if (atomNode is JsonArray atomsArr)
            {
                for (int i = 0; i < atomsArr.Count; i++)
                {
                    if (atomsArr[i] is not JsonObject obj) continue;
                    if (!TryReadString(obj, "Id", out string id)) continue;
                    _atoms.GetOrAdd(id);
                }
            }

            var projectionRules = Array.Empty<WorldStateProjectionRule>();
            var projectionEntry = GetEntry(catalog, "AI/projection.json", ConfigMergePolicy.ArrayById, idField: "Id");
            var projectionNode = Merge(in projectionEntry, report);
            if (projectionNode is JsonArray projArr)
            {
                var tmp = new List<WorldStateProjectionRule>(projArr.Count);
                for (int i = 0; i < projArr.Count; i++)
                {
                    if (projArr[i] is not JsonObject obj) continue;
                    if (!TryReadString(obj, "Atom", out string atomName)) continue;
                    int atomId = _atoms.GetOrAdd(atomName);
                    if (!TryReadString(obj, "Op", out string op)) continue;
                    if (!TryParseProjectionOp(op, out var pOp)) continue;

                    int intKey = TryReadInt(obj, "IntKey", out int ik) ? ik : -1;
                    int intValue = TryReadInt(obj, "IntValue", out int iv) ? iv : 0;
                    int entityKey = TryReadInt(obj, "EntityKey", out int ek) ? ek : -1;

                    tmp.Add(new WorldStateProjectionRule(atomId, pOp, intKey, intValue, entityKey));
                }
                projectionRules = tmp.ToArray();
            }

            var projectionTable = new WorldStateProjectionTable(projectionRules, atomCapacity: _atoms.Capacity);

            var goalDefs = Array.Empty<UtilityGoalPresetDefinition>();
            var utilityEntry = GetEntry(catalog, "AI/utility.json", ConfigMergePolicy.ArrayById, idField: "Id");
            var utilityNode = Merge(in utilityEntry, report);
            if (utilityNode is JsonArray goalsArr)
            {
                var tmp = new List<UtilityGoalPresetDefinition>(goalsArr.Count);
                for (int i = 0; i < goalsArr.Count; i++)
                {
                    if (goalsArr[i] is not JsonObject obj) continue;

                    int goalPresetId = TryReadInt(obj, "GoalPresetId", out int gpid) ? gpid : 0;
                    int planningStrategyId = TryReadInt(obj, "PlanningStrategyId", out int psid) ? psid : 0;
                    float weight = TryReadFloat(obj, "Weight", out float w) ? w : 1f;

                    var boolCons = Array.Empty<UtilityConsiderationBool256>();
                    if (obj.TryGetPropertyValue("Bool", out var boolNode) && boolNode is JsonArray boolArr)
                    {
                        var bc = new List<UtilityConsiderationBool256>(boolArr.Count);
                        for (int c = 0; c < boolArr.Count; c++)
                        {
                            if (boolArr[c] is not JsonObject bObj) continue;
                            if (!TryReadString(bObj, "Atom", out string atomName)) continue;
                            int atomId = _atoms.GetOrAdd(atomName);
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
            var goapEntry = GetEntry(catalog, "AI/goap_actions.json", ConfigMergePolicy.ArrayById, idField: "Id");
            var goapNode = Merge(in goapEntry, report);
            if (goapNode is JsonArray actArr)
            {
                var tmp = new List<ActionOpDefinition256>(actArr.Count);
                for (int i = 0; i < actArr.Count; i++)
                {
                    if (actArr[i] is not JsonObject obj) continue;
                    int cost = TryReadInt(obj, "Cost", out int c) ? c : 1;

                    var pre = ReadCondition(obj, "Pre", _atoms);
                    var post = ReadCondition(obj, "Post", _atoms);

                    var orderSpec = default(ActionOrderSpec);
                    var execKind = ActionExecutorKind.SubmitOrder;
                    if (obj.TryGetPropertyValue("Order", out var orderNode) && orderNode is JsonObject orderObj)
                    {
                        int orderTagId = TryReadInt(orderObj, "OrderTagId", out int ot) ? ot : 0;
                        byte submitModeByte = TryReadByte(orderObj, "SubmitMode", out byte sm) ? sm : (byte)OrderSubmitMode.Immediate;
                        int playerId = TryReadInt(orderObj, "PlayerId", out int pid) ? pid : 0;
                        orderSpec = new ActionOrderSpec(orderTagId, (OrderSubmitMode)submitModeByte, playerId);
                    }

                    var bindings = Array.Empty<ActionBinding>();
                    if (obj.TryGetPropertyValue("Bindings", out var bindNode) && bindNode is JsonArray bindArr)
                    {
                        var btmp = new List<ActionBinding>(bindArr.Count);
                        for (int b = 0; b < bindArr.Count; b++)
                        {
                            if (bindArr[b] is not JsonObject bObj) continue;
                            if (!TryReadString(bObj, "Op", out string op)) continue;
                            if (!TryParseBindingOp(op, out var bop)) continue;
                            if (!TryReadInt(bObj, "SourceKey", out int sk)) continue;
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
            var goapGoalEntry = GetEntry(catalog, "AI/goap_goals.json", ConfigMergePolicy.ArrayById, idField: "Id");
            var goapGoalNode = Merge(in goapGoalEntry, report);
            if (goapGoalNode is JsonArray gArr)
            {
                var tmp = new List<GoapGoalPreset256>(gArr.Count);
                for (int i = 0; i < gArr.Count; i++)
                {
                    if (gArr[i] is not JsonObject obj) continue;
                    int goalPresetId = TryReadInt(obj, "GoalPresetId", out int gpid) ? gpid : 0;
                    int hw = TryReadInt(obj, "HeuristicWeight", out int hwi) ? hwi : 1;
                    var cond = ReadCondition(obj, "Goal", _atoms);
                    var goalCond = new WorldStateCondition256(in cond.Mask, in cond.Values);
                    tmp.Add(new GoapGoalPreset256(goalPresetId, in goalCond, hw));
                }
                goapGoals = tmp.ToArray();
            }

            var goapGoalTable = new GoapGoalTable256(goapGoals);

            var htnDomain = new HtnDomainCompiled256(Array.Empty<HtnCompoundTask>(), Array.Empty<HtnMethod256>(), Array.Empty<HtnSubtask>());
            var htnRoots = new HtnRootTable(Array.Empty<(int GoalPresetId, int RootTaskId)>());

            var htnEntry = GetEntry(catalog, "AI/htn_domain.json", ConfigMergePolicy.DeepObject, idField: "Id");
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
                        if (tArr[i] is not JsonObject o) continue;
                        if (!TryReadInt(o, "TaskId", out int tid)) continue;
                        int fm = TryReadInt(o, "FirstMethod", out int x) ? x : 0;
                        int mc = TryReadInt(o, "MethodCount", out int y) ? y : 0;
                        if ((uint)tid < (uint)tasks.Length) tasks[tid] = new HtnCompoundTask(fm, mc);
                    }

                    var methods = new HtnMethod256[mArr.Count];
                    for (int i = 0; i < mArr.Count; i++)
                    {
                        if (mArr[i] is not JsonObject o) continue;
                        if (!TryReadInt(o, "MethodId", out int mid)) continue;
                        int cost = TryReadInt(o, "Cost", out int cc) ? cc : 0;
                        int off = TryReadInt(o, "SubtaskOffset", out int so) ? so : 0;
                        int cnt = TryReadInt(o, "SubtaskCount", out int sc) ? sc : 0;
                        var cond = ReadCondition(o, "Condition", _atoms);
                        var cnd = new WorldStateCondition256(in cond.Mask, in cond.Values);
                        if ((uint)mid < (uint)methods.Length) methods[mid] = new HtnMethod256(in cnd, off, cnt, cost);
                    }

                    var subtasks = new HtnSubtask[sArr.Count];
                    for (int i = 0; i < sArr.Count; i++)
                    {
                        if (sArr[i] is not JsonObject o) continue;
                        if (!TryReadInt(o, "Index", out int idx)) continue;
                        if (!TryReadString(o, "Kind", out string kind)) continue;
                        if (!TryReadInt(o, "RefId", out int rid)) continue;
                        var k = string.Equals(kind, "Compound", StringComparison.OrdinalIgnoreCase) ? HtnSubtaskKind.Compound : HtnSubtaskKind.Action;
                        if ((uint)idx < (uint)subtasks.Length) subtasks[idx] = new HtnSubtask(k, rid);
                    }

                    htnDomain = new HtnDomainCompiled256(tasks, methods, subtasks);
                }

                if (htnObj.TryGetPropertyValue("Roots", out var rNode) && rNode is JsonArray rArr)
                {
                    var roots = new (int GoalPresetId, int RootTaskId)[rArr.Count];
                    int count = 0;
                    for (int i = 0; i < rArr.Count; i++)
                    {
                        if (rArr[i] is not JsonObject o) continue;
                        if (!TryReadInt(o, "GoalPresetId", out int gpid)) continue;
                        if (!TryReadInt(o, "RootTaskId", out int rid)) continue;
                        roots[count++] = (gpid, rid);
                    }
                    if (count != roots.Length) Array.Resize(ref roots, count);
                    htnRoots = new HtnRootTable(roots);
                }
            }

            return new AiCompiledRuntime(_atoms, projectionTable, goalSelector, actionLibrary, goapGoalTable, htnDomain, htnRoots);
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

        private static (WorldStateBits256 Mask, WorldStateBits256 Values) ReadCondition(JsonObject obj, string propertyName, AtomRegistry atoms)
        {
            var mask = new WorldStateBits256();
            var values = new WorldStateBits256();

            if (!obj.TryGetPropertyValue(propertyName, out var n) || n is not JsonObject c) return (mask, values);

            if (c.TryGetPropertyValue("Mask", out var mNode) && mNode is JsonArray maskArr)
            {
                for (int i = 0; i < maskArr.Count; i++)
                {
                    if (maskArr[i] == null) continue;
                    int id = atoms.GetOrAdd(maskArr[i]!.ToString());
                    mask.SetBit(id, true);
                }
            }

            if (c.TryGetPropertyValue("Values", out var vNode) && vNode is JsonArray valArr)
            {
                for (int i = 0; i < valArr.Count; i++)
                {
                    if (valArr[i] == null) continue;
                    int id = atoms.GetOrAdd(valArr[i]!.ToString());
                    values.SetBit(id, true);
                }
            }

            return (mask, values);
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
