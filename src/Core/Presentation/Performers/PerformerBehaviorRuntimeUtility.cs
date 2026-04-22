namespace Ludots.Core.Presentation.Performers
{
    public static class PerformerBehaviorRuntimeUtility
    {
        public static int ComposeBehaviorStableId(int performerStableId, int slotIndex)
        {
            unchecked
            {
                return (performerStableId * 397) ^ (slotIndex + 1);
            }
        }

        public static int ComposeVisualStableId(int performerStableId, int slotIndex, AssetKind assetKind, int discriminator)
        {
            int seed = ComposeBehaviorStableId(performerStableId, slotIndex);
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
    }
}
