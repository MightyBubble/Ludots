namespace Ludots.Core.Presentation.Assets
{
    public enum AnimatorConditionKind : byte
    {
        None = 0,
        Trigger = 1,
        BoolTrue = 2,
        BoolFalse = 3,
        FloatGreaterOrEqual = 4,
        FloatLessOrEqual = 5,
        AutoOnNormalizedTime = 6,
    }
}
