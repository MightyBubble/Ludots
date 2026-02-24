using Ludots.Core.Modding;
using Navigation2DPlaygroundMod.Triggers;

namespace Navigation2DPlaygroundMod
{
    public sealed class Navigation2DPlaygroundModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.TriggerManager.RegisterTrigger(new EnableNavigation2DPlaygroundOnEntryTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}

