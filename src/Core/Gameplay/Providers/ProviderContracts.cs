using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.Providers
{
    public readonly record struct ProviderSignal(
        string SourceKey,
        string SignalId,
        long OccurredAt,
        Entity ScopeRef,
        IReadOnlyList<Entity> ObjectRefs,
        IReadOnlyDictionary<string, object?> Parameters);

    public readonly record struct ProviderEffectCall(
        string EffectKey,
        string TargetReference,
        IReadOnlyDictionary<string, object?> Parameters,
        int ExecutionOrder);

    public readonly record struct ProviderLookupResult<T>(
        bool Found,
        T? Implementation,
        ProviderParameterSchema? Schema,
        string FailureCode,
        string Reason)
        where T : class
    {
        public static ProviderLookupResult<T> Hit(T implementation, ProviderParameterSchema schema) =>
            new(true, implementation, schema, string.Empty, string.Empty);

        public static ProviderLookupResult<T> Miss(string failureCode, string reason) =>
            new(false, null, null, failureCode, reason);
    }

    public interface ISourceProvider
    {
        void Emit(in ProviderSignal signal, ProviderExecutionContext context);
    }

    public interface ISelectorProvider
    {
        bool TrySelect(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters, out Entity single);
        bool TrySelectSet(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters, List<Entity> results);
    }

    public interface IConditionProvider
    {
        bool Evaluate(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters);
    }

    public interface IEffectHandler
    {
        void Execute(in ProviderEffectCall call, ProviderExecutionContext context);
    }

    public sealed class ProviderExecutionContext
    {
        public ProviderExecutionContext(World world, Entity subject, IReadOnlyDictionary<string, object?> bindings)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Subject = subject;
            Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        }

        public World World { get; }
        public Entity Subject { get; }
        public IReadOnlyDictionary<string, object?> Bindings { get; }

        public bool TryResolveReference(string reference, out object? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            if (reference.StartsWith("literal.", StringComparison.Ordinal))
            {
                value = reference.Substring("literal.".Length);
                return true;
            }

            if (reference.StartsWith("signal.", StringComparison.Ordinal))
            {
                string key = reference.Substring("signal.".Length);
                return Bindings.TryGetValue("signal." + key, out value) || Bindings.TryGetValue(key, out value);
            }

            if (reference.StartsWith("context.", StringComparison.Ordinal))
            {
                string key = reference.Substring("context.".Length);
                return Bindings.TryGetValue("context." + key, out value) || Bindings.TryGetValue(key, out value);
            }

            return false;
        }
    }
}
