using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

public sealed class CrowdWebFuzzOpRecord
{
    public required string Kind { get; init; }
    public JsonObject? Params { get; init; }
}

public sealed class CrowdWebFuzzStepRecord
{
    public required IReadOnlyList<uint> RngDraws { get; init; }
    public required CrowdWebFuzzOpRecord Op { get; init; }
    public required int Units { get; init; }
    public required bool ValidateOk { get; init; }
    public IReadOnlyList<string>? ValidateErrors { get; init; }
    public required string UnitChecksumHex { get; init; }
    public required long PositionStatL1Cm { get; init; }
}

public sealed class CrowdWebFuzzRunResult
{
    public required bool Ok { get; init; }
    public required long Seed { get; init; }
    public int FailedAt { get; init; }
    public string? FailedOp { get; init; }
    public IReadOnlyList<string>? Errors { get; init; }
    public required IReadOnlyList<CrowdWebFuzzStepRecord> Steps { get; init; }
}

/// <summary>
/// Web 端按数组下标抽取结构模板、阵型与待拆结构；C# 运行时表按 id 排序且可能多出地图专属模板，
/// 开局结构的实体 id 序也取地图实体序，与 Web 结构表迭代序不同。下标必须落在 Web 序上，再解析到 C# 实体。
/// </summary>
public sealed class CrowdWebFuzzPalette
{
    public required IReadOnlyList<string> TemplateIds { get; init; }
    public required IReadOnlyList<string> FormationIds { get; init; }

    /// <summary>开局结构锚点（米；矩形与圆取中心，路径取起点），按 Web 结构表迭代序。</summary>
    public required IReadOnlyList<(double X, double Y)> InitialStructureAnchors { get; init; }
}

public sealed class CrowdWebFuzzParityFixture
{
    public const string SchemaV3 = "crowd-fuzz-parity-v3";

    public required string ParitySchema { get; init; }
    public required long Seed { get; init; }
    public required int OpCount { get; init; }
    public required CrowdWebFuzzBlast Blast { get; init; }
    public required CrowdWebFuzzPalette Palette { get; init; }
    public required IReadOnlyList<CrowdWebFuzzStepRecord> Steps { get; init; }

    public static CrowdWebFuzzParityFixture Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string schema = RequireString(root, "paritySchema");
        if (schema != SchemaV3)
        {
            throw new InvalidOperationException($"模糊对拍夹具 paritySchema 需为 {SchemaV3}，实际是 {schema}。");
        }

        var palette = new CrowdWebFuzzPalette
        {
            TemplateIds = RequireIdList(root, "templateOrder"),
            FormationIds = RequireIdList(root, "formationOrder"),
            InitialStructureAnchors = RequirePointList(root, "initialStructures"),
        };

        long seed = RequireLong(root, "seed");
        int opCount = RequireInt(root, "opCount", 1, null);
        var blastNode = root["blast"] as JsonObject
            ?? throw new InvalidOperationException("模糊对拍夹具缺少 blast。");
        var blast = new CrowdWebFuzzBlast(
            Fix64.FromDouble(RequireDouble(blastNode, "ringScale")),
            Fix64.FromDouble(RequireDouble(blastNode, "impulse")));
        var stepsNode = root["steps"] as JsonArray
            ?? throw new InvalidOperationException("模糊对拍夹具缺少 steps。");
        if (stepsNode.Count != opCount)
        {
            throw new InvalidOperationException($"模糊对拍夹具 steps 长度 {stepsNode.Count} 与 opCount {opCount} 不一致。");
        }

        var steps = new List<CrowdWebFuzzStepRecord>(stepsNode.Count);
        for (int i = 0; i < stepsNode.Count; i++)
        {
            steps.Add(ParseStep(stepsNode[i] as JsonObject ?? throw new InvalidOperationException($"steps[{i}] 需为对象。")));
        }

        return new CrowdWebFuzzParityFixture
        {
            ParitySchema = schema,
            Seed = seed,
            OpCount = opCount,
            Blast = blast,
            Palette = palette,
            Steps = steps,
        };
    }

    private static CrowdWebFuzzStepRecord ParseStep(JsonObject row)
    {
        var rngNode = row["rngDraws"] as JsonArray
            ?? throw new InvalidOperationException("step 缺少 rngDraws。");
        var draws = new List<uint>(rngNode.Count);
        for (int i = 0; i < rngNode.Count; i++)
        {
            draws.Add(rngNode[i]?.GetValue<uint>()
                ?? throw new InvalidOperationException($"rngDraws[{i}] 需为无符号整数。"));
        }

        var opNode = row["op"] as JsonObject
            ?? throw new InvalidOperationException("step 缺少 op。");
        string kind = RequireString(opNode, "kind");
        JsonObject? @params = opNode["params"] as JsonObject;
        return new CrowdWebFuzzStepRecord
        {
            RngDraws = draws,
            Op = new CrowdWebFuzzOpRecord { Kind = kind, Params = @params },
            Units = RequireInt(row, "units", 0, null),
            ValidateOk = RequireBool(row, "validateOk"),
            ValidateErrors = ParseErrors(row),
            UnitChecksumHex = RequireString(row, "unitChecksumHex"),
            PositionStatL1Cm = RequireLong(row, "positionStatL1Cm"),
        };
    }

    private static IReadOnlyList<string>? ParseErrors(JsonObject row)
    {
        if (row["validateErrors"] is not JsonArray arr || arr.Count == 0) return null;
        var list = new List<string>(arr.Count);
        for (int i = 0; i < arr.Count; i++)
        {
            list.Add(arr[i]?.GetValue<string>() ?? "");
        }

        return list;
    }

    private static string RequireString(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为非空字符串。");
        }

        return text;
    }

    private static IReadOnlyList<string> RequireIdList(JsonObject obj, string key)
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

    private static IReadOnlyList<(double X, double Y)> RequirePointList(JsonObject obj, string key)
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

    private static bool RequireBool(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为布尔值。");
        }

        return flag;
    }

    private static int RequireInt(JsonObject obj, string key, int min, int? max)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<int>(out int number) || number < min || (max is int hi && number > hi))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为 ≥ {min} 的整数。");
        }

        return number;
    }

    private static long RequireLong(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<long>(out long number))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为整数。");
        }

        return number;
    }

    private static double RequireDouble(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<double>(out double number)
            || double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为有限数值。");
        }

        return number;
    }
}
