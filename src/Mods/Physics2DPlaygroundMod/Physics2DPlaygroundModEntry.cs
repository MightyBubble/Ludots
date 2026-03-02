using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Physics2DPlaygroundMod.Triggers;

namespace Physics2DPlaygroundMod
{
    public sealed class Physics2DPlaygroundModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.OnEvent(GameEvents.MapLoaded, new EnablePhysics2DPlaygroundOnEntryTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
