using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>逐 tick 逐单位快照行：句柄为稳定身份，位置米制（Web f32 位型），其余为结构字段。</summary>
public sealed record CrowdWebFuzzUnitTick(
    uint Handle, double Xm, double Ym, byte State, int Group, byte Player, byte Level, uint Order, byte Mode);

public sealed record CrowdWebFuzzNavTick(int NavId, int Version);

public sealed record CrowdWebFuzzPathPoint(double Xm, double Ym);

/// <summary>指令簿里的领队：同指令多领队按 Index 对齐；路径点为米制坐标。</summary>
public sealed record CrowdWebFuzzLeaderTick(
    int OrderId, int Index, double Xm, double Ym, int Seg, bool Done, IReadOnlyList<CrowdWebFuzzPathPoint> Path);

public sealed record CrowdWebFuzzTickTrace(
    int Tick, int OpIndex,
    IReadOnlyList<CrowdWebFuzzUnitTick> Units,
    IReadOnlyList<CrowdWebFuzzNavTick> Navs,
    IReadOnlyList<CrowdWebFuzzLeaderTick> Leaders);

/// <summary>
/// crowd-fuzz-trace-v1 夹具：Web <c>scripts/exportFuzzTrace.mjs</c> 导出的逐 tick trace。
/// 解析合同严格：schema 版本不符、未知字段、null/类型错、条目形状不对都报错；
/// <see cref="ContentSha256"/> 由导出端计算（steps+ticks 载荷），装载方不复算，只透传给验收记录。
/// </summary>
public sealed class CrowdWebFuzzTraceFixture
{
    public const string SchemaV1 = "crowd-fuzz-trace-v1";

    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
    {
        "paritySchema", "seed", "opCount", "worldSeed", "tickHz", "blast",
        "templateOrder", "formationOrder", "initialStructures",
        "generatedBy", "generationCommand", "contentSha256", "steps", "ticks",
    };

    private static readonly HashSet<string> StepFields = new(StringComparer.Ordinal)
    {
        "rngDraws", "op", "units", "validateOk", "validateErrors", "unitChecksumHex", "positionStatL1Cm",
    };

    private static readonly HashSet<string> TickFields = new(StringComparer.Ordinal)
    {
        "tick", "op", "units", "navs", "leaders",
    };

    public required string ParitySchema { get; init; }
    public required long Seed { get; init; }
    public required int OpCount { get; init; }
    public required int WorldSeed { get; init; }
    public required int TickHz { get; init; }
    public required CrowdWebFuzzBlast Blast { get; init; }
    public required CrowdWebFuzzPalette Palette { get; init; }
    public required string GeneratedBy { get; init; }
    public required string GenerationCommand { get; init; }
    public required string ContentSha256 { get; init; }
    public required IReadOnlyList<CrowdWebFuzzStepRecord> Steps { get; init; }
    public required IReadOnlyList<CrowdWebFuzzTickTrace> Ticks { get; init; }

    public static CrowdWebFuzzTraceFixture Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("trace 夹具需为 JSON 对象。");
        CrowdWebFuzzFixtureJson.RejectUnknownFields(root, RootFields, "trace 夹具");
        string schema = CrowdWebFuzzFixtureJson.RequireString(root, "paritySchema");
        if (schema != SchemaV1)
        {
            throw new InvalidOperationException($"trace 夹具 paritySchema 需为 {SchemaV1}，实际是 {schema}；schema 演进时装载器与导出器必须同步升版。");
        }

        var palette = new CrowdWebFuzzPalette
        {
            TemplateIds = CrowdWebFuzzFixtureJson.RequireIdList(root, "templateOrder"),
            FormationIds = CrowdWebFuzzFixtureJson.RequireIdList(root, "formationOrder"),
            InitialStructureAnchors = CrowdWebFuzzFixtureJson.RequirePointList(root, "initialStructures"),
        };

