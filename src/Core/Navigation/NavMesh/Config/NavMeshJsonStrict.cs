using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// navmesh.json 的严格 JSON 校验原语。config/loader 与各子校验器共用，
    /// 避免每个新段落重写一遍"未知键拒绝 + 必需键存在"的规则。
    /// </summary>
    internal static class NavMeshJsonStrict
    {
        public static void RequireOnlyProperties(JsonObject obj, string path, params string[] allowed)
            => RequireOnlyProperties(obj, path, allowed, Array.Empty<string>());

        /// <summary>未知键拒绝是绝对的；<paramref name="optional"/> 键存在时校验、不存在不要求，
        /// 使新词汇落地不必强迫每份既有 navmesh.json 重复声明。</summary>
        public static void RequireOnlyProperties(JsonObject obj, string path, string[] allowed, string[] optional)
        {
            foreach (var property in obj)
            {
                bool known = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(property.Key, allowed[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                for (int i = 0; !known && i < optional.Length; i++)
                {
                    if (string.Equals(property.Key, optional[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new InvalidOperationException($"{path} contains unknown property '{property.Key}'.");
                }
            }

            for (int i = 0; i < allowed.Length; i++)
            {
                if (!obj.ContainsKey(allowed[i]))
                {
                    throw new InvalidOperationException($"{path} must explicitly define '{allowed[i]}'.");
                }
            }
        }

        public static string RequireString(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{path}.{key} must be a non-empty string.");
            }

            if (!string.Equals(text.Trim(), text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.{key} must not contain leading or trailing whitespace.");
            }

            return text;
        }

        public static void RequireNumber(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value ||
                (!value.TryGetValue<int>(out _) &&
                 !value.TryGetValue<float>(out _) &&
                 !value.TryGetValue<double>(out _)))
            {
                throw new InvalidOperationException($"{path}.{key} must be a number.");
            }
        }

        public static int RequireInt(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<int>(out int number))
            {
                throw new InvalidOperationException($"{path}.{key} must be an integer.");
            }

            return number;
        }

        public static float RequireFloat(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value)
            {
                throw new InvalidOperationException($"{path}.{key} must be a number.");
            }

            if (value.TryGetValue<float>(out float number))
            {
                return number;
            }

            if (value.TryGetValue<double>(out double numberDouble))
            {
                return (float)numberDouble;
            }

            throw new InvalidOperationException($"{path}.{key} must be a number.");
        }

        public static bool RequireBool(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
            {
                throw new InvalidOperationException($"{path}.{key} must be a boolean.");
            }

            return flag;
        }

        public static bool IsBoolean(JsonNode? node)
        {
            return node is not null &&
                (node.GetValueKind() == JsonValueKind.True || node.GetValueKind() == JsonValueKind.False);
        }
    }
}
