using Ludots.Core.Scripting;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation;

public static class MassNavigationKeys
{
    public static readonly ServiceKey<MassNavigationRuntimeBinding> RuntimeBinding = new("MassNavigation_RuntimeBinding");
    public static readonly ServiceKey<MassNavigationSimulationRuntime> SimulationRuntime = new("MassNavigation_SimulationRuntime");
    public static readonly ServiceKey<IMassNavigationSceneController> SceneController = new("MassNavigation_SceneController");
    public static readonly ServiceKey<MassNavigationRouteExecutionSink> RouteExecutionSink = new("MassNavigation_RouteExecutionSink");
}
