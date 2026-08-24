using System;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Entity-side carrier for child instance behaviors. Slots execute on the existing
    /// PresenterBehaviorSystem lanes after the referenced definition's own behaviors; the shared
    /// <see cref="PresenterDefinition.Behaviors"/> array is never appended to.
    /// </summary>
    public struct PresenterInstanceBehaviors
    {
        public BehaviorSlot[] Slots;
        public int[] ExtensionBootstrapIndices;
        public int[] ExtensionTickIndices;
        public uint PresenceMask;
        public uint DefaultActiveMask;
        public bool HasSound;

        public static PresenterInstanceBehaviors Compile(BehaviorSlot[] slots)
        {
            if (slots == null || slots.Length == 0)
            {
                throw new InvalidOperationException(
                    "Presenter instance behaviors require at least one behavior slot.");
            }

            var extensionBootstrapIndices = new System.Collections.Generic.List<int>(2);
            var extensionTickIndices = new System.Collections.Generic.List<int>(2);
            uint presenceMask = 0u;
            uint defaultActiveMask = 0u;
            bool hasSound = false;
            for (int i = 0; i < slots.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref slots[i];
                if (slot.SlotIndex is < 0 or >= 32)
                {
                    throw new InvalidOperationException(
                        $"Presenter instance behavior slot {slot.SlotIndex} is outside the valid 0..31 range.");
                }

                uint bit = 1u << slot.SlotIndex;
                presenceMask |= bit;
                if (slot.ActiveByDefault)
                {
                    defaultActiveMask |= bit;
                }

                hasSound |= slot.Kind == BehaviorKind.Sound;
                if (slot.Kind == BehaviorKind.Extension)
                {
                    switch (slot.ExtensionLane)
                    {
                        case PresenterBehaviorExecutionLane.Bootstrap:
                            extensionBootstrapIndices.Add(i);
                            break;
                        case PresenterBehaviorExecutionLane.ContinuousTick:
                            extensionTickIndices.Add(i);
                            break;
                    }
                }
            }

            return new PresenterInstanceBehaviors
            {
                Slots = slots,
                ExtensionBootstrapIndices = extensionBootstrapIndices.ToArray(),
                ExtensionTickIndices = extensionTickIndices.ToArray(),
                PresenceMask = presenceMask,
                DefaultActiveMask = defaultActiveMask,
                HasSound = hasSound,
            };
        }
    }
}
