using System;

namespace Ludots.Core.Gameplay.Relationships
{
    public struct RelationshipEdge
    {
        private short[]? _metrics;

        public uint Flags;
        public int Version;
        public ReadOnlySpan<short> Metrics => _metrics;

        public short GetMetric(int metricId)
        {
            return metricId < 0
                ? throw new ArgumentOutOfRangeException(nameof(metricId), $"Relationship metric id {metricId} is out of range.")
                : _metrics != null && metricId < _metrics.Length
                    ? _metrics[metricId]
                    : (short)0;
        }

        public void SetMetric(int metricId, short value)
        {
            if (metricId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(metricId), $"Relationship metric id {metricId} is out of range.");
            }

            EnsureMetricCapacity(metricId + 1);
            _metrics![metricId] = value;
        }

        public bool EnsureMetricCapacity(RelationshipMetricRegistry metrics)
        {
            ArgumentNullException.ThrowIfNull(metrics);

            if (metrics.Count <= 0)
            {
                return false;
            }

            int previousLength = _metrics?.Length ?? 0;
            if (previousLength >= metrics.Count)
            {
                return false;
            }

            EnsureMetricCapacity(metrics.Count);
            for (int i = previousLength; i < metrics.Count; i++)
            {
                _metrics![i] = metrics.Get(i).DefaultValue;
            }

            return true;
        }

        private void EnsureMetricCapacity(int requiredCount)
        {
            if (requiredCount <= 0)
            {
                return;
            }

            if (_metrics == null)
            {
                _metrics = new short[requiredCount];
                return;
            }

            if (_metrics.Length < requiredCount)
            {
                Array.Resize(ref _metrics, requiredCount);
            }
        }

        public static RelationshipEdge CreateDefault(RelationshipMetricRegistry metrics)
        {
            var edge = default(RelationshipEdge);
            if (metrics.Count <= 0)
            {
                return edge;
            }

            edge._metrics = new short[metrics.Count];
            for (int i = 0; i < metrics.Count; i++)
            {
                edge._metrics[i] = metrics.Get(i).DefaultValue;
            }

            return edge;
        }
    }
}
