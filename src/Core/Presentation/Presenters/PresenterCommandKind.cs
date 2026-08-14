namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterCommandKind : byte
    {
        None = 0,
        CreatePresenter = 1,
        DestroyPresenter = 2,
        DestroyPresenterScope = 3,
        SetParam = 4,
        ActivateBehavior = 5,
        DeactivateBehavior = 6,
        SinkParamToAsset = 7,
        InitializeTransform = 8,
        DestroyScopedPresenter = 9,
    }
}
