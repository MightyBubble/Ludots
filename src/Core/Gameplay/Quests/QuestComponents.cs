using Arch.Core;

namespace Ludots.Core.Gameplay.Quests
{
    public enum QuestState : byte
    {
        Inactive = 0,
        Active = 1,
        Completed = 2,
        Failed = 3,
    }

    public struct QuestInstanceCm
    {
        public int DefinitionId;
        public QuestState State;
        public int StageIndex;
        public Entity ScopeHost;
        public int Revision;
    }
}
