using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Ludots.Core.Gameplay.GAS.Capacity
{
    public sealed class GasCapacityBenchmarkReport
    {
        public const string SchemaVersion = "gas-capacity-benchmark-v1";
        public const double DefaultRegressionThreshold = 0.10;

        public string Schema { get; set; } = SchemaVersion;
        public string Phase { get; set; } = "baseline";
        public string StorageKind { get; set; } = "legacy-embedded";
        public int AttributeSlotCount { get; set; }
        public int TagIdSpace { get; set; }
        public int EntityCount { get; set; }
        public int Iterations { get; set; }
        public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        public List<GasCapacityBenchmarkMetric> Metrics { get; set; } = new();

        public void AddMetric(string id, double value, string unit, long allocatedBytes = 0)
        {
            Metrics.Add(new GasCapacityBenchmarkMetric
            {
                Id = id,
                Value = value,
                Unit = unit,
                AllocatedBytes = allocatedBytes,
            });
        }

        public string ToJson(bool indented = true)
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = indented,
            });
        }

        public void WriteToFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("path is required", nameof(path));
            }

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static GasCapacityBenchmarkReport FromJsonFile(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            var report = JsonSerializer.Deserialize<GasCapacityBenchmarkReport>(json);
            if (report == null || report.Schema != SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Invalid gas capacity benchmark report at '{path}' (schema {SchemaVersion} required).");
            }

            return report;
        }

        public static string Compare(
            GasCapacityBenchmarkReport baseline,
            GasCapacityBenchmarkReport after,
            double regressionThreshold = DefaultRegressionThreshold)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (after == null) throw new ArgumentNullException(nameof(after));
            if (baseline.EntityCount != after.EntityCount || baseline.Iterations != after.Iterations)
            {
                throw new InvalidOperationException(
                    $"Benchmark parameters drifted: baseline entities={baseline.EntityCount}/iters={baseline.Iterations}, " +
                    $"after entities={after.EntityCount}/iters={after.Iterations}.");
            }

            var baselineById = new Dictionary<string, GasCapacityBenchmarkMetric>(StringComparer.Ordinal);
            foreach (var m in baseline.Metrics)
            {
                baselineById[m.Id] = m;
            }

            var failures = new List<string>();
            foreach (var afterMetric in after.Metrics)
            {
                if (!baselineById.TryGetValue(afterMetric.Id, out var beforeMetric))
                {
                    failures.Add($"Missing baseline metric '{afterMetric.Id}'.");
                    continue;
                }

                if (IsHigherBetter(afterMetric.Id))
                {
                    double floor = beforeMetric.Value * (1.0 - regressionThreshold);
                    if (afterMetric.Value < floor)
                    {
                        failures.Add(
                            $"{afterMetric.Id}: ops regressed {beforeMetric.Value:F2} -> {afterMetric.Value:F2} " +
                            $"(threshold {regressionThreshold:P0}).");
                    }
                }
                else
                {
                    double ceiling = beforeMetric.Value * (1.0 + regressionThreshold);
                    if (afterMetric.Value > ceiling && beforeMetric.Value > 0)
                    {
                        failures.Add(
                            $"{afterMetric.Id}: cost regressed {beforeMetric.Value:F4} -> {afterMetric.Value:F4} " +
                            $"(threshold {regressionThreshold:P0}).");
                    }
                }

                if (afterMetric.AllocatedBytes > beforeMetric.AllocatedBytes)
                {
                    failures.Add(
                        $"{afterMetric.Id}: hot-path allocated bytes grew {beforeMetric.AllocatedBytes} -> {afterMetric.AllocatedBytes}.");
                }
            }

            if (failures.Count == 0)
            {
                return "OK";
            }

            return "REGRESSION:\n- " + string.Join("\n- ", failures);
        }

        private static bool IsHigherBetter(string metricId)
        {
            return metricId.EndsWith(".hot", StringComparison.Ordinal) ||
                   metricId.Contains(".ops", StringComparison.Ordinal);
        }
    }

    public sealed class GasCapacityBenchmarkMetric
    {
        public string Id { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public long AllocatedBytes { get; set; }
    }
}
