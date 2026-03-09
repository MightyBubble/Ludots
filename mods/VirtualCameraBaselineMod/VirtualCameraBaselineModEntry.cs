using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using VirtualCameraBaselineMod.Triggers;

namespace VirtualCameraBaselineMod
{
    public sealed class VirtualCameraBaselineModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[VirtualCameraBaselineMod] Loaded");
            context.OnEvent(GameEvents.MapLoaded, new VirtualCameraBaselineOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
