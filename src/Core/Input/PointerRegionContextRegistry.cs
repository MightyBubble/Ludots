using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Input
{
    /// <summary>
    /// One inclusive screen rectangle that activates an interaction context while the
    /// pointer is inside it. The publisher owns the pixels; this record does not know
    /// which widget drew them.
    /// </summary>
    public readonly struct PointerRegionRect
    {
        public PointerRegionRect(string contextId, float minX, float minY, float maxX, float maxY)
        {
            if (string.IsNullOrWhiteSpace(contextId))
            {
                throw new ArgumentException("Pointer region context id is required.", nameof(contextId));
            }

            if (maxX < minX || maxY < minY)
            {
                throw new InvalidOperationException(
                    $"POINTER_REGION.ERR.Empty: context '{contextId}' rectangle ({minX},{minY})-({maxX},{maxY}) is empty.");
            }

            ContextId = contextId;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public string ContextId { get; }

        public float MinX { get; }

        public float MinY { get; }

        public float MaxX { get; }

        public float MaxY { get; }

        public bool Contains(Vector2 point)
        {
            return point.X >= MinX && point.Y >= MinY && point.X <= MaxX && point.Y <= MaxY;
        }
    }

    /// <summary>
    /// Screen rectangles published by whatever is actually showing. The first published
    /// source that contains the point wins. Publishing again under the same source id
    /// replaces that source's rectangles and keeps its place in the order.
    /// </summary>
    public sealed class PointerRegionContextRegistry
    {
        private readonly List<Source> _sources = new();

        public int Count => _sources.Count;

        public void Publish(string sourceId, IReadOnlyList<PointerRegionRect> rects)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Pointer region source id is required.", nameof(sourceId));
            }

            if (rects == null || rects.Count == 0)
            {
                throw new InvalidOperationException(
                    $"POINTER_REGION.ERR.Empty: source '{sourceId}' published no rectangles.");
            }

            var copy = new PointerRegionRect[rects.Count];
            for (int i = 0; i < rects.Count; i++)
            {
                copy[i] = rects[i];
            }

            for (int i = 0; i < _sources.Count; i++)
            {
                if (string.Equals(_sources[i].Id, sourceId, StringComparison.Ordinal))
                {
                    _sources[i] = new Source(sourceId, copy);
                    return;
                }
            }

            _sources.Add(new Source(sourceId, copy));
        }

        public void Clear(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Pointer region source id is required.", nameof(sourceId));
            }

            for (int i = 0; i < _sources.Count; i++)
            {
                if (string.Equals(_sources[i].Id, sourceId, StringComparison.Ordinal))
                {
                    _sources.RemoveAt(i);
                    return;
                }
            }
        }

        public bool TryHit(Vector2 point, out string contextId)
        {
            for (int s = 0; s < _sources.Count; s++)
            {
                PointerRegionRect[] rects = _sources[s].Rects;
                for (int i = 0; i < rects.Length; i++)
                {
                    if (rects[i].Contains(point))
                    {
                        contextId = rects[i].ContextId;
                        return true;
                    }
                }
            }

            contextId = string.Empty;
            return false;
        }

        public void CopyContextIds(List<string> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            for (int s = 0; s < _sources.Count; s++)
            {
                PointerRegionRect[] rects = _sources[s].Rects;
                for (int i = 0; i < rects.Length; i++)
                {
                    string contextId = rects[i].ContextId;
                    bool seen = false;
                    for (int d = 0; d < destination.Count; d++)
                    {
                        if (string.Equals(destination[d], contextId, StringComparison.Ordinal))
                        {
                            seen = true;
                            break;
                        }
                    }

                    if (!seen)
                    {
                        destination.Add(contextId);
                    }
                }
            }
        }

        private readonly struct Source
        {
            public Source(string id, PointerRegionRect[] rects)
            {
                Id = id;
                Rects = rects;
            }

            public string Id { get; }

            public PointerRegionRect[] Rects { get; }
        }
    }
}
