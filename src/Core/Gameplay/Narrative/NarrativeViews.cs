using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Narrative
{
    public sealed record NarrativeDialogueChoiceView(
        string ChoiceId,
        string Text,
        string NextNodeId);

    public sealed record NarrativeDialogueView(
        string DialogueId,
        string DisplayName,
        string NodeId,
        string SpeakerName,
        string BodyText,
        string CameraId,
        float AutoAdvanceSeconds,
        float ElapsedSeconds,
        IReadOnlyList<NarrativeDialogueChoiceView> Choices)
    {
        public bool WaitForInput => Choices.Count == 0 && AutoAdvanceSeconds <= 0f;
        public bool AutoAdvance => Choices.Count == 0 && AutoAdvanceSeconds > 0f;
        public float Progress01 => AutoAdvanceSeconds <= 0f ? 0f : Math.Clamp(ElapsedSeconds / AutoAdvanceSeconds, 0f, 1f);
    }

    public sealed record NarrativeCinematicView(
        string CinematicId,
        string DisplayName,
        string StepId,
        string SpeakerName,
        string BodyText,
        string CameraId,
        float DurationSeconds,
        float ElapsedSeconds,
        bool RequiresAdvance)
    {
        public float Progress01 => RequiresAdvance || DurationSeconds <= 0f
            ? 0f
            : Math.Clamp(ElapsedSeconds / DurationSeconds, 0f, 1f);
    }
}
