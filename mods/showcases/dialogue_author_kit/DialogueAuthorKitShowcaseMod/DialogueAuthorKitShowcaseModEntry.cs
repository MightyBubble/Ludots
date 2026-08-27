using DialogueAuthorKitShowcaseMod.Runtime;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace DialogueAuthorKitShowcaseMod
{
    public sealed class DialogueAuthorKitShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[DialogueAuthorKitShowcaseMod] Loaded");
            var runtime = new DialogueAuthorKitRuntime(context);

            context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
            context.OnEvent(DialogueEventKeys.NodeEntered, runtime.HandleDialogueChangedAsync);
            context.OnEvent(DialogueEventKeys.ChoiceCommitted, runtime.HandleDialogueChangedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
