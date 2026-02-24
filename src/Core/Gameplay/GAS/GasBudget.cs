namespace Ludots.Core.Gameplay.GAS
{
    public sealed class GasBudget
    {
        public int FrameIndex { get; private set; }

        public int ResponseWindows;
        public int ResponseSteps;
        public int ResponseCreates;
        public int ResponseCreatesDropped;
        public int ResponseDepthDropped;
        public int ResponseStepBudgetFused;
        public int ResponseQueueOverflowDropped;

        public int OnApplyCreatesDropped;
        public int DurationCallbackCreatesDropped;
        public int TagCountOverflowDropped;

        public void Reset()
        {
            FrameIndex++;
            ResponseWindows = 0;
            ResponseSteps = 0;
            ResponseCreates = 0;
            ResponseCreatesDropped = 0;
            ResponseDepthDropped = 0;
            ResponseStepBudgetFused = 0;
            ResponseQueueOverflowDropped = 0;
            OnApplyCreatesDropped = 0;
            DurationCallbackCreatesDropped = 0;
            TagCountOverflowDropped = 0;
        }

        public bool HasWarnings =>
            ResponseCreatesDropped != 0 ||
            ResponseDepthDropped != 0 ||
            ResponseStepBudgetFused != 0 ||
            ResponseQueueOverflowDropped != 0 ||
            OnApplyCreatesDropped != 0 ||
            DurationCallbackCreatesDropped != 0 ||
            TagCountOverflowDropped != 0;
    }
}
