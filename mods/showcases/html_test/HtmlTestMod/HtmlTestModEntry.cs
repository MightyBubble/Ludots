using System;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using HtmlTestMod.Triggers;

namespace HtmlTestMod
{
    public class HtmlTestModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("HtmlTestMod Loaded!");
            context.OnEvent(GameEvents.MapLoaded, new HtmlStartTrigger().ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
