using System;
using System.Collections.Generic;
using Ludots.Core.Map;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Engine;

/// <summary>
/// Optional lifetime hook for a map-load gate that retains backend leases while a map is
/// focused. The engine calls it when a session is unloaded or replaced; older gates remain
/// valid without implementing this extension.
/// </summary>
public interface IMapLoadCompletionGateLifetime
{
    void Release(MapSession session);
}

/// <summary>
/// Backend-neutral map/visual rendezvous. The gate only polls the residency contract; it never
/// knows about GPU handles, file paths, or a particular renderer.
/// </summary>
public sealed class RenderAssetMapLoadCompletionGate : IMapLoadCompletionGate, IMapLoadCompletionGateLifetime, IDisposable
{
    private readonly IRenderAssetResidency _residency;
    private readonly Dictionary<MapId, Pending> _active = new();
    private bool _disposed;

    public RenderAssetMapLoadCompletionGate(IRenderAssetResidency residency)
    {
        _residency = residency ?? throw new ArgumentNullException(nameof(residency));
    }

    public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
    {
        ThrowIfDisposed();
        MapPresentationAssetManifest manifest = request.PresentationAssets
            ?? request.Engine.MapLoader.BuildPresentationAssetManifest(request.MapConfig);
        return Begin(request.Session, manifest);
    }

    public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
    {
        ThrowIfDisposed();
        MapPresentationAssetManifest manifest = request.PresentationAssets
            ?? request.Engine.MapLoader.BuildPresentationAssetManifest(request.ResumedSession.MapConfig);
        return Begin(request.ResumedSession, manifest);
    }

    public void Release(MapSession session)
    {
        if (session == null)
        {
            return;
        }

        if (_active.TryGetValue(session.MapId, out Pending? pending) &&
            ReferenceEquals(pending.Session, session))
        {
            _active.Remove(session.MapId);
            pending.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Pending pending in _active.Values)
        {
            pending.Cancel();
        }

        _active.Clear();
    }

