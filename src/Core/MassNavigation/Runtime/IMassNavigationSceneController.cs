using Ludots.Core.Engine;

namespace Ludots.Core.MassNavigation.Runtime;

public interface IMassNavigationSceneController
{
    void PopulateScene(GameEngine engine, MassNavigationSimulationRuntime simulation);
}
