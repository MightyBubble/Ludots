using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>对拍门：按报告顺序排列，每道门只记首个分歧。</summary>
public enum CrowdFuzzTraceGate
{
    OpSequence,
    OpParams,
    Identity,
    Structural,
    NavVersion,
    LeaderPath,
    PositionUnit,
    PositionMean,
}

/// <summary>一道门的首个分歧：tick / op 序号 / 单位句柄 / 指令 id / 字段 / 两端值 / 明细。</summary>
public sealed record CrowdFuzzTraceDivergence(
    CrowdFuzzTraceGate Gate, int Tick, int OpIndex, uint? Handle, int? OrderId,
    string Field, string Expected, string Actual, string Detail);

public sealed class CrowdFuzzTraceReport
{
    public required IReadOnlyList<CrowdFuzzTraceDivergence> Divergences { get; init; }
    public required int ComparedOps { get; init; }
    public required int ComparedTicks { get; init; }

    public bool Green => Divergences.Count == 0;

    public string Describe() =>
        Divergences.Count == 0
            ? $"对拍绿：{ComparedOps} op / {ComparedTicks} tick 全部门无分歧。"
            : string.Join(Environment.NewLine,
                $"对拍 {ComparedOps} op / {ComparedTicks} tick，{Divergences.Count} 道门有分歧：",
                string.Join(Environment.NewLine,
                    Divergences.Select(d =>
                        $"[{d.Gate}] tick {d.Tick} op {d.OpIndex}" +
                        (d.Handle is { } h ? $" 单位 {h}" : "") +
                        (d.OrderId is { } o ? $" 指令 {o}" : "") +
                        $" 字段 {d.Field}: Web {d.Expected} / C# {d.Actual}（{d.Detail}）")));
}

/// <summary>位置两道门与领队坐标口径（厘米）；op 参数米制带宽沿用 <see cref="CrowdWebFuzzRunner.PositionAgreementMeters"/>。</summary>
public sealed record CrowdFuzzTraceBands
{
    public const double DefaultPerUnitAxisCm = 1;
    public const double DefaultMeanCm = 4;
    public const double DefaultLeaderAxisCm = 1;

    public double PerUnitAxisCm { get; init; } = DefaultPerUnitAxisCm;
    public double MeanCm { get; init; } = DefaultMeanCm;
    public double LeaderAxisCm { get; init; } = DefaultLeaderAxisCm;
}

/// <summary>
/// 逐单位严格对拍：单位身份 = 句柄（18 位槽位 + 14 位代，两端同构；删除走 swap-remove，句柄不失效）。
/// 逐 tick 比较：身份集合（多/漏）、结构状态（state/group/player/level/order/mode 严格相等）、导航版本（严格）、
/// 领队路径（seg/done/点数严格，坐标逐轴带宽）、逐单位位置两道门——逐轴 |Δ|≤1cm 与全体单位逐轴绝对误差均值≤4cm
/// （均值分母 = 双端都在的单位数，不用有符号和）。unitChecksumHex 与 positionStatL1Cm 是诊断字段，不作门。
/// </summary>
public static class CrowdWebFuzzTraceCompare
{
    public static CrowdFuzzTraceReport Compare(
        IReadOnlyList<CrowdWebFuzzStepRecord> expectedSteps,
        IReadOnlyList<CrowdWebFuzzTickTrace> expectedTicks,
        IReadOnlyList<CrowdWebFuzzStepRecord> actualSteps,
        IReadOnlyList<CrowdWebFuzzTickTrace> actualTicks,
        CrowdFuzzTraceBands? bands = null)
    {
        ArgumentNullException.ThrowIfNull(expectedSteps);
        ArgumentNullException.ThrowIfNull(expectedTicks);
        ArgumentNullException.ThrowIfNull(actualSteps);
        ArgumentNullException.ThrowIfNull(actualTicks);
        bands ??= new CrowdFuzzTraceBands();

        int commonOps = Math.Min(expectedSteps.Count, actualSteps.Count);
        int commonTicks = Math.Min(expectedTicks.Count, actualTicks.Count);
        var divergences = new List<CrowdFuzzTraceDivergence>();

        AddIfNotNull(divergences, CompareOpSequence(expectedSteps, actualSteps, commonOps));
        AddIfNotNull(divergences, CompareOpParams(expectedSteps, actualSteps, commonOps));
        AddIfNotNull(divergences, CompareIdentity(expectedTicks, actualTicks, commonTicks));
        AddIfNotNull(divergences, CompareStructural(expectedTicks, actualTicks, commonTicks));
        AddIfNotNull(divergences, CompareNav(expectedTicks, actualTicks, commonTicks));
        AddIfNotNull(divergences, CompareLeader(expectedTicks, actualTicks, commonTicks, bands));
        AddIfNotNull(divergences, ComparePositionPerUnit(expectedTicks, actualTicks, commonTicks, bands));
        AddIfNotNull(divergences, ComparePositionMean(expectedTicks, actualTicks, commonTicks, bands));

        return new CrowdFuzzTraceReport
        {
            Divergences = divergences,
            ComparedOps = commonOps,
            ComparedTicks = commonTicks,
        };
    }

