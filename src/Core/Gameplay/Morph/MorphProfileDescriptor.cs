using Ludots.Core.Gameplay.Morph;

namespace Ludots.Core.Gameplay.Morph
{
    public sealed class MorphProfileDescriptor
    {
        public MorphPlacementMode Placement { get; init; }
        public MorphStableIdPolicy StableIdPolicy { get; init; }
        public bool DestroySource { get; init; }
        public bool CopyPlayerOwner { get; init; }
        public bool CopyTeam { get; init; }
        public MorphAttributeInheritMode AttributeInheritMode { get; init; }
        public int[] InheritAttributeIds { get; init; } = [];
        public int[] CarryTagIds { get; init; } = [];
        public int[] StripTagIds { get; init; } = [];
        public MorphEffectInheritMode EffectInheritMode { get; init; } = MorphEffectInheritMode.StripAll;
        public bool ReplaceSelection { get; init; }
    }
}
