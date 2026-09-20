using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Platform.Abstractions;

namespace Ludots.Content.EngineGallery
{
    /// <summary>
    /// 工程材质资产（.mat.json）读取器：把材质行（含 parent 实例链）注册进 GalleryMaterialAssets。
    /// 行形状与世界侧 assets/Presentation/material_assets.schema.json 一致；贴图槽为工程根相对 URI。
    /// </summary>
    public static class MaterialAssetReader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        public static int Register(GalleryMaterialAssets materials, string materialJsonPath, int materialAssetId)
        {
            return RegisterChain(materials, materialJsonPath, materialAssetId, []);
        }

        /// <summary>
        /// 沿 parent 链迭代注册（根优先）：同路径环 fail-fast；父已注册则复用其 id，
        /// 不重复消费 id 段；分配到的 id 若已被其他材质占用即冲突 fail-fast。
        /// </summary>
        private static int RegisterChain(
            GalleryMaterialAssets materials,
            string materialJsonPath,
            int materialAssetId,
            HashSet<string> visitedPaths)
        {
            string canonicalPath = Path.GetFullPath(materialJsonPath);
            if (!visitedPaths.Add(canonicalPath))
            {
                throw new InvalidDataException(
                    $"Material parent chain contains a cycle at '{materialJsonPath}'.");
            }

            MaterialRow? row;
            try
            {
                row = JsonSerializer.Deserialize<MaterialRow>(File.ReadAllText(materialJsonPath), JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Material asset '{materialJsonPath}' is not valid JSON: {exception.Message}", exception);
            }

            if (row == null || string.IsNullOrWhiteSpace(row.Id))
            {
                throw new InvalidDataException($"Material asset '{materialJsonPath}' is missing required field id.");
            }

            if (!string.IsNullOrWhiteSpace(row.Domain) && !string.Equals(row.Domain, "Surface", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Material asset '{materialJsonPath}' uses unsupported domain '{row.Domain}'; only 'Surface' is registered.");
            }

            string key = $"project.{row.Id}";
            int existingId = materials.GetId(key);
            if (existingId != 0)
            {
                if (existingId != materialAssetId && !IsSameKeyAtId(materials, existingId, key))
                {
                    throw new InvalidDataException(
                        $"Material asset '{row.Id}' is already registered under id {existingId}; requested id {materialAssetId} conflicts.");
                }

                return existingId;
            }

            if (materials.TryGet(materialAssetId, out MaterialAssetDescriptor taken) && materials.GetName(materialAssetId) != key)
            {
                throw new InvalidDataException(
                    $"Material asset '{row.Id}' would take id {materialAssetId} which is already held by '{materials.GetName(materialAssetId)}'.");
            }

            if (!string.IsNullOrWhiteSpace(row.Parent) && materials.GetId($"project.{row.Parent}") == 0)
            {
                string parentPath = Path.Combine(Path.GetDirectoryName(materialJsonPath)!, $"{row.Parent}.json");
                if (!File.Exists(parentPath))
                {
                    throw new FileNotFoundException(
                        $"Material asset '{row.Id}' declares parent '{row.Parent}' which has no sibling material file.", parentPath);
                }

                if (materialAssetId <= 1)
                {
                    throw new InvalidDataException(
                        $"Material asset '{row.Id}' has no id budget left for its parent chain under id {materialAssetId}.");
                }

                RegisterChain(materials, parentPath, materialAssetId - 1, visitedPaths);
            }

            var floats = new Dictionary<string, float>(StringComparer.Ordinal);
            if (row.Roughness is float roughness)
            {
                floats[MaterialParameterNames.Roughness] = roughness;
            }

            if (row.Metalness is float metalness)
            {
                floats[MaterialParameterNames.Metallic] = metalness;
            }

            if (row.Params?.Floats != null)
            {
                foreach (KeyValuePair<string, float> pair in row.Params.Floats)
                {
                    floats[pair.Key] = pair.Value;
                }
            }

            var colors = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            if (row.Params?.Colors != null)
            {
                foreach (KeyValuePair<string, float[]> pair in row.Params.Colors)
                {
                    if (pair.Value is not { Length: 4 })
                    {
                        throw new InvalidDataException($"Material asset '{materialJsonPath}' params.colors.{pair.Key} must be an RGBA quad.");
                    }

                    colors[pair.Key] = new Vector4(pair.Value[0], pair.Value[1], pair.Value[2], pair.Value[3]);
                }
            }

            Dictionary<string, string>? textureUris = null;
            if (row.Textures != null)
            {
                textureUris = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> slot in row.Textures)
                {
                    textureUris[slot.Key] = slot.Value;
                }
            }

            materials.Register(key, new MaterialAssetDescriptor(
                materialAssetId,
                MaterialAssetDomain.Surface,
                ParseFlags(row.Flags, materialJsonPath),
                shaderKey: string.IsNullOrWhiteSpace(row.ShaderKey) ? null : row.ShaderKey,
                parentKey: string.IsNullOrWhiteSpace(row.Parent) ? null : $"project.{row.Parent}",
                floatParams: floats,
                colorParams: colors), textureUris);
            return materialAssetId;
        }

        private static bool IsSameKeyAtId(GalleryMaterialAssets materials, int id, string key)
        {
            return string.Equals(materials.GetName(id), key, StringComparison.Ordinal);
        }

        private static MaterialAssetFlags ParseFlags(List<string>? flags, string sourcePath)
        {
            MaterialAssetFlags result = MaterialAssetFlags.None;
            if (flags == null)
            {
                return result;
            }

            foreach (string flag in flags)
            {
                result |= flag switch
                {
                    "Opaque" => MaterialAssetFlags.None,
                    "AlphaBlend" or "Transparent" => MaterialAssetFlags.Transparent,
                    "Cutout" => MaterialAssetFlags.Cutout,
                    "Additive" => MaterialAssetFlags.Additive,
                    "DoubleSided" => MaterialAssetFlags.DoubleSided,
                    "Unlit" => MaterialAssetFlags.Unlit,
                    _ => throw new InvalidDataException($"Material asset '{sourcePath}' uses unknown flag '{flag}'."),
                };
            }

            return result;
        }

        private sealed class MaterialRow
        {
            public string Id { get; set; } = string.Empty;

            public string? Domain { get; set; }

            public string? Parent { get; set; }

            public string? ShaderKey { get; set; }

            public List<string>? Flags { get; set; }

            public float? Roughness { get; set; }

            public float? Metalness { get; set; }

            public ParamsBlock? Params { get; set; }

            public Dictionary<string, string>? Textures { get; set; }
        }

        private sealed class ParamsBlock
        {
            public Dictionary<string, float>? Floats { get; set; }

            public Dictionary<string, float[]>? Colors { get; set; }
        }
    }
}
