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

public sealed class CrowdWebFuzzParityFixture
{
    public const string SchemaV2 = "crowd-fuzz-parity-v2";

    public required string ParitySchema { get; init; }
    public required long Seed { get; init; }
    public required int OpCount { get; init; }
    public required CrowdWebFuzzBlast Blast { get; init; }
    public required IReadOnlyList<CrowdWebFuzzStepRecord> Steps { get; init; }

    public static CrowdWebFuzzParityFixture Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string schema = RequireString(root, "paritySchema");
        if (schema != SchemaV2)
        {
            throw new InvalidOperationException($"模糊对拍夹具 paritySchema 需为 {SchemaV2}，实际是 {schema}。");
        }

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
            Steps = steps,
        };
    }

    public void Save(string path)
    {
        var steps = new JsonArray();
        foreach (var step in Steps)
        {
            var rng = new JsonArray();
            for (int i = 0; i < step.RngDraws.Count; i++) rng.Add(step.RngDraws[i]);
            var op = new JsonObject { ["kind"] = step.Op.Kind };
            if (step.Op.Params != null) op["params"] = step.Op.Params.DeepClone();
            var row = new JsonObject
            {
                ["rngDraws"] = rng,
                ["op"] = op,
                ["units"] = step.Units,
                ["validateOk"] = step.ValidateOk,
                ["unitChecksumHex"] = step.UnitChecksumHex,
                ["positionStatL1Cm"] = step.PositionStatL1Cm,
            };
            if (step.ValidateErrors != null && step.ValidateErrors.Count > 0)
            {
                var errs = new JsonArray();
                foreach (string e in step.ValidateErrors) errs.Add(e);
                row["validateErrors"] = errs;
            }

            steps.Add(row);
        }

        var doc = new JsonObject
        {
            ["paritySchema"] = ParitySchema,
            ["seed"] = Seed,
            ["opCount"] = OpCount,
            ["blast"] = new JsonObject
            {
                ["ringScale"] = Blast.RingScale.ToDouble(),
                ["impulse"] = Blast.Impulse.ToDouble(),
            },
            ["steps"] = steps,
        };
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
