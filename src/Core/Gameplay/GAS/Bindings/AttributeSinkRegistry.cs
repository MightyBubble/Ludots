using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public interface IAttributeSink
    {
        void Apply(Arch.Core.World world, AttributeBindingEntry[] entries, int start, int count);
    }

    public sealed class AttributeSinkRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<IAttributeSink> _sinks = new();
        private bool _frozen;

        public bool IsFrozen => _frozen;

        public void Clear()
        {
            if (_frozen) throw new InvalidOperationException("AttributeSinkRegistry is frozen.");
            _nameToId.Clear();
            _sinks.Clear();
        }

        public void Freeze()
        {
            _frozen = true;
        }

        public int Register(string sinkName, IAttributeSink sink)
        {
            if (_frozen) throw new InvalidOperationException("AttributeSinkRegistry is frozen.");
            if (string.IsNullOrWhiteSpace(sinkName)) throw new ArgumentException("sinkName is empty.", nameof(sinkName));
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            if (_nameToId.TryGetValue(sinkName, out var existing))
            {
                _sinks[existing] = sink;
                return existing;
            }

            int id = _sinks.Count;
            _nameToId[sinkName] = id;
            _sinks.Add(sink);
            return id;
        }

        public int GetId(string sinkName)
        {
            return _nameToId.TryGetValue(sinkName, out var id) ? id : -1;
        }

        public IAttributeSink GetSink(int sinkId)
        {
            if ((uint)sinkId >= (uint)_sinks.Count) throw new ArgumentOutOfRangeException(nameof(sinkId));
            return _sinks[sinkId];
        }
    }
}
