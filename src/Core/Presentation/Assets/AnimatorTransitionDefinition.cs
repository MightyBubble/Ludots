namespace Ludots.Core.Presentation.Assets
{
    public struct AnimatorTransitionDefinition
    {
        public int FromStateIndex;
        public int ToStateIndex;
        public AnimatorConditionKind ConditionKind;
        public int ParameterIndex;
        public float Threshold;
        public float DurationSeconds;
        public bool ConsumeTrigger;
    }
}
