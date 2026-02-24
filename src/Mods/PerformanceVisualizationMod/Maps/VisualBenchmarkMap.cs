using System.Collections.Generic;
using Ludots.Core.Map;

namespace PerformanceVisualizationMod.Maps
{
    public class VisualBenchmarkMap : MapDefinition
    {
        public override MapId Id => VisualBenchmarkMapIds.VisualBenchmark;
        public override IReadOnlyList<MapTag> Tags => new[] { new MapTag("Benchmark"), new MapTag("Visual") };
    }
}
