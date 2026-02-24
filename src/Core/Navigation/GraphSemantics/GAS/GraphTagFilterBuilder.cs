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

        public static unsafe TagFilter256 Compile(in GameplayTagContainer requiredAll, in GameplayTagContainer forbiddenAny)
        {
            var req = new TagBits256(requiredAll.Bits[0], requiredAll.Bits[1], requiredAll.Bits[2], requiredAll.Bits[3]);
            var forb = new TagBits256(forbiddenAny.Bits[0], forbiddenAny.Bits[1], forbiddenAny.Bits[2], forbiddenAny.Bits[3]);
            return new TagFilter256(in req, in forb);
        }
    }
}

