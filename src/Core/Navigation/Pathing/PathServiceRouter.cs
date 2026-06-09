using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.GraphWorld;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class PathServiceRouter : IPathService
    {
        private const int DefaultCacheCapacity = 512;

        private readonly object _sync = new object();
        private readonly IPathService _nodeGraph;
        private readonly IPathService _navMesh;
        private readonly IPathService _auto;
        private readonly PathStore _store;
        private readonly LoadedGraphRuntime _graphRuntime;
        private readonly int _cacheCapacity;
        private readonly Dictionary<PathCacheKey, LinkedListNode<PathCacheEntry>> _cacheByKey;
        private readonly LinkedList<PathCacheEntry> _cacheLru;

        private long _cacheHits;
        private long _cacheMisses;
        private long _cacheStores;
        private long _cacheEvictions;

        public PathServiceRouter(IPathService nodeGraph, IPathService navMesh, PathStore store)
            : this(nodeGraph, navMesh, auto: null, store, graphRuntime: null, cacheCapacity: DefaultCacheCapacity)
        {
        }

        public PathServiceRouter(IPathService nodeGraph, IPathService navMesh, PathStore store, int cacheCapacity)
            : this(nodeGraph, navMesh, auto: null, store, graphRuntime: null, cacheCapacity)
        {
        }

        public PathServiceRouter(IPathService nodeGraph, IPathService navMesh, IPathService auto, PathStore store)
            : this(nodeGraph, navMesh, auto, store, graphRuntime: null, cacheCapacity: DefaultCacheCapacity)
        {
        }

        public PathServiceRouter(IPathService nodeGraph, IPathService navMesh, IPathService auto, PathStore store, LoadedGraphRuntime graphRuntime)
            : this(nodeGraph, navMesh, auto, store, graphRuntime, DefaultCacheCapacity)
        {
        }

        private PathServiceRouter(IPathService nodeGraph, IPathService navMesh, IPathService auto, PathStore store, LoadedGraphRuntime graphRuntime, int cacheCapacity)
        {
            _nodeGraph = nodeGraph ?? throw new ArgumentNullException(nameof(nodeGraph));
            _navMesh = navMesh ?? throw new ArgumentNullException(nameof(navMesh));
            _auto = auto;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _graphRuntime = graphRuntime;
            _cacheCapacity = Math.Max(0, cacheCapacity);
            _cacheByKey = new Dictionary<PathCacheKey, LinkedListNode<PathCacheEntry>>(Math.Max(1, Math.Min(_cacheCapacity, 1024)));
            _cacheLru = new LinkedList<PathCacheEntry>();
        }

        public PathQueryCacheDiagnostics CacheDiagnostics
        {
            get
            {
                lock (_sync)
                {
                    return new PathQueryCacheDiagnostics(
                        Capacity: _cacheCapacity,
                        Count: _cacheByKey.Count,
                        Hits: _cacheHits,
                        Misses: _cacheMisses,
                        Stores: _cacheStores,
                        Evictions: _cacheEvictions);
                }
            }
        }

        public void ClearCache()
        {
            lock (_sync)
            {
                _cacheByKey.Clear();
                _cacheLru.Clear();
            }
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            lock (_sync)
            {
                PathCacheKey key = BuildCacheKey(in request);
                if (_cacheCapacity > 0 && TryReplayCachedPath(in request, in key, out result))
                {
                    _cacheHits++;
                    return true;
                }

                _cacheMisses++;
                bool solved = request.Domain switch
                {
                    PathDomain.NodeGraph => _nodeGraph.TrySolve(in request, out result),
                    PathDomain.NavMesh => _navMesh.TrySolve(in request, out result),
                    PathDomain.Auto => _auto != null ? _auto.TrySolve(in request, out result) : FailInvalid(in request, out result),
                    _ => FailInvalid(in request, out result)
                };

                if (_cacheCapacity > 0 &&
                    solved &&
                    result.Status == PathStatus.Found &&
                    result.Handle.IsValid)
                {
                    StoreCachedPath(in key, in result);
                }

                return solved;
            }
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            lock (_sync)
            {
                return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
            }
        }

        private static bool FailInvalid(in PathRequest request, out PathResult result)
        {
            result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 1);
            return false;
        }

        private bool TryReplayCachedPath(in PathRequest request, in PathCacheKey key, out PathResult result)
        {
            if (!_cacheByKey.TryGetValue(key, out LinkedListNode<PathCacheEntry> node))
            {
                result = default;
                return false;
            }

            _cacheLru.Remove(node);
            _cacheLru.AddFirst(node);
            PathCacheEntry entry = node.Value;
            int count = entry.Xcm.Length;
            if (!_store.TryAllocate(count, out PathHandle handle) ||
                !_store.TryWrite(in handle, entry.Xcm, entry.Ycm, count))
            {
                result = new PathResult(
                    request.RequestId,
                    request.Actor,
                    PathStatus.BudgetExceeded,
                    default,
                    entry.Expanded,
                    errorCode: 4);
                return true;
            }

            result = new PathResult(
                request.RequestId,
                request.Actor,
                PathStatus.Found,
                handle,
                entry.Expanded,
                errorCode: 0);
            return true;
        }

        private void StoreCachedPath(in PathCacheKey key, in PathResult result)
        {
            int maxPoints = _store.MaxPointsPerPath;
            int[] xs = new int[maxPoints];
            int[] ys = new int[maxPoints];
            if (!_store.TryCopy(in result.Handle, xs, ys, out int count) || count <= 0)
            {
                return;
            }

            if (count != maxPoints)
            {
                Array.Resize(ref xs, count);
                Array.Resize(ref ys, count);
            }

            if (_cacheByKey.TryGetValue(key, out LinkedListNode<PathCacheEntry> existing))
            {
                existing.Value = new PathCacheEntry(key, xs, ys, result.Expanded);
                _cacheLru.Remove(existing);
                _cacheLru.AddFirst(existing);
                _cacheStores++;
                return;
            }

            var node = new LinkedListNode<PathCacheEntry>(new PathCacheEntry(key, xs, ys, result.Expanded));
            _cacheLru.AddFirst(node);
            _cacheByKey[key] = node;
            _cacheStores++;

            while (_cacheByKey.Count > _cacheCapacity && _cacheLru.Last != null)
            {
                LinkedListNode<PathCacheEntry> last = _cacheLru.Last;
                _cacheLru.RemoveLast();
                _cacheByKey.Remove(last.Value.Key);
                _cacheEvictions++;
            }
        }

        private PathCacheKey BuildCacheKey(in PathRequest request)
        {
            int graphRevision = _graphRuntime?.Revision ?? ResolveDataRevision(_nodeGraph);
            int dataRevision = request.Domain switch
            {
                PathDomain.NodeGraph => graphRevision,
                PathDomain.NavMesh => ResolveDataRevision(_navMesh),
                PathDomain.Auto => ResolveDataRevision(_auto),
                _ => 0
            };
            return new PathCacheKey(
                request.Domain,
                request.AgentTypeId ?? string.Empty,
                request.Start.Kind,
                request.Start.NodeId,
                request.Start.Xcm,
                request.Start.Ycm,
                request.Goal.Kind,
                request.Goal.NodeId,
                request.Goal.Xcm,
                request.Goal.Ycm,
                request.Budget.MaxExpanded,
                request.Budget.MaxPoints,
                graphRevision,
                dataRevision);
        }

        private static int ResolveDataRevision(IPathService service)
        {
            return service is IPathDataRevisionProvider provider
                ? provider.DataRevision
                : 0;
        }

        private readonly record struct PathCacheKey(
            PathDomain Domain,
            string AgentTypeId,
            PathEndpointKind StartKind,
            int StartNodeId,
            int StartXcm,
            int StartYcm,
            PathEndpointKind GoalKind,
            int GoalNodeId,
            int GoalXcm,
            int GoalYcm,
            int MaxExpanded,
            int MaxPoints,
            int GraphRevision,
            int DataRevision);

        private readonly struct PathCacheEntry
        {
            public readonly PathCacheKey Key;
            public readonly int[] Xcm;
            public readonly int[] Ycm;
            public readonly int Expanded;

            public PathCacheEntry(PathCacheKey key, int[] xcm, int[] ycm, int expanded)
            {
                Key = key;
                Xcm = xcm;
                Ycm = ycm;
                Expanded = expanded;
            }
        }
    }

    public readonly record struct PathQueryCacheDiagnostics(
        int Capacity,
        int Count,
        long Hits,
        long Misses,
        long Stores,
        long Evictions);
}
