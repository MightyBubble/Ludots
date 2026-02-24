using Ludots.Core.Modding;
using Physics2DPlaygroundMod.Triggers;

namespace Physics2DPlaygroundMod
{
    public sealed class Physics2DPlaygroundModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.TriggerManager.RegisterTrigger(new EnablePhysics2DPlaygroundOnEntryTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}
