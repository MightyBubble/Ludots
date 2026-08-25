using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Single-consumption guard for <see cref="AssetBindingConfig.LocalOffset"/> within one emit visit.
    /// The root transform must never bake a slot's local offset in; only the asset emit stage consumes it.
    /// </summary>
    internal static class PresenterLocalOffsetConsumption
    {
        public static void MarkSlotConsumed(
            int slotIndex,
            in AssetBindingConfig asset,
            int presenterDefinitionId,
            ref uint consumedMask)
        {
            if (slotIndex is < 0 or > 31 || asset.LocalOffset == Vector3.Zero)
            {
                return;
            }

            uint slotBit = 1u << slotIndex;
            if ((consumedMask & slotBit) != 0u)
            {
                throw new InvalidOperationException(
                    $"Presenter definition {presenterDefinitionId} slot {slotIndex} consumed AssetBinding.localOffset twice in one emit visit; the resolved root transform must not pre-apply a slot local offset.");
            }

            consumedMask |= slotBit;
        }
    }
}
