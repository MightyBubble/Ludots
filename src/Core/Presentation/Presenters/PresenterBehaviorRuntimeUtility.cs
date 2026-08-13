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
    }
}
