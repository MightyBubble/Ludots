using Ludots.Core.Config;

namespace PerformerBlacksmithShowcaseMod.Runtime
{
    internal static class PerformerBlacksmithShowcaseComponentAuthoring
    {
        public static void Register()
        {
            ComponentRegistry.Register<DynamicWorkerCrowdTag>("DynamicWorkerCrowdTag");
        }
    }
}
