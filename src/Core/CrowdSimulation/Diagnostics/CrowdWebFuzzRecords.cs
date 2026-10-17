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

    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
    {
        "paritySchema", "seed", "opCount", "blast", "templateOrder", "formationOrder", "initialStructures", "steps",
    };

    private static readonly HashSet<string> StepFields = new(StringComparer.Ordinal)
    {
        "rngDraws", "op", "units", "validateOk", "validateErrors", "unitChecksumHex", "positionStatL1Cm",
    };

    public required string ParitySchema { get; init; }
    public required long Seed { get; init; }
    public required int OpCount { get; init; }
    public required CrowdWebFuzzBlast Blast { get; init; }
    public required CrowdWebFuzzPalette Palette { get; init; }
    public required IReadOnlyList<CrowdWebFuzzStepRecord> Steps { get; init; }

    public static CrowdWebFuzzParityFixture Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        CrowdWebFuzzFixtureJson.RejectUnknownFields(root, RootFields, "模糊对拍夹具");
        string schema = CrowdWebFuzzFixtureJson.RequireString(root, "paritySchema");
        if (schema != SchemaV3)
        {
            throw new InvalidOperationException($"模糊对拍夹具 paritySchema 需为 {SchemaV3}，实际是 {schema}。");
        }

        var palette = new CrowdWebFuzzPalette
        {
            TemplateIds = CrowdWebFuzzFixtureJson.RequireIdList(root, "templateOrder"),
            FormationIds = CrowdWebFuzzFixtureJson.RequireIdList(root, "formationOrder"),
            InitialStructureAnchors = CrowdWebFuzzFixtureJson.RequirePointList(root, "initialStructures"),
        };

        long seed = CrowdWebFuzzFixtureJson.RequireLong(root, "seed");
        int opCount = CrowdWebFuzzFixtureJson.RequireInt(root, "opCount", 1, null);
        var blastNode = root["blast"] as JsonObject
            ?? throw new InvalidOperationException("模糊对拍夹具缺少 blast。");
        CrowdWebFuzzFixtureJson.RejectUnknownFields(blastNode, new HashSet<string> { "ringScale", "impulse" }, "模糊对拍夹具 blast");
        var blast = new CrowdWebFuzzBlast(
            Fix64.FromDouble(CrowdWebFuzzFixtureJson.RequireDouble(blastNode, "ringScale")),
            Fix64.FromDouble(CrowdWebFuzzFixtureJson.RequireDouble(blastNode, "impulse")));
        var stepsNode = root["steps"] as JsonArray
            ?? throw new InvalidOperationException("模糊对拍夹具缺少 steps。");
        if (stepsNode.Count != opCount)
        {
            throw new InvalidOperationException($"模糊对拍夹具 steps 长度 {stepsNode.Count} 与 opCount {opCount} 不一致。");
        }

        var steps = new List<CrowdWebFuzzStepRecord>(stepsNode.Count);
        for (int i = 0; i < stepsNode.Count; i++)
        {
            var row = stepsNode[i] as JsonObject
                ?? throw new InvalidOperationException($"模糊对拍夹具 steps[{i}] 需为对象。");
            CrowdWebFuzzFixtureJson.RejectUnknownFields(row, StepFields, $"模糊对拍夹具 steps[{i}]");
            steps.Add(CrowdWebFuzzStepParse.ParseStep(row, $"模糊对拍夹具 steps[{i}]"));
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
}

/// <summary>step 行（v3 对拍与 v1 trace 共用）的解析。</summary>
internal static class CrowdWebFuzzStepParse
{
    public static CrowdWebFuzzStepRecord ParseStep(JsonObject row, string where)
    {
        var rngNode = row["rngDraws"] as JsonArray
            ?? throw new InvalidOperationException($"{where} 缺少 rngDraws。");
        var draws = new List<uint>(rngNode.Count);
        for (int i = 0; i < rngNode.Count; i++)
        {
            draws.Add(rngNode[i]?.GetValue<uint>()
                ?? throw new InvalidOperationException($"{where} rngDraws[{i}] 需为无符号整数。"));
        }

        var opNode = row["op"] as JsonObject
            ?? throw new InvalidOperationException($"{where} 缺少 op。");
        string kind = CrowdWebFuzzFixtureJson.RequireString(opNode, "kind");
        JsonObject? @params = opNode["params"] as JsonObject;
        return new CrowdWebFuzzStepRecord
        {
            RngDraws = draws,
            Op = new CrowdWebFuzzOpRecord { Kind = kind, Params = @params },
            Units = CrowdWebFuzzFixtureJson.RequireInt(row, "units", 0, null),
            ValidateOk = CrowdWebFuzzFixtureJson.RequireBool(row, "validateOk"),
            ValidateErrors = ParseErrors(row, where),
            UnitChecksumHex = CrowdWebFuzzFixtureJson.RequireString(row, "unitChecksumHex"),
            PositionStatL1Cm = CrowdWebFuzzFixtureJson.RequireLong(row, "positionStatL1Cm"),
        };
    }

    private static IReadOnlyList<string>? ParseErrors(JsonObject row, string where)
    {
        if (row["validateErrors"] is not JsonArray arr || arr.Count == 0) return null;
        var list = new List<string>(arr.Count);
        for (int i = 0; i < arr.Count; i++)
        {
            list.Add(arr[i]?.GetValue<string>() ?? "");
        }

        return list;
    }
}
