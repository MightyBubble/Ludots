using Ludots.Core.Modding;
using AIInspectorMod.Triggers;

namespace AIInspectorMod
{
    public class AIInspectorModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[AIInspectorMod] Loaded.");
            context.TriggerManager.RegisterTrigger(new PrintAiConfigTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}

