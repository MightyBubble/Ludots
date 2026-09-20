using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Providers
{
    public enum ProviderKind : byte
    {
        Source = 1,
        Selector = 2,
        Condition = 3,
        Effect = 4,
    }

    public class ProviderRegistry<T>
        where T : class
    {
        private readonly ProviderKind _kind;
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly ProviderGapCatalog _gaps;
        private readonly bool _allowTestDomainOverride;

        public ProviderRegistry(ProviderKind kind, ProviderGapCatalog gaps, bool allowTestDomainOverride = false)
        {
            _kind = kind;
            _gaps = gaps ?? throw new ArgumentNullException(nameof(gaps));
            _allowTestDomainOverride = allowTestDomainOverride;
        }

        public ProviderKind Kind => _kind;

        public IReadOnlyCollection<string> RegisteredKeys => _entries.Keys;

        public void Register(string key, T implementation, ProviderParameterSchema schema)
        {
            ArgumentNullException.ThrowIfNull(implementation);
            ArgumentNullException.ThrowIfNull(schema);

            ProviderKey parsed = ProviderKey.Parse(key);
            if (_gaps.Contains(parsed.Value))
            {
                throw new InvalidOperationException(
                    $"{ProviderFailureCodes.NeedsProviderRegistration}: cannot register implementation for gap entry '{parsed.Value}'.");
            }

            if (_entries.ContainsKey(parsed.Value))
            {
                if (_allowTestDomainOverride && string.Equals(parsed.Domain, "fixture", StringComparison.Ordinal))
                {
                    _entries[parsed.Value] = new Entry(implementation, schema);
                    return;
                }

                throw new InvalidOperationException(
                    $"{ProviderFailureCodes.DuplicateProviderKey}: provider key '{parsed.Value}' is already registered for {_kind}.");
            }

            _entries.Add(parsed.Value, new Entry(implementation, schema));
        }

        public ProviderLookupResult<T> TryGet(string key)
        {
            if (!ProviderKey.TryParse(key, out ProviderKey parsed, out string formCode, out string formReason))
            {
                return ProviderLookupResult<T>.Miss(formCode, formReason);
            }

            if (_entries.TryGetValue(parsed.Value, out Entry entry))
            {
                return ProviderLookupResult<T>.Hit(entry.Implementation, entry.Schema);
            }

            if (_gaps.TryGet(parsed.Value, out ProviderGapEntry gap))
            {
                return ProviderLookupResult<T>.Miss(
                    ProviderFailureCodes.NeedsProviderRegistration,
                    $"Provider key '{parsed.Value}' is a gap entry ({gap.ExpectedSemantics}).");
            }

            return ProviderLookupResult<T>.Miss(
                ProviderFailureCodes.UnknownProviderKey,
                $"Provider key '{parsed.Value}' is not registered for {_kind}.");
        }

        public T MustGet(string key, out ProviderParameterSchema schema)
        {
            ProviderLookupResult<T> result = TryGet(key);
            if (!result.Found || result.Implementation == null || result.Schema == null)
            {
                throw new InvalidOperationException(
                    $"{result.FailureCode}: {result.Reason}");
            }

            schema = result.Schema;
            return result.Implementation;
        }

        public bool Contains(string key) =>
            ProviderKey.TryParse(key, out ProviderKey parsed, out _, out _) &&
            _entries.ContainsKey(parsed.Value);

        private readonly struct Entry
        {
            public Entry(T implementation, ProviderParameterSchema schema)
            {
                Implementation = implementation;
                Schema = schema;
            }

            public T Implementation { get; }
            public ProviderParameterSchema Schema { get; }
        }
    }

    public sealed class SourceProviderRegistry : ProviderRegistry<ISourceProvider>
    {
        public SourceProviderRegistry(ProviderGapCatalog gaps, bool allowTestDomainOverride = false)
            : base(ProviderKind.Source, gaps, allowTestDomainOverride)
        {
        }
    }

    public sealed class SelectorProviderRegistry : ProviderRegistry<ISelectorProvider>
    {
        public SelectorProviderRegistry(ProviderGapCatalog gaps, bool allowTestDomainOverride = false)
            : base(ProviderKind.Selector, gaps, allowTestDomainOverride)
        {
        }
    }

    public sealed class ConditionProviderRegistry : ProviderRegistry<IConditionProvider>
    {
        public ConditionProviderRegistry(ProviderGapCatalog gaps, bool allowTestDomainOverride = false)
            : base(ProviderKind.Condition, gaps, allowTestDomainOverride)
        {
        }
    }

    public sealed class EffectHandlerRegistry : ProviderRegistry<IEffectHandler>
    {
        public EffectHandlerRegistry(ProviderGapCatalog gaps, bool allowTestDomainOverride = false)
            : base(ProviderKind.Effect, gaps, allowTestDomainOverride)
        {
        }
    }
}
