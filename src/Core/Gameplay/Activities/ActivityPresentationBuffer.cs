using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Activities
{
    public static class ActivityLifecycleKeys
    {
        public const string Started = "activity.started";
        public const string Presented = "activity.presented";
        public const string OptionSelected = "activity.option_selected";
        public const string Settled = "activity.settled";
        public const string Archived = "activity.archived";

        public static bool IsLifecycleKey(string key) => key is
            Started or Presented or OptionSelected or Settled or Archived;
    }

    public readonly record struct ActivityLifecycleEvent(
        string Key,
        string ActivityId,
        int InstanceId,
        string OptionId,
        int ScopeKey);

    public sealed class ActivityLifecycleBuffer
    {
        private readonly List<ActivityLifecycleEvent> _events = new(32);

        public IReadOnlyList<ActivityLifecycleEvent> Events => _events;

        public void Clear() => _events.Clear();

        public void Add(in ActivityLifecycleEvent lifecycleEvent) => _events.Add(lifecycleEvent);
    }

    public enum ActivityPresentationCueKind : byte
    {
        Presented = 1,
        OptionBlocked = 2,
        Resolved = 3,
        AutomaticSettled = 4,
        AdmissionRejected = 5,
    }

    public readonly record struct ActivityPresentationCue(
        ActivityPresentationCueKind Kind,
        string ActivityId,
        int InstanceId,
        string OptionId,
        string Reason,
        int ScopeKey = 0);

    public sealed class ActivityPresentationBuffer
    {
        private readonly List<ActivityPresentationCue> _cues = new(32);

        public IReadOnlyList<ActivityPresentationCue> Cues => _cues;

        public void Clear() => _cues.Clear();

        public void Add(in ActivityPresentationCue cue) => _cues.Add(cue);
    }
}
