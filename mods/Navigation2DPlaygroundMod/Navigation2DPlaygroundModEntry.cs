using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Navigation2DPlaygroundMod.Triggers;

namespace Navigation2DPlaygroundMod
{
    public sealed class Navigation2DPlaygroundModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.OnEvent(GameEvents.MapLoaded, new EnableNavigation2DPlaygroundOnEntryTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}

