using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>UAT-FOG 的种子目录和场景资产。场景路径从仓库根起。</summary>
public sealed class CrowdSimRegressFogSpec
{
    public required string SeedAssets { get; init; }
    public required string SceneAsset { get; init; }
}

/// <summary>场景表里的一条。没有脚本时 <see cref="UnavailableReason"/> 说明缺哪份数据。</summary>
public sealed class CrowdSimRegressScenarioSpec
{
    public string? UnavailableReason { get; init; }
    public string? SeedAssets { get; init; }
    public string? ScriptAsset { get; init; }
    public bool ScriptFromRepo { get; init; }
    public string? TicksFrom { get; init; }
    public bool Movement { get; init; }
    public bool TruthNavFrozen { get; init; }
}

/// <summary>
/// 回归场景表。重复次数、种子、模糊操作数和场景路径都在这份 JSON 里。
/// 工具只负责把表读进来并按参数覆盖种子、操作数和重复次数。
/// </summary>
public sealed class CrowdSimRegressCatalog
{
    private readonly int _stepsPerOp;
    private readonly int _probeHandles;
    private readonly int _spawnCount;
    private readonly string _placeTemplate;
    private readonly int _placeSizeCm;
    private readonly string _orderShape;
    private readonly int _orderMemberCap;
    private readonly int _stallGuard;
    private readonly int _boomRadiusCm;
    private readonly Fix64 _ringScale;
    private readonly Fix64 _impulse;
    private readonly string _bridgeTemplate;
    private readonly int _bridgeWidthCm;
    private readonly int _bridgeSpanCm;
    private readonly IReadOnlyList<CrowdFuzzOp> _ops;

    private CrowdSimRegressCatalog(
        IReadOnlyList<string> defaults,
        IReadOnlyList<string> full,
        int repeat,
        long seed,
        int fuzzOps,
        string standardAssets,
        string capabilityAssets,
        IReadOnlyDictionary<string, CrowdSimRegressScenarioSpec> scenarios,
        string fuzzSeedAssets,
        bool fuzzMovement,
        bool fuzzTruthNavFrozen,
        int stepsPerOp,
        int probeHandles,
        int spawnCount,
        string placeTemplate,
        int placeSizeCm,
        string orderShape,
        int orderMemberCap,
        int stallGuard,
        int boomRadiusCm,
        Fix64 ringScale,
        Fix64 impulse,
        string bridgeTemplate,
        int bridgeWidthCm,
        int bridgeSpanCm,
        IReadOnlyList<CrowdFuzzOp> ops,
        CrowdSimRegressFogSpec fog)
    {
        Defaults = defaults;
        Full = full;
        Repeat = repeat;
        Seed = seed;
        FuzzOps = fuzzOps;
        StandardAssets = standardAssets;
        CapabilityAssets = capabilityAssets;
        Scenarios = scenarios;
        FuzzSeedAssets = fuzzSeedAssets;
        FuzzMovement = fuzzMovement;
        FuzzTruthNavFrozen = fuzzTruthNavFrozen;
        _stepsPerOp = stepsPerOp;
        _probeHandles = probeHandles;
        _spawnCount = spawnCount;
        _placeTemplate = placeTemplate;
        _placeSizeCm = placeSizeCm;
        _orderShape = orderShape;
        _orderMemberCap = orderMemberCap;
        _stallGuard = stallGuard;
        _boomRadiusCm = boomRadiusCm;
        _ringScale = ringScale;
        _impulse = impulse;
        _bridgeTemplate = bridgeTemplate;
        _bridgeWidthCm = bridgeWidthCm;
        _bridgeSpanCm = bridgeSpanCm;
        _ops = ops;
        Fog = fog;
    }

    public IReadOnlyList<string> Defaults { get; }
    public IReadOnlyList<string> Full { get; }
    public int Repeat { get; }
    public long Seed { get; }
    public int FuzzOps { get; }
    public string StandardAssets { get; }
    public string CapabilityAssets { get; }
    public IReadOnlyDictionary<string, CrowdSimRegressScenarioSpec> Scenarios { get; }
    public string FuzzSeedAssets { get; }
    public bool FuzzMovement { get; }
    public bool FuzzTruthNavFrozen { get; }
    public CrowdSimRegressFogSpec Fog { get; }

    public CrowdFuzzPlan CreateFuzzPlan(long seed, int opCount) => new()
    {
        Seed = seed,
        OpCount = opCount,
        StepsPerOp = _stepsPerOp,
        ProbeHandleCount = _probeHandles,
        SpawnCount = _spawnCount,
        PlaceTemplate = _placeTemplate,
        PlaceSizeCm = _placeSizeCm,
        OrderShape = _orderShape,
        OrderMemberCap = _orderMemberCap,
        StallGuard = _stallGuard,
        BoomRadiusCm = _boomRadiusCm,
        RingScale = _ringScale,
        Impulse = _impulse,
        BridgeTemplate = _bridgeTemplate,
        BridgeWidthCm = _bridgeWidthCm,
        BridgeSpanCm = _bridgeSpanCm,
        Ops = _ops,
    };

