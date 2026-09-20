using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Tasks
{
    public enum TaskPresentationCueKind : byte
    {
        Offered = 1,
        Progress = 2,
        Completed = 3,
        Failed = 4,
        Abandoned = 5,
    }

    public readonly record struct TaskPresentationCue(
        TaskPresentationCueKind Kind,
        string TaskId,
        int InstanceId,
        string ObjectiveId,
        string Reason);

    public sealed class TaskPresentationBuffer
    {
        private readonly List<TaskPresentationCue> _cues = new(32);

        public IReadOnlyList<TaskPresentationCue> Cues => _cues;

        public void Clear() => _cues.Clear();

        public void Add(in TaskPresentationCue cue) => _cues.Add(cue);
    }
}