    private IPendingMapLoad Begin(MapSession session, MapPresentationAssetManifest manifest)
    {
        if (_active.Remove(session.MapId, out Pending? previous))
        {
            previous.Cancel();
        }

        var pending = new Pending(_residency, session, manifest, RemoveIfCurrent);
        _active.Add(session.MapId, pending);
        return pending;

        void RemoveIfCurrent(Pending canceled)
        {
            if (_active.TryGetValue(session.MapId, out Pending? current) && ReferenceEquals(current, canceled))
            {
                _active.Remove(session.MapId);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RenderAssetMapLoadCompletionGate));
        }
    }

    private sealed class Pending : IPendingMapLoad
    {
        private readonly IRenderAssetResidency _residency;
        private readonly MapSession _session;
        private readonly MapPresentationAssetManifest _manifest;
        private readonly List<MapPresentationAsset> _retained = new();
        private readonly List<MapPresentationAsset> _pendingAssets = new();
        private readonly Action<Pending> _onCanceled;
        private MapLoadCompletionResult? _terminalResult;
        private bool _released;

        public MapSession Session => _session;

        public Pending(
            IRenderAssetResidency residency,
            MapSession session,
            MapPresentationAssetManifest manifest,
            Action<Pending> onCanceled)
        {
            _residency = residency;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _manifest = manifest ?? MapPresentationAssetManifest.Empty;
            _onCanceled = onCanceled ?? throw new ArgumentNullException(nameof(onCanceled));
        }

        public MapLoadCompletionResult Poll()
        {
            if (_released)
            {
                return MapLoadCompletionResult.Failed(
                    $"Map '{_session.MapId.Value}' presentation residency poll occurred after cancellation.");
            }

            if (_terminalResult.HasValue)
            {
                return _terminalResult.Value;
            }

            int resident = 0;
            int inFlight = 0;
            int failed = 0;
            for (int i = 0; i < _manifest.Count; i++)
            {
                MapPresentationAsset asset = _manifest[i];
                if (IsRetained(in asset))
                {
                    resident++;
                    continue;
                }

                RenderAssetResidencySnapshot snapshot;
                try
                {
                    snapshot = _residency.EnsureResident(in asset);
                }
                catch (Exception ex)
                {
                    failed++;
                    ReleaseRetained();
                    ReleasePending();
                    MapLoadCompletionResult result = MapLoadCompletionResult.Failed(
                        FormatFailure(in asset, ex.Message),
                        _manifest.Count,
                        resident,
                        inFlight,
                        failed);
                    _terminalResult = result;
                    return result;
                }

                if (snapshot.IsFailed)
                {
                    failed++;
                    ReleaseRetained();
                    ReleasePending();
                    MapLoadCompletionResult result = MapLoadCompletionResult.Failed(
                        FormatFailure(in asset, snapshot.FailureReason ?? "backend reported Failed"),
                        _manifest.Count,
                        resident,
                        inFlight,
                        failed);
                    _terminalResult = result;
                    return result;
                }

                if (snapshot.IsPending)
                {
                    inFlight++;
                    TrackPending(in asset);
                    continue;
                }

                if (snapshot.IsResident)
                {
                    resident++;
                    RemovePending(in asset);
                    Retain(in asset);
                    continue;
                }

                failed++;
                ReleaseRetained();
                ReleasePending();
                MapLoadCompletionResult unexpectedState = MapLoadCompletionResult.Failed(
                    FormatFailure(in asset, $"unexpected residency state '{snapshot.State}'"),
                    _manifest.Count,
                    resident,
                    inFlight,
                    failed);
                _terminalResult = unexpectedState;
                return unexpectedState;
            }

            if (resident == _manifest.Count && _manifest.IsSealed)
            {
                MapLoadCompletionResult ready = MapLoadCompletionResult.Ready(_manifest.Count, resident, 0, 0);
                _terminalResult = ready;
                return ready;
            }

            return MapLoadCompletionResult.Pending(_manifest.Count, resident, inFlight, failed);
        }

        public void Cancel()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            ReleaseRetained();
            ReleasePending();
            _onCanceled(this);
        }

        private void Retain(in MapPresentationAsset asset)
        {
            _retained.Add(asset);
        }

        private void TrackPending(in MapPresentationAsset asset)
        {
            if (!Contains(_pendingAssets, in asset))
            {
                _pendingAssets.Add(asset);
            }
        }

        private void RemovePending(in MapPresentationAsset asset)
        {
            for (int i = _pendingAssets.Count - 1; i >= 0; i--)
            {
                MapPresentationAsset pending = _pendingAssets[i];
                if (ManifestKeys.Equal(in pending, in asset))
                {
                    _pendingAssets.RemoveAt(i);
                    return;
                }
            }
        }

        private bool IsRetained(in MapPresentationAsset asset)
        {
            for (int i = 0; i < _retained.Count; i++)
            {
                MapPresentationAsset retained = _retained[i];
                if (ManifestKeys.Equal(in retained, in asset))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReleaseRetained()
        {
            if (_retained.Count == 0)
            {
                return;
            }

            for (int i = _retained.Count - 1; i >= 0; i--)
            {
                MapPresentationAsset asset = _retained[i];
                _residency.Release(in asset);
            }

            _retained.Clear();
        }

        private void ReleasePending()
        {
            for (int i = _pendingAssets.Count - 1; i >= 0; i--)
            {
                MapPresentationAsset asset = _pendingAssets[i];
                _residency.Release(in asset);
            }

            _pendingAssets.Clear();
        }

        private static bool Contains(List<MapPresentationAsset> assets, in MapPresentationAsset candidate)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                MapPresentationAsset existing = assets[i];
                if (ManifestKeys.Equal(in existing, in candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatFailure(in MapPresentationAsset asset, string reason)
        {
            string uris = asset.SourceUris == null ? string.Empty : string.Join("|", asset.SourceUris);
            return $"render asset kind={asset.AssetKind} id={asset.AssetId} renderPath={asset.RenderPath} uris='{uris}' failed: {reason}";
        }
    }

    private static class ManifestKeys
    {
        public static bool Equal(in MapPresentationAsset left, in MapPresentationAsset right)
        {
            if (left.AssetKind != right.AssetKind ||
                left.AssetId != right.AssetId ||
                left.RenderPath != right.RenderPath)
            {
                return false;
            }

            string[] leftUris = left.SourceUris ?? Array.Empty<string>();
            string[] rightUris = right.SourceUris ?? Array.Empty<string>();
            if (leftUris.Length != rightUris.Length)
            {
                return false;
            }

            for (int i = 0; i < leftUris.Length; i++)
            {
                if (!string.Equals(leftUris[i], rightUris[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
