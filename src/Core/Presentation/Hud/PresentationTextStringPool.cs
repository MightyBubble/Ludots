using System;
using System.Collections.Generic;
using System.Threading;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Interns string args for blittable <see cref="PresentationTextArg"/> packets.
    /// Pool identity equals the owning catalog instance lifetime — cross-pool resolve throws.
    /// </summary>
    public sealed class PresentationTextStringPool
    {
        private static int _nextIdentity;

        private readonly Dictionary<string, int> _indexByValue;
        private readonly List<string> _values;
        private readonly short _identity;

        public PresentationTextStringPool(IEqualityComparer<string>? comparer = null)
        {
            int identity = Interlocked.Increment(ref _nextIdentity);
            if (identity <= 0 || identity > short.MaxValue)
            {
                throw new InvalidOperationException("PresentationTextStringPool identity space exhausted.");
            }

            _identity = (short)identity;
            _indexByValue = new Dictionary<string, int>(comparer ?? StringComparer.Ordinal);
            _values = new List<string>(16);
        }

        public short Identity => _identity;

        public int Count => _values.Count;

        public int Intern(string value)
        {
            value ??= string.Empty;
            if (_indexByValue.TryGetValue(value, out int existing))
            {
                return existing;
            }

            int index = _values.Count;
            _values.Add(value);
            _indexByValue.Add(value, index);
            return index;
        }

        public string Get(int index)
        {
            if ((uint)index >= (uint)_values.Count)
            {
                throw new InvalidOperationException(
                    $"Presentation text string pool index {index} is invalid (pool identity={_identity}, count={_values.Count}).");
            }

            return _values[index];
        }

        public string Get(in PresentationTextArg arg)
        {
            if (arg.Type != PresentationTextArgType.String)
            {
                throw new InvalidOperationException(
                    $"Presentation text arg type '{arg.Type}' is not String.");
            }

            if (arg.Reserved != _identity)
            {
                throw new InvalidOperationException(
                    $"Presentation text string arg was interned in pool identity={arg.Reserved}, but resolved against pool identity={_identity}.");
            }

            return Get(arg.Raw32);
        }
    }
}
