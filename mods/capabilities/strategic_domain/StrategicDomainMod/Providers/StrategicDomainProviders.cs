using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;
using StrategicDomainMod.Components;
using StrategicDomainMod.Runtime;

namespace StrategicDomainMod.Providers
{
    internal static class StrategicDomainParameterReader
    {
        public static int ReadInt(IReadOnlyDictionary<string, object?> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out object? value) || value == null)
            {
                throw new InvalidOperationException($"Missing int parameter '{key}'.");
            }

            return Convert.ToInt32(value);
        }

        public static float ReadFloat(IReadOnlyDictionary<string, object?> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out object? value) || value == null)
            {
                throw new InvalidOperationException($"Missing float parameter '{key}'.");
            }

            return Convert.ToSingle(value);
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

            return Convert.ToBoolean(value);
        }

        public static string ReadString(IReadOnlyDictionary<string, object?> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out object? value) || value is not string text || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"Missing string parameter '{key}'.");
            }

            return text;
        }
    }

    public sealed class SupplyNetworkChangedSource : ISourceProvider
    {
        public readonly List<ProviderSignal> Emitted = new();

        public void Emit(in ProviderSignal signal, ProviderExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Emitted.Add(signal);
        }
    }

    public sealed class DefenseBreachedSource : ISourceProvider
    {
        public readonly List<ProviderSignal> Emitted = new();

        public void Emit(in ProviderSignal signal, ProviderExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Emitted.Add(signal);
        }
    }

    public sealed class SettlementOwnerCondition : IConditionProvider
    {
        private readonly StrategicDomainRuntime _runtime;

        public SettlementOwnerCondition(StrategicDomainRuntime runtime) =>
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        public bool Evaluate(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(parameters, "settlement_key");
            int expectedOwner = StrategicDomainParameterReader.ReadInt(parameters, "faction_owner");
            return _runtime.GetIdentity(settlementKey).FactionOwner == expectedOwner;
        }
    }

    public sealed class SettlementCapturableCondition : IConditionProvider
    {
        private readonly StrategicDomainRuntime _runtime;

        public SettlementCapturableCondition(StrategicDomainRuntime runtime) =>
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        public bool Evaluate(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(parameters, "settlement_key");
            SettlementControlState state = _runtime.GetDefense(settlementKey).ControlState;
            return state is SettlementControlState.Capturable or SettlementControlState.Ruined;
        }
    }

    public sealed class CommitTroopsTakeoverEffect : IEffectHandler
    {
        private readonly StrategicDomainRuntime _runtime;

        public CommitTroopsTakeoverEffect(StrategicDomainRuntime runtime) =>
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(call.Parameters, "settlement_key");
            int newOwner = StrategicDomainParameterReader.ReadInt(call.Parameters, "faction_owner");
            float troops = StrategicDomainParameterReader.ReadFloat(call.Parameters, "troop_commitment");
            bool logistics = StrategicDomainParameterReader.ReadBool(call.Parameters, "logistics_deployed", defaultValue: false);
            _runtime.CommitTroopsTakeover(settlementKey, newOwner, troops, logistics);
        }
    }

    public sealed class AppointGovernorEffect : IEffectHandler
    {
        private readonly StrategicDomainRuntime _runtime;

        public AppointGovernorEffect(StrategicDomainRuntime runtime) =>
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(call.Parameters, "settlement_key");
            int heroKey = StrategicDomainParameterReader.ReadInt(call.Parameters, "hero_key");
            _runtime.AppointGovernor(settlementKey, heroKey);
        }
    }

    public sealed class SiegeInvestEffect : IEffectHandler
    {
        private readonly StrategicDomainRuntime _runtime;
        private readonly DefenseBreachedSource _breachSource;

        public SiegeInvestEffect(StrategicDomainRuntime runtime, DefenseBreachedSource breachSource)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _breachSource = breachSource ?? throw new ArgumentNullException(nameof(breachSource));
        }

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(call.Parameters, "settlement_key");
            string path = StrategicDomainParameterReader.ReadString(call.Parameters, "path");
            float amount = StrategicDomainParameterReader.ReadFloat(call.Parameters, "amount");
            bool hasSiege = StrategicDomainParameterReader.ReadBool(call.Parameters, "has_siege_capability", defaultValue: false);

            SettlementDefenseCm before = _runtime.GetDefense(settlementKey);
            if (string.Equals(path, "garrison", StringComparison.Ordinal))
            {
                _runtime.ApplyGarrisonDamage(settlementKey, amount);
            }
            else if (string.Equals(path, "wall", StringComparison.Ordinal))
            {
                _runtime.ApplyWallDamage(settlementKey, amount, hasSiege);
            }
            else
            {
                throw new InvalidOperationException($"Unknown siege path '{path}'.");
            }

            SettlementDefenseCm after = _runtime.GetDefense(settlementKey);
            if (before.ControlState == SettlementControlState.Intact &&
                after.ControlState != SettlementControlState.Intact)
            {
                _breachSource.Emit(
                    new ProviderSignal(
                        "city_control.defense_breached",
                        $"{settlementKey}:{after.ControlState}",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        context.Subject,
                        Array.Empty<Entity>(),
                        new Dictionary<string, object?>
                        {
                            ["settlement_key"] = settlementKey,
                            ["state"] = after.ControlState.ToString(),
                        }),
                    context);
            }
        }
    }

    public sealed class SiegeLiftEffect : IEffectHandler
    {
        private readonly StrategicDomainRuntime _runtime;

        public SiegeLiftEffect(StrategicDomainRuntime runtime) =>
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(call.Parameters, "settlement_key");
            _runtime.LiftSiege(settlementKey);
        }
    }

    public sealed class SiegeAcceptSurrenderEffect : IEffectHandler
    {
        private readonly StrategicDomainRuntime _runtime;

        public SiegeAcceptSurrenderEffect(StrategicDomainRuntime runtime) =>
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(call.Parameters, "settlement_key");
            int newOwner = StrategicDomainParameterReader.ReadInt(call.Parameters, "faction_owner");
            _runtime.CommitTroopsTakeover(settlementKey, newOwner, troopCommitment: 1f, logisticsDeployed: true);
        }
    }

    public sealed class PrisonerEffect : IEffectHandler
    {
        private readonly StrategicDomainRuntime _runtime;
        private readonly string _action;

        public PrisonerEffect(StrategicDomainRuntime runtime, string action)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _action = action;
        }

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            int settlementKey = StrategicDomainParameterReader.ReadInt(call.Parameters, "settlement_key");
            _runtime.DisposeCaptive(settlementKey, _action);
        }
    }

    public static class StrategicDomainProviderInstaller
    {
        public static void Install(ProviderServices providers, StrategicDomainRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(providers);
            ArgumentNullException.ThrowIfNull(runtime);

            providers.Gaps.TryResolve("population.appoint_governor", out _);
            providers.Gaps.TryResolve("city_control.commit_troops_takeover", out _);
            providers.Gaps.TryResolve("combat.siege_invest", out _);
            providers.Gaps.TryResolve("combat.siege_lift", out _);
            providers.Gaps.TryResolve("combat.siege_accept_surrender", out _);

            var breach = new DefenseBreachedSource();
            var supplyChanged = new SupplyNetworkChangedSource();

            RegisterSource(providers, "supply.network_changed", supplyChanged);
            RegisterSource(providers, "city_control.defense_breached", breach);

            providers.Conditions.Register(
                "city_control.owner",
                new SettlementOwnerCondition(runtime),
                IntParams("settlement_key", "faction_owner"));
            providers.Conditions.Register(
                "city_control.capturable",
                new SettlementCapturableCondition(runtime),
                IntParams("settlement_key"));

            providers.Effects.Register(
                "city_control.commit_troops_takeover",
                new CommitTroopsTakeoverEffect(runtime),
                new ProviderParameterSchema(new[]
                {
                    new ProviderParameterField("settlement_key", ProviderParameterKind.Int, required: true),
                    new ProviderParameterField("faction_owner", ProviderParameterKind.Int, required: true),
                    new ProviderParameterField("troop_commitment", ProviderParameterKind.Float, required: true),
                    new ProviderParameterField("logistics_deployed", ProviderParameterKind.Bool, required: false),
                }));
            providers.Effects.Register(
                "population.appoint_governor",
                new AppointGovernorEffect(runtime),
                IntParams("settlement_key", "hero_key"));
            providers.Effects.Register(
                "combat.siege_invest",
                new SiegeInvestEffect(runtime, breach),
                new ProviderParameterSchema(new[]
                {
                    new ProviderParameterField("settlement_key", ProviderParameterKind.Int, required: true),
                    new ProviderParameterField("path", ProviderParameterKind.String, required: true),
                    new ProviderParameterField("amount", ProviderParameterKind.Float, required: true),
                    new ProviderParameterField("has_siege_capability", ProviderParameterKind.Bool, required: false),
                }));
            providers.Effects.Register(
                "combat.siege_lift",
                new SiegeLiftEffect(runtime),
                IntParams("settlement_key"));
            providers.Effects.Register(
                "combat.siege_accept_surrender",
                new SiegeAcceptSurrenderEffect(runtime),
                IntParams("settlement_key", "faction_owner"));
            providers.Effects.Register(
                "prisoner.recruit",
                new PrisonerEffect(runtime, "recruit"),
                IntParams("settlement_key"));
            providers.Effects.Register(
                "prisoner.release",
                new PrisonerEffect(runtime, "release"),
                IntParams("settlement_key"));
            providers.Effects.Register(
                "prisoner.execute",
                new PrisonerEffect(runtime, "execute"),
                IntParams("settlement_key"));
        }

        private static void RegisterSource(ProviderServices providers, string key, ISourceProvider provider)
        {
            if (!providers.Sources.Contains(key))
            {
                providers.Sources.Register(key, provider, ProviderParameterSchema.Empty);
            }
        }

        private static ProviderParameterSchema IntParams(params string[] names)
        {
            var fields = new ProviderParameterField[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                fields[i] = new ProviderParameterField(names[i], ProviderParameterKind.Int, required: true);
            }

            return new ProviderParameterSchema(fields);
        }
    }
}
