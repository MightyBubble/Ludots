using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Quests;

namespace Ludots.Core.Gameplay.Tasks
{
    /// <summary>
    /// Read-only Quest-shaped projection over TaskInstance progress.
    /// Dual write of Quest progress alongside Task progress is forbidden.
    /// </summary>
    public sealed class TaskQuestAdapter
    {
        public const string DualProgressStoreCode = "dual_progress_store";

        private readonly TaskRuntimeService _tasks;

        public TaskQuestAdapter(TaskRuntimeService tasks)
        {
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public IReadOnlyList<QuestLikeView> CaptureQuestLikeViews()
        {
            List<TaskView> taskViews = _tasks.CaptureViews();
            var result = new List<QuestLikeView>(taskViews.Count);
            for (int i = 0; i < taskViews.Count; i++)
            {
                TaskView task = taskViews[i];
                result.Add(new QuestLikeView(
                    task.TaskId,
                    task.DisplayName,
                    MapState(task.State),
                    BuildObjectiveText(task),
                    task.InstanceId));
            }

            return result;
        }

        public static void GuardAgainstDualProgressStore(
            IReadOnlyCollection<string> questActiveIds,
            IReadOnlyCollection<string> taskActiveIds)
        {
            ArgumentNullException.ThrowIfNull(questActiveIds);
            ArgumentNullException.ThrowIfNull(taskActiveIds);

            var overlap = new List<string>();
            foreach (string questId in questActiveIds)
            {
                foreach (string taskId in taskActiveIds)
                {
                    if (string.Equals(questId, taskId, StringComparison.OrdinalIgnoreCase))
                    {
                        overlap.Add(questId);
                    }
                }
            }

            if (overlap.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{DualProgressStoreCode}: progress for '{string.Join(",", overlap)}' exists in both Quest and Task stores.");
            }
        }

        private static string MapState(TaskInstanceState state) =>
            state switch
            {
                TaskInstanceState.Offered => "Offered",
                TaskInstanceState.Active => nameof(QuestState.Active),
                TaskInstanceState.Completed => nameof(QuestState.Completed),
                TaskInstanceState.Failed => nameof(QuestState.Failed),
                TaskInstanceState.Abandoned => "Abandoned",
                _ => state.ToString(),
            };

        private static string BuildObjectiveText(TaskView task)
        {
            if (task.Objectives.Count == 0)
            {
                return string.Empty;
            }

            TaskObjectiveProgressView first = task.Objectives[0];
            return string.IsNullOrWhiteSpace(first.Title) ? first.ObjectiveId : first.Title;
        }
    }

    public readonly record struct QuestLikeView(
        string QuestId,
        string DisplayName,
        string State,
        string ObjectiveText,
        int InstanceId);
}
