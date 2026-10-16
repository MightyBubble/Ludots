using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Diagnostics;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

internal static class CrowdWebFuzzOpParamAssert
{
    private static readonly HashSet<string> MeterKeys = new(StringComparer.Ordinal)
    {
        "x", "y", "radius", "size", "ax", "ay",
    };

    public static void Equal(CrowdWebFuzzOpRecord expected, CrowdWebFuzzOpRecord actual, int step)
    {
        var e = expected.Params ?? new JsonObject();
        var a = actual.Params ?? new JsonObject();
        CompareNodes(e, a, $"第 {step} 步 op 参数", CrowdWebFuzzRunner.OpParamMeterBandwidth);
    }

    private static void CompareNodes(JsonNode expected, JsonNode actual, string path, double meterBandwidth)
    {
        Assert.That(actual.GetValueKind(), Is.EqualTo(expected.GetValueKind()), $"{path} 节点类型不一致");
        switch (expected.GetValueKind())
        {
            case System.Text.Json.JsonValueKind.Object:
                var eo = expected.AsObject();
                var ao = actual.AsObject();
                Assert.That(ao.Count, Is.EqualTo(eo.Count), $"{path} 字段数不一致");
                foreach (var kv in eo)
                {
                    Assert.That(ao.ContainsKey(kv.Key), Is.True, $"{path}.{kv.Key} 缺失");
                    CompareNodes(kv.Value!, ao[kv.Key]!, $"{path}.{kv.Key}", meterBandwidth);
                }

                break;
            case System.Text.Json.JsonValueKind.Array:
                var ea = expected.AsArray();
                var aa = actual.AsArray();
                Assert.That(aa.Count, Is.EqualTo(ea.Count), $"{path} 数组长度不一致");
                for (int i = 0; i < ea.Count; i++)
                {
                    CompareNodes(ea[i]!, aa[i]!, $"{path}[{i}]", meterBandwidth);
                }

                break;
            case System.Text.Json.JsonValueKind.Number:
                if (path.Contains("probe", StringComparison.Ordinal))
                {
                    Assert.That(ReadIntegral(actual), Is.EqualTo(ReadIntegral(expected)), $"{path} 探针数值不一致");
                    break;
                }

                double ev = ReadNumber(expected);
                double av = ReadNumber(actual);
                if (meterBandwidth > 0 && UsesMeterBandwidth(path))
                {
                    Assert.That(Math.Abs(av - ev), Is.LessThanOrEqualTo(meterBandwidth), $"{path} 米制坐标差 {av - ev}");
                }
                else
                {
                    Assert.That(av, Is.EqualTo(ev), $"{path} 数值不一致");
                }

                break;
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                Assert.That(actual.GetValue<bool>(), Is.EqualTo(expected.GetValue<bool>()), $"{path} 布尔不一致");
                break;
            case System.Text.Json.JsonValueKind.String:
                Assert.That(actual.GetValue<string>(), Is.EqualTo(expected.GetValue<string>()), $"{path} 字符串不一致");
                break;
            case System.Text.Json.JsonValueKind.Null:
                break;
            default:
                Assert.That(actual.ToJsonString(), Is.EqualTo(expected.ToJsonString()), $"{path} 值不一致");
                break;
        }
    }

    private static double ReadNumber(JsonNode node)
    {
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue(out double d)) return d;
            if (jv.TryGetValue(out int i)) return i;
            if (jv.TryGetValue(out long l)) return l;
            if (jv.TryGetValue(out uint u)) return u;
        }

        return node.GetValue<double>();
    }

    private static long ReadIntegral(JsonNode node)
    {
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue(out long l)) return l;
            if (jv.TryGetValue(out int i)) return i;
            if (jv.TryGetValue(out uint u)) return u;
            if (jv.TryGetValue(out bool b)) return b ? 1 : 0;
        }

        return node.GetValue<long>();
    }

    private static bool UsesMeterBandwidth(string path)
    {
        foreach (string seg in path.Split('.', '[', ']'))
        {
            if (string.IsNullOrEmpty(seg)) continue;
            if (MeterKeys.Contains(seg)) return true;
            if (seg is "to" or "face") return true;
        }

        return false;
    }
}
