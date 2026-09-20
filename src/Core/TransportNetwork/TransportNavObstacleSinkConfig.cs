using System;
using System.Collections.Generic;

namespace Ludots.Core.TransportNetwork
{
    public enum TransportNavObstacleGeometryKind : byte
    {
        Corridor = 0,
        FilledRing = 1
    }

    public sealed class TransportNavObstacleSinkConfig
    {
        public List<TransportNavObstacleSinkRule> Rules { get; set; } = new();

        public void Validate()
        {
            if (Rules == null)
            {
                throw new InvalidOperationException("TransportNavObstacleSinkConfig.rules must be an explicit array.");
            }

            if (Rules.Count == 0)
            {
                throw new InvalidOperationException("TransportNavObstacleSinkConfig.rules must contain at least one rule.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Rules.Count; i++)
            {
                TransportNavObstacleSinkRule rule = Rules[i]
                    ?? throw new InvalidOperationException($"TransportNavObstacleSinkConfig.rules[{i}] is null.");
                rule.Validate(i);
                if (!ids.Add(rule.Id))
                {
                    throw new InvalidOperationException($"TransportNavObstacleSinkConfig contains duplicate rule id '{rule.Id}'.");
                }
            }
        }
    }

    public sealed class TransportNavObstacleSinkRule
    {
        public string Id { get; set; } = string.Empty;
        public List<string> RequiredTagsAll { get; set; } = new();
        public List<string> ForbiddenTagsAny { get; set; } = new();
        public string LayerId { get; set; } = "Ground";
        public string WidthSource { get; set; } = "widthCm";
        public int MinWidthCm { get; set; }
        public int SampleStepCm { get; set; }
        public bool CapEnds { get; set; } = true;
        public TransportNavObstacleGeometryKind Geometry { get; set; } = TransportNavObstacleGeometryKind.Corridor;

        public void Validate(int index)
        {
            string path = $"TransportNavObstacleSinkConfig.rules[{index}]";
            if (string.IsNullOrWhiteSpace(Id) || !string.Equals(Id.Trim(), Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.id must be a non-empty canonical string.");
            }

            TransportNetworkValidation.RequireCanonicalTags(RequiredTagsAll, $"{path}.requiredTagsAll");
            TransportNetworkValidation.RequireCanonicalTags(ForbiddenTagsAny, $"{path}.forbiddenTagsAny");

            if (string.IsNullOrWhiteSpace(LayerId) || !string.Equals(LayerId.Trim(), LayerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.layerId must be a non-empty canonical string.");
            }

            if (!string.Equals(WidthSource, "widthCm", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{path}.widthSource must be 'widthCm' (visualWidthMeters is presentation-only and cannot drive nav carve).");
            }

            if (MinWidthCm < 0)
            {
                throw new InvalidOperationException($"{path}.minWidthCm must be >= 0.");
            }

            if (SampleStepCm < 0)
            {
                throw new InvalidOperationException($"{path}.sampleStepCm must be >= 0.");
            }

            if (Geometry != TransportNavObstacleGeometryKind.Corridor &&
                Geometry != TransportNavObstacleGeometryKind.FilledRing)
            {
                throw new InvalidOperationException($"{path}.geometry '{Geometry}' is not supported.");
            }

            if (Geometry == TransportNavObstacleGeometryKind.Corridor && SampleStepCm <= 0)
            {
                throw new InvalidOperationException($"{path}.sampleStepCm must be > 0 for Corridor geometry.");
            }
        }
    }
}
