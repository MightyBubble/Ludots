namespace Ludots.Core.Presentation.Components
{
    public enum AnimatorFeedbackKind : byte
    {
        None = 0,
        Initialized = 1,
        TransitionStarted = 2,
        TransitionCompleted = 3,
        StateCompleted = 4,
        ControllerMissing = 5,
    }
}
