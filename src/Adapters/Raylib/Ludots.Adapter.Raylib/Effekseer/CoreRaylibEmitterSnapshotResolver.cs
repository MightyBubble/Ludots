using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Adapter.Raylib.Effekseer
{
    internal sealed class CoreRaylibEmitterSnapshotResolver : IRaylibEmitterSnapshotResolver
    {
        private readonly EmitterAssetRegistry _assets;
        private readonly IVirtualFileSystem _vfs;
        private readonly Dictionary<int, ResolvedEmitterAsset> _resolvedAssets = new();

        public CoreRaylibEmitterSnapshotResolver(EmitterAssetRegistry assets, IVirtualFileSystem vfs)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        }

        public bool TryResolve(in PrimitiveDrawItem item, out RaylibEmitterSnapshotItem emitter)
        {
            if (!item.AssetKind.IsEmitterKind())
            {
                emitter = default;
                return false;
            }

            if (!_resolvedAssets.TryGetValue(item.AssetId, out ResolvedEmitterAsset asset))
            {
                asset = ResolveAsset(item.AssetId, item.AssetKind, item.StableId);
                _resolvedAssets.Add(item.AssetId, asset);
            }
            else if (asset.AssetKind != item.AssetKind)
            {
                throw new InvalidOperationException(
                    $"Raylib emitter stableId={item.StableId} declares '{item.AssetKind}', " +
                    $"but cached assetId={item.AssetId} is '{asset.AssetKind}'.");
            }

            byte dynamicInputCount = item.MaterialCustomData.Count;
            if (dynamicInputCount > MaterialCustomDataBinding.MaxSlots)
            {
                throw new InvalidOperationException(
                    $"Raylib emitter stableId={item.StableId} exposes {dynamicInputCount} dynamic inputs; ABI supports at most {MaterialCustomDataBinding.MaxSlots}.");
            }

            emitter = new RaylibEmitterSnapshotItem(
                item.StableId,
                item.AssetId,
                asset.AssetKind,
                asset.RuntimeFormatVersion,
                asset.FullPath,
                asset.Sha256,
                item.Position,
                item.Rotation,
                item.Scale,
                item.Color,
                item.Visibility,
                item.HasTarget,
                item.TargetPosition,
                dynamicInputCount,
                item.MaterialCustomData.Slot0.X,
                item.MaterialCustomData.Slot1.X,
                item.MaterialCustomData.Slot2.X,
                item.MaterialCustomData.Slot3.X);
            return true;
        }

        private ResolvedEmitterAsset ResolveAsset(int assetId, AssetKind itemKind, int stableId)
        {
            if (!_assets.TryGetDescriptor(assetId, out EmitterAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"Raylib emitter stableId={stableId} references unknown emitter assetId={assetId}.");
            }
            if (descriptor.AssetKind != itemKind)
            {
                throw new InvalidOperationException(
                    $"Raylib emitter stableId={stableId} declares '{itemKind}', " +
                    $"but assetId={assetId} is registered as '{descriptor.AssetKind}'.");
            }
            if (descriptor.SourceUris is not { Length: 1 } || string.IsNullOrWhiteSpace(descriptor.SourceUris[0]))
            {
                throw new InvalidOperationException(
                    $"Raylib emitter assetId={assetId} kind={itemKind} requires exactly one host-bound source URI.");
            }

            string sourceUri = descriptor.SourceUris[0];
            if (!_vfs.TryResolveFullPath(sourceUri, out string fullPath))
            {
                throw new FileNotFoundException(
                    $"Raylib emitter assetId={assetId} source URI '{sourceUri}' could not be resolved by the VFS.");
            }
            fullPath = Path.GetFullPath(fullPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Raylib emitter assetId={assetId} resolved to a missing file: {fullPath}",
                    fullPath);
            }

            return new ResolvedEmitterAsset(
                descriptor.AssetKind,
                descriptor.RuntimeFormatVersion,
                fullPath,
                descriptor.Sha256);
        }

        private readonly record struct ResolvedEmitterAsset(
            AssetKind AssetKind,
            int RuntimeFormatVersion,
            string FullPath,
            string Sha256);
    }
}
