namespace Ludots.Core.Gameplay.GAS
{
    public sealed class GasClockStepPolicy
    {
        private GasStepMode _mode;
        private int _stepEveryFixedTicks;
        private int _pendingManualSteps;
        private int _scalePermille = 1000;
        private int _autoAccumulatorPermille;
        private int _version;

        public int StepEveryFixedTicks => _stepEveryFixedTicks;
        public GasStepMode Mode => _mode;
        public int ScalePermille => _scalePermille;
        public int Version => _version;
        public int LastConsumedSteps { get; private set; }
        public int LastConsumedManualSteps { get; private set; }

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
            _autoAccumulatorPermille = 0;
            _version++;
            if (_version == 0) _version = 1;
        }

        public void SetMode(GasStepMode mode)
        {
            if (_mode == mode && _version != 0) return;
            _mode = mode;
            _autoAccumulatorPermille = 0;
            _pendingManualSteps = 0;
            _version++;
            if (_version == 0) _version = 1;
        }

        public void SetScalePermille(int value)
        {
            if (value < 0) throw new System.ArgumentOutOfRangeException(nameof(value));
            if (_scalePermille == value && _version != 0) return;
            _scalePermille = value;
            _autoAccumulatorPermille = 0;
            _version++;
            if (_version == 0) _version = 1;
        }

        public void RequestStep(int count = 1)
        {
            if (count < 1) throw new System.ArgumentOutOfRangeException(nameof(count));
            _pendingManualSteps += count;
        }

        public int ConsumeStepsForThisFixedTick()
        {
            LastConsumedSteps = 0;
            LastConsumedManualSteps = 0;
            switch (_mode)
            {
                case GasStepMode.Paused:
                    return 0;
                case GasStepMode.Manual:
                    if (_pendingManualSteps < 1) return 0;
                    _pendingManualSteps--;
                    LastConsumedSteps = 1;
                    LastConsumedManualSteps = 1;
                    return 1;
                case GasStepMode.Auto:
                    if (_scalePermille <= 0) return 0;
                    int threshold = _stepEveryFixedTicks * 1000;
                    int steps = PermilleStepAccumulator.Consume(
                        ref _autoAccumulatorPermille,
                        _scalePermille,
                        threshold);
                    LastConsumedSteps = steps;
                    return steps;
                default:
                    throw new System.ArgumentOutOfRangeException();
            }
        }
    }
}
