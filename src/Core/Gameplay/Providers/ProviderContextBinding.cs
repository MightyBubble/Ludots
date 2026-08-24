using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Providers
{
    public static class ProviderContextBinding
    {
        public const string LiteralPrefix = "literal.";
        public const string SignalPrefix = "signal.";
        public const string ContextPrefix = "context.";

        public static void ValidateReference(string reference, string referencePath)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new InvalidOperationException(
                    $"Provider reference at '{referencePath}' is empty.");
            }

            if (reference.StartsWith(LiteralPrefix, StringComparison.Ordinal) ||
                reference.StartsWith(SignalPrefix, StringComparison.Ordinal) ||
                reference.StartsWith(ContextPrefix, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Provider reference '{reference}' at '{referencePath}' must use literal.*, signal.*, or context.* prefix.");
        }

        public static Dictionary<string, object?> CreateBindings(
            IReadOnlyDictionary<string, object?>? contextValues = null,
            IReadOnlyDictionary<string, object?>? signalValues = null)
        {
            var bindings = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (contextValues != null)
            {
                foreach (KeyValuePair<string, object?> pair in contextValues)
                {
                    string key = pair.Key.StartsWith(ContextPrefix, StringComparison.Ordinal)
                        ? pair.Key
                        : ContextPrefix + pair.Key;
                    bindings[key] = pair.Value;
                }
            }

            if (signalValues != null)
            {
                foreach (KeyValuePair<string, object?> pair in signalValues)
                {
                    string key = pair.Key.StartsWith(SignalPrefix, StringComparison.Ordinal)
                        ? pair.Key
                        : SignalPrefix + pair.Key;
                    bindings[key] = pair.Value;
                }
            }

            return bindings;
        }
    }
}
