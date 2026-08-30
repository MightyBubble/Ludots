using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 材质运行时装订库：经 IRenderMaterialAssets.TryResolve 解析实例链（命名参数 + 命名贴图），
    /// 加载并持有贴图所有权，把结果挂到 Raylib Material 槽位。改贴图/改参数不动父材质——
    /// 实例链合并已在 Core 侧完成，这里只消费 resolved 视图。
    /// </summary>
    public sealed unsafe class RaylibMaterialLibrary : IDisposable
    {
        public const float DefaultRoughness = MaterialAssetDescriptor.DefaultRoughness;
        public const float DefaultMetallic = MaterialAssetDescriptor.DefaultMetalness;

        private readonly IRenderAssetPathResolver _vfs;
        private readonly IRenderMaterialAssets _materials;
        private readonly RaylibAssetStore<Texture2D> _textureStore;
        private readonly Dictionary<int, List<RaylibAssetStore<Texture2D>.Lease>> _bindingLeases = new();
        private readonly Dictionary<int, MaterialBinding> _bindingsByMaterialId = new();
        private readonly HashSet<uint> _ownedTextureIds = new();
        private bool _disposed;

        private readonly bool _ownsTextureStore;

        public RaylibMaterialLibrary(IRenderAssetPathResolver vfs, IRenderMaterialAssets materials)
            : this(vfs, materials, CreateStandaloneTextureStore(vfs))
        {
            _ownsTextureStore = true;
        }

        public RaylibMaterialLibrary(IRenderAssetPathResolver vfs, IRenderMaterialAssets materials, RaylibAssetStore<Texture2D> textureStore)
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _textureStore = textureStore ?? throw new ArgumentNullException(nameof(textureStore));
        }

        private static RaylibAssetStore<Texture2D> CreateStandaloneTextureStore(IRenderAssetPathResolver? vfs)
        {
            return new RaylibAssetStore<Texture2D>(vfs, fullPath =>
            {
                Texture2D texture = RaylibNativeResources.LoadTexture(fullPath);
                if (texture.id == 0 || texture.width <= 0 || texture.height <= 0)
                {
                    if (texture.id != 0)
                    {
                        RaylibNativeResources.UnloadTexture(texture);
                    }

                    throw new InvalidOperationException(
                        $"raylib rejected texture '{fullPath}' (textureId={texture.id}, size={texture.width}x{texture.height}).");
                }

                return texture;
            }, RaylibNativeResources.UnloadTexture);
        }

        /// <summary>解析后的材质视图（shaderKey/命名参数/命名贴图 URI）；未注册返回 false。</summary>
        public bool TryGetResolved(int materialAssetId, out ResolvedMaterialAsset resolved)
        {
            ThrowIfDisposed();
            return _materials.TryResolve(materialAssetId, out resolved);
        }

        public bool TryApplyMaps(ref Material material, int materialAssetId)
        {
            ThrowIfDisposed();
            if (materialAssetId <= 0)
            {
                return false;
            }

            if (!TryResolveBinding(materialAssetId, out MaterialBinding binding))
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

        public bool TryGetPbrParams(
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

            if (!TryResolveBinding(materialAssetId, out MaterialBinding binding))
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

            foreach (KeyValuePair<int, MaterialBinding> entry in _bindingsByMaterialId)
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
            foreach (List<RaylibAssetStore<Texture2D>.Lease> leases in _bindingLeases.Values)
            {
                foreach (RaylibAssetStore<Texture2D>.Lease lease in leases)
                {
                    lease.Dispose();
                }
            }

            _bindingLeases.Clear();
            _ownedTextureIds.Clear();
            if (_ownsTextureStore)
            {
                _textureStore.Dispose();
            }

            _disposed = true;
        }

        private bool TryResolveBinding(int materialAssetId, out MaterialBinding binding)
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
                    $"{nameof(RaylibMaterialLibrary)} cannot bind materialId={materialAssetId}: material is not registered in {nameof(IRenderMaterialAssets)}.");
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
                        $"{nameof(RaylibMaterialLibrary)} materialId={materialAssetId} ({materialName}) declares unknown texture slot '{pair.Key}'; well-known slots are albedo/roughness/metallic/normal.");
                }
            }

            Texture2D albedo;
            Texture2D roughness = default;
            Texture2D metallic = default;
            Texture2D normal = default;
            bool hasRoughness;
            bool hasMetallic;
            bool hasNormal;
            try
            {
                albedo = LoadRequiredMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Albedo);
                hasRoughness = TryLoadOptionalMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Roughness, out roughness);
                hasMetallic = TryLoadOptionalMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Metallic, out metallic);
                hasNormal = TryLoadOptionalMap(materialAssetId, materialName, resolved.TextureUris, MaterialTextureSlots.Normal, out normal);
            }
            catch
            {
                // 绑定未成立：回收本次已取得的租约，避免部分失败累积引用（#1327 复核修复）。
                if (_bindingLeases.TryGetValue(materialAssetId, out List<RaylibAssetStore<Texture2D>.Lease>? partial))
                {
                    foreach (RaylibAssetStore<Texture2D>.Lease lease in partial)
                    {
                        lease.Dispose();
                    }

                    _bindingLeases.Remove(materialAssetId);
                }

                throw;
            }

            binding = new MaterialBinding(
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
                    $"{nameof(RaylibMaterialLibrary)} materialId={materialAssetId} ({materialName}) has no '{slotName}' texture URI.");
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
            RaylibAssetStore<Texture2D>.Lease lease;
            try
            {
                lease = _textureStore.Acquire(uri);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibMaterialLibrary)} {slotName} texture load failed for materialId={materialAssetId} ({materialName}): uri='{uri}': {ex.Message}");
            }

            if (!_bindingLeases.TryGetValue(materialAssetId, out List<RaylibAssetStore<Texture2D>.Lease>? leases))
            {
                leases = new List<RaylibAssetStore<Texture2D>.Lease>();
                _bindingLeases[materialAssetId] = leases;
            }

            leases.Add(lease);
            Texture2D texture = lease.Resource;
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
            // 贴图生命周期归 RaylibAssetStore：库只在 Dispose 释放租约，实际销毁延迟到存储冲刷（#1327）。
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibMaterialLibrary));
            }
        }

        private readonly struct MaterialBinding
        {
            public MaterialBinding(
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
