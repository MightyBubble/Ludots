namespace Ludots.Core.Presentation.Performers
{
    public struct PerformerTransformSource
    {
        public TransformSource Value;
    }

    public enum TransformSource : byte
    {
        InheritParent = 0,
        EntityTransform = 1,
        SplineDriven = 2,
        BoneAttached = 3,
        AttachedToParent = 4,
        WorldFixed = 5,
    }
}
