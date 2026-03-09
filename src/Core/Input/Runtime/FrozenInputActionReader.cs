using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Input.Runtime
{
    public sealed class FrozenInputActionReader : IInputActionReader
    {
        private readonly Dictionary<string, Vector3> _values = new(StringComparer.Ordinal);

        public void Clear()
        {
            _values.Clear();
        }

        public void SetActionValue(string actionId, Vector3 value)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            _values[actionId] = value;
        }

        public void AddActionValue(string actionId, Vector3 value)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            if (_values.TryGetValue(actionId, out var existing))
            {
                _values[actionId] = existing + value;
                return;
            }

            _values[actionId] = value;
        }

        public T ReadAction<T>(string actionId) where T : struct
        {
            if (string.IsNullOrWhiteSpace(actionId) || !_values.TryGetValue(actionId, out var value))
            {
                return default;
            }

            if (typeof(T) == typeof(bool)) return (T)(object)(value.LengthSquared() > 0.25f);
            if (typeof(T) == typeof(float)) return (T)(object)value.X;
            if (typeof(T) == typeof(Vector2)) return (T)(object)new Vector2(value.X, value.Y);
            if (typeof(T) == typeof(Vector3)) return (T)(object)value;
            return default;
        }
    }
}
