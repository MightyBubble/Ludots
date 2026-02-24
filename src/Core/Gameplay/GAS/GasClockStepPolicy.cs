namespace Ludots.Core.Gameplay.GAS
{
    public sealed class GasClockStepPolicy
    {
        private GasStepMode _mode;
        private int _stepEveryFixedTicks;
        private int _fixedTicksSinceLastStep;
        private int _pendingManualSteps;
        private int _version;

        public int StepEveryFixedTicks => _stepEveryFixedTicks;
        public GasStepMode Mode => _mode;
        public int Version => _version;

        public GasClockStepPolicy(int stepEveryFixedTicks, GasStepMode mode = GasStepMode.Auto)
        {
            SetStepEveryFixedTicks(stepEveryFixedTicks);
            SetMode(mode);
        }

        public void SetStepEveryFixedTicks(int value)
        {
            if (value < 1) throw new System.ArgumentOutOfRangeException(nameof(value));
            if (_stepEveryFixedTicks == value && _version != 0) return;
            _stepEveryFixedTicks = value;
            _fixedTicksSinceLastStep = 0;
            _version++;
            if (_version == 0) _version = 1;
        }

        public void SetMode(GasStepMode mode)
        {
            if (_mode == mode && _version != 0) return;
            _mode = mode;
            _fixedTicksSinceLastStep = 0;
            _pendingManualSteps = 0;
            _version++;
            if (_version == 0) _version = 1;
        }

        public void RequestStep(int count = 1)
        {
            if (count < 1) throw new System.ArgumentOutOfRangeException(nameof(count));
            _pendingManualSteps += count;
        }

        public bool ShouldAdvanceStepOnThisFixedTick()
        {
            switch (_mode)
            {
                case GasStepMode.Paused:
                    return false;
                case GasStepMode.Manual:
                    if (_pendingManualSteps < 1) return false;
                    _pendingManualSteps--;
                    return true;
                case GasStepMode.Auto:
                    _fixedTicksSinceLastStep++;
                    if (_fixedTicksSinceLastStep < _stepEveryFixedTicks) return false;
                    _fixedTicksSinceLastStep = 0;
                    return true;
                default:
                    throw new System.ArgumentOutOfRangeException();
            }
        }
    }
}