    public static CrowdSimRegressCatalog Parse(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        RequireExact(root, "regress catalog",
            "defaults", "full", "repeat", "seed", "fuzzOps", "standardAssets", "capabilityAssets", "scenarios", "fuzz", "fog");
        var scenarios = ParseScenarios(RequireObject(root, "scenarios"));
        var defaults = Strings(root["defaults"], "defaults");
        var full = Strings(root["full"], "full");
        foreach (var id in defaults) RequireKnown(scenarios, "defaults", id);
        foreach (var id in full) RequireKnown(scenarios, "full", id);
        var fuzz = RequireObject(root, "fuzz");
        RequireExact(fuzz, "regress catalog fuzz",
            "seedAssets", "movement", "truthNavFrozen", "stepsPerOp", "probeHandles", "spawnCount",
            "placeTemplate", "placeSizeCm", "orderShape", "orderMemberCap", "stallGuard",
            "boomRadiusCm", "ringScale", "impulse", "bridgeTemplate", "bridgeWidthCm", "bridgeSpanCm", "ops");
        return new CrowdSimRegressCatalog(
            defaults,
            full,
            RequireInt(root, "repeat", 1, null),
            RequireLong(root, "seed"),
            RequireInt(root, "fuzzOps", 0, null),
            RequireString(root, "standardAssets"),
            RequireString(root, "capabilityAssets"),
            scenarios,
            RequireString(fuzz, "seedAssets"),
            RequireBool(fuzz, "movement"),
            RequireBool(fuzz, "truthNavFrozen"),
            RequireInt(fuzz, "stepsPerOp", 1, null),
            RequireInt(fuzz, "probeHandles", 0, null),
            RequireInt(fuzz, "spawnCount", 0, null),
            RequireString(fuzz, "placeTemplate"),
            RequireInt(fuzz, "placeSizeCm", 1, null),
            RequireString(fuzz, "orderShape"),
            RequireInt(fuzz, "orderMemberCap", 0, null),
            RequireInt(fuzz, "stallGuard", 1, null),
            RequireInt(fuzz, "boomRadiusCm", 1, null),
            RequireRingScale(fuzz),
            RequireFix(fuzz, "impulse", Fix64.Zero),
            RequireString(fuzz, "bridgeTemplate"),
            RequireInt(fuzz, "bridgeWidthCm", 1, null),
            RequireInt(fuzz, "bridgeSpanCm", 1, null),
            ParseOps(fuzz["ops"] as JsonArray ?? throw new InvalidOperationException("regress catalog: fuzz.ops 需为数组。")),
            ParseFog(RequireObject(root, "fog")));
    }

    private static CrowdSimRegressFogSpec ParseFog(JsonObject fog)
    {
        RequireExact(fog, "regress catalog fog", "seedAssets", "sceneAsset");
        return new CrowdSimRegressFogSpec
        {
            SeedAssets = RequireString(fog, "seedAssets"),
            SceneAsset = RequireString(fog, "sceneAsset"),
        };
    }

    public static JsonArray Script(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document["script"]?.AsArray()
            ?? throw new InvalidOperationException("场景脚本缺少 script。");
    }

    public static int TickCount(JsonObject document, string ticksFrom)
    {
        ArgumentNullException.ThrowIfNull(document);
        switch (ticksFrom)
        {
            case "frames":
                return document["frames"]?.AsArray()?.Count
                    ?? throw new InvalidOperationException("场景脚本缺少 frames。");
            case "ticks":
                if (document["ticks"] is not JsonValue value || !value.TryGetValue<int>(out int ticks) || ticks < 0)
                {
                    throw new InvalidOperationException("场景脚本的 ticks 需为 ≥ 0 的整数。");
                }

                return ticks;
            default:
                throw new InvalidOperationException($"ticksFrom 需为 frames 或 ticks，实际是 {ticksFrom}。");
        }
    }

