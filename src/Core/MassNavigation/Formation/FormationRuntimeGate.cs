using Ludots.Core.Engine;

namespace Ludots.Core.MassNavigation.Formation;

public interface IFormationRuntimeGate
{
    bool IsFormationRuntimeActive(GameEngine engine);
}
