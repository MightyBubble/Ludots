using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    public static class PresenterBehaviorRuntimeUtility
    {
        public static int ComposeBehaviorStableId(int presenterStableId, int slotIndex)
        {
            unchecked
            {
                return (presenterStableId * 397) ^ (slotIndex + 1);
            }
        }

        public static int ComposeVisualStableId(int presenterStableId, int slotIndex, AssetKind assetKind, int discriminator)
        {
            int seed = ComposeBehaviorStableId(presenterStableId, slotIndex);
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + seed;
                hash = hash * 31 + (int)assetKind;
                hash = hash * 31 + discriminator;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }

        public static PresenterVisualStableKey ComposeVisualStableKey(
            int presenterStableId,
            int slotIndex,
            AssetKind assetKind,
            int discriminator)
        {
            return new PresenterVisualStableKey(presenterStableId, slotIndex, assetKind, discriminator);
        }

        public static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        /// <summary>
        /// retained 呈现请求移除时用的稳定 id：HUD 走 HudItemIdentity 的 presenter 组合，
        /// Spline/GroundOverlay 走视觉稳定 id。id 来源统一取 state.DefId
        /// （与 definition.Id 运行期同值），两处调用方不得各选一边。
        /// </summary>
        public static int ComposeRetainedRemovalStableId(in PresenterState state, in BehaviorSlot slot)
        {
            return slot.AssetBinding.AssetKind switch
            {
                AssetKind.WorldHud => HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Bar, state.DefId, slot.SlotIndex),
                AssetKind.WorldText => HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Text, state.DefId, slot.SlotIndex),
                AssetKind.Spline => ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId),
                AssetKind.GroundOverlay => ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId),
                _ => 0,
            };
        }
    }
}
