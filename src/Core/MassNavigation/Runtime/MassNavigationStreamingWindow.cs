using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;

namespace Ludots.Core.MassNavigation.Runtime;

internal readonly record struct MassNavigationStreamingWindowUpdate(
    int MinChunkX,
    int MaxChunkX,
    int MinChunkY,
    int MaxChunkY,
    bool SameWindow);

internal sealed class MassNavigationStreamingWindow
{
    private readonly string _contributorKey;
    private readonly int _chunkCapacity;
    private readonly float _retainSeconds;
    private readonly int _radiusCm;
    private readonly Dictionary<long, float> _lastTouchedSeconds;
    private readonly List<long> _chunksToEvict;
    private WorldGridLoadedChunks? _loadedChunks;
    private WorldGridLoadedChunkContributor? _contributor;
    private float _clockSeconds;
    private int _minChunkX = int.MinValue;
    private int _maxChunkX = int.MinValue;
    private int _minChunkY = int.MinValue;
    private int _maxChunkY = int.MinValue;
    private int _cachedRadiusCm = int.MinValue;

    public MassNavigationStreamingWindow(
        string contributorKey,
        int chunkCapacity,
        in MassNavigationStreamingPlan plan)
    {
        _contributorKey = contributorKey;
        _chunkCapacity = chunkCapacity;
        _retainSeconds = plan.RetainSeconds;
        _radiusCm = plan.RadiusCm;
        _lastTouchedSeconds = new Dictionary<long, float>(chunkCapacity);
        _chunksToEvict = new List<long>(chunkCapacity);
    }

    public WorldGridLoadedChunks LoadedChunks => RequireLoadedChunks();
    public int LoadedChunkCount => _contributor?.ActiveChunkKeys.Count ?? 0;
    public int PeakLoadedChunkCount { get; private set; }
    public int? ChunkSizeCm => _loadedChunks?.ChunkSizeCm;

    public void Bind(WorldGridLoadedChunks loadedChunks)
    {
        ArgumentNullException.ThrowIfNull(loadedChunks);
        if (_loadedChunks != null && !ReferenceEquals(_loadedChunks, loadedChunks))
        {
            Release();
        }

        _loadedChunks = loadedChunks;
        _contributor ??= loadedChunks.AcquireContributor(_contributorKey, _chunkCapacity);
        InvalidateCache();
    }

    public void AdvanceClock(float dt)
    {
        _clockSeconds += MathF.Max(0f, dt);
    }

    public bool Update(Vector2 worldCenterCm)
    {
        MassNavigationStreamingWindowUpdate update = PrepareUpdate(worldCenterCm);
        return ApplyUpdate(update);
    }

    public MassNavigationStreamingWindowUpdate PrepareUpdate(Vector2 worldCenterCm)
    {
        WorldGridLoadedChunks loadedChunks = RequireLoadedChunks();
        int centerX = (int)MathF.Round(worldCenterCm.X);
        int centerY = (int)MathF.Round(worldCenterCm.Y);
        int chunkSize = loadedChunks.ChunkSizeCm;
        int minChunkX = MathUtil.FloorDiv(centerX - _radiusCm, chunkSize);
        int maxChunkX = MathUtil.FloorDiv(centerX + _radiusCm, chunkSize);
        int minChunkY = MathUtil.FloorDiv(centerY - _radiusCm, chunkSize);
        int maxChunkY = MathUtil.FloorDiv(centerY + _radiusCm, chunkSize);
        bool sameWindow = minChunkX == _minChunkX &&
            maxChunkX == _maxChunkX &&
            minChunkY == _minChunkY &&
            maxChunkY == _maxChunkY &&
            _radiusCm == _cachedRadiusCm;
        var update = new MassNavigationStreamingWindowUpdate(
            minChunkX,
            maxChunkX,
            minChunkY,
            maxChunkY,
            sameWindow);
        if (!sameWindow)
        {
            EnsureUpdateCapacity(update);
        }

        return update;
    }

    public bool ApplyUpdate(in MassNavigationStreamingWindowUpdate update)
    {
        if (update.SameWindow)
        {
            TouchWindow(update.MinChunkX, update.MaxChunkX, update.MinChunkY, update.MaxChunkY);
            EvictExpiredChunks();
            return false;
        }

        if (_cachedRadiusCm != int.MinValue && _retainSeconds > 0f)
        {
            TouchWindow(_minChunkX, _maxChunkX, _minChunkY, _maxChunkY);
        }

        _minChunkX = update.MinChunkX;
        _maxChunkX = update.MaxChunkX;
        _minChunkY = update.MinChunkY;
        _maxChunkY = update.MaxChunkY;
        _cachedRadiusCm = _radiusCm;
        EvictExpiredChunks();
        TouchWindow(update.MinChunkX, update.MaxChunkX, update.MinChunkY, update.MaxChunkY);
        return true;
    }

