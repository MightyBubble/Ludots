using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Activities
{
    public enum ActivityPresentationCueKind : byte
    {
        Presented = 1,
        OptionBlocked = 2,
        Resolved = 3,
        AutomaticSettled = 4,
    }

    public readonly record struct ActivityPresentationCue(
        ActivityPresentationCueKind Kind,
        string ActivityId,
        int InstanceId,
        string OptionId,
        string Reason);

    public sealed class ActivityPresentationBuffer
    {
        private readonly List<ActivityPresentationCue> _cues = new(32);

        public IReadOnlyList<ActivityPresentationCue> Cues => _cues;

        public void Clear() => _cues.Clear();

        public void Add(in ActivityPresentationCue cue) => _cues.Add(cue);
    }
}
