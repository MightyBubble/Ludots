using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Gameplay.Camera
{
    internal sealed class CameraInputAccumulator
    {
        private readonly Dictionary<string, Vector3> _continuousValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector3> _oneShotValues = new(StringComparer.Ordinal);

        public void Clear()
        {
            _continuousValues.Clear();
            _oneShotValues.Clear();
        }

        public void CaptureContinuous(string actionId, Vector3 value)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            _continuousValues[actionId] = value;
        }

        public void CaptureContinuous(string actionId, bool value)
        {
            CaptureContinuous(actionId, value ? Vector3.One : Vector3.Zero);
        }

        public void CaptureContinuous(string actionId, float value)
        {
            CaptureContinuous(actionId, new Vector3(value, 0f, 0f));
        }

        public void CaptureContinuous(string actionId, Vector2 value)
        {
            CaptureContinuous(actionId, new Vector3(value.X, value.Y, 0f));
        }

        public void AccumulateOneShot(string actionId, float value)
        {
            if (string.IsNullOrWhiteSpace(actionId) || MathF.Abs(value) < 0.0001f)
            {
                return;
            }

            var delta = new Vector3(value, 0f, 0f);
            if (_oneShotValues.TryGetValue(actionId, out var existing))
            {
                _oneShotValues[actionId] = existing + delta;
                return;
            }

            _oneShotValues[actionId] = delta;
        }

        public void AccumulateOneShot(string actionId, Vector2 value)
        {
            if (string.IsNullOrWhiteSpace(actionId) || value.LengthSquared() < 0.0001f)
            {
                return;
            }

            var delta = new Vector3(value.X, value.Y, 0f);
            if (_oneShotValues.TryGetValue(actionId, out var existing))
            {
                _oneShotValues[actionId] = existing + delta;
                return;
            }

            _oneShotValues[actionId] = delta;
        }

        public void BuildTickSnapshot(FrozenInputActionReader snapshot)
        {
            snapshot.Clear();

            foreach (var pair in _continuousValues)
            {
                snapshot.SetActionValue(pair.Key, pair.Value);
            }

            foreach (var pair in _oneShotValues)
            {
                snapshot.AddActionValue(pair.Key, pair.Value);
            }

            _oneShotValues.Clear();
        }
    }
}
