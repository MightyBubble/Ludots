namespace Ludots.Core.Presentation.Assets
{
    public enum AnimatorTransitionInterruptSource : byte
    {
        None = 0,
        CurrentState = 1,
        NextState = 2,
        CurrentThenNext = 3,
        NextThenCurrent = 4,
    }
}