        long seed = CrowdWebFuzzFixtureJson.RequireLong(root, "seed");
        int opCount = CrowdWebFuzzFixtureJson.RequireInt(root, "opCount", 1, null);
        int worldSeed = CrowdWebFuzzFixtureJson.RequireInt(root, "worldSeed", 0, null);
        int tickHz = CrowdWebFuzzFixtureJson.RequireInt(root, "tickHz", 1, null);
        var blastNode = root["blast"] as JsonObject
            ?? throw new InvalidOperationException("trace 夹具缺少 blast。");
        CrowdWebFuzzFixtureJson.RejectUnknownFields(blastNode, new HashSet<string> { "ringScale", "impulse" }, "trace 夹具 blast");
        var blast = new CrowdWebFuzzBlast(
            Ludots.Core.Mathematics.FixedPoint.Fix64.FromDouble(CrowdWebFuzzFixtureJson.RequireDouble(blastNode, "ringScale")),
            Ludots.Core.Mathematics.FixedPoint.Fix64.FromDouble(CrowdWebFuzzFixtureJson.RequireDouble(blastNode, "impulse")));

        var stepsNode = root["steps"] as JsonArray
            ?? throw new InvalidOperationException("trace 夹具缺少 steps。");
        if (stepsNode.Count != opCount)
        {
            throw new InvalidOperationException($"trace 夹具 steps 长度 {stepsNode.Count} 与 opCount {opCount} 不一致。");
        }

        var steps = new List<CrowdWebFuzzStepRecord>(stepsNode.Count);
        for (int i = 0; i < stepsNode.Count; i++)
        {
            var row = stepsNode[i] as JsonObject
                ?? throw new InvalidOperationException($"trace 夹具 steps[{i}] 需为对象。");
            CrowdWebFuzzFixtureJson.RejectUnknownFields(row, StepFields, $"trace 夹具 steps[{i}]");
            steps.Add(CrowdWebFuzzStepParse.ParseStep(row, $"trace 夹具 steps[{i}]"));
        }

        var ticksNode = root["ticks"] as JsonArray
            ?? throw new InvalidOperationException("trace 夹具缺少 ticks。");
        if (ticksNode.Count != opCount * 2)
        {
            throw new InvalidOperationException($"trace 夹具 ticks 长度 {ticksNode.Count} 与 opCount {opCount}×2 不一致。");
        }

        var ticks = new List<CrowdWebFuzzTickTrace>(ticksNode.Count);
        for (int i = 0; i < ticksNode.Count; i++)
        {
            ticks.Add(ParseTick(ticksNode[i], i));
        }

