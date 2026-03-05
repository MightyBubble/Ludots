using System.Collections.Generic;
using Ludots.Core.Map;

namespace GasBenchmarkMod.Maps
{
    public class GasBenchmarkMap : MapDefinition
    {
        public override MapId Id => GasBenchmarkMapIds.GasBenchmark;
        public override IReadOnlyList<MapTag> Tags => new[] { MapTags.Benchmark };
        public override string DataFilePath => "maps/gas_benchmark.json";
    }
}
