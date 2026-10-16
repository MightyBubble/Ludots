using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>模糊运行每步结束后的对拍采样（结构性字段 + 状态断面）。</summary>
public sealed class CrowdFuzzStepRecord
{
    public required string OpId { get; init; }
    public required int Units { get; init; }
    public required ulong StateDigest { get; init; }
    public required ulong PositionDigest { get; init; }
}

/// <summary>Web 导出的模糊对拍夹具；C# 按 <see cref="Steps"/> 里的 op 顺序回放。</summary>
public sealed class CrowdFuzzParityFixture
{
    public required string ParitySchema { get; init; }
    public required long Seed { get; init; }
    public required int OpCount { get; init; }
    public required CrowdFuzzPlan Plan { get; init; }
    public required IReadOnlyList<CrowdFuzzStepRecord> Steps { get; init; }

    public static CrowdFuzzParityFixture Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string schema = RequireString(root, "paritySchema");
        if (schema != "crowd-fuzz-parity-v1")
        {
            throw new InvalidOperationException($"模糊对拍夹具 paritySchema 需为 crowd-fuzz-parity-v1，实际是 {schema}。");
        }

        long seed = RequireLong(root, "seed");
        int opCount = RequireInt(root, "opCount", 1, null);
        var planNode = root["plan"] as JsonObject
            ?? throw new InvalidOperationException("模糊对拍夹具缺少 plan。");
        var plan = ParsePlan(planNode, seed, opCount);
        var stepsNode = root["steps"] as JsonArray
            ?? throw new InvalidOperationException("模糊对拍夹具缺少 steps。");
        if (stepsNode.Count != opCount)
        {
            throw new InvalidOperationException($"模糊对拍夹具 steps 长度 {stepsNode.Count} 与 opCount {opCount} 不一致。");
        }

        var steps = new List<CrowdFuzzStepRecord>(stepsNode.Count);
        for (int i = 0; i < stepsNode.Count; i++)
        {
            var row = stepsNode[i] as JsonObject
                ?? throw new InvalidOperationException($"模糊对拍夹具 steps[{i}] 需为对象。");
            steps.Add(new CrowdFuzzStepRecord
            {
                OpId = RequireString(row, "opId"),
                Units = RequireInt(row, "units", 0, null),
                StateDigest = ParseU64(row, "stateDigest"),
                PositionDigest = ParseU64(row, "positionDigest"),
            });
        }

        return new CrowdFuzzParityFixture
        {
            ParitySchema = schema,
            Seed = seed,
            OpCount = opCount,
            Plan = plan,
            Steps = steps,
        };
    }

    public void Save(string path)
    {
        var steps = new JsonArray();
        for (int i = 0; i < Steps.Count; i++)
        {
            var s = Steps[i];
            steps.Add(new JsonObject
            {
                ["opId"] = s.OpId,
                ["units"] = s.Units,
                ["stateDigest"] = FormatU64(s.StateDigest),
                ["positionDigest"] = FormatU64(s.PositionDigest),
            });
        }

        var planObj = new JsonObject
        {
            ["stepsPerOp"] = Plan.StepsPerOp,
            ["probeHandleCount"] = Plan.ProbeHandleCount,
            ["spawnCount"] = Plan.SpawnCount,
            ["placeTemplate"] = Plan.PlaceTemplate,
            ["placeSizeCm"] = Plan.PlaceSizeCm,
            ["orderShape"] = Plan.OrderShape,
            ["orderMemberCap"] = Plan.OrderMemberCap,
            ["stallGuard"] = Plan.StallGuard,
            ["boomRadiusCm"] = Plan.BoomRadiusCm,
            ["ringScale"] = Plan.RingScale.ToDouble(),
            ["impulse"] = Plan.Impulse.ToDouble(),
            ["pathTemplate"] = Plan.PathTemplate,
            ["pathWidthCm"] = Plan.PathWidthCm,
            ["pathLengthCm"] = Plan.PathLengthCm,
            ["ops"] = new JsonArray(Plan.Ops.Select(o => JsonValue.Create(o.Id)).ToArray()),
        };

        var doc = new JsonObject
        {
            ["paritySchema"] = ParitySchema,
            ["seed"] = Seed,
            ["opCount"] = OpCount,
            ["plan"] = planObj,
            ["steps"] = steps,
        };
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static CrowdFuzzPlan ParsePlan(JsonObject plan, long seed, int opCount)
    {
        var opsArray = plan["ops"] as JsonArray
            ?? throw new InvalidOperationException("模糊对拍夹具 plan.ops 需为数组。");
        var opIds = new List<string>(opsArray.Count);
        for (int i = 0; i < opsArray.Count; i++)
        {
            opIds.Add(opsArray[i]?.GetValue<string>()
                ?? throw new InvalidOperationException($"模糊对拍夹具 plan.ops[{i}] 需为字符串。"));
        }

        return new CrowdFuzzPlan
        {
            Seed = seed,
            OpCount = opCount,
            StepsPerOp = RequireInt(plan, "stepsPerOp", 1, null),
            ProbeHandleCount = RequireInt(plan, "probeHandleCount", 0, null),
            SpawnCount = RequireInt(plan, "spawnCount", 0, null),
            PlaceTemplate = RequireString(plan, "placeTemplate"),
            PlaceSizeCm = RequireInt(plan, "placeSizeCm", 1, null),
            OrderShape = RequireString(plan, "orderShape"),
            OrderMemberCap = RequireInt(plan, "orderMemberCap", 0, null),
            StallGuard = RequireInt(plan, "stallGuard", 1, null),
            BoomRadiusCm = RequireInt(plan, "boomRadiusCm", 1, null),
            RingScale = Ludots.Core.Mathematics.FixedPoint.Fix64.FromDouble(RequireDouble(plan, "ringScale")),
            Impulse = Ludots.Core.Mathematics.FixedPoint.Fix64.FromDouble(RequireDouble(plan, "impulse")),
            PathTemplate = RequireString(plan, "pathTemplate"),
            PathWidthCm = RequireInt(plan, "pathWidthCm", 1, null),
            PathLengthCm = RequireInt(plan, "pathLengthCm", 1, null),
            Ops = CrowdFuzzOpTraits.ResolveAll(opIds, "模糊对拍夹具 plan"),
        };
    }

    private static string RequireString(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为非空字符串。");
        }

        return text;
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

    private static ulong ParseU64(JsonObject obj, string key)
    {
        string text = RequireString(obj, key);
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"模糊对拍夹具: {key} 需为 0x 前缀的十六进制。");
        }

        return ulong.Parse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber);
    }

    private static string FormatU64(ulong value) => $"0x{value:x16}";
}
