using Arch.Core;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Tasks
{
    public static class TaskEventKeys
    {
        public static readonly EventKey Signal = new("Task.Signal");
        public static readonly EventKey Offered = new("Task.Offered");
        public static readonly EventKey Activated = new("Task.Activated");
        public static readonly EventKey Completed = new("Task.Completed");
        public static readonly EventKey Failed = new("Task.Failed");
        public static readonly EventKey Abandoned = new("Task.Abandoned");
    }

    public static class TaskServiceKeys
    {
        public static readonly ServiceKey<string> SignalId = new("Task.SignalId");
        public static readonly ServiceKey<int> SignalIntValue = new("Task.SignalIntValue");
        public static readonly ServiceKey<string> SignalStringValue = new("Task.SignalStringValue");
        public static readonly ServiceKey<string> TaskId = new("Task.TaskId");
        public static readonly ServiceKey<string> ObjectiveText = new("Task.ObjectiveText");
        public static readonly ServiceKey<Entity> TaskEntity = new("Task.Entity");
    }
}
