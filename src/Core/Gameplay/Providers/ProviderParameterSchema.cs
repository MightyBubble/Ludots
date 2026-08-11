using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Ludots.Core.Gameplay.Providers
{
    public enum ProviderParameterKind : byte
    {
        String = 1,
        Int = 2,
        Float = 3,
        Bool = 4,
        EntityRef = 5,
        StringList = 6,
    }

    public sealed class ProviderParameterField
    {
        public ProviderParameterField(string name, ProviderParameterKind kind, bool required)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Parameter field name is required.", nameof(name));
            }

            Name = name;
            Kind = kind;
            Required = required;
        }

        public string Name { get; }
        public ProviderParameterKind Kind { get; }
        public bool Required { get; }
    }

    public sealed class ProviderParameterSchema
    {
        private readonly Dictionary<string, ProviderParameterField> _fields;

        public ProviderParameterSchema(IEnumerable<ProviderParameterField> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);
            _fields = new Dictionary<string, ProviderParameterField>(StringComparer.Ordinal);
            foreach (ProviderParameterField field in fields)
            {
                if (!_fields.TryAdd(field.Name, field))
                {
                    throw new InvalidOperationException(
                        $"Duplicate provider parameter field '{field.Name}'.");
                }
            }
        }

        public static ProviderParameterSchema Empty { get; } = new(Array.Empty<ProviderParameterField>());

        public IReadOnlyDictionary<string, ProviderParameterField> Fields => _fields;

        public void Validate(IReadOnlyDictionary<string, object?> parameters, string referencePath)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            if (string.IsNullOrWhiteSpace(referencePath))
            {
                throw new ArgumentException("referencePath is required.", nameof(referencePath));
            }

            foreach (KeyValuePair<string, ProviderParameterField> pair in _fields)
            {
                if (pair.Value.Required && !parameters.ContainsKey(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"{ProviderFailureCodes.ParameterSchemaMismatch}: missing required parameter '{pair.Key}' at '{referencePath}'.");
                }
            }

            foreach (KeyValuePair<string, object?> pair in parameters)
            {
                if (!_fields.TryGetValue(pair.Key, out ProviderParameterField? field))
                {
                    throw new InvalidOperationException(
                        $"{ProviderFailureCodes.ParameterSchemaMismatch}: undeclared parameter '{pair.Key}' at '{referencePath}'.");
                }

                if (pair.Value == null)
                {
                    if (field.Required)
                    {
                        throw new InvalidOperationException(
                            $"{ProviderFailureCodes.ParameterSchemaMismatch}: required parameter '{pair.Key}' is null at '{referencePath}'.");
                    }

                    continue;
                }

                if (!IsCompatible(field.Kind, pair.Value))
                {
                    throw new InvalidOperationException(
                        $"{ProviderFailureCodes.ParameterSchemaMismatch}: parameter '{pair.Key}' expected {field.Kind} at '{referencePath}'.");
                }
            }
        }

        private static bool IsCompatible(ProviderParameterKind kind, object value)
        {
            if (value is JsonElement json)
            {
                return kind switch
                {
                    ProviderParameterKind.String => json.ValueKind == JsonValueKind.String,
                    ProviderParameterKind.Int => json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out _),
                    ProviderParameterKind.Float => json.ValueKind == JsonValueKind.Number,
                    ProviderParameterKind.Bool => json.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    ProviderParameterKind.EntityRef =>
                        json.ValueKind == JsonValueKind.String ||
                        (json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out _)),
                    ProviderParameterKind.StringList => json.ValueKind == JsonValueKind.Array,
                    _ => false,
                };
            }

            return kind switch
            {
                ProviderParameterKind.String => value is string,
                ProviderParameterKind.Int => value is int or long or short or byte,
                ProviderParameterKind.Float => value is float or double or decimal or int or long,
                ProviderParameterKind.Bool => value is bool,
                ProviderParameterKind.EntityRef => value is string or int or long,
                ProviderParameterKind.StringList => value is IEnumerable<string>,
                _ => false,
            };
        }
    }
}
