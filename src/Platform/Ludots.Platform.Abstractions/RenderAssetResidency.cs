using System;
using System.Collections;
using System.Collections.Generic;

namespace Ludots.Platform.Abstractions;

/// <summary>
/// Backend-neutral lifecycle for a render asset. Backends may use different workers and
/// upload mechanisms, but consumers only observe these six states.
/// </summary>
public enum RenderAssetResidencyState : byte
{
    Unrequested = 0,
    Preparing = 1,
    CpuReady = 2,
    UploadQueued = 3,
    Resident = 4,
    Failed = 5,
}

public readonly record struct RenderAssetResidencySnapshot(
    RenderAssetResidencyState State,
    string? FailureReason = null)
{
    public bool IsPending => State is RenderAssetResidencyState.Preparing or
        RenderAssetResidencyState.CpuReady or
        RenderAssetResidencyState.UploadQueued;

    public bool IsResident => State == RenderAssetResidencyState.Resident;

    public bool IsFailed => State == RenderAssetResidencyState.Failed;
}

/// <summary>
/// Deterministic map-owned list of presentation assets that must be resident before the map
/// becomes playable. Source URI order is the authored fallback order.
/// </summary>
public readonly record struct MapPresentationAsset(
    AssetKind AssetKind,
    int AssetId,
    VisualRenderPath RenderPath,
    string[] SourceUris)
{
    public static MapPresentationAsset Create(
        AssetKind assetKind,
        int assetId,
        VisualRenderPath renderPath,
        IReadOnlyList<string>? sourceUris)
    {
        string[] uris = sourceUris == null || sourceUris.Count == 0
            ? Array.Empty<string>()
            : new List<string>(sourceUris).ToArray();
        return new MapPresentationAsset(assetKind, assetId, renderPath, uris);
    }
}

public sealed class MapPresentationAssetManifest : IReadOnlyList<MapPresentationAsset>
{
    private readonly List<MapPresentationAsset> _entries = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private bool _sealed;

    public int Count => _entries.Count;

    public bool IsSealed => _sealed;

    public MapPresentationAsset this[int index] => Clone(_entries[index]);

    public void Add(in MapPresentationAsset asset)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("A sealed map presentation asset manifest cannot accept more assets.");
        }

        MapPresentationAsset owned = MapPresentationAsset.Create(
            asset.AssetKind,
            asset.AssetId,
            asset.RenderPath,
            asset.SourceUris);
        string key = BuildKey(in owned);
        if (_keys.Add(key))
        {
            _entries.Add(owned);
        }
    }

    public IEnumerator<MapPresentationAsset> GetEnumerator()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            yield return Clone(_entries[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void SubmitManifest(in MapPresentationAsset asset) => Add(in asset);

    public void SealManifest() => _sealed = true;

    public static MapPresentationAssetManifest Empty { get; } = CreateEmpty();

    private static MapPresentationAssetManifest CreateEmpty()
    {
        var manifest = new MapPresentationAssetManifest();
        manifest.SealManifest();
        return manifest;
    }

    private static string BuildKey(in MapPresentationAsset asset)
    {
        string uris = asset.SourceUris == null ? string.Empty : string.Join('\u001f', asset.SourceUris);
        return $"{(byte)asset.AssetKind}:{asset.AssetId}:{(byte)asset.RenderPath}:{uris}";
    }

    private static MapPresentationAsset Clone(in MapPresentationAsset asset)
        => MapPresentationAsset.Create(asset.AssetKind, asset.AssetId, asset.RenderPath, asset.SourceUris);
}

/// <summary>
/// Host/backend bridge for map loading. EnsureResident must be non-blocking; callers poll the
/// returned state on the host's normal frame/tick cadence.
/// </summary>
public interface IRenderAssetResidency
{
    RenderAssetResidencySnapshot EnsureResident(in MapPresentationAsset asset);

    void Release(in MapPresentationAsset asset);
}
