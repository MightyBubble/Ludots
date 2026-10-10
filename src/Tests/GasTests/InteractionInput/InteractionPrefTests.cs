using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Persistence;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// InteractionPref entity component: the player-owned order routing preferences
    /// (player-level default intent + dispatch profile, per-ability-template overrides), the
    /// resolution chain reading the possessed representative instead of the active control
    /// scheme, the map-binding seed contract, and the world-save round trip.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionPrefTests
    {
        private const string IntentId = "intent.command.pref.test";
        private const string AltIntentId = "intent.command.pref.alt";
        private const string DispatchId = "dispatch.pref.all";
        private const string AltDispatchId = "dispatch.pref.one";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            ContextGroupIdRegistry.Clear();
        }

        // ── Component contract: player default / ability override / mixed ──

        [Test]
        public void Resolve_PlayerDefaultOnly_AppliesToEveryAbilityScope()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);

            Assert.That(pref.ResolveCommandIntent(0), Is.EqualTo(11), "whole-command scope reads the player default");
            Assert.That(pref.ResolveCommandIntent(501), Is.EqualTo(11), "an ability without an override inherits the player default");
            Assert.That(pref.ResolveCastDispatchProfile(0), Is.EqualTo(21));
            Assert.That(pref.ResolveCastDispatchProfile(501), Is.EqualTo(21));
        }

        [Test]
        public void Resolve_AbilityOverride_ReplacesBothFields()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            pref.SetAbilityOverride(502, commandIntentId: 12, castDispatchProfileId: 22);

            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(12));
            Assert.That(pref.ResolveCastDispatchProfile(502), Is.EqualTo(22));
            Assert.That(pref.ResolveCommandIntent(503), Is.EqualTo(11), "other abilities keep the player default");
            Assert.That(pref.ResolveCastDispatchProfile(503), Is.EqualTo(21));
        }

        [Test]
        public void Resolve_PartialOverride_InheritsTheUnoverriddenField()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            pref.SetAbilityOverride(504, commandIntentId: 0, castDispatchProfileId: 22);
            pref.SetAbilityOverride(505, commandIntentId: 12, castDispatchProfileId: 0);

            Assert.That(pref.ResolveCommandIntent(504), Is.EqualTo(11), "intent not overridden inherits the player default");
            Assert.That(pref.ResolveCastDispatchProfile(504), Is.EqualTo(22));
            Assert.That(pref.ResolveCommandIntent(505), Is.EqualTo(12));
            Assert.That(pref.ResolveCastDispatchProfile(505), Is.EqualTo(21), "dispatch not overridden inherits the player default");
        }

        [Test]
        public void Mutators_FailFastOnIllegalWrites()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);

            Assert.That(() => pref.SetPlayerDefault(0, 21), Throws.InvalidOperationException, "the player default is complete: no half defaults");
            Assert.That(() => pref.SetAbilityOverride(0, 12, 0), Throws.InvalidOperationException, "overrides need a positive ability template id");
            Assert.That(() => pref.SetAbilityOverride(506, 0, 0), Throws.InvalidOperationException, "an all-zero override is a silent no-op and is rejected");

            for (int i = 1; i <= InteractionPref.MaxAbilityOverrides; i++)
            {
                pref.SetAbilityOverride(i, commandIntentId: 0, castDispatchProfileId: 30 + i);
            }

            Assert.That(
                () => pref.SetAbilityOverride(999, 12, 22),
                Throws.InvalidOperationException.With.Message.Contains(nameof(InteractionPref.MaxAbilityOverrides)));

            pref.SetAbilityOverride(1, commandIntentId: 99, castDispatchProfileId: 0);
            Assert.That(pref.ResolveCommandIntent(1), Is.EqualTo(99), "rewriting an existing override replaces it in place");

            Assert.That(pref.ClearAbilityOverride(1), Is.True);
            Assert.That(pref.ResolveCommandIntent(1), Is.EqualTo(11), "a cleared override falls back to the player default");
            Assert.That(pref.ClearAbilityOverride(1), Is.False, "clearing twice reports the missing entry");
            Assert.That(pref.TryGetAbilityOverride(2, out int intent, out int dispatch), Is.True);
            Assert.That((intent, dispatch), Is.EqualTo((0, 32)), "surviving entries stay readable after a clear");
        }

        // ── Seed config contract ──

        [Test]
        public void SeedConfig_MissingOrBlankFields_FailFast()
        {
            Assert.That(
                () => InteractionPrefConfigLoader.Validate(new InteractionPrefsConfig { Defaults = null }, "test"),
                Throws.InvalidOperationException.With.Message.Contains("defaults"));

            Assert.That(
                () => InteractionPrefConfigLoader.Validate(new InteractionPrefsConfig
                {
                    Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = " ", CastDispatchProfileId = DispatchId },
                }, "test"),
                Throws.InvalidOperationException.With.Message.Contains("commandIntentId"));
        }

        [Test]
        public void SeedResolution_RequiresInstalledProfiles_AndResolvesInTheKernelIdSpace()
        {
            using var world = World.Create();
            CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
            intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(IntentId)));
            var dispatch = NewDispatchRegistry();

            InteractionPrefSeed seed = InteractionPrefConfigLoader.ResolveSeed(
                new InteractionPrefsConfig
                {
                    Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = IntentId, CastDispatchProfileId = DispatchId },
                },
                intents.Intents,
                dispatch);
            Assert.That(seed.CommandIntentId, Is.EqualTo(intents.Intents.ProfileIdRegistry.GetId(IntentId)));
            Assert.That(seed.CastDispatchProfileId, Is.EqualTo(dispatch.ProfileIdRegistry.GetId(DispatchId)));

            Assert.That(
                () => InteractionPrefConfigLoader.ResolveSeed(
                    new InteractionPrefsConfig
                    {
                        Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = "intent.command.not_installed", CastDispatchProfileId = DispatchId },
                    },
                    intents.Intents,
                    dispatch),
                Throws.InvalidOperationException.With.Message.Contains("intent.command.not_installed"));

            Assert.That(
                () => InteractionPrefConfigLoader.ResolveSeed(
                    new InteractionPrefsConfig
                    {
                        Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = IntentId, CastDispatchProfileId = "dispatch.not_installed" },
                    },
                    intents.Intents,
                    dispatch),
                Throws.InvalidOperationException.With.Message.Contains("dispatch.not_installed"));
        }

        // ── Resolution chain: possessed rep, not the scheme ──

        [Test]
        public void CommandIntentResolution_DefaultFrame_ReadsSeededPlayerPref()
        {
            using var world = World.Create();
            ChainHarness harness = ChainHarness.Create(world);

            Assert.That(
                harness.Intents.ProfileIdRegistry.GetName(
                    CommandIntentArbiter.ResolveActiveCommandIntent(world, harness.Rep, in harness.Pref)),
                Is.EqualTo(IntentId));
        }

        [Test]
        public void CommandIntentResolution_SwitchingScheme_NeverChangesRoutingPreferences()
        {
            using var world = World.Create();
            ChainHarness harness = ChainHarness.Create(world);
            harness.InstallSchemes();

            int intentBefore = CommandIntentArbiter.ResolveActiveCommandIntent(world, harness.Rep, in harness.Pref);
            int dispatchBefore = harness.Pref.ResolveCastDispatchProfile(abilityTemplateId: 0);

            Assert.That(harness.Schemes!.TrySwitch(harness.SchemeId("scheme.pref.alternate")), Is.True);
            Assert.That(harness.Schemes.ActiveSchemeId, Is.EqualTo(harness.SchemeId("scheme.pref.alternate")), "the switch really happened");

            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, harness.Rep, in harness.Pref),
                Is.EqualTo(intentBefore),
                "the player's routing preference lives on the representative and survives scheme switches");
            Assert.That(harness.Pref.ResolveCastDispatchProfile(abilityTemplateId: 0), Is.EqualTo(dispatchBefore));
        }

        // ── World-save round trip ──

        [Test]
        public void WorldSave_RoundTripsPlayerDefaultAndAbilityOverrides()
        {
            byte[] payload;
            var serializer = new LudotsBinaryWorldSerializer();
            using (var world = World.Create())
            {
                world.Create(NewPref(intent: 4242, dispatch: 8484), NewOverrides());
                payload = serializer.Serialize(world);
            }

            using var restored = serializer.Deserialize(payload);
            Entity restoredEntity = Entity.Null;
            restored.Query(in QueryDescription.Null, entity =>
            {
                if (restored.Has<InteractionPref>(entity))
                {
                    restoredEntity = entity;
                }
            });
            Assert.That(restoredEntity, Is.Not.EqualTo(Entity.Null));

            InteractionPref pref = restored.Get<InteractionPref>(restoredEntity);
            Assert.That(pref.DefaultCommandIntentId, Is.EqualTo(4242), "the raw registry id must round-trip untouched");
            Assert.That(pref.DefaultCastDispatchProfileId, Is.EqualTo(8484));
            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(4243), "the ability override survives byte-for-byte");
            Assert.That(pref.ResolveCastDispatchProfile(504), Is.EqualTo(8485), "a partial override keeps inheriting the unoverridden field");
            Assert.That(pref.TryGetAbilityOverride(505, out int intent, out int dispatch), Is.True);
            Assert.That((intent, dispatch), Is.EqualTo((4244, 0)));
        }

        // ── Harness ──

        private static InteractionPref NewPref(int intent, int dispatch)
        {
            InteractionPref pref = default;
            pref.SetPlayerDefault(intent, dispatch);
            return pref;
        }

        private static InteractionPref NewOverrides()
        {
            InteractionPref pref = default;
            pref.SetPlayerDefault(4242, 8484);
            pref.SetAbilityOverride(502, commandIntentId: 4243, castDispatchProfileId: 8485);
            pref.SetAbilityOverride(504, commandIntentId: 0, castDispatchProfileId: 8485);
            pref.SetAbilityOverride(505, commandIntentId: 4244, castDispatchProfileId: 0);
            return pref;
        }

        private static CommandIntentProfileDefinition NewIntentDefinition(string id)
        {
            return new CommandIntentProfileDefinition
            {
                Id = id,
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                Rules = new List<CommandIntentRuleDefinition>
                {
                    CommandIntentProfileTests.Harness.GroundRule(priority: 10, orderTypeKey: "moveTo"),
                },
            };
        }

        private static CastDispatchProfileRegistry NewDispatchRegistry()
        {
            var dispatch = new CastDispatchProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            dispatch.Install(CastDispatchProfileTests.Harness.Config(
                new CastDispatchProfileDefinition
                {
                    Id = DispatchId,
                    Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                    Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                },
                new CastDispatchProfileDefinition
                {
                    Id = AltDispatchId,
                    Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 1 },
                    Scorer = new CastDispatchScorerDefinition
                    {
                        Kind = "utility",
                        Considerations = new List<string> { "distanceToTarget:invert" },
                    },
                    Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                }));
            return dispatch;
        }

        private sealed class ChainHarness
        {
            public const int MoveToOrderTypeId = 2;

            public CommandIntentProfileRegistry Intents = null!;
            public CastDispatchProfileRegistry Dispatch = null!;
            public ControlSchemeRuntime? Schemes;
            public InteractionPref Pref;
            public Entity Rep;
            private StringIntRegistry? _schemeIds;

            public static ChainHarness Create(World world)
            {
                var harness = new ChainHarness();
                Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
                harness.Rep = rep;

                CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
                intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(IntentId)));
                intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(AltIntentId)));
                harness.Intents = intents.Intents;
                harness.Dispatch = NewDispatchRegistry();

                InteractionPref pref = default;
                pref.SetPlayerDefault(
                    intents.Intents.ProfileIdRegistry.Register(IntentId),
                    harness.Dispatch.ProfileIdRegistry.GetId(DispatchId));
                world.Add(rep, pref);
                harness.Pref = pref;
                return harness;
            }

            public void InstallSchemes()
            {
                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = ChainHarness.MoveToOrderTypeId });
                _schemeIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                Schemes = new ControlSchemeRuntime(_schemeIds, orderTypes);
                Schemes.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        new() { Id = "scheme.pref.primary", InputContexts = new List<string>() },
                        new() { Id = "scheme.pref.alternate", InputContexts = new List<string>() },
                    },
                });
            }

            public int SchemeId(string name) => _schemeIds!.GetId(name);
        }
    }
}
