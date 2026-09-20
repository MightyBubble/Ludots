using Arch.Core;

namespace Ludots.Core.Gameplay.Tasks
{
    public enum TaskInstanceState : byte
    {
        Offered = 1,
        Active = 2,
        Completed = 3,
        Failed = 4,
        Abandoned = 5,
    }

    public enum TaskStartPolicy : byte
    {
        PlayerAccept = 1,
        Automatic = 2,
    }

    public enum TaskCompletionRule : byte
    {
        All = 1,
        Any = 2,
    }

    public enum TaskObjectiveKind : byte
    {
        Signal = 1,
        Count = 2,
        Accumulate = 3,
        Condition = 4,
    }

    public struct TaskInstanceCm
    {
        public int DefinitionId;
        public int InstanceId;
        public TaskInstanceState State;
        public Entity ScopeHost;
        public int ObjectiveMask;
        public int Revision;
    }
}
