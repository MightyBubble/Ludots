using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using AIInspectorMod.Triggers;

namespace AIInspectorMod
{
    public class AIInspectorModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[AIInspectorMod] Loaded.");
            context.OnEvent(AIInspectorEvents.PrintAiConfig, new PrintAiConfigTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}

