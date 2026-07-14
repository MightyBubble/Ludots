using System;
using System.Collections.Generic;

namespace Ludots.Core.Config
{
    public sealed class ComponentAuthoringContext
    {
        private readonly Dictionary<string, object> _services;

        public ComponentAuthoringContext(Dictionary<string, object>? services = null)
        {
            _services = services ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public static ComponentAuthoringContext Empty { get; } = new();

        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Component authoring service key is required.", nameof(key));
            }

            _services[key] = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_services.TryGetValue(key, out object? raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }

        public T Require<T>(string key)
        {
            if (TryGet(key, out T value))
            {
                return value;
            }

            throw new InvalidOperationException($"Component authoring service '{key}' is required.");
        }
    }

    public static class ComponentAuthoringServiceKeys
    {
        public const string AbilityDefinitionRegistry = "AbilityDefinitionRegistry";
        public const string AbilityFormSetRegistry = "AbilityFormSetRegistry";
        public const string Physics2DShapeStorage = "Physics2D.ShapeStorage";
    }
}
