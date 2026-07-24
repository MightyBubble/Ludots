using Ludots.Core.Scripting;

namespace Ludots.Core.Physics3D;

public static class Physics3DServiceKeys
{
    public static readonly ServiceKey<IPhysics3DWorld> World = new("Physics3D.World");
    public static readonly ServiceKey<Physics3DSimulationSystem> SimulationSystem = new("Physics3D.SimulationSystem");
}
