using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.TransportNetwork
{
    public sealed class TransportNetworkRibbonSource
    {
        private readonly TransportNetworkBakedAsset _baked;
        private readonly Dictionary<long, int> _activeScopes = new();

        public TransportNetworkRibbonSource(TransportNetworkBakedAsset baked)
        {
            _baked = baked ?? throw new ArgumentNullException(nameof(baked));
        }

        public IReadOnlyDictionary<long, int> ActiveScopes => _activeScopes;

        public static int ComposeDefaultSurfaceScopeId(long chunkKey)
        {
            unchecked
            {
                int mixed = (int)(chunkKey ^ (chunkKey >> 32));
                return 700000000 + Math.Abs(mixed % 100000000);
            }
        }

        public void SyncPayloads(
            IEnumerable<long> activeChunkKeys,
            SurfaceSourcePayloadRegistry payloads,
            Func<long, int> resolveScopeId)
        {
            if (activeChunkKeys == null) throw new ArgumentNullException(nameof(activeChunkKeys));
            if (payloads == null) throw new ArgumentNullException(nameof(payloads));
            if (resolveScopeId == null) throw new ArgumentNullException(nameof(resolveScopeId));

            var desired = new HashSet<long>(activeChunkKeys);
            foreach (long chunkKey in desired)
            {
                if (!_baked.TryGetRibbonChunk(chunkKey, out SurfaceSplineSegment[] segments))
                {
                    continue;
                }

                if (!_activeScopes.TryGetValue(chunkKey, out int scopeId))
                {
                    scopeId = resolveScopeId(chunkKey);
                    if (scopeId <= 0)
                    {
                        throw new InvalidOperationException($"TransportNetwork ribbon scope for chunk {chunkKey} must be > 0.");
                    }

                    _activeScopes.Add(chunkKey, scopeId);
                }

                payloads.SetSplineRibbon(scopeId, segments);
            }

            List<long>? remove = null;
            foreach ((long chunkKey, int scopeId) in _activeScopes)
            {
                if (desired.Contains(chunkKey))
                {
                    continue;
                }

                remove ??= new List<long>();
                remove.Add(chunkKey);
                payloads.Remove(scopeId);
            }

            if (remove == null)
            {
                return;
            }

            for (int i = 0; i < remove.Count; i++)
            {
                _activeScopes.Remove(remove[i]);
            }
        }
    }
}
