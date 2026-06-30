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

    public enum MorphAttributeValueSource : byte
    {
        Base = 0,
        Current = 1,
    }

    public enum MorphTagInheritMode : byte
    {
        None = 0,
        StripListed = 1,
        CarryListed = 2,
        StripListedAndCarryListed = 3,
    }

    public enum MorphEffectInheritMode : byte
    {
        StripAll = 0,
    }
}
