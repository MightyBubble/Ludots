using System;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiTestMod.Triggers;

namespace UiTestMod
{
    public class UiTestModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("UiTestMod Loaded!");
            var uiTrigger = new UiStartTrigger();
            context.OnEvent(GameEvents.MapLoaded, ctx => uiTrigger.CheckConditions(ctx) ? uiTrigger.ExecuteAsync(ctx) : Task.CompletedTask);
        }

        public void OnUnload()
        {
        }
    }
}
