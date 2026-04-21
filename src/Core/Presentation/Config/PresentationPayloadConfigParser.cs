using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Config
{
    internal static class PresentationPayloadConfigParser
    {
        public static PresentationPayloadField[] ParsePayload(JsonNode node, string context)
        {
            if (node == null)
            {
                return Array.Empty<PresentationPayloadField>();
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException($"{context} payload must be an array of typed fields.");
            }

            if (arr.Count == 0)
            {
                return Array.Empty<PresentationPayloadField>();
            }

            var fields = new PresentationPayloadField[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"{context} payload field at index {i} must be an object.");
                }

                string name =
                    obj["name"]?.GetValue<string>() ??
                    obj["fieldName"]?.GetValue<string>() ??
                    obj["field"]?.GetValue<string>() ??
                    string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException($"{context} payload field at index {i} requires a non-empty name.");
                }

                string kindText =
                    obj["type"]?.GetValue<string>() ??
                    obj["valueKind"]?.GetValue<string>() ??
                    obj["kind"]?.GetValue<string>() ??
                    string.Empty;
                if (!TryParseTypedValueKind(kindText, out var kind))
                {
                    throw new InvalidOperationException($"{context} payload field '{name}' has invalid type '{kindText}'.");
                }

                JsonNode valueNode = obj["value"] ?? obj["defaultValue"] ?? obj["default"];
                if (valueNode == null && kind != PresentationTypedValueKind.StructuredObject)
                {
                    throw new InvalidOperationException($"{context} payload field '{name}' requires a value.");
                }

                fields[i] = new PresentationPayloadField(name, ParseTypedValue(kind, valueNode ?? obj));
            }

            return fields;
        }

        public static bool TryParseTypedValueKind(string text, out PresentationTypedValueKind kind)
        {
            if (Enum.TryParse(text, ignoreCase: true, out kind))
            {
                return true;
            }

            string normalized = NormalizeEnumToken(text);
            if (string.Equals(normalized, "vector", StringComparison.OrdinalIgnoreCase))
            {
                kind = PresentationTypedValueKind.Vector4;
                return true;
            }

            if (string.Equals(normalized, "asset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "assetreference", StringComparison.OrdinalIgnoreCase))
            {
                kind = PresentationTypedValueKind.AssetRef;
                return true;
            }

            string[] names = Enum.GetNames<PresentationTypedValueKind>();
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(NormalizeEnumToken(names[i]), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    kind = Enum.Parse<PresentationTypedValueKind>(names[i]);
                    return true;
                }
            }

            kind = PresentationTypedValueKind.None;
            return false;
        }

        private static PresentationTypedValue ParseTypedValue(PresentationTypedValueKind kind, JsonNode node)
        {
            return kind switch
            {
                PresentationTypedValueKind.Bool => PresentationTypedValue.FromBool(ParseBool(node)),
                PresentationTypedValueKind.Int => PresentationTypedValue.FromInt(ParseInt(node)),
                PresentationTypedValueKind.Float => PresentationTypedValue.FromFloat(ParseFloat(node)),
                PresentationTypedValueKind.Vector4 => PresentationTypedValue.FromVector4(ParseVector4(node)),
                PresentationTypedValueKind.Color => PresentationTypedValue.FromColor(ParseVector4(node)),
                PresentationTypedValueKind.AssetRef => PresentationTypedValue.FromAssetRef(ParseString(node, "asset-ref payload value")),
                PresentationTypedValueKind.StructuredObject => PresentationTypedValue.FromStructuredObject(node?.ToJsonString() ?? "{}"),
                PresentationTypedValueKind.Entity => throw new InvalidOperationException("Static config payload cannot encode runtime Entity values."),
                _ => throw new InvalidOperationException($"Unsupported presentation payload type '{kind}'."),
            };
        }

        private static bool ParseBool(JsonNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node is JsonValue value && value.TryGetValue(out bool boolValue))
            {
                return boolValue;
            }

            if (node is JsonValue numeric && numeric.TryGetValue(out int intValue))
            {
                return intValue != 0;
            }

            string text = node.GetValue<string>();
            if (bool.TryParse(text, out bool parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"Invalid bool presentation payload value '{text}'.");
        }

        private static int ParseInt(JsonNode node)
        {
            if (node == null)
            {
                return 0;
            }

            if (node is JsonValue value && value.TryGetValue(out int intValue))
            {
                return intValue;
            }

            if (node is JsonValue numeric && numeric.TryGetValue(out float floatValue))
            {
                return (int)MathF.Round(floatValue);
            }

            string text = node.GetValue<string>();
            if (int.TryParse(text, out int parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"Invalid int presentation payload value '{text}'.");
        }

        private static float ParseFloat(JsonNode node)
        {
            if (node == null)
            {
                return 0f;
            }

            if (node is JsonValue value && value.TryGetValue(out float floatValue))
            {
                return floatValue;
            }

            string text = node.GetValue<string>();
            if (float.TryParse(text, out float parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"Invalid float presentation payload value '{text}'.");
        }

        private static string ParseString(JsonNode node, string label)
        {
            string value = node?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{label} must be a non-empty string.");
            }

            return value;
        }

        private static Vector4 ParseVector4(JsonNode node)
        {
            if (node is JsonArray arr && arr.Count >= 4)
            {
                return new Vector4(
                    arr[0]?.GetValue<float>() ?? 0f,
                    arr[1]?.GetValue<float>() ?? 0f,
                    arr[2]?.GetValue<float>() ?? 0f,
                    arr[3]?.GetValue<float>() ?? 0f);
            }

            if (node is JsonObject obj)
            {
                return new Vector4(
                    obj["x"]?.GetValue<float>() ?? obj["r"]?.GetValue<float>() ?? 0f,
                    obj["y"]?.GetValue<float>() ?? obj["g"]?.GetValue<float>() ?? 0f,
                    obj["z"]?.GetValue<float>() ?? obj["b"]?.GetValue<float>() ?? 0f,
                    obj["w"]?.GetValue<float>() ?? obj["a"]?.GetValue<float>() ?? 0f);
            }

            throw new InvalidOperationException("Vector4/color presentation payload values must be [x,y,z,w] arrays or objects.");
        }

        private static string NormalizeEnumToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal);
        }
    }
}
