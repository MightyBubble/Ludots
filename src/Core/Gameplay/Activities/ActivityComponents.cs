using Arch.Core;

namespace Ludots.Core.Gameplay.Activities
{
    public enum ActivityInstanceState : byte
    {
        Pending = 1,
        Active = 2,
        Resolved = 3,
    }

    public enum ActivityDispatchPolicy : byte
    {
        Forced = 1,
        Pooled = 2,
        Automatic = 3,
    }

    public struct ActivityInstanceCm
    {
        public int DefinitionId;
        public int InstanceId;
        public ActivityInstanceState State;
        public Entity ScopeHost;
        public int SelectedOptionIndex;
        public int Revision;
    }
}
