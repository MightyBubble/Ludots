using System;
using Ludots.Core.Modding;
using HtmlTestMod.Triggers;

namespace HtmlTestMod
{
    public class HtmlTestModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("HtmlTestMod Loaded!");
            context.TriggerManager.RegisterTrigger(new HtmlStartTrigger());
        }

        public void OnUnload()
        {
        }
    }
}
