using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    [Flags]
    public enum MaterialAssetFlags : ushort
    {
        None = 0,
        Transparent = 1 << 0,
        DoubleSided = 1 << 1,
        Unlit = 1 << 2,
        Cutout = 1 << 3,
        Additive = 1 << 4,
    }

    public enum MaterialBlendMode : byte
    {
        Opaque = 0,
        Cutout = 1,
        AlphaBlend = 2,
        Additive = 3,
    }

    public static class MaterialBlendModeResolver
    {
        private const MaterialAssetFlags ExclusiveBlendFlags =
            MaterialAssetFlags.Transparent | MaterialAssetFlags.Cutout | MaterialAssetFlags.Additive;

        public static MaterialBlendMode Resolve(MaterialAssetFlags flags)
        {
            MaterialAssetFlags blend = flags & ExclusiveBlendFlags;
            return blend switch
            {
                MaterialAssetFlags.None => MaterialBlendMode.Opaque,
                MaterialAssetFlags.Cutout => MaterialBlendMode.Cutout,
                MaterialAssetFlags.Transparent => MaterialBlendMode.AlphaBlend,
                MaterialAssetFlags.Additive => MaterialBlendMode.Additive,
                _ => throw new InvalidOperationException(
                    $"Material flags '{flags}' combine mutually exclusive blend modes; use exactly one of Cutout, Transparent/AlphaBlend, or Additive (or none for Opaque)."),
            };
        }
    }

    public enum MaterialAssetDomain : byte
    {
        Surface = 0,
    }

    /// <summary>命名参数里的知名 key；其余 key 原样传给着色器 uniform。</summary>
    public static class MaterialParameterNames
    {
        public const string Roughness = "roughness";
        public const string Metallic = "metallic";
    }

    /// <summary>知名贴图槽；其余命名贴图由 shaderKey 对应的着色器声明消费。</summary>
    public static class MaterialTextureSlots
    {
        public const string Albedo = "albedo";
        public const string Roughness = "roughness";
        public const string Metallic = "metallic";
        public const string Normal = "normal";
    }

    /// <summary>
    /// 材质逻辑定义：shaderKey 选择着色行为，parentKey 派生实例（稀疏覆盖命名参数），
    /// 命名参数表承载 float/color 覆盖。平台贴图 URI 不在此——经宿主侧按名挂载后由解析结果携带。
    /// </summary>
    public readonly struct MaterialAssetDescriptor
    {
        private static readonly IReadOnlyDictionary<string, float> EmptyFloats =
            new Dictionary<string, float>(0, StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<string, Vector4> EmptyColors =
            new Dictionary<string, Vector4>(0, StringComparer.Ordinal);

        public const float DefaultRoughness = 0.85f;
        public const float DefaultMetalness = 0f;
        public const string DefaultShaderKey = "lit";

        public MaterialAssetDescriptor(
            int id,
            MaterialAssetDomain domain,
            MaterialAssetFlags flags,
            string? shaderKey = null,
            string? parentKey = null,
            IReadOnlyDictionary<string, float>? floatParams = null,
            IReadOnlyDictionary<string, Vector4>? colorParams = null)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            Id = id;
            Domain = domain;
            Flags = flags;
            ShaderKey = string.IsNullOrWhiteSpace(shaderKey) ? DefaultShaderKey : shaderKey;
            ParentKey = string.IsNullOrWhiteSpace(parentKey) ? null : parentKey;
            FloatParams = floatParams ?? EmptyFloats;
            ColorParams = colorParams ?? EmptyColors;
        }

        public int Id { get; }

        public MaterialAssetDomain Domain { get; }

        public MaterialAssetFlags Flags { get; }

        public string ShaderKey { get; }

        public string? ParentKey { get; }

        public IReadOnlyDictionary<string, float> FloatParams { get; }

        public IReadOnlyDictionary<string, Vector4> ColorParams { get; }

        public float Roughness => FloatParams.TryGetValue(MaterialParameterNames.Roughness, out float value) ? value : DefaultRoughness;

        public float Metalness => FloatParams.TryGetValue(MaterialParameterNames.Metallic, out float value) ? value : DefaultMetalness;
    }

    /// <summary>实例链合并后的运行时视图：根级供给 shaderKey/flags/domain，命名参数与贴图子覆盖父。</summary>
    public readonly struct ResolvedMaterialAsset
    {
        public ResolvedMaterialAsset(
            int id,
            string shaderKey,
            MaterialAssetDomain domain,
            MaterialAssetFlags flags,
            IReadOnlyDictionary<string, float> floats,
            IReadOnlyDictionary<string, Vector4> colors,
            IReadOnlyDictionary<string, string> textureUris)
        {
            Id = id;
            ShaderKey = shaderKey ?? throw new ArgumentNullException(nameof(shaderKey));
            Domain = domain;
            Flags = flags;
            Floats = floats ?? throw new ArgumentNullException(nameof(floats));
            Colors = colors ?? throw new ArgumentNullException(nameof(colors));
            TextureUris = textureUris ?? throw new ArgumentNullException(nameof(textureUris));
        }

        public int Id { get; }

        public string ShaderKey { get; }

        public MaterialAssetDomain Domain { get; }

        public MaterialAssetFlags Flags { get; }

        public IReadOnlyDictionary<string, float> Floats { get; }

        public IReadOnlyDictionary<string, Vector4> Colors { get; }

        public IReadOnlyDictionary<string, string> TextureUris { get; }

        public float Roughness => Floats.TryGetValue(MaterialParameterNames.Roughness, out float value) ? value : MaterialAssetDescriptor.DefaultRoughness;

        public float Metallic => Floats.TryGetValue(MaterialParameterNames.Metallic, out float value) ? value : MaterialAssetDescriptor.DefaultMetalness;

        public MaterialBlendMode BlendMode => MaterialBlendModeResolver.Resolve(Flags);
    }

    /// <summary>
    /// 实例链合并的唯一实现（注册表与画廊直读表共用）。实例只覆盖命名参数与贴图；
    /// shaderKey/flags 属根的静态开关，实例声明即拒绝（对齐商业引擎材质实例语义）。
    /// </summary>
    public static class MaterialAssetResolver
    {
        public static ResolvedMaterialAsset Resolve(
            IRenderMaterialAssets assets,
            int materialId,
            Func<int, IReadOnlyDictionary<string, string>?> hostTextureUris)
        {
            if (assets == null)
            {
                throw new ArgumentNullException(nameof(assets));
            }

            if (hostTextureUris == null)
            {
                throw new ArgumentNullException(nameof(hostTextureUris));
            }

            var chain = new List<MaterialAssetDescriptor>(capacity: 4);
            var visited = new HashSet<int>();
            MaterialAssetDescriptor current;
            if (!assets.TryGet(materialId, out current))
            {
                throw new InvalidOperationException(
                    $"{nameof(MaterialAssetResolver)} cannot resolve materialId={materialId}: not registered in {nameof(IRenderMaterialAssets)}.");
            }

            while (true)
            {
                if (!visited.Add(current.Id))
                {
                    throw new InvalidOperationException(
                        $"{nameof(MaterialAssetResolver)} detected a parent cycle at materialId={current.Id} ('{assets.GetName(current.Id)}') while resolving materialId={materialId}.");
                }

                chain.Add(current);
                if (current.ParentKey == null)
                {
                    break;
                }

                int parentId = assets.GetId(current.ParentKey);
                if (parentId == 0 || !assets.TryGet(parentId, out MaterialAssetDescriptor parent))
                {
                    throw new InvalidOperationException(
                        $"{nameof(MaterialAssetResolver)} material '{assets.GetName(current.Id)}' declares unknown parent '{current.ParentKey}'.");
                }

                current = parent;
            }

            MaterialAssetDescriptor root = chain[chain.Count - 1];
            var floats = new SortedDictionary<string, float>(StringComparer.Ordinal);
            var colors = new SortedDictionary<string, Vector4>(StringComparer.Ordinal);
            var textureUris = new SortedDictionary<string, string>(StringComparer.Ordinal);

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                MaterialAssetDescriptor node = chain[i];
                bool isRoot = i == chain.Count - 1;
                if (!isRoot &&
                    (!string.Equals(node.ShaderKey, MaterialAssetDescriptor.DefaultShaderKey, StringComparison.Ordinal) ||
                     node.Flags != MaterialAssetFlags.None))
                {
                    throw new InvalidOperationException(
                        $"{nameof(MaterialAssetResolver)} instance '{assets.GetName(node.Id)}' cannot override shaderKey/flags; only named params and textures are overridable.");
                }

                foreach (KeyValuePair<string, float> pair in node.FloatParams)
                {
                    floats[pair.Key] = pair.Value;
                }

                foreach (KeyValuePair<string, Vector4> pair in node.ColorParams)
                {
                    colors[pair.Key] = pair.Value;
                }

                IReadOnlyDictionary<string, string>? nodeTextures = hostTextureUris(node.Id);
                if (nodeTextures != null)
                {
                    foreach (KeyValuePair<string, string> pair in nodeTextures)
                    {
                        textureUris[pair.Key] = pair.Value;
                    }
                }
            }

            return new ResolvedMaterialAsset(
                materialId,
                root.ShaderKey,
                root.Domain,
                root.Flags,
                floats,
                colors,
                textureUris);
        }
    }
}
