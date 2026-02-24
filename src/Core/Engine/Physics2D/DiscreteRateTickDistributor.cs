using System;

namespace Ludots.Core.Engine.Physics2D
{
    public sealed class DiscreteRateTickDistributor
    {
        private int _fixedHz;
        private int _targetHz;
        private int _numerator;
        private int _denominator;
        private int _remainder;
        private int _maxStepsPerFixedTick;

        public int FixedHz => _fixedHz;
        public int TargetHz => _targetHz;
        public int MaxStepsPerFixedTick => _maxStepsPerFixedTick;
        public float TargetDeltaTime => _targetHz == 0 ? 0f : 1f / _targetHz;
        
        /// <summary>
        /// Interpolation alpha [0, 1] representing the fraction of the current physics step
        /// that has elapsed since the last physics tick.
        /// 
        /// Used for smooth visual rendering when physics Hz differs from render Hz.
        /// - 0 = Just after physics tick (use previous position)
        /// - 1 = Just before next physics tick (use current position)
        /// 
        /// Formula: remainder / denominator (where denominator = fixedHz)
        /// </summary>
        public float InterpolationAlpha => _denominator == 0 ? 1f : (float)_remainder / _denominator;

        public DiscreteRateTickDistributor(int fixedHz, int targetHz, int maxStepsPerFixedTick)
        {
            Reset(fixedHz, targetHz, maxStepsPerFixedTick);
        }

        public void Reset(int fixedHz, int targetHz, int maxStepsPerFixedTick)
        {
            if (fixedHz < 1) throw new ArgumentOutOfRangeException(nameof(fixedHz));
            if (targetHz < 0) throw new ArgumentOutOfRangeException(nameof(targetHz));
            if (maxStepsPerFixedTick < 1) throw new ArgumentOutOfRangeException(nameof(maxStepsPerFixedTick));

            _fixedHz = fixedHz;
            _targetHz = targetHz;
            _maxStepsPerFixedTick = maxStepsPerFixedTick;

            if (targetHz == 0)
            {
                _numerator = 0;
                _denominator = 1;
                _remainder = 0;
                return;
            }

            int maxStepsNeeded = (targetHz + fixedHz - 1) / fixedHz;
            if (maxStepsNeeded > maxStepsPerFixedTick)
            {
                throw new InvalidOperationException($"Tick rate requires up to {maxStepsNeeded} steps per fixed tick, exceeding MaxStepsPerFixedTick={maxStepsPerFixedTick}.");
            }

            _numerator = targetHz;
            _denominator = fixedHz;
            _remainder = 0;
        }

        public int NextStepCount()
        {
            int carry = _remainder + _numerator;
            int steps = carry / _denominator;
            _remainder = carry - steps * _denominator;
            return steps;
        }
    }
}
