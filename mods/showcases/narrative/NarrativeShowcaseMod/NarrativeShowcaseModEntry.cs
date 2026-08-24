using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NarrativeShowcaseMod.Runtime;

namespace NarrativeShowcaseMod
{
    public sealed class NarrativeShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[NarrativeShowcaseMod] Loaded");
            var runtime = new NarrativeShowcaseRuntime(context);

            context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
            context.OnEvent(TaskEventKeys.Activated, runtime.HandleTaskActivatedAsync);
            context.OnEvent(TaskEventKeys.Completed, runtime.HandleTaskCompletedAsync);
            context.OnEvent(NarrativeEventKeys.DialogueNodeEntered, runtime.HandleDialogueNodeEnteredAsync);
            context.OnEvent(NarrativeEventKeys.DialogueChoiceCommitted, runtime.HandleDialogueChoiceCommittedAsync);
            context.OnEvent(NarrativeEventKeys.CinematicStepEntered, runtime.HandleCinematicStepEnteredAsync);
            context.OnEvent(TaskEventKeys.Signal, runtime.HandleTaskSignalAsync);
            context.OnEvent(NarrativeEventKeys.CinematicCompleted, runtime.HandleCinematicCompletedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
