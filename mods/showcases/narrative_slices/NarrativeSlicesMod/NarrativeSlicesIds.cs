namespace NarrativeSlicesMod
{
    public static class NarrativeSlicesIds
    {
        public const string MapId = "narrative_slices_hub";
        public const string InputContextId = "NarrativeSlices.Controls";

        public const string SliceDialogueGate = "dialogue_gate";
        public const string SliceActionGallery = "action_gallery";
        public const string SliceTaskRules = "task_rules";
        public const string SliceTaskChain = "task_chain";
        public const string SliceActivityExecuteCondition = "activity_execute_condition";
        public const string SliceSubtitlePresenter = "subtitle_presenter";
        public const string SlicePresenterTrack = "presenter_track";
        public const string SliceMapVariableWrite = "map_variable_write";

        public const string GateDialogueId = "Dialogue.Slice.Gate";
        public const string GalleryDialogueId = "Dialogue.Slice.Gallery";
        public const string MapTriggerDialogueId = "Dialogue.Slice.MapTrigger";
        public const string MapEvenDialogueId = "Dialogue.Slice.MapEven";
        public const string MapOddDialogueId = "Dialogue.Slice.MapOdd";
        public const string GalleryAlphaTaskId = "Slice.Gallery.Alpha";
        public const string GalleryBetaTaskId = "Slice.Gallery.Beta";
        public const string RulesAnyCheckTaskId = "Slice.Rules.AnyCheck";
        public const string ChainOneTaskId = "Slice.Chain.One";
        public const string ChainTwoTaskId = "Slice.Chain.Two";
        public const string ChainIntroCinematicId = "Cinematic.Slice.ChainIntro";
        public const string SubtitleCinematicId = "Cinematic.Slice.Subtitle";
        public const string TrackCinematicId = "Cinematic.Slice.Track";
        public const string ActivitySliceExecuteId = "Slice.Execute";
        public const string ActivityOptionGoId = "opt_go";
        public const string ActivityOptionWaitId = "opt_wait";

        public const string GateRootNodeId = "gate_root";
        public const string ChoiceOpenYes = "open_yes";
        public const string ChoiceOpenLocked = "open_locked";

        public const string SignalGateGranted = "slice.gate.granted";
        public const string SignalGateFinished = "slice.gate.finished";
        public const string SignalGalleryDone = "slice.gallery.done";
        public const string SignalRulesFirst = "rules.first";
        public const string SignalRulesSecond = "rules.second";
        public const string SignalChainOneDone = "chain.one.done";
        public const string SignalMapWrite = "slice.map.write";

        public const string GalleryLoreVariableId = "gallery_lore";
        public const string GallerySliceVariableId = "slice_var";
        public const string MapVariableSliceCounter = "slice_counter";

        public const string MapDefaultCameraId = "Camera.Profile.Tactical";
        public const string GalleryInspectCameraId = "Camera.Profile.Inspect";

        public const string FrontendOwnerId = "NarrativeSlices";
    }
}
