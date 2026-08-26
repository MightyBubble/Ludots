namespace NarrativeShowcaseMod
{
    public static class NarrativeShowcaseIds
    {
        public const string MapId = "narrative_showcase_hub";
        public const string PlayerName = "Arcweaver";
        public const string ElderName = "WardenMirelle";
        public const string ShrineName = "EmberShrine";
        public const string BeastName = "AshenBeast";
        public const string SpawnedBeastTemplateName = "EnemyBruiser";

        public const string PlayerAlias = "player";
        public const string ElderAlias = "elder";
        public const string ShrineAlias = "shrine";
        public const string BeastAlias = "beast";
        public const string WardenSpeakerId = "speaker.warden";
        public const string ShrineSpeakerId = "speaker.shrine";
        public const string PlayerSpeakerId = "speaker.player";

        public const string BriefingTaskId = "Task.Narrative.AshenOath.Briefing";
        public const string TrialTaskId = "Task.Narrative.AshenOath.Trial";
        public const string ReturnTaskId = "Task.Narrative.AshenOath.Return";
        public const string BriefingDialogueId = "Dialogue.Narrative.Briefing";
        public const string ReturnDialogueId = "Dialogue.Narrative.Return";
        public const string IntroSequenceId = "Sequence.Narrative.Intro";
        public const string TrialRevealSequenceId = "Sequence.Narrative.TrialReveal";

        public const string SpawnBeastSignal = "showcase.spawn_beast";
        public const string BeastDefeatedSignal = "showcase.beast_defeated";
        public const string RewardSignal = "showcase.reward_apply";

        public const string TrustVariableId = "trust";
        public const string LoreVariableId = "lore";
        public const string EndingVariableId = "ending";
        public const string TrialPhaseVariableId = "trial_phase";

        public const int EndingUnwritten = 0;
        public const int EndingDuty = 1;
        public const int EndingMercy = 2;

        public const string PresentationDialogueOverlay = "story.dialogue_overlay";
        public const string PresentationWorldBubble = "story.world_bubble";
        public const string PresentationImmersiveSubtitle = "story.immersive_subtitle";

        public const string ActiveMapKey = "NarrativeShowcase.ActiveMap";
        public const string BootstrappedKey = "NarrativeShowcase.Bootstrapped";
        public const string BeastSpawnedKey = "NarrativeShowcase.BeastSpawned";
        public const string BeastDefeatedKey = "NarrativeShowcase.BeastDefeated";
        public const string RewardAppliedKey = "NarrativeShowcase.RewardApplied";

        public const string IntroElderCameraId = "Narrative.Intro.Elder";
        public const string IntroShrineCameraId = "Narrative.Intro.Shrine";
        public const string TrialRevealCameraId = "Narrative.Trial.Reveal";
        public const string ReturnCameraId = "Narrative.Return.Close";
    }
}
