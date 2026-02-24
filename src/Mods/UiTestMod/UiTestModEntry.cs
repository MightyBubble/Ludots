using System;
using Ludots.Core.Modding;
using UiTestMod.Triggers;

namespace UiTestMod
{
    public class UiTestModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("UiTestMod Loaded!");
            context.TriggerManager.RegisterTrigger(new UiStartTrigger());
        }

        public void OnUnload()
        {
        }
    }
}
