namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public static class GasSinkNames
    {
        public const string ForceInput2D = "Physics.ForceInput2D";
    }

    public static class GasAttributeSinks
    {
        public static void RegisterBuiltins(AttributeSinkRegistry sinks)
        {
            sinks.Register(GasSinkNames.ForceInput2D, new ForceInput2DSink());
        }
    }
}
