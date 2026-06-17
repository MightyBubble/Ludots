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
        public AnimatorTransitionDurationMode DurationMode;
        public bool ConsumeTrigger;
        public bool HasExitTime;
        public float ExitTime;
        public AnimatorTransitionInterruptSource InterruptSource;
        public bool OrderedInterruption;
        public int DefinitionIndex;
    }
}
