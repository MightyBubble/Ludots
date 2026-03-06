using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Tween
{
    public sealed class TweenSinkRegistry
    {
        private readonly StringIntRegistry _ids;
        private ITweenSink[] _items;
        private bool[] _has;

        public TweenSinkRegistry(int capacity = 64)
        {
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _items = new ITweenSink[capacity];
            _has = new bool[capacity];
        }

        public int Register(string key, ITweenSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            int id = _ids.Register(key);
            EnsureCapacity(id);
            _items[id] = sink;
            _has[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryApply(in TweenTarget target, float value)
        {
            int sinkId = target.SinkId;
            if (sinkId <= 0 || sinkId >= _items.Length || !_has[sinkId])
                return false;

            return _items[sinkId].TryApply(in target, value);
        }

        private void EnsureCapacity(int id)
        {
            if (id < _items.Length) return;
            int newLen = Math.Max(_items.Length * 2, id + 1);
            Array.Resize(ref _items, newLen);
            Array.Resize(ref _has, newLen);
        }
    }
}
