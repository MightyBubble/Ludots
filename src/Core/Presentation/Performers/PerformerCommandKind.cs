namespace Ludots.Core.Presentation.Performers
{
    public enum PerformerCommandKind : byte
    {
        None = 0,
        CreatePerformer = 1,
        DestroyPerformer = 2,
        DestroyPerformerScope = 3,
        SetParam = 4,
        ActivateBehavior = 5,
        DeactivateBehavior = 6,
        SinkParamToAsset = 7,
        InitializeTransform = 8,
    }
}
