using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Sequencer
{
    public sealed record SequenceSubtitleView(
        string LineId,
        string PresentationProfile,
        string ResolvedText,
        string SpeakerId,
        string ResolvedSpeakerName,
        float Start,
        float Duration,
        float LocalElapsed);

    public sealed record SequenceView(
        string SequenceId,
        string DisplayName,
        float Time,
        float Rate,
        bool Paused,
        bool Playing,
        string ActiveCameraProfile,
        IReadOnlyList<SequenceSubtitleView> ActiveSubtitles);
}
