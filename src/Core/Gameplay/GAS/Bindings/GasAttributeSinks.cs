using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public static class GasSinkNames
    {
        public const string ForceInput2D = "Physics.ForceInput2D";
        public const string CameraBehaviorInput = "Camera.BehaviorInput";
    }

    public static class GasAttributeSinks
    {
        public static void RegisterBuiltins(
            AttributeSinkRegistry sinks,
            CameraBehaviorInputState? cameraBehaviorInput = null)
        {
            sinks.Register(GasSinkNames.ForceInput2D, new ForceInput2DSink());
            if (cameraBehaviorInput != null)
            {
                sinks.Register(
                    GasSinkNames.CameraBehaviorInput,
                    new CameraBehaviorInputSink(cameraBehaviorInput));
            }
        }
    }
}
