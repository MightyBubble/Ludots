using Ludots.Core.Config;

namespace PresenterTreeOverrideShowcaseMod.Runtime
{
    internal static class PresenterTreeShowcaseComponentAuthoring
    {
        public static void Register(string modId)
        {
            ComponentRegistry.Register<PresenterTreeOrbitMotion>("PresenterTreeOrbitMotion", modId);
            ComponentRegistry.Register<PresenterTreeRideHostTag>("PresenterTreeRideHostTag", modId);
            ComponentRegistry.Register<PresenterTreeRiderTag>("PresenterTreeRiderTag", modId);
        }
    }
}
