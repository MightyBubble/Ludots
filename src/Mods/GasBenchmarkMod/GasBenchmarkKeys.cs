using Ludots.Core.Scripting;
using Ludots.Core.Map;

namespace GasBenchmarkMod
{
    public static class GasBenchmarkEvents
    {
        public static readonly EventKey RunGasBenchmark = new EventKey("RunGasBenchmark");
    }

    public static class GasBenchmarkMapIds
    {
        public static readonly MapId GasBenchmark = new MapId("gas_benchmark");
    }
}
