using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    public sealed unsafe class RaylibMaterialHostBinder : IDisposable
    {
        public const float DefaultRoughness = 0.85f;
        public const float DefaultMetallic = 0f;

        private readonly IRenderAssetPathResolver _vfs;
        private readonly IRenderMaterialAssets _materials;
        private readonly Dictionary<int, HostMaterialBinding> _bindingsByMaterialId = new();
        private readonly HashSet<uint> _ownedTextureIds = new();
        private bool _disposed;

        public RaylibMaterialHostBinder(IRenderAssetPathResolver vfs, IRenderMaterialAssets materials)
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        }

        public bool TryApplyAlbedo(ref Material material, int materialAssetId)
        {
            return TryApplyHostMaps(ref material, materialAssetId);
        }

        public bool TryApplyHostMaps(ref Material material, int materialAssetId)
        {
            ThrowIfDisposed();
            if (materialAssetId <= 0)
            {
                return false;
            }

            if (!TryResolveHostBinding(materialAssetId, out HostMaterialBinding binding))
            {
                return false;
            }

            Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO, binding.Albedo);

            int roughnessIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ROUGHNESS;
            int metalnessIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_METALNESS;
            int normalIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL;

            if (material.maps != null)
            {
                if (binding.HasRoughnessMap)
                {
                    Rl.SetMaterialTexture(ref material, roughnessIndex, binding.Roughness);
                }
                else
                {
                    ClearMapIfOwned(ref material, roughnessIndex);
                }

                material.maps[roughnessIndex].value = binding.RoughnessScalar;

                if (binding.HasMetallicMap)
                {
                    Rl.SetMaterialTexture(ref material, metalnessIndex, binding.Metallic);
                }
                else
                {
                    ClearMapIfOwned(ref material, metalnessIndex);
                }

                material.maps[metalnessIndex].value = binding.MetallicScalar;

                if (binding.HasNormalMap)
                {
                    Rl.SetMaterialTexture(ref material, normalIndex, binding.Normal);
                }
                else
                {
                    ClearMapIfOwned(ref material, normalIndex);
                }
            }

            return true;
        }

        public bool TryGetHostPbrParams(
            int materialAssetId,
            out float roughnessScalar,
            out float metallicScalar,
            out bool hasRoughnessMap,
            out bool hasMetallicMap,
            out bool hasNormalMap)
        {
            ThrowIfDisposed();
            roughnessScalar = DefaultRoughness;
            metallicScalar = DefaultMetallic;
            hasRoughnessMap = false;
            hasMetallicMap = false;
            hasNormalMap = false;

            if (materialAssetId <= 0)
            {
                return false;
            }

            if (!TryResolveHostBinding(materialAssetId, out HostMaterialBinding binding))
            {
                return false;
            }

            roughnessScalar = binding.RoughnessScalar;
            metallicScalar = binding.MetallicScalar;
            hasRoughnessMap = binding.HasRoughnessMap;
            hasMetallicMap = binding.HasMetallicMap;
            hasNormalMap = binding.HasNormalMap;
            return true;
        }

        public bool TryResolveHostAlbedo(int materialAssetId, out Texture2D albedo)
        {
            ThrowIfDisposed();
            albedo = default;
            if (!TryResolveHostBinding(materialAssetId, out HostMaterialBinding binding))
            {
                return false;
            }

            albedo = binding.Albedo;
            return true;
        }

        public void DetachOwnedAlbedoMaps(Model model)
        {
            DetachOwnedMaps(model);
        }

        public void DetachOwnedMaps(Model model)
        {
            if (_disposed || model.materialCount <= 0 || model.materials == null)
            {
                return;
            }

            for (int i = 0; i < model.materialCount; i++)
            {
                ref Material material = ref model.materials[i];
                DetachOwnedMaps(ref material);
            }
        }

        public void DetachOwnedAlbedoMap(ref Material material)
        {
            DetachOwnedMaps(ref material);
        }

        public void DetachOwnedMaps(ref Material material)
        {
            if (_disposed || material.maps == null)
            {
                return;
            }

            ClearMapIfOwned(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO);
            ClearMapIfOwned(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_ROUGHNESS);
            ClearMapIfOwned(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_METALNESS);
            ClearMapIfOwned(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_NORMAL);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (KeyValuePair<int, HostMaterialBinding> entry in _bindingsByMaterialId)
            {
                UnloadOwned(entry.Value.Albedo);
                if (entry.Value.HasRoughnessMap)
                {
                    UnloadOwned(entry.Value.Roughness);
                }

                if (entry.Value.HasMetallicMap)
                {
                    UnloadOwned(entry.Value.Metallic);
                }

                if (entry.Value.HasNormalMap)
                {
                    UnloadOwned(entry.Value.Normal);
                }
            }

            _bindingsByMaterialId.Clear();
            _ownedTextureIds.Clear();
            _disposed = true;
        }

        private bool TryResolveHostBinding(int materialAssetId, out HostMaterialBinding binding)
        {
            binding = default;
            if (materialAssetId <= 0)
            {
                return false;
            }

            if (_bindingsByMaterialId.TryGetValue(materialAssetId, out binding))
            {
                return true;
            }

            if (!_materials.TryResolve(materialAssetId, out ResolvedMaterialAsset resolved))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} cannot bind materialId={materialAssetId}: material is not registered in {nameof(IRenderMaterialAssets)}.");
            }

            if (resolved.TextureUris.Count == 0)
            {
                return false;
            }

            string materialName = _materials.GetName(materialAssetId);
            foreach (KeyValuePair<string, string> pair in resolved.TextureUris)
            {
                if (!IsWellKnownSlot(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibMaterialHostBinder)} materialId={materialAssetId} ({materialName}) declares unknown texture slot '{pair.Key}'; well-known slots are albedo/roughness/metallic/normal.");
                }
            }

            Texture2D albedo = LoadRequiredMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Albedo);
            Texture2D roughness = default;
            Texture2D metallic = default;
            Texture2D normal = default;
            bool hasRoughness = TryLoadOptionalMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Roughness, out roughness);
            bool hasMetallic = TryLoadOptionalMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Metallic, out metallic);
            bool hasNormal = TryLoadOptionalMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Normal, out normal);

            binding = new HostMaterialBinding(
                albedo,
                roughness,
                metallic,
                normal,
                resolved.Roughness,
                resolved.Metallic,
                hasRoughness,
                hasMetallic,
                hasNormal);
            _bindingsByMaterialId[materialAssetId] = binding;
            return true;
        }

        private static bool IsWellKnownSlot(string slot)
        {
            return string.Equals(slot, MaterialTextureSlots.Albedo, StringComparison.Ordinal) ||
                   string.Equals(slot, MaterialTextureSlots.Roughness, StringComparison.Ordinal) ||
                   string.Equals(slot, MaterialTextureSlots.Metallic, StringComparison.Ordinal) ||
                   string.Equals(slot, MaterialTextureSlots.Normal, StringComparison.Ordinal);
        }

        private Texture2D LoadRequiredMap(
            int materialAssetId,
            string materialName,
            IReadOnlyDictionary<string, string> textureUris,
            string slotName)
        {
            if (!textureUris.TryGetValue(slotName, out string? uri) || string.IsNullOrWhiteSpace(uri))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} materialId={materialAssetId} ({materialName}) has no '{slotName}' texture URI.");
            }

            return LoadMapOrThrow(materialAssetId, materialName, uri, slotName);
        }

        private bool TryLoadOptionalMap(
            int materialAssetId,
            string materialName,
            IReadOnlyDictionary<string, string> textureUris,
            string slotName,
            out Texture2D texture)
        {
            texture = default;
            if (!textureUris.TryGetValue(slotName, out string? uri) || string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            texture = LoadMapOrThrow(materialAssetId, materialName, uri, slotName);
            return true;
        }

        private Texture2D LoadMapOrThrow(
            int materialAssetId,
            string materialName,
            string uri,
            string slotName)
        {
            if (!_vfs.TryResolveFullPath(uri, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} cannot resolve {slotName} URI '{uri}' for materialId={materialAssetId} ({materialName}).");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} {slotName} file missing for materialId={materialAssetId} ({materialName}): uri='{uri}' fullPath='{fullPath}'.");
            }

            Texture2D texture = Rl.LoadTexture(fullPath);
            if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
            {
                if (texture.id != 0)
                {
                    Rl.UnloadTexture(texture);
                }

                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialHostBinder)} LoadTexture failed for {slotName} materialId={materialAssetId} ({materialName}): uri='{uri}' fullPath='{fullPath}'.");
            }

            _ownedTextureIds.Add(texture.id);
            return texture;
        }

        private void ClearMapIfOwned(ref Material material, int mapIndex)
        {
            if (material.maps == null)
            {
                return;
            }

            uint textureId = material.maps[mapIndex].texture.id;
            if (textureId == 0 || !_ownedTextureIds.Contains(textureId))
            {
                return;
            }

            material.maps[mapIndex].texture = default;
        }

        private void UnloadOwned(Texture2D texture)
        {
            if (texture.id != 0)
            {
                Rl.UnloadTexture(texture);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibMaterialHostBinder));
            }
        }

        private readonly struct HostMaterialBinding
        {
            public HostMaterialBinding(
                Texture2D albedo,
                Texture2D roughness,
                Texture2D metallic,
                Texture2D normal,
                float roughnessScalar,
                float metallicScalar,
                bool hasRoughnessMap,
                bool hasMetallicMap,
                bool hasNormalMap)
            {
                Albedo = albedo;
                Roughness = roughness;
                Metallic = metallic;
                Normal = normal;
                RoughnessScalar = roughnessScalar;
                MetallicScalar = metallicScalar;
                HasRoughnessMap = hasRoughnessMap;
                HasMetallicMap = hasMetallicMap;
                HasNormalMap = hasNormalMap;
            }

            public Texture2D Albedo { get; }
            public Texture2D Roughness { get; }
            public Texture2D Metallic { get; }
            public Texture2D Normal { get; }
            public float RoughnessScalar { get; }
            public float MetallicScalar { get; }
            public bool HasRoughnessMap { get; }
            public bool HasMetallicMap { get; }
            public bool HasNormalMap { get; }
        }
    }
}