        return new CrowdWebFuzzTraceFixture
        {
            ParitySchema = schema,
            Seed = seed,
            OpCount = opCount,
            WorldSeed = worldSeed,
            TickHz = tickHz,
            Blast = blast,
            Palette = palette,
            GeneratedBy = CrowdWebFuzzFixtureJson.RequireString(root, "generatedBy"),
            GenerationCommand = CrowdWebFuzzFixtureJson.RequireString(root, "generationCommand"),
            ContentSha256 = CrowdWebFuzzFixtureJson.RequireHex64(root, "contentSha256"),
            Steps = steps,
            Ticks = ticks,
        };
    }

    private static CrowdWebFuzzTickTrace ParseTick(JsonNode? node, int index)
    {
        string where = $"trace 夹具 ticks[{index}]";
        var row = node as JsonObject
            ?? throw new InvalidOperationException($"{where} 需为对象。");
        CrowdWebFuzzFixtureJson.RejectUnknownFields(row, TickFields, where);
        int tick = CrowdWebFuzzFixtureJson.RequireInt(row, "tick", 1, null);
        if (tick != index + 1)
        {
            throw new InvalidOperationException($"{where}.tick 需为 {index + 1}（连续编号），实际是 {tick}。");
        }

        int op = CrowdWebFuzzFixtureJson.RequireInt(row, "op", 1, null);
        int expectedOp = (index >> 1) + 1;
        if (op != expectedOp)
        {
            throw new InvalidOperationException($"{where}.op 需为 {expectedOp}（每 op 两 tick），实际是 {op}。");
        }

        var unitsNode = row["units"] as JsonArray
            ?? throw new InvalidOperationException($"{where}.units 需为单位行数组。");
        var units = new List<CrowdWebFuzzUnitTick>(unitsNode.Count);
        var seen = new HashSet<uint>();
        for (int i = 0; i < unitsNode.Count; i++)
        {
            var u = unitsNode[i] as JsonArray
                ?? throw new InvalidOperationException($"{where}.units[{i}] 需为 9 元数组。");
            if (u.Count != 9)
            {
                throw new InvalidOperationException($"{where}.units[{i}] 需为 9 元数组，实际 {u.Count} 元。");
            }

            uint handle = CrowdWebFuzzFixtureJson.RequireU32(u, 0, $"{where}.units[{i}][0]");
            if (!seen.Add(handle))
            {
                throw new InvalidOperationException($"{where}.units[{i}] 句柄 {handle} 重复。");
            }

            units.Add(new CrowdWebFuzzUnitTick(
                handle,
                CrowdWebFuzzFixtureJson.RequireFiniteDouble(u, 1, $"{where}.units[{i}][1]"),
                CrowdWebFuzzFixtureJson.RequireFiniteDouble(u, 2, $"{where}.units[{i}][2]"),
                CrowdWebFuzzFixtureJson.RequireByte(u, 3, $"{where}.units[{i}][3]"),
                CrowdWebFuzzFixtureJson.RequireIntValue(u, 4, $"{where}.units[{i}][4]"),
                CrowdWebFuzzFixtureJson.RequireByte(u, 5, $"{where}.units[{i}][5]"),
                CrowdWebFuzzFixtureJson.RequireByte(u, 6, $"{where}.units[{i}][6]"),
                CrowdWebFuzzFixtureJson.RequireU32(u, 7, $"{where}.units[{i}][7]"),
                CrowdWebFuzzFixtureJson.RequireByte(u, 8, $"{where}.units[{i}][8]")));
        }

        var navsNode = row["navs"] as JsonArray
            ?? throw new InvalidOperationException($"{where}.navs 需为 [navId, version] 数组。");
        var navs = new List<CrowdWebFuzzNavTick>(navsNode.Count);
        for (int i = 0; i < navsNode.Count; i++)
        {
            var n = navsNode[i] as JsonArray
                ?? throw new InvalidOperationException($"{where}.navs[{i}] 需为 2 元数组。");
            if (n.Count != 2)
            {
                throw new InvalidOperationException($"{where}.navs[{i}] 需为 2 元数组，实际 {n.Count} 元。");
            }

            navs.Add(new CrowdWebFuzzNavTick(
                CrowdWebFuzzFixtureJson.RequireIntValue(n, 0, $"{where}.navs[{i}][0]"),
                CrowdWebFuzzFixtureJson.RequireIntValue(n, 1, $"{where}.navs[{i}][1]")));
        }

        for (int i = 1; i < navs.Count; i++)
        {
            if (navs[i - 1].NavId >= navs[i].NavId)
            {
                throw new InvalidOperationException($"{where}.navs 需按 navId 升序且不重复。");
            }
        }

        var leadersNode = row["leaders"] as JsonArray
            ?? throw new InvalidOperationException($"{where}.leaders 需为领队行数组。");
        var leaders = new List<CrowdWebFuzzLeaderTick>(leadersNode.Count);
        for (int i = 0; i < leadersNode.Count; i++)
        {
            var l = leadersNode[i] as JsonArray
                ?? throw new InvalidOperationException($"{where}.leaders[{i}] 需为 7 元数组。");
            if (l.Count != 7)
            {
                throw new InvalidOperationException($"{where}.leaders[{i}] 需为 7 元数组，实际 {l.Count} 元。");
            }

            var ptsNode = l[6] as JsonArray
                ?? throw new InvalidOperationException($"{where}.leaders[{i}][6] 需为路点数组。");
            var pts = new List<CrowdWebFuzzPathPoint>(ptsNode.Count);
            for (int p = 0; p < ptsNode.Count; p++)
            {
                var pair = ptsNode[p] as JsonArray
                    ?? throw new InvalidOperationException($"{where}.leaders[{i}][6][{p}] 需为 [x, y]。");
                if (pair.Count != 2)
                {
                    throw new InvalidOperationException($"{where}.leaders[{i}][6][{p}] 需为 2 元数组。");
                }

                pts.Add(new CrowdWebFuzzPathPoint(
                    CrowdWebFuzzFixtureJson.RequireFiniteDouble(pair, 0, $"{where}.leaders[{i}][6][{p}][0]"),
                    CrowdWebFuzzFixtureJson.RequireFiniteDouble(pair, 1, $"{where}.leaders[{i}][6][{p}][1]")));
            }

            leaders.Add(new CrowdWebFuzzLeaderTick(
                CrowdWebFuzzFixtureJson.RequireIntValue(l, 0, $"{where}.leaders[{i}][0]"),
                CrowdWebFuzzFixtureJson.RequireIntValue(l, 1, $"{where}.leaders[{i}][1]"),
                CrowdWebFuzzFixtureJson.RequireFiniteDouble(l, 2, $"{where}.leaders[{i}][2]"),
                CrowdWebFuzzFixtureJson.RequireFiniteDouble(l, 3, $"{where}.leaders[{i}][3]"),
                CrowdWebFuzzFixtureJson.RequireIntValue(l, 4, $"{where}.leaders[{i}][4]"),
                CrowdWebFuzzFixtureJson.RequireBoolValue(l, 5, $"{where}.leaders[{i}][5]"),
                pts));
        }

        return new CrowdWebFuzzTickTrace(tick, op, units, navs, leaders);
    }
}

