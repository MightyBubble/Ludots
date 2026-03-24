using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Relationships
{
    public enum RelationshipBandComparison : byte
    {
        GreaterOrEqual = 0,
        LessOrEqual = 1,
    }

    public readonly struct RelationshipBandDefinition
    {
        public RelationshipBandDefinition(int typeId, int metricId, int flagId, short threshold, RelationshipBandComparison comparison)
        {
            TypeId = typeId;
            MetricId = metricId;
            FlagId = flagId;
            Threshold = threshold;
            Comparison = comparison;
        }

        public int TypeId { get; }
        public int MetricId { get; }
        public int FlagId { get; }
        public short Threshold { get; }
        public RelationshipBandComparison Comparison { get; }
    }

    public sealed class RelationshipBandRegistry
    {
        private readonly List<RelationshipBandDefinition> _bands = new();

        public IReadOnlyList<RelationshipBandDefinition> Bands => _bands;

        public void Register(RelationshipBandDefinition definition)
        {
            _bands.Add(definition);
        }

        public void Clear()
        {
            _bands.Clear();
        }

    }
}
