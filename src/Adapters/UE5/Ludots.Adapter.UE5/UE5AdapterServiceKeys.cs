using Ludots.Core.Scripting;

namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// Adapter-owned service keys for generic UE5 host extensions.
    /// </summary>
    public static class UE5AdapterServiceKeys
    {
        public static readonly ServiceKey<IHostLevelNavigator> HostLevelNavigator = new("UE5.HostLevelNavigator");
        public static readonly ServiceKey<HostWorldBindingSnapshot> HostWorldBindingState = new("UE5.HostWorldBindingState");
        public static readonly ServiceKey<string> HostConfiguredMainMenuWorldName = new("UE5.HostConfiguredMainMenuWorldName");
    }
}