    private static Dictionary<string, CrowdSimRegressScenarioSpec> ParseScenarios(JsonObject scenarios)
    {
        var map = new Dictionary<string, CrowdSimRegressScenarioSpec>(scenarios.Count, StringComparer.Ordinal);
        foreach (var entry in scenarios)
        {
            var body = entry.Value as JsonObject
                ?? throw new InvalidOperationException($"regress catalog: 场景 {entry.Key} 需为对象。");
            if (body.ContainsKey("unavailable"))
            {
                RequireExact(body, $"regress catalog 场景 {entry.Key}", "unavailable");
                map.Add(entry.Key, new CrowdSimRegressScenarioSpec
                {
                    UnavailableReason = RequireString(body, "unavailable"),
                });
                continue;
            }

            RequireExact(body, $"regress catalog 场景 {entry.Key}",
                "seedAssets", "scriptAsset", "ticksFrom", "movement", "truthNavFrozen");
            string ticksFrom = RequireString(body, "ticksFrom");
            if (ticksFrom is not ("frames" or "ticks"))
            {
                throw new InvalidOperationException($"regress catalog: 场景 {entry.Key} 的 ticksFrom 需为 frames 或 ticks。");
            }

            string scriptAsset = RequireString(body, "scriptAsset");
            map.Add(entry.Key, new CrowdSimRegressScenarioSpec
            {
                SeedAssets = RequireString(body, "seedAssets"),
                ScriptAsset = scriptAsset,
                ScriptFromRepo = scriptAsset.StartsWith("mods/", StringComparison.Ordinal),
                TicksFrom = ticksFrom,
                Movement = RequireBool(body, "movement"),
                TruthNavFrozen = RequireBool(body, "truthNavFrozen"),
            });
        }

        return map;
    }

    private static List<CrowdFuzzOp> ParseOps(JsonArray ops)
    {
        var list = new List<CrowdFuzzOp>(ops.Count);
        for (int i = 0; i < ops.Count; i++)
        {
            var body = ops[i] as JsonObject
                ?? throw new InvalidOperationException($"regress catalog: fuzz.ops[{i}] 需为对象。");
            RequireExact(body, $"regress catalog fuzz.ops[{i}]", "id", "structure", "probeHandles", "reserved");
            list.Add(new CrowdFuzzOp
            {
                Id = RequireString(body, "id"),
                Structure = RequireBool(body, "structure"),
                ProbeHandles = RequireBool(body, "probeHandles"),
                Reserved = RequireBool(body, "reserved"),
            });
        }

        if (list.Count == 0) throw new InvalidOperationException("regress catalog: fuzz.ops 为空。");
        return list;
    }

    private static void RequireKnown(
        Dictionary<string, CrowdSimRegressScenarioSpec> scenarios, string list, string id)
    {
        if (!scenarios.ContainsKey(id))
        {
            throw new InvalidOperationException($"regress catalog: {list} 指向未知场景 {id}。");
        }
    }

    private static void RequireExact(JsonObject obj, string where, params string[] keys)
    {
        var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var entry in obj)
        {
            if (!allowed.Contains(entry.Key))
            {
                throw new InvalidOperationException($"{where}: 未知字段 {entry.Key}。");
            }
        }

        for (int i = 0; i < keys.Length; i++)
        {
            if (!obj.ContainsKey(keys[i]) || obj[keys[i]] is null)
            {
                throw new InvalidOperationException($"{where}: 缺少 {keys[i]}。");
            }
        }
    }

    private static JsonObject RequireObject(JsonObject obj, string key)
        => obj[key] as JsonObject ?? throw new InvalidOperationException($"regress catalog: {key} 需为对象。");

    private static List<string> Strings(JsonNode? node, string key)
    {
        var array = node as JsonArray ?? throw new InvalidOperationException($"regress catalog: {key} 需为字符串数组。");
        var list = new List<string>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"regress catalog: {key}[{i}] 需为非空字符串。");
            }

            list.Add(text);
        }

        return list;
    }

    private static string RequireString(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"regress catalog: {key} 需为非空字符串。");
        }

        return text;
    }

    private static bool RequireBool(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
        {
            throw new InvalidOperationException($"regress catalog: {key} 需为布尔值。");
        }

        return flag;
    }

    private static int RequireInt(JsonObject obj, string key, int min, int? max)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<int>(out int number) || number < min || (max is int hi && number > hi))
        {
            throw new InvalidOperationException($"regress catalog: {key} 需为 ≥ {min} 的整数。");
        }

        return number;
    }

    private static Fix64 RequireRingScale(JsonObject obj)
    {
        var scale = RequireFix(obj, "ringScale", Fix64.Zero);
        if (scale <= Fix64.OneValue)
        {
            throw new InvalidOperationException("regress catalog: ringScale 必须 > 1。");
        }

        return scale;
    }

    private static Fix64 RequireFix(JsonObject obj, string key, Fix64 min)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<double>(out double number)
            || double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new InvalidOperationException($"regress catalog: {key} 需为有限数值。");
        }

        var fix = Fix64.FromDouble(number);
        if (fix < min)
        {
            throw new InvalidOperationException($"regress catalog: {key} 低于下限。");
        }

        return fix;
    }

    private static long RequireLong(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue<long>(out long number))
        {
            throw new InvalidOperationException($"regress catalog: {key} 需为整数。");
        }

        return number;
    }
}
