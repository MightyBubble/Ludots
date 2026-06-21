using System;

namespace Ludots.Core.Engine.Physics2D
{
    public sealed class Physics2DBroadphasePolicy
    {
        private Physics2DBroadphaseStrategyKind _strategy;
        private int _cellSizeCm;
        private int _version;

        public Physics2DBroadphaseStrategyKind Strategy => _strategy;
        public int CellSizeCm => _cellSizeCm;
        public int Version => _version;

        public Physics2DBroadphasePolicy(Physics2DBroadphaseConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            SetStrategy(config.Strategy);
            SetCellSizeCm(config.CellSizeCm);
        }

        public void SetStrategy(Physics2DBroadphaseStrategyKind value)
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_strategy == value && _version != 0) return;
            _strategy = value;
            _version++;
            if (_version == 0) _version = 1;
        }

        public void SetCellSizeCm(int value)
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            if (_cellSizeCm == value && _version != 0) return;
            _cellSizeCm = value;
            _version++;
            if (_version == 0) _version = 1;
        }
    }
}
