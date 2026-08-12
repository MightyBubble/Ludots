using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphSemantics.GAS
{
    public static class GraphTagFilterBuilder
    {
        public static TagFilter256 Compile(ReadOnlySpan<int> requiredAllTagIds, ReadOnlySpan<int> forbiddenAnyTagIds)
        {
            var req = GraphTagSetRegistry.TagBitsFromIds(requiredAllTagIds);
            var forb = GraphTagSetRegistry.TagBitsFromIds(forbiddenAnyTagIds);
            return new TagFilter256(in req, in forb);
        }

        public static TagFilter256 Compile(in GameplayTagBitSet requiredAll, in GameplayTagBitSet forbiddenAny)
        {
            // P3 bridge: first four words only; freeze fails closed when Plan.TagIdSpace > 256.
            var req = new TagBits256(requiredAll.WordAt(0), requiredAll.WordAt(1), requiredAll.WordAt(2), requiredAll.WordAt(3));
            var forb = new TagBits256(forbiddenAny.WordAt(0), forbiddenAny.WordAt(1), forbiddenAny.WordAt(2), forbiddenAny.WordAt(3));
            return new TagFilter256(in req, in forb);
        }
    }
}
