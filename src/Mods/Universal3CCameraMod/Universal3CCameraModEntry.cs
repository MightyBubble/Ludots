using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Universal3CCameraMod.Triggers;

namespace Universal3CCameraMod
{
    public sealed class Universal3CCameraModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[Universal3CCameraMod] Loaded");
            context.TriggerManager.RegisterTrigger(new Universal3CCameraOnMapLoadedTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}
