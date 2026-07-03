namespace Ludots.Core.Gameplay.Lifecycle
{
    /// <summary>Layer 0 entity lifecycle atomic operations.</summary>
    public enum LifecycleOpId : byte
    {
        MaterializeTemplate = 0,
        CopyIdentityComponents = 1,
        CopyAttributeSlice = 2,
        ClearActiveEffects = 3,
        TransferStableId = 4,
        RewireSelection = 5,
        ConsumeEntity = 6,
    }
}
