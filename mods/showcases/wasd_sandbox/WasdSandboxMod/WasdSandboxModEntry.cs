using Ludots.Core.Modding;

namespace WasdSandboxMod
{
    /// <summary>
    /// Data-only sandbox: the seat activates scheme.wasd_move at startup, the map spawns the core
    /// moba_hero template, and Camera.Profile.Follow tracks the sole possessed rep. No runtime code.
    /// </summary>
    public sealed class WasdSandboxModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[WasdSandboxMod] Loaded - minimal WASD move sandbox");
        }

        public void OnUnload()
        {
        }
    }
}