    private static void AddIfNotNull(List<CrowdFuzzTraceDivergence> list, CrowdFuzzTraceDivergence? divergence)
    {
        if (divergence != null) list.Add(divergence);
    }

    private static CrowdFuzzTraceDivergence? CompareOpSequence(
        IReadOnlyList<CrowdWebFuzzStepRecord> expected, IReadOnlyList<CrowdWebFuzzStepRecord> actual, int common)
    {
        if (expected.Count != actual.Count)
        {
            int shorter = Math.Min(expected.Count, actual.Count);
            return new CrowdFuzzTraceDivergence(
                CrowdFuzzTraceGate.OpSequence, shorter * 2, shorter + 1, null, null,
                "steps.length", expected.Count.ToString(CultureInfo.InvariantCulture),
                actual.Count.ToString(CultureInfo.InvariantCulture), "步数不一致");
        }

        for (int i = 0; i < common; i++)
        {
            int tick = (i + 1) * 2;
            var e = expected[i];
            var a = actual[i];
            if (e.Op.Kind != a.Op.Kind)
            {
                return Div(CrowdFuzzTraceGate.OpSequence, tick, i + 1, "op.kind", e.Op.Kind, a.Op.Kind, "op 种类不一致");
            }

            var ed = e.RngDraws;
            var ad = a.RngDraws;
            if (ed.Count != ad.Count)
            {
                return Div(CrowdFuzzTraceGate.OpSequence, tick, i + 1, "op.rngDraws.length",
                    ed.Count.ToString(CultureInfo.InvariantCulture), ad.Count.ToString(CultureInfo.InvariantCulture), "rng 抽取数不一致");
            }

            for (int d = 0; d < ed.Count; d++)
            {
                if (ed[d] != ad[d])
                {
                    return Div(CrowdFuzzTraceGate.OpSequence, tick, i + 1, $"op.rngDraws[{d}]",
                        ed[d].ToString(CultureInfo.InvariantCulture), ad[d].ToString(CultureInfo.InvariantCulture), "rng 原始值分叉");
                }
            }

            if (e.Units != a.Units)
            {
                return Div(CrowdFuzzTraceGate.OpSequence, tick, i + 1, "units",
                    e.Units.ToString(CultureInfo.InvariantCulture), a.Units.ToString(CultureInfo.InvariantCulture), "存活数不一致");
            }

            if (e.ValidateOk != a.ValidateOk)
            {
                return Div(CrowdFuzzTraceGate.OpSequence, tick, i + 1, "validateOk",
                    e.ValidateOk.ToString(), a.ValidateOk.ToString(), "校验结论不一致");
            }

            if (!e.ValidateOk && !SameErrors(e.ValidateErrors, a.ValidateErrors))
            {
                return Div(CrowdFuzzTraceGate.OpSequence, tick, i + 1, "validateErrors",
                    Join(e.ValidateErrors), Join(a.ValidateErrors), "校验错误不一致");
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence? CompareOpParams(
        IReadOnlyList<CrowdWebFuzzStepRecord> expected, IReadOnlyList<CrowdWebFuzzStepRecord> actual, int common)
    {
        for (int i = 0; i < common; i++)
        {
            var diffs = CrowdWebFuzzOpParamContract.Equal(expected[i].Op, actual[i].Op);
            if (diffs.Count > 0)
            {
                var first = diffs[0];
                return new CrowdFuzzTraceDivergence(
                    CrowdFuzzTraceGate.OpParams, (i + 1) * 2, i + 1, null, null,
                    $"op.params.{first.Path}", first.Expected, first.Actual, first.Reason);
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence? CompareIdentity(
        IReadOnlyList<CrowdWebFuzzTickTrace> expected, IReadOnlyList<CrowdWebFuzzTickTrace> actual, int common)
    {
        if (expected.Count != actual.Count)
        {
            int shorter = Math.Min(expected.Count, actual.Count);
            return new CrowdFuzzTraceDivergence(
                CrowdFuzzTraceGate.Identity, shorter + 1, (shorter >> 1) + 1, null, null,
                "ticks.length", expected.Count.ToString(CultureInfo.InvariantCulture),
                actual.Count.ToString(CultureInfo.InvariantCulture), "tick 条数不一致");
        }

        for (int i = 0; i < common; i++)
        {
            var e = expected[i];
            var a = actual[i];
            if (e.Tick != a.Tick)
            {
                return Div(CrowdFuzzTraceGate.Identity, e.Tick, a.OpIndex, "tick",
                    e.Tick.ToString(CultureInfo.InvariantCulture), a.Tick.ToString(CultureInfo.InvariantCulture), "tick 编号错位");
            }

            var actualHandles = new HashSet<uint>();
            for (int k = 0; k < a.Units.Count; k++) actualHandles.Add(a.Units[k].Handle);
            foreach (var eu in e.Units)
            {
                if (!actualHandles.Remove(eu.Handle))
                {
                    return new CrowdFuzzTraceDivergence(
                        CrowdFuzzTraceGate.Identity, e.Tick, e.OpIndex, eu.Handle, null,
                        "units.handle", eu.Handle.ToString(CultureInfo.InvariantCulture), "缺失", "C# 侧没有这个句柄");
                }
            }

            foreach (var au in a.Units)
            {
                if (actualHandles.Contains(au.Handle))
                {
                    return new CrowdFuzzTraceDivergence(
                        CrowdFuzzTraceGate.Identity, a.Tick, a.OpIndex, au.Handle, null,
                        "units.handle", "缺失", au.Handle.ToString(CultureInfo.InvariantCulture), "C# 侧多出这个句柄");
                }
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence? CompareStructural(
        IReadOnlyList<CrowdWebFuzzTickTrace> expected, IReadOnlyList<CrowdWebFuzzTickTrace> actual, int common)
    {
        for (int i = 0; i < common; i++)
        {
            var e = expected[i];
            var a = actual[i];
            var byHandle = new Dictionary<uint, CrowdWebFuzzUnitTick>(a.Units.Count);
            foreach (var au in a.Units) byHandle.Add(au.Handle, au);
            foreach (var eu in e.Units)
            {
                if (!byHandle.TryGetValue(eu.Handle, out var au)) continue;
                if (eu.State != au.State)
                {
                    return UnitDiv(e, eu, au, "state", eu.State, au.State);
                }

                if (eu.Group != au.Group)
                {
                    return UnitDiv(e, eu, au, "group", eu.Group, au.Group);
                }

                if (eu.Player != au.Player)
                {
                    return UnitDiv(e, eu, au, "player", eu.Player, au.Player);
                }

                if (eu.Level != au.Level)
                {
                    return UnitDiv(e, eu, au, "level", eu.Level, au.Level);
                }

                if (eu.Order != au.Order)
                {
                    return UnitDiv(e, eu, au, "order", eu.Order, au.Order);
                }

                if (eu.Mode != au.Mode)
                {
                    return UnitDiv(e, eu, au, "mode", eu.Mode, au.Mode);
                }
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence? CompareNav(
        IReadOnlyList<CrowdWebFuzzTickTrace> expected, IReadOnlyList<CrowdWebFuzzTickTrace> actual, int common)
    {
        for (int i = 0; i < common; i++)
        {
            var e = expected[i];
            var a = actual[i];
            int max = Math.Max(e.Navs.Count, a.Navs.Count);
            for (int k = 0; k < max; k++)
            {
                if (k >= e.Navs.Count || k >= a.Navs.Count || e.Navs[k].NavId != a.Navs[k].NavId)
                {
                    string eid = k < e.Navs.Count ? e.Navs[k].NavId.ToString(CultureInfo.InvariantCulture) : "缺失";
                    string aid = k < a.Navs.Count ? a.Navs[k].NavId.ToString(CultureInfo.InvariantCulture) : "缺失";
                    return new CrowdFuzzTraceDivergence(
                        CrowdFuzzTraceGate.NavVersion, e.Tick, e.OpIndex, null, null,
                        $"navs[{k}].navId", eid, aid, "导航 id 集合不一致");
                }

                if (e.Navs[k].Version != a.Navs[k].Version)
                {
                    return new CrowdFuzzTraceDivergence(
                        CrowdFuzzTraceGate.NavVersion, e.Tick, e.OpIndex, null, null,
                        $"navs[{k}].version",
                        e.Navs[k].Version.ToString(CultureInfo.InvariantCulture),
                        a.Navs[k].Version.ToString(CultureInfo.InvariantCulture),
                        "导航版本严格相等");
                }
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence? CompareLeader(
        IReadOnlyList<CrowdWebFuzzTickTrace> expected, IReadOnlyList<CrowdWebFuzzTickTrace> actual,
        int common, CrowdFuzzTraceBands bands)
    {
        for (int i = 0; i < common; i++)
        {
            var e = expected[i];
            var a = actual[i];
            var byKey = new Dictionary<(int OrderId, int Index), CrowdWebFuzzLeaderTick>(a.Leaders.Count);
            foreach (var al in a.Leaders) byKey.Add((al.OrderId, al.Index), al);
            foreach (var el in e.Leaders)
            {
                if (!byKey.TryGetValue((el.OrderId, el.Index), out var al))
                {
                    return new CrowdFuzzTraceDivergence(
                        CrowdFuzzTraceGate.LeaderPath, e.Tick, e.OpIndex, null, el.OrderId,
                        $"leaders[{el.OrderId}#{el.Index}]", "存在", "缺失", "C# 侧没有这个领队");
                }

                if (el.Seg != al.Seg)
                {
                    return LeaderDiv(e, el, al, "seg", el.Seg.ToString(CultureInfo.InvariantCulture), al.Seg.ToString(CultureInfo.InvariantCulture), "路点下标严格相等");
                }

                if (el.Done != al.Done)
                {
                    return LeaderDiv(e, el, al, "done", el.Done.ToString(), al.Done.ToString(), "完成位严格相等");
                }

                if (el.Path.Count != al.Path.Count)
                {
                    return LeaderDiv(e, el, al, "path.count", el.Path.Count.ToString(CultureInfo.InvariantCulture), al.Path.Count.ToString(CultureInfo.InvariantCulture), "路点数严格相等");
                }

                var axis = AxisOver(CrowdFuzzTraceGate.LeaderPath, e.Tick, e.OpIndex, null, el.OrderId, "leader.x", el.Xm, al.Xm, bands.LeaderAxisCm);
                if (axis != null) return axis;
                axis = AxisOver(CrowdFuzzTraceGate.LeaderPath, e.Tick, e.OpIndex, null, el.OrderId, "leader.y", el.Ym, al.Ym, bands.LeaderAxisCm);
                if (axis != null) return axis;
                for (int p = 0; p < el.Path.Count; p++)
                {
                    axis = AxisOver(CrowdFuzzTraceGate.LeaderPath, e.Tick, e.OpIndex, null, el.OrderId, $"leader.path[{p}].x", el.Path[p].Xm, al.Path[p].Xm, bands.LeaderAxisCm);
                    if (axis != null) return axis;
                    axis = AxisOver(CrowdFuzzTraceGate.LeaderPath, e.Tick, e.OpIndex, null, el.OrderId, $"leader.path[{p}].y", el.Path[p].Ym, al.Path[p].Ym, bands.LeaderAxisCm);
                    if (axis != null) return axis;
                }
            }

            var expectedKeys = new HashSet<(int, int)>(e.Leaders.Count);
            foreach (var el in e.Leaders) expectedKeys.Add((el.OrderId, el.Index));
            foreach (var al in a.Leaders)
            {
                if (!expectedKeys.Contains((al.OrderId, al.Index)))
                {
                    return new CrowdFuzzTraceDivergence(
                        CrowdFuzzTraceGate.LeaderPath, a.Tick, a.OpIndex, null, al.OrderId,
                        $"leaders[{al.OrderId}#{al.Index}]", "缺失", "存在", "C# 侧多出这个领队");
                }
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence? ComparePositionPerUnit(
        IReadOnlyList<CrowdWebFuzzTickTrace> expected, IReadOnlyList<CrowdWebFuzzTickTrace> actual,
        int common, CrowdFuzzTraceBands bands)
    {
        for (int i = 0; i < common; i++)
        {
            var e = expected[i];
            var a = actual[i];
            var byHandle = new Dictionary<uint, CrowdWebFuzzUnitTick>(a.Units.Count);
            foreach (var au in a.Units) byHandle.Add(au.Handle, au);
            foreach (var eu in e.Units)
            {
                if (!byHandle.TryGetValue(eu.Handle, out var au)) continue;
                var axis = AxisOver(CrowdFuzzTraceGate.PositionUnit, e.Tick, e.OpIndex, eu.Handle, null, "units.x", eu.Xm, au.Xm, bands.PerUnitAxisCm);
                if (axis != null) return axis;
                axis = AxisOver(CrowdFuzzTraceGate.PositionUnit, e.Tick, e.OpIndex, eu.Handle, null, "units.y", eu.Ym, au.Ym, bands.PerUnitAxisCm);
                if (axis != null) return axis;
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence? ComparePositionMean(
        IReadOnlyList<CrowdWebFuzzTickTrace> expected, IReadOnlyList<CrowdWebFuzzTickTrace> actual,
        int common, CrowdFuzzTraceBands bands)
    {
        const double cmPerMeter = CrowdReferenceMeters.CentimetersPerMeter;
        for (int i = 0; i < common; i++)
        {
            var e = expected[i];
            var a = actual[i];
            var byHandle = new Dictionary<uint, CrowdWebFuzzUnitTick>(a.Units.Count);
            foreach (var au in a.Units) byHandle.Add(au.Handle, au);
            double sumAbsDx = 0, sumAbsDy = 0;
            int matched = 0;
            foreach (var eu in e.Units)
            {
                if (!byHandle.TryGetValue(eu.Handle, out var au)) continue;
                matched++;
                sumAbsDx += Math.Abs(au.Xm - eu.Xm);
                sumAbsDy += Math.Abs(au.Ym - eu.Ym);
            }

            if (matched == 0) continue;
            double meanDxCm = sumAbsDx / matched * cmPerMeter;
            double meanDyCm = sumAbsDy / matched * cmPerMeter;
            if (meanDxCm > bands.MeanCm)
            {
                return MeanDiv(e, "units.x.mean", meanDxCm, matched, bands.MeanCm);
            }

            if (meanDyCm > bands.MeanCm)
            {
                return MeanDiv(e, "units.y.mean", meanDyCm, matched, bands.MeanCm);
            }
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence AxisOver(
        CrowdFuzzTraceGate gate, int tick, int op, uint? handle, int? orderId,
        string field, double expectedM, double actualM, double bandCm)
    {
        const double cmPerMeter = CrowdReferenceMeters.CentimetersPerMeter;
        double deltaCm = Math.Abs(actualM - expectedM) * cmPerMeter;
        if (deltaCm > bandCm)
        {
            return new CrowdFuzzTraceDivergence(
                gate, tick, op, handle, orderId,
                field,
                expectedM.ToString("R", CultureInfo.InvariantCulture),
                actualM.ToString("R", CultureInfo.InvariantCulture),
                $"|Δ|={deltaCm.ToString("F4", CultureInfo.InvariantCulture)}cm 上限 {bandCm.ToString(CultureInfo.InvariantCulture)}cm");
        }

        return null;
    }

    private static CrowdFuzzTraceDivergence UnitDiv<T>(
        CrowdWebFuzzTickTrace tick, CrowdWebFuzzUnitTick expected, CrowdWebFuzzUnitTick actual,
        string field, T expectedValue, T actualValue)
        => new(CrowdFuzzTraceGate.Structural, tick.Tick, tick.OpIndex, expected.Handle, null,
            $"units.{field}",
            expectedValue?.ToString() ?? "null", actualValue?.ToString() ?? "null",
            "结构字段严格相等");

    private static CrowdFuzzTraceDivergence LeaderDiv(
        CrowdWebFuzzTickTrace tick, CrowdWebFuzzLeaderTick expected, CrowdWebFuzzLeaderTick actual,
        string field, string expectedValue, string actualValue, string reason)
        => new(CrowdFuzzTraceGate.LeaderPath, tick.Tick, tick.OpIndex, null, expected.OrderId,
            $"leaders[{expected.OrderId}#{expected.Index}].{field}",
            expectedValue, actualValue, reason);

    private static CrowdFuzzTraceDivergence MeanDiv(
        CrowdWebFuzzTickTrace tick, string field, double meanCm, int matched, double bandCm)
        => new(CrowdFuzzTraceGate.PositionMean, tick.Tick, tick.OpIndex, null, null,
            field,
            "≤ " + bandCm.ToString(CultureInfo.InvariantCulture) + "cm",
            meanCm.ToString("F4", CultureInfo.InvariantCulture) + "cm",
            $"均值分母 = 双端都在的 {matched} 个单位，逐轴绝对误差的算术平均");

    private static CrowdFuzzTraceDivergence Div(
        CrowdFuzzTraceGate gate, int tick, int op, string field, string expected, string actual, string reason)
        => new(gate, tick, op, null, null, field, expected, actual, reason);

    private static bool SameErrors(IReadOnlyList<string>? expected, IReadOnlyList<string>? actual)
    {
        if (expected is not { Count: > 0 } && actual is not { Count: > 0 }) return true;
        if (expected?.Count != actual?.Count) return false;
        for (int i = 0; i < expected!.Count; i++)
        {
            if (expected[i] != actual![i]) return false;
        }

        return true;
    }

    private static string Join(IReadOnlyList<string>? errors)
        => errors is { Count: > 0 } ? string.Join("; ", errors) : "无";
}

/// <summary>op 参数的一处不一致：完整字段路径 + 两端 JSON 文本 + 原因。</summary>
public sealed record CrowdWebFuzzOpParamDiff(string Path, string Expected, string Actual, string Reason);

/// <summary>
/// op 参数的容差合同：按 op 种类 + 完整字段路径逐字段定规则（米制坐标走 1 厘米带宽，其余严格相等），
/// 不做任意深度的字段名放行；合同外的字段一律报错。
/// </summary>
public static class CrowdWebFuzzOpParamContract
{
    private enum Rule
    {
        StringExact,
        IntegralExact,
        BoolExact,
        Meter,
        /// <summary>合同内登记但由调用侧专用代码比较（explode.probe 的探针行）。</summary>
        Skip,
    }

    public static IReadOnlyList<CrowdWebFuzzOpParamDiff> Equal(CrowdWebFuzzOpRecord expected, CrowdWebFuzzOpRecord actual)
    {
        var diffs = new List<CrowdWebFuzzOpParamDiff>();
        string kind = expected.Kind;
        if (kind is not ("structure" or "explode" or "spawn" or "order" or "error"))
        {
            diffs.Add(new CrowdWebFuzzOpParamDiff("kind", kind, actual.Kind, "未知 op 种类"));
            return diffs;
        }

        var e = expected.Params ?? new JsonObject();
        var a = actual.Params ?? new JsonObject();

        if (kind == "structure")
        {
            string action = e["action"]?.AsValue().TryGetValue<string>(out var s) == true ? s : "";
            if (action is not ("place" or "remove"))
            {
                diffs.Add(new CrowdWebFuzzOpParamDiff("action", action, "<place|remove>", "structure 需点明 place/remove"));
                return diffs;
            }

            CollectFields(e, a, StructureFields(action), diffs);
            if (action == "place")
            {
                CompareToArray(e["to"], a["to"], "to", diffs);
            }

            return diffs;
        }

        if (kind == "explode")
        {
            CollectFields(e, a, ExplodeFields, diffs);
            var ep = e["probe"] as JsonArray;
            var ap = a["probe"] as JsonArray;
            if (ep == null || ap == null)
            {
                diffs.Add(new CrowdWebFuzzOpParamDiff("probe", ep == null ? "缺失" : "存在", ap == null ? "缺失" : "存在", "explode 需要 probe 数组"));
                return diffs;
            }

            if (ep.Count != ap.Count)
            {
                diffs.Add(new CrowdWebFuzzOpParamDiff("probe.length",
                    ep.Count.ToString(CultureInfo.InvariantCulture), ap.Count.ToString(CultureInfo.InvariantCulture), "探针条数不一致"));
                return diffs;
            }

            for (int i = 0; i < ep.Count; i++)
            {
                if (ep[i] is not JsonArray er || er.Count != 2 || ap[i] is not JsonArray ar || ar.Count != 2)
                {
                    diffs.Add(new CrowdWebFuzzOpParamDiff($"probe[{i}]", "2 元数组", "2 元数组", "探针行形状不对"));
                    return diffs;
                }

                CompareValue(er[0], ar[0], Rule.IntegralExact, $"probe[{i}][0]", diffs);
                CompareValue(er[1], ar[1], Rule.BoolExact, $"probe[{i}][1]", diffs);
            }

            return diffs;
        }

        CollectFields(e, a, FieldsFor(kind), diffs);
        return diffs;
    }

    private static (string Key, Rule Rule)[] StructureFields(string action)
        => action == "place"
            ? new (string, Rule)[] { ("action", Rule.StringExact), ("template", Rule.StringExact), ("x", Rule.Meter), ("y", Rule.Meter), ("size", Rule.Meter), ("to", Rule.Skip) }
            : new (string, Rule)[] { ("action", Rule.StringExact), ("x", Rule.Meter), ("y", Rule.Meter) };

    private static (string Key, Rule Rule)[] ExplodeFields
        => new (string, Rule)[] { ("x", Rule.Meter), ("y", Rule.Meter), ("radius", Rule.Meter), ("probe", Rule.Skip) };

    private static (string Key, Rule Rule)[] FieldsFor(string kind) => kind switch
    {
        "spawn" => new (string, Rule)[]
        {
            ("x", Rule.Meter), ("y", Rule.Meter), ("count", Rule.IntegralExact),
            ("player", Rule.IntegralExact), ("unitType", Rule.IntegralExact), ("rIdx", Rule.IntegralExact),
        },
        "order" => new (string, Rule)[]
        {
            ("x", Rule.Meter), ("y", Rule.Meter), ("player", Rule.IntegralExact),
            ("select", Rule.BoolExact), ("ax", Rule.Meter), ("ay", Rule.Meter), ("drag", Rule.BoolExact),
        },
        "error" => new (string, Rule)[] { ("message", Rule.StringExact) },
        _ => Array.Empty<(string, Rule)>(),
    };

    private static void CollectFields(JsonObject e, JsonObject a, (string Key, Rule Rule)[] fields, List<CrowdWebFuzzOpParamDiff> diffs)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in fields) known.Add(key);
        foreach (var kv in e)
        {
            if (!known.Contains(kv.Key))
            {
                diffs.Add(new CrowdWebFuzzOpParamDiff(kv.Key, "合同内字段", "未知字段", "未知字段不在 op 合同里；加字段必须同步本合同"));
            }
        }

        foreach (var kv in a)
        {
            if (!known.Contains(kv.Key))
            {
                diffs.Add(new CrowdWebFuzzOpParamDiff(kv.Key, "未知字段", "合同外字段", "未知字段不在 op 合同里；加字段必须同步本合同"));
            }
        }

        foreach (var (key, rule) in fields)
        {
            if (rule == Rule.Skip) continue;
            var ev = e[key];
            var av = a[key];
            if (ev == null || av == null)
            {
                diffs.Add(new CrowdWebFuzzOpParamDiff(key, ev == null ? "缺失" : "存在", av == null ? "缺失" : "存在", "字段缺失"));
                continue;
            }

            CompareValue(ev, av, rule, key, diffs);
        }
    }

    private static void CompareToArray(JsonNode? e, JsonNode? a, string path, List<CrowdWebFuzzOpParamDiff> diffs)
    {
        if (e is not JsonArray ea || a is not JsonArray aa)
        {
            diffs.Add(new CrowdWebFuzzOpParamDiff(path, e is JsonArray ? "数组" : "非数组", a is JsonArray ? "数组" : "非数组", $"{path} 需为 2 元数组"));
            return;
        }

        if (ea.Count != 2 || aa.Count != 2)
        {
            diffs.Add(new CrowdWebFuzzOpParamDiff($"{path}.length",
                ea.Count.ToString(CultureInfo.InvariantCulture), aa.Count.ToString(CultureInfo.InvariantCulture), $"{path} 需为 2 元"));
            return;
        }

        for (int i = 0; i < 2; i++)
        {
            CompareValue(ea[i], aa[i], Rule.Meter, $"{path}[{i}]", diffs);
        }
    }

    private static void CompareValue(JsonNode e, JsonNode a, Rule rule, string path, List<CrowdWebFuzzOpParamDiff> diffs)
    {
        switch (rule)
        {
            case Rule.StringExact:
                if (!TryString(e, out string? es) || !TryString(a, out string? as_)
                    || !string.Equals(es, as_, StringComparison.Ordinal))
                {
                    diffs.Add(new CrowdWebFuzzOpParamDiff(path, e.ToJsonString(), a.ToJsonString(), "字符串严格相等"));
                }

                break;
            case Rule.BoolExact:
                if (!TryBool(e, out bool eb) || !TryBool(a, out bool ab) || eb != ab)
                {
                    diffs.Add(new CrowdWebFuzzOpParamDiff(path, e.ToJsonString(), a.ToJsonString(), "布尔严格相等"));
                }

                break;
            case Rule.IntegralExact:
                if (!TryIntegral(e, out long el) || !TryIntegral(a, out long al) || el != al)
                {
                    diffs.Add(new CrowdWebFuzzOpParamDiff(path, e.ToJsonString(), a.ToJsonString(), "整数严格相等"));
                }

                break;
            case Rule.Meter:
                if (!TryNumber(e, out double ed) || !TryNumber(a, out double ad)
                    || Math.Abs(ad - ed) > CrowdWebFuzzRunner.PositionAgreementMeters)
                {
                    diffs.Add(new CrowdWebFuzzOpParamDiff(path, e.ToJsonString(), a.ToJsonString(),
                        $"米制坐标带宽 {CrowdWebFuzzRunner.PositionAgreementMeters.ToString(CultureInfo.InvariantCulture)} 米"));
                }

                break;
        }
    }

    private static bool TryString(JsonNode node, out string? text)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out text)) return true;
        text = null;
        return false;
    }

    private static bool TryBool(JsonNode node, out bool flag)
    {
        if (node is JsonValue v && v.TryGetValue<bool>(out flag)) return true;
        flag = false;
        return false;
    }

    private static bool TryIntegral(JsonNode node, out long value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        if (v.TryGetValue<long>(out value)) return true;
        if (v.TryGetValue<int>(out int i)) { value = i; return true; }
        if (v.TryGetValue<uint>(out uint u)) { value = u; return true; }
        if (v.TryGetValue<ulong>(out ulong ul) && ul <= long.MaxValue) { value = (long)ul; return true; }
        // 新建的 JsonValue 只对创建时的 CLR 类型返回真；double 兜底覆盖 261.0 这类整值数。
        if (v.TryGetValue<double>(out double d) && Math.Floor(d) == d && Math.Abs(d) <= long.MaxValue)
        {
            value = (long)d;
            return true;
        }

        return false;
    }

    private static bool TryNumber(JsonNode node, out double value)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out value)) return IsFinite(value);
            if (v.TryGetValue<float>(out float f)) { value = f; return IsFinite(value); }
            if (v.TryGetValue<long>(out long l)) { value = l; return true; }
            if (v.TryGetValue<int>(out int i)) { value = i; return true; }
        }

        value = 0;
        return false;
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
