namespace Ludots.Core.Engine.Physics2D
{
    public sealed class Physics2DTickPolicy
    {
        private int _targetHz;
        private int _maxStepsPerFixedTick;
        private int _version;

        public int TargetHz => _targetHz;
        public int MaxStepsPerFixedTick => _maxStepsPerFixedTick;
        public int Version => _version;

        public Physics2DTickPolicy(int targetHz, int maxStepsPerFixedTick)
        {
            SetTargetHz(targetHz);
            SetMaxStepsPerFixedTick(maxStepsPerFixedTick);
        }

        public void SetTargetHz(int value)
        {
            if (value < 0) throw new System.ArgumentOutOfRangeException(nameof(value));
            if (_targetHz == value && _version != 0) return;
            _targetHz = value;
            _version++;
            if (_version == 0) _version = 1;
        }

        public void SetMaxStepsPerFixedTick(int value)
        {
            if (value < 1) throw new System.ArgumentOutOfRangeException(nameof(value));
            if (_maxStepsPerFixedTick == value && _version != 0) return;
            _maxStepsPerFixedTick = value;
            _version++;
            if (_version == 0) _version = 1;
        }
    }
}
