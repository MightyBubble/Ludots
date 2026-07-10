using Arch.Core;

namespace SuperweaponContextShowcaseMod.Runtime
{
    public sealed class SuperweaponContextShowcaseState
    {
        public Entity LocalPlayer { get; internal set; } = Entity.Null;
        public Entity Commander { get; internal set; } = Entity.Null;
        public Entity Arcweaver { get; internal set; } = Entity.Null;
        public Entity Vanguard { get; internal set; } = Entity.Null;
        public int AbilityId { get; internal set; }
        public bool IsActive { get; internal set; }
        public int RoutedTargetCount { get; internal set; }
        public bool ConfirmInputObserved { get; internal set; }
        public bool ConfirmEventPublished { get; internal set; }
        public int ConfirmEventCount { get; internal set; }
        public uint Revision { get; internal set; }
    }
}
