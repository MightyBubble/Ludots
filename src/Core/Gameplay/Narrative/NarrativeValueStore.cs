using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Narrative
{
    public readonly struct NarrativeValue
    {
        public NarrativeValue(NarrativeValueKind kind, int intValue, float floatValue, bool boolValue, string stringValue)
        {
            Kind = kind;
            IntValue = intValue;
            FloatValue = floatValue;
            BoolValue = boolValue;
            StringValue = stringValue ?? string.Empty;
        }

        public NarrativeValueKind Kind { get; }
        public int IntValue { get; }
        public float FloatValue { get; }
        public bool BoolValue { get; }
        public string StringValue { get; }

        public static NarrativeValue FromDefinition(NarrativeVariableDefinition definition)
        {
            return definition.Kind switch
            {
                NarrativeValueKind.Float => FromFloat(definition.DefaultFloat),
                NarrativeValueKind.Bool => FromBool(definition.DefaultBool),
                NarrativeValueKind.String => FromString(definition.DefaultString),
                _ => FromInt(definition.DefaultInt),
            };
        }

        public static NarrativeValue FromInt(int value) => new(NarrativeValueKind.Int, value, value, value != 0, value.ToString());
        public static NarrativeValue FromFloat(float value) => new(NarrativeValueKind.Float, (int)value, value, Math.Abs(value) > 0.0001f, value.ToString("0.###"));
        public static NarrativeValue FromBool(bool value) => new(NarrativeValueKind.Bool, value ? 1 : 0, value ? 1f : 0f, value, value ? "true" : "false");
        public static NarrativeValue FromString(string value) => new(NarrativeValueKind.String, 0, 0f, !string.IsNullOrWhiteSpace(value), value ?? string.Empty);
    }

    public sealed class NarrativeValueStore
    {
        private readonly NarrativeDefinitionRegistry _definitions;
        private readonly Dictionary<int, NarrativeValue> _values = new();

        public NarrativeValueStore(NarrativeDefinitionRegistry definitions)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            ResetToDefaults();
        }

        public void ResetToDefaults()
        {
            _values.Clear();
            foreach (var variable in _definitions.Variables)
            {
                int variableId = _definitions.VariableIds.Register(variable.Id);
                _values[variableId] = NarrativeValue.FromDefinition(variable);
            }
        }

        public NarrativeValue Get(string variableId)
        {
            int variableKey = _definitions.VariableIds.GetId(variableId);
            if (variableKey <= 0 || !_values.TryGetValue(variableKey, out var value))
            {
                if (_definitions.TryGetVariable(variableId, out var definition))
                {
                    return NarrativeValue.FromDefinition(definition);
                }

                return NarrativeValue.FromInt(0);
            }

            return value;
        }

        public void Set(string variableId, NarrativeValue value)
        {
            int variableKey = _definitions.VariableIds.Register(variableId);
            _values[variableKey] = value;
        }

        public void Add(string variableId, NarrativeValue value)
        {
            var current = Get(variableId);
            switch (value.Kind)
            {
                case NarrativeValueKind.Float:
                    Set(variableId, NarrativeValue.FromFloat(current.FloatValue + value.FloatValue));
                    break;
                case NarrativeValueKind.Bool:
                    Set(variableId, NarrativeValue.FromBool(value.BoolValue));
                    break;
                case NarrativeValueKind.String:
                    Set(variableId, NarrativeValue.FromString(value.StringValue));
                    break;
                default:
                    Set(variableId, NarrativeValue.FromInt(current.IntValue + value.IntValue));
                    break;
            }
        }
    }
}
