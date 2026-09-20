using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ludots.Core.Gameplay.Providers
{
    public readonly struct ProviderKey : IEquatable<ProviderKey>
    {
        private static readonly Regex KeyPattern = new(
            @"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> AllowedDomains = new(StringComparer.Ordinal)
        {
            "time",
            "condition",
            "world",
            "supply",
            "deployment",
            "combat",
            "city_control",
            "facility",
            "training",
            "troop_pool",
            "city_economy",
            "intelligence",
            "population",
            "hero",
            "hero_relation",
            "prisoner",
            "faction",
            "court",
            "petition",
            "succession",
            "authorization",
            "diplomacy",
            "agreement",
            "technology",
            "task",
            "activity",
            "fixture",
        };

        public ProviderKey(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            int separator = value.IndexOf('.');
            Domain = separator > 0 ? value.Substring(0, separator) : string.Empty;
            Action = separator > 0 && separator < value.Length - 1
                ? value.Substring(separator + 1)
                : string.Empty;
        }

        public string Value { get; }
        public string Domain { get; }
        public string Action { get; }

        public static bool TryParse(string? raw, out ProviderKey key, out string failureCode, out string reason)
        {
            key = default;
            failureCode = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
            {
                failureCode = ProviderFailureCodes.InvalidProviderKeyForm;
                reason = "Provider key is empty.";
                return false;
            }

            string trimmed = raw.Trim();
            if (!string.Equals(trimmed, raw, StringComparison.Ordinal))
            {
                failureCode = ProviderFailureCodes.InvalidProviderKeyForm;
                reason = $"Provider key '{raw}' must not include leading or trailing whitespace.";
                return false;
            }

            if (!KeyPattern.IsMatch(trimmed))
            {
                failureCode = ProviderFailureCodes.InvalidProviderKeyForm;
                reason = $"Provider key '{trimmed}' must match domain.snake_case.";
                return false;
            }

            var parsed = new ProviderKey(trimmed);
            if (!AllowedDomains.Contains(parsed.Domain))
            {
                failureCode = ProviderFailureCodes.DomainNotAllowed;
                reason = $"Provider domain '{parsed.Domain}' is not in the allowed domain whitelist (key '{trimmed}').";
                return false;
            }

            key = parsed;
            return true;
        }

        public static ProviderKey Parse(string raw)
        {
            if (!TryParse(raw, out ProviderKey key, out string failureCode, out string reason))
            {
                throw new InvalidOperationException($"{failureCode}: {reason}");
            }

            return key;
        }

        public bool Equals(ProviderKey other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ProviderKey other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static IReadOnlyCollection<string> GetAllowedDomains() => AllowedDomains;
    }
}
