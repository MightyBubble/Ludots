namespace Ludots.Core.Presentation.Components
{
    public struct AnimatorFeedbackEvent
    {
        public AnimatorFeedbackKind Kind;
        public int ControllerId;
        public int FromStateIndex;
        public int ToStateIndex;
        public float NormalizedTime01;
        public float Value0;
    }
}
