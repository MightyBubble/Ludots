using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed unsafe class RaylibMaterialHostBinder : IDisposable
    {
        private readonly IVirtualFileSystem _vfs;
        private readonly PresentationMaterialRegistry _materials;
        private readonly Dictionary<int, Texture2D> _albedoByMaterialId = new();
        private readonly HashSet<uint> _ownedTextureIds = new();
        private bool _disposed;

        public RaylibMaterialHostBinder(IVirtualFileSystem vfs, PresentationMaterialRegistry materials)
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        }

        public bool TryApplyAlbedo(ref Material material, int materialAssetId)
        {
            ThrowIfDisposed();
            if (materialAssetId <= 0)
            {
                return false;
            }

            if (!TryResolveHostAlbedo(materialAssetId, out Texture2D albedo))
            {
                return false;
            }

            Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO, albedo);
            return true;
        }

        public bool TryResolveHostAlbedo(int materialAssetId, out Texture2D albedo)
        {
            ThrowIfDisposed();
            albedo = default;
            if (materialAssetId <= 0)
            {
                return false;
            }

            if (_albedoByMaterialId.TryGetValue(materialAssetId, out albedo))
            {
                return true;
            }

            if (!_materials.TryGet(materialAssetId, out MaterialAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} cannot bind materialId={materialAssetId}: material is not registered in {nameof(PresentationMaterialRegistry)}.");
            }

            if (descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
            {
                return false;
            }

            string albedoUri = descriptor.SourceUris[0];
            if (string.IsNullOrWhiteSpace(albedoUri))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} materialId={materialAssetId} ({_materials.GetName(materialAssetId)}) has an empty albedo sourceUris[0].");
            }

            if (!_vfs.TryResolveFullPath(albedoUri, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} cannot resolve albedo URI '{albedoUri}' for materialId={materialAssetId} ({_materials.GetName(materialAssetId)}).");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} albedo file missing for materialId={materialAssetId} ({_materials.GetName(materialAssetId)}): uri='{albedoUri}' fullPath='{fullPath}'.");
            }

            Texture2D texture = Rl.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} LoadTexture failed for materialId={materialAssetId} ({_materials.GetName(materialAssetId)}): uri='{albedoUri}' fullPath='{fullPath}'.");
            }

            _albedoByMaterialId[materialAssetId] = texture;
            _ownedTextureIds.Add(texture.id);
            albedo = texture;
            return true;
        }

        public void DetachOwnedAlbedoMaps(Model model)
        {
            if (_disposed || model.materialCount <= 0 || model.materials == null)
            {
                return;
            }

            int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
            for (int i = 0; i < model.materialCount; i++)
            {
                ref Material material = ref model.materials[i];
                DetachOwnedAlbedoMap(ref material, albedoIndex);
            }
        }

        public void DetachOwnedAlbedoMap(ref Material material)
        {
            if (_disposed)
            {
                return;
            }

            DetachOwnedAlbedoMap(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO);
        }

        private void DetachOwnedAlbedoMap(ref Material material, int albedoIndex)
        {
            if (material.maps == null)
            {
                return;
            }

            uint textureId = material.maps[albedoIndex].texture.id;
            if (textureId == 0 || !_ownedTextureIds.Contains(textureId))
            {
                return;
            }

            material.maps[albedoIndex].texture = default;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (KeyValuePair<int, Texture2D> entry in _albedoByMaterialId)
            {
                if (entry.Value.id != 0)
                {
                    Rl.UnloadTexture(entry.Value);
                }
            }

            _albedoByMaterialId.Clear();
            _ownedTextureIds.Clear();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibMaterialHostBinder));
            }
        }
    }
}
