using System;
using System.Collections.Generic;
using Ludots.Core.Map;

namespace PerformanceMod.Maps
{
    public class BenchmarkMap : MapDefinition
    {
        public override MapId Id => PerformanceMapIds.Benchmark;
        public override IReadOnlyList<MapTag> Tags => new[] { PerformanceMapTags.Benchmark };
    }
}
