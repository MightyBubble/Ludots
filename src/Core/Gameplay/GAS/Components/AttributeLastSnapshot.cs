using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Per-entity last-seen attribute currents for deferred triggers.
    /// Sized to absolute max so the component layout stays fixed; live ids are validated against the plan.
    /// Values live beside the world store (handle) model rather than a second dynamic path.
    /// </summary>
    public unsafe struct AttributeLastSnapshot
    {
        public const int Capacity = GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots;

        public fixed float Values[Capacity];
    }
}