/// <summary>fuzz 夹具（v3 对拍与 v1 trace 共用）的严格 JSON 读取合同。</summary>
internal static class CrowdWebFuzzFixtureJson
{
    public static void RejectUnknownFields(JsonObject obj, HashSet<string> known, string where)
    {
        foreach (var kv in obj)
        {
            if (!known.Contains(kv.Key))
            {
                throw new InvalidOperationException($"{where} 有未知字段 {kv.Key}；schema 未登记的字段不许出现，加字段必须升版本并同步装载器。");
            }
        }
    }

    public static string RequireString(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为非空字符串。");
        }

        return text;
    }

    public static string RequireHex64(JsonObject obj, string key)
    {
        string text = RequireString(obj, key);
        if (text.Length != 64)
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为 64 位十六进制串。");
        }

        foreach (char c in text)
        {
            if (!Uri.IsHexDigit(c))
            {
                throw new InvalidOperationException($"模糊对拍夹具: {key} 需为 64 位十六进制串。");
            }
        }

        return text;
    }

    public static IReadOnlyList<string> RequireIdList(JsonObject obj, string key)
    {
        if (obj[key] is not JsonArray arr || arr.Count == 0)
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为非空 id 数组。");
        }

        var ids = new List<string>(arr.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JsonValue value || !value.TryGetValue<string>(out string? id) || string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"模糊对拍夹具: {key}[{i}] 需为非空字符串。");
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException($"模糊对拍夹具: {key} 中 id {id} 重复。");
            }

            ids.Add(id);
        }

        return ids;
    }

    public static IReadOnlyList<(double X, double Y)> RequirePointList(JsonObject obj, string key)
    {
        if (obj[key] is not JsonArray arr)
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为 [x, y] 数组。");
        }

        var points = new List<(double X, double Y)>(arr.Count);
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JsonArray pair || pair.Count != 2
                || pair[0] is not JsonValue xv || !xv.TryGetValue<double>(out double x) || !double.IsFinite(x)
                || pair[1] is not JsonValue yv || !yv.TryGetValue<double>(out double y) || !double.IsFinite(y))
            {
                throw new InvalidOperationException($"模糊对拍夹具: {key}[{i}] 需为两个有限数值。");
            }

            points.Add((x, y));
        }

        return points;
    }

    public static bool RequireBool(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为布尔值。");
        }

        return flag;
    }

    public static bool RequireBoolValue(JsonArray arr, int index, string where)
    {
        if (arr[index] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {where} 需为布尔值。");
        }

        return flag;
    }

    public static int RequireInt(JsonObject obj, string key, int min, int? max)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<int>(out int number) || number < min || (max is int hi && number > hi))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为 ≥ {min} 的整数。");
        }

        return number;
    }

    public static long RequireLong(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<long>(out long number))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为整数。");
        }

        return number;
    }

    public static double RequireDouble(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<double>(out double number)
            || double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为有限数值。");
        }

        return number;
    }

    public static int RequireIntValue(JsonArray arr, int index, string where)
    {
        if (arr[index] is not JsonValue value || !value.TryGetValue<int>(out int number))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {where} 需为整数。");
        }

        return number;
    }

    public static uint RequireU32(JsonArray arr, int index, string where)
    {
        if (arr[index] is not JsonValue value || !value.TryGetValue<uint>(out uint number))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {where} 需为无符号整数。");
        }

        return number;
    }

    public static byte RequireByte(JsonArray arr, int index, string where)
    {
        int number = RequireIntValue(arr, index, where);
        if (number is < 0 or > 255)
        {
            throw new InvalidOperationException($"模糊对拍夹具: {where} 需为 0..255。");
        }

        return (byte)number;
    }

    public static double RequireFiniteDouble(JsonArray arr, int index, string where)
    {
        if (arr[index] is not JsonValue value || !value.TryGetValue<double>(out double number)
            || double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {where} 需为有限数值。");
        }

        return number;
    }
}
