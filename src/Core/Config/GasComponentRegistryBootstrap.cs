using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Config
{
    public static class GasComponentRegistryBootstrap
    {
        private static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            ComponentRegistry.Register<TagCountContainer>("TagCountContainer");
            ComponentRegistry.Register<DirtyFlags>("DirtyFlags");
            ComponentRegistry.Register<BlackboardFloatBuffer>("BlackboardFloatBuffer");
            _registered = true;
        }
    }
}
