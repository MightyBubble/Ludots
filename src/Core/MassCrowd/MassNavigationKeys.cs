using Ludots.Core.Scripting;
using Ludots.Core.MassCrowd.Runtime;

namespace Ludots.Core.MassCrowd;

public static class MassNavigationKeys
{
    public static readonly ServiceKey<MassNavigationSimulationRuntime> SimulationRuntime = new("MassNavigation_SimulationRuntime");
    public static readonly ServiceKey<MassNavigationRouteExecutionSink> RouteExecutionSink = new("MassNavigation_RouteExecutionSink");
}
