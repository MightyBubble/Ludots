using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.Providers.FixtureProviders
{
    public sealed class FixtureSourceProvider : ISourceProvider
    {
        public readonly List<ProviderSignal> Emitted = new();

        public void Emit(in ProviderSignal signal, ProviderExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Emitted.Add(signal);
        }
    }

    public sealed class FixtureSelectorProvider : ISelectorProvider
    {
        public bool TrySelect(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters, out Entity single)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameters);
            single = context.Subject;
            return context.World.IsAlive(single);
        }

        public bool TrySelectSet(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters, List<Entity> results)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameters);
            ArgumentNullException.ThrowIfNull(results);
            results.Clear();
            if (context.World.IsAlive(context.Subject))
            {
                results.Add(context.Subject);
                return true;
            }

            return false;
        }
    }

    public sealed class FixtureConditionProvider : IConditionProvider
    {
        private readonly bool _result;

        public FixtureConditionProvider(bool result = true)
        {
            _result = result;
        }

        public bool Evaluate(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameters);
            return _result;
        }
    }

    /// <summary>
    /// Deliberately illegal Condition that writes world state. Used only to prove write detection.
    /// </summary>
    public sealed class WritingConditionProbe : IConditionProvider
    {
        public bool Evaluate(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters)
        {
            ArgumentNullException.ThrowIfNull(context);
            // Leave the entity alive so read-only guards can observe a world mutation.
            context.World.Create();
            return true;
        }
    }

    public sealed class FixtureEffectHandler : IEffectHandler
    {
        public readonly List<ProviderEffectCall> Executed = new();

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Executed.Add(call);
        }
    }

    public static class FixtureProviderInstaller
    {
        public static void InstallMinimal(ProviderServices services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.Sources.Register(
                "fixture.signal_ping",
                new FixtureSourceProvider(),
                ProviderParameterSchema.Empty);

            services.Selectors.Register(
                "fixture.select_subject",
                new FixtureSelectorProvider(),
                ProviderParameterSchema.Empty);

            services.Conditions.Register(
                "fixture.always_true",
                new FixtureConditionProvider(true),
                ProviderParameterSchema.Empty);

            services.Effects.Register(
                "fixture.noop",
                new FixtureEffectHandler(),
                new ProviderParameterSchema(new[]
                {
                    new ProviderParameterField("note", ProviderParameterKind.String, required: false),
                }));
        }
    }
}