    public void Release()
    {
        if (_contributor == null)
        {
            return;
        }

        _lastTouchedSeconds.Clear();
        _chunksToEvict.Clear();
        _contributor.Dispose();
        _contributor = null;
        InvalidateCache();
    }

    private void EvictExpiredChunks()
    {
        if (_retainSeconds < 0f)
        {
            return;
        }

        _chunksToEvict.Clear();
        foreach (KeyValuePair<long, float> pair in _lastTouchedSeconds)
        {
            if (IsInCurrentWindow(pair.Key))
            {
                continue;
            }

            float elapsedSeconds = _clockSeconds - pair.Value;
            if ((_retainSeconds == 0f && elapsedSeconds >= 0f) || elapsedSeconds > _retainSeconds)
            {
                _chunksToEvict.Add(pair.Key);
            }
        }

        WorldGridLoadedChunkContributor contributor = RequireContributor();
        for (int i = 0; i < _chunksToEvict.Count; i++)
        {
            long chunkKey = _chunksToEvict[i];
            _lastTouchedSeconds.Remove(chunkKey);
            contributor.SetLoaded(chunkKey, false);
        }
    }

    private void TouchWindow(int minChunkX, int maxChunkX, int minChunkY, int maxChunkY)
    {
        for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
        {
            for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                TouchChunk(GraphChunkKey.Pack(chunkX, chunkY));
            }
        }
    }

    private void EnsureUpdateCapacity(in MassNavigationStreamingWindowUpdate update)
    {
        int requiredCount = 0;
        bool refreshPreviousWindow = _cachedRadiusCm != int.MinValue && _retainSeconds > 0f;
        foreach (KeyValuePair<long, float> pair in _lastTouchedSeconds)
        {
            bool inNextWindow = IsInWindow(
                pair.Key,
                update.MinChunkX,
                update.MaxChunkX,
                update.MinChunkY,
                update.MaxChunkY);
            bool refreshedByPreviousWindow = refreshPreviousWindow && IsInCurrentWindow(pair.Key);
            float elapsedSeconds = _clockSeconds - pair.Value;
            bool expired = !inNextWindow &&
                !refreshedByPreviousWindow &&
                ((_retainSeconds == 0f && elapsedSeconds >= 0f) || elapsedSeconds > _retainSeconds);
            if (!expired)
            {
                requiredCount++;
            }
        }

        for (int chunkY = update.MinChunkY; chunkY <= update.MaxChunkY; chunkY++)
        {
            for (int chunkX = update.MinChunkX; chunkX <= update.MaxChunkX; chunkX++)
            {
                if (!_lastTouchedSeconds.ContainsKey(GraphChunkKey.Pack(chunkX, chunkY)))
                {
                    requiredCount++;
                }
            }
        }

        if (requiredCount > _chunkCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation streaming update requires {requiredCount} retained chunks, exceeding runtime.capacity.loadedChunkCapacity {_chunkCapacity}.");
        }
    }

    private bool IsInCurrentWindow(long chunkKey)
    {
        return IsInWindow(chunkKey, _minChunkX, _maxChunkX, _minChunkY, _maxChunkY);
    }

    private static bool IsInWindow(
        long chunkKey,
        int minChunkX,
        int maxChunkX,
        int minChunkY,
        int maxChunkY)
    {
        (int chunkX, int chunkY) = GraphChunkKey.Unpack(chunkKey);
        return chunkX >= minChunkX &&
               chunkX <= maxChunkX &&
               chunkY >= minChunkY &&
               chunkY <= maxChunkY;
    }

    private void TouchChunk(long chunkKey)
    {
        ref float lastTouchedSeconds = ref CollectionsMarshal.GetValueRefOrNullRef(_lastTouchedSeconds, chunkKey);
        if (Unsafe.IsNullRef(ref lastTouchedSeconds))
        {
            if (_lastTouchedSeconds.Count >= _chunkCapacity)
            {
                throw new InvalidOperationException(
                    $"MassNavigation streaming required more than runtime.capacity.loadedChunkCapacity {_chunkCapacity} chunks.");
            }

            _lastTouchedSeconds.Add(chunkKey, _clockSeconds);
            RequireContributor().SetLoaded(chunkKey, true);
            PeakLoadedChunkCount = Math.Max(PeakLoadedChunkCount, LoadedChunkCount);
            return;
        }

        lastTouchedSeconds = _clockSeconds;
    }

    private WorldGridLoadedChunks RequireLoadedChunks()
    {
        return _loadedChunks
            ?? throw new InvalidOperationException("MassNavigation requires board-owned loaded chunks before streaming operations.");
    }

    private WorldGridLoadedChunkContributor RequireContributor()
    {
        return _contributor
            ?? throw new InvalidOperationException("MassNavigation requires an active board loaded-chunk contribution before streaming operations.");
    }

    private void InvalidateCache()
    {
        _minChunkX = int.MinValue;
        _maxChunkX = int.MinValue;
        _minChunkY = int.MinValue;
        _maxChunkY = int.MinValue;
        _cachedRadiusCm = int.MinValue;
    }
}
