using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Quests;
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
            context.OnEvent(QuestEventKeys.StageChanged, runtime.HandleQuestStageChangedAsync);
            context.OnEvent(QuestEventKeys.Completed, runtime.HandleQuestCompletedAsync);
            context.OnEvent(NarrativeEventKeys.DialogueNodeEntered, runtime.HandleDialogueNodeEnteredAsync);
            context.OnEvent(NarrativeEventKeys.DialogueChoiceCommitted, runtime.HandleDialogueChoiceCommittedAsync);
            context.OnEvent(NarrativeEventKeys.CinematicStepEntered, runtime.HandleCinematicStepEnteredAsync);
            context.OnEvent(QuestEventKeys.Signal, runtime.HandleQuestSignalAsync);
            context.OnEvent(NarrativeEventKeys.CinematicCompleted, runtime.HandleCinematicCompletedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
