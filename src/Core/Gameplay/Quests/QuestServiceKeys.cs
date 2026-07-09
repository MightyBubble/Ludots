using Arch.Core;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Quests
{
    public static class QuestEventKeys
    {
        public static readonly EventKey Signal = new("Quest.Signal");
        public static readonly EventKey Started = new("Quest.Started");
        public static readonly EventKey StageChanged = new("Quest.StageChanged");
        public static readonly EventKey Completed = new("Quest.Completed");
        public static readonly EventKey Failed = new("Quest.Failed");
    }

    public static class QuestServiceKeys
    {
        public static readonly ServiceKey<string> SignalId = new("Quest.SignalId");
        public static readonly ServiceKey<int> SignalIntValue = new("Quest.SignalIntValue");
        public static readonly ServiceKey<string> SignalStringValue = new("Quest.SignalStringValue");
        public static readonly ServiceKey<string> QuestId = new("Quest.QuestId");
        public static readonly ServiceKey<string> StageId = new("Quest.StageId");
        public static readonly ServiceKey<string> ObjectiveText = new("Quest.ObjectiveText");
        public static readonly ServiceKey<Entity> QuestEntity = new("Quest.Entity");
    }
}
