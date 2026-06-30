namespace Ludots.Core.Gameplay.Morph
{
    public enum MorphPlacementMode : byte
    {
        AtSource = 0,
        AtTargetPoint = 1,
        PreservedExplicit = 2,
    }

    public enum MorphStableIdPolicy : byte
    {
        AllocateNew = 0,
        Transfer = 1,
    }

    public enum MorphAttributeInheritMode : byte
    {
        None = 0,
        IntersectByName = 1,
        AllDefined = 2,
    }

    public enum MorphEffectInheritMode : byte
    {
        StripAll = 0,
    }
}
