namespace NarrativeChainShowcaseMod
{
    public static class NarrativeChainIds
    {
        public const string MapId = "narrative_chain_hub";
        public const string InputContextId = "NarrativeChain.Controls";

        public const string OpeningDialogueId = "Dialogue.Chain.Opening";
        public const string VerdictDialogueId = "Dialogue.Chain.Verdict";
        public const string RevealCinematicId = "Cinematic.Chain.Reveal";
        public const string SurveyTaskId = "Task.Chain.Survey";
        public const string DebriefTaskId = "Task.Chain.Debrief";
        public const string DecideActivityId = "activity.chain.decide";

        public const string HubDefaultCameraId = "Camera.Profile.Inspect";
        public const string RevealStepCameraId = "Camera.Profile.Tactical";

        public const string SignalOpened = "chain.opened";
        public const string SignalSetAlarm = "chain.cmd.set_alarm";
        public const string SignalHerald = "chain.event.herald";
        public const string SignalFinished = "chain.finished";
        public const string SignalObjectiveDone = "chain.objective.done";

        public const string NarrativeVariableLore = "chain.lore";
        public const string MapVariableAlarms = "chain_alarms";

        public const string FrontendOwnerId = "NarrativeChain";

        public const string ActivityConfirmActionId = "ActivityConfirm";
        public const string ActivityDeclineActionId = "ActivityDecline";
        public const string ActivityOptionConfirm = "confirm";
        public const string ActivityOptionDecline = "decline";

        public const string HudManifestResourceUri = "NarrativeChainShowcaseMod:assets/PanelKit/chain_hud_manifest.json";
        public const string HudActivityTopic = "chain.topic.activity";
        public const string HudObjectiveTopic = "chain.topic.objective";
    }
}
