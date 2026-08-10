using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Ludots.Core.Gameplay.Providers
{
    public static class ProviderParameterValues
    {
        public static int ReadInt(IReadOnlyDictionary<string, object?> parameters, string key)
        {
            object value = Require(parameters, key);
            return ToInt(value, key);
        }

        public static float ReadFloat(IReadOnlyDictionary<string, object?> parameters, string key)
        {
            object value = Require(parameters, key);
            return ToFloat(value, key);
        }

        public static bool ReadBool(
            IReadOnlyDictionary<string, object?> parameters,
            string key,
            bool defaultValue)
        {
            if (!parameters.TryGetValue(key, out object? value) || value == null)
            {
                return defaultValue;
            }

            return ToBool(value, key);
        }

        public static string ReadString(IReadOnlyDictionary<string, object?> parameters, string key)
        {
            object value = Require(parameters, key);
            string text = ToStringValue(value, key);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"Missing string parameter '{key}'.");
            }

            return text;
        }

        public static object? Normalize(object? value)
        {
            if (value is not JsonElement json)
            {
                return value;
            }

            return json.ValueKind switch
            {
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number when json.TryGetInt64(out long asLong) && asLong is >= int.MinValue and <= int.MaxValue
                    && json.TryGetDouble(out double asDouble) && Math.Abs(asDouble - asLong) < double.Epsilon
                    => (int)asLong,
                JsonValueKind.Number => json.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => json,
            };
        }

        public static Dictionary<string, object?> NormalizeMap(IReadOnlyDictionary<string, object?> parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            var normalized = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> pair in parameters)
            {
                normalized[pair.Key] = Normalize(pair.Value);
            }

            return normalized;
        }

        private static object Require(IReadOnlyDictionary<string, object?> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out object? value) || value == null)
            {
                throw new InvalidOperationException($"Missing parameter '{key}'.");
            }

            return value;
        }

        private static int ToInt(object value, string key)
        {
            object? normalized = Normalize(value);
            return normalized switch
            {
                int i => i,
                long l => checked((int)l),
                short s => s,
                byte b => b,
                float f => checked((int)f),
                double d => checked((int)d),
                string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
                _ => throw new InvalidOperationException($"Parameter '{key}' is not an int ({value.GetType().Name})."),
            };
        }

        private static float ToFloat(object value, string key)
        {
            object? normalized = Normalize(value);
            return normalized switch
            {
                float f => f,
                double d => (float)d,
                decimal m => (float)m,
                int i => i,
                long l => l,
                string text when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) => parsed,
                _ => throw new InvalidOperationException($"Parameter '{key}' is not a float ({value.GetType().Name})."),
            };
        }

        private static bool ToBool(object value, string key)
        {
            object? normalized = Normalize(value);
            return normalized switch
            {
                bool b => b,
                string text when bool.TryParse(text, out bool parsed) => parsed,
                _ => throw new InvalidOperationException($"Parameter '{key}' is not a bool ({value.GetType().Name})."),
            };
        }

        private static string ToStringValue(object value, string key)
        {
            object? normalized = Normalize(value);
            return normalized switch
            {
                string text => text,
                _ => throw new InvalidOperationException($"Parameter '{key}' is not a string ({value.GetType().Name})."),
            };
        }
    }
}
