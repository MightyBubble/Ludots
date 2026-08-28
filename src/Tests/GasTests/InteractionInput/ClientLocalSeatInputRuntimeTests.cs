using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Per-seat input channels: sole-seat tables keep the global interpretation chain (no
    /// channels), multi-seat tables give every seat an isolated handler/snapshot pair, declared
    /// schemes activate per seat, and hot switches write back to the switched seat only.
    /// </summary>
    [TestFixture]
    public sealed class ClientLocalSeatInputRuntimeTests
    {
        private const string SchemeA = "scheme.perseat.a";
        private const string SchemeB = "scheme.perseat.b";
        private const string PerSeatContext = "imc.perseat";
        private const string IntentA = "intent.perseat.a";
        private const string IntentB = "intent.perseat.b";
        private const string DispatchA = "dispatch.perseat.a";
        private const string DispatchB = "dispatch.perseat.b";

        [Test]
        public void PublishSeats_SoleSeatTable_HoldsNoChannels()
        {
            using Harness harness = Harness.Create();
            harness.PublishDualSeat();
            Assert.That(harness.Runtime.ChannelCount, Is.EqualTo(2));

            harness.Seats.Clear();
            harness.Seats.Add(new ClientLocalSeat("seat.0", SchemeA) { PossessedPlayerId = 7, PossessedRep = harness.RepSeven });
            harness.Runtime.PublishSeats(harness.Seats);

            Assert.That(harness.Runtime.ChannelCount, Is.EqualTo(0),
                "the sole seat's interpretation stack is the global chain; no per-seat channel may shadow it.");
        }

        [Test]
        public void PublishSeats_MultiSeat_DeclaredSchemesActivatePerChannel()
        {
            using Harness harness = Harness.Create();
            int schemeAId = harness.Schemes.SchemeIdRegistry.GetId(SchemeA);
            uint globalRevision = harness.Schemes.Revision;

            harness.PublishDualSeat();

            Assert.That(harness.Runtime.TryGetChannel("seat.0", out ClientLocalSeatInputChannel channelZero), Is.True);
            Assert.That(harness.Runtime.TryGetChannel("seat.1", out ClientLocalSeatInputChannel channelOne), Is.True);
            Assert.That(channelZero.ActiveSchemeId, Is.EqualTo(schemeAId));
            Assert.That(channelOne.ActiveSchemeId, Is.EqualTo(schemeAId),
                "the same scheme declared by two seats is a legal form: each channel activates it independently.");
            Assert.That(channelZero.TryGetActiveAxisMove(out _), Is.True);
            Assert.That(channelOne.TryGetActiveAxisMove(out _), Is.True);
            Assert.That(harness.Schemes.Revision, Is.EqualTo(globalRevision),
                "per-seat activation never switches the global runtime.");
        }

        [Test]
        public void PublishSeats_MultiSeatUnknownScheme_FailsFastNamingSeatAndScheme()
        {
            using Harness harness = Harness.Create();
            harness.Seats.Add(new ClientLocalSeat("seat.0", SchemeA) { PossessedPlayerId = 7, PossessedRep = harness.RepSeven });
            harness.Seats.Add(new ClientLocalSeat("seat.1", "scheme.perseat.missing") { PossessedPlayerId = 8, PossessedRep = harness.RepEight });

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => harness.Runtime.PublishSeats(harness.Seats));

            Assert.That(error!.Message, Does.Contain("seat.1"));
            Assert.That(error.Message, Does.Contain("scheme.perseat.missing"));
            Assert.That(error.Message, Does.Contain("not installed"));
        }

        [Test]
        public void PublishSeats_MultiSeatRefusedScheme_FailsFast()
        {
            using Harness harness = Harness.Create(allowedSchemes: new List<string> { SchemeA });
            harness.Seats.Add(new ClientLocalSeat("seat.0", SchemeA) { PossessedPlayerId = 7, PossessedRep = harness.RepSeven });
            harness.Seats.Add(new ClientLocalSeat("seat.1", SchemeB) { PossessedPlayerId = 8, PossessedRep = harness.RepEight });

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => harness.Runtime.PublishSeats(harness.Seats));

            Assert.That(error!.Message, Does.Contain("seat.1"));
            Assert.That(error.Message, Does.Contain("allowed-set"));
        }

        [Test]
        public void TrySwitchSeatScheme_MultiSeat_SwitchesChannelAndWritesBackThatSeatOnly()
        {
            using Harness harness = Harness.Create(withPreferences: true);
            harness.PublishDualSeat();
            uint preferenceRevision = harness.Preferences.Revision;
            int schemeBId = harness.Schemes.SchemeIdRegistry.GetId(SchemeB);

            Assert.That(harness.Runtime.TrySwitchSeatScheme(harness.Seats, "seat.1", SchemeB), Is.True);

            Assert.That(harness.Seats.Require("seat.1").ControlSchemeId, Is.EqualTo(SchemeB),
                "the hot switch writes back to the switched seat.");
            Assert.That(harness.Seats.Require("seat.0").ControlSchemeId, Is.EqualTo(SchemeA),
                "other seats keep their declarations.");
            Assert.That(harness.Runtime.TryGetChannel("seat.1", out ClientLocalSeatInputChannel channelOne), Is.True);
            Assert.That(channelOne.ActiveSchemeId, Is.EqualTo(schemeBId));
            Assert.That(channelOne.TryGetActiveAxisMove(out _), Is.False, "scheme B declares no axis move.");
            Assert.That(harness.Schemes.ActiveSchemeId, Is.Not.EqualTo(schemeBId),
                "a per-seat switch never switches the global runtime.");
            Assert.That(harness.Preferences.Revision, Is.EqualTo(preferenceRevision),
                "per-seat switches are runtime-only; the client-global preference store belongs to no individual seat.");
        }

        [Test]
        public void TrySwitchSeatScheme_SoleSeat_DelegatesToGlobalRuntimeAndWritesBackSeat()
        {
            using Harness harness = Harness.Create(withPreferences: true);
            harness.Seats.Add(new ClientLocalSeat("seat.0", SchemeA) { PossessedPlayerId = 7, PossessedRep = harness.RepSeven });
            harness.Runtime.PublishSeats(harness.Seats);
            uint preferenceRevision = harness.Preferences.Revision;
            int schemeBId = harness.Schemes.SchemeIdRegistry.GetId(SchemeB);
            Assert.That(harness.Schemes.ActiveSchemeId, Is.EqualTo(harness.Schemes.SchemeIdRegistry.GetId(SchemeA)));

            Assert.That(harness.Runtime.TrySwitchSeatScheme(harness.Seats, "seat.0", SchemeB), Is.True);

            Assert.That(harness.Schemes.ActiveSchemeId, Is.EqualTo(schemeBId),
                "the sole seat's stack is the global runtime; its switch keeps explicit-user semantics.");
            Assert.That(harness.Preferences.Revision, Is.GreaterThan(preferenceRevision),
                "explicit user switches keep writing the persisted preference.");
            Assert.That(harness.Seats.Require("seat.0").ControlSchemeId, Is.EqualTo(SchemeB));
        }

        [Test]
        public void ChannelPumpAndFreeze_KeepSeatActionSpacesIsolated()
        {
            using Harness harness = Harness.Create();
            harness.PublishDualSeat();
            Assert.That(harness.Runtime.TryGetChannel("seat.0", out ClientLocalSeatInputChannel channelZero), Is.True);
            Assert.That(harness.Runtime.TryGetChannel("seat.1", out ClientLocalSeatInputChannel channelOne), Is.True);

            channelZero.Handler.InjectAction("Move", new Vector3(1f, 0f, 0f));
            harness.Runtime.UpdateVisualFrame();
            harness.Runtime.FreezeSnapshots(discardLiveInput: false);

            Assert.That(channelZero.Reader.ReadAction<Vector2>("Move"), Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(channelOne.Reader.ReadAction<Vector2>("Move"), Is.EqualTo(Vector2.Zero),
                "an action injected on one seat's handler never reaches the other seat's channel.");
        }

        private sealed class Harness : IDisposable
        {
            public World World = null!;
            public Entity RepSeven;
            public Entity RepEight;
            public Dictionary<string, object> Globals = null!;
            public ClientLocalSeatRegistry Seats = null!;
            public ControlSchemeRuntime Schemes = null!;
            public ClientCastPreferenceStore Preferences = null!;
            public ClientLocalSeatInputRuntime Runtime = null!;

            public static Harness Create(List<string>? allowedSchemes = null, bool withPreferences = false)
            {
                var world = World.Create();
                var harness = new Harness
                {
                    World = world,
                    RepSeven = world.Create(),
                    RepEight = world.Create(),
                };

                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });

                CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
                InstallGroundIntent(intents, IntentA);
                InstallGroundIntent(intents, IntentB);

                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var stack = new InteractionContextStack(collectionKeys);
                stack.Push(InteractionContextFrameDescriptor.Create(
                    InteractionContextIds.Default,
                    "collection.perseat.command_source",
                    "view.perseat.default"));

                var dispatchIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var dispatchAdvanceIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var dispatch = new CastDispatchProfileRegistry(dispatchIds, dispatchAdvanceIds);
                dispatch.Install(CastDispatchProfileTests.Harness.Config(
                    DispatchDefinition(DispatchA),
                    DispatchDefinition(DispatchB)));

                if (withPreferences)
                {
                    harness.Preferences = CreatePreferenceStore();
                }

                var inputConfig = new InputConfigRoot
                {
                    Actions = new List<InputActionDef>
                    {
                        new() { Id = "Move", Type = InputActionType.Axis2D },
                        new() { Id = "CmdA", Type = InputActionType.Button },
                    },
                    Contexts = new List<InputContextDef>
                    {
                        new()
                        {
                            Id = PerSeatContext,
                            Priority = 1,
                            Bindings = new List<InputBindingDef>
                            {
                                new() { ActionId = "CmdA", Path = "<Keyboard>/q", Processors = new() },
                            },
                        },
                    },
                };

                harness.Schemes = new ControlSchemeRuntime(
                    new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                    stack,
                    intents.Intents,
                    dispatch,
                    orderTypes,
                    preferences: harness.Preferences,
                    inputConfig: inputConfig);
                harness.Schemes.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        new()
                        {
                            Id = SchemeA,
                            InputContexts = new List<string> { PerSeatContext },
                            Defaults = new ControlSchemeDefaults
                            {
                                CommandIntentId = IntentA,
                                CastDispatchProfileId = DispatchA,
                            },
                            AxisMove = new ControlSchemeAxisMove
                            {
                                ActionId = "Move",
                                OrderTypeKey = "moveTo",
                                ThrottleTicks = 6,
                                StepDistanceCm = 400,
                            },
                        },
                        new()
                        {
                            Id = SchemeB,
                            InputContexts = new List<string>(),
                            Defaults = new ControlSchemeDefaults
                            {
                                CommandIntentId = IntentB,
                                CastDispatchProfileId = DispatchB,
                            },
                        },
                    },
                    AllowedSchemes = allowedSchemes,
                });

                harness.Seats = new ClientLocalSeatRegistry();
                harness.Globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.ClientLocalSeatRegistry.Name] = harness.Seats,
                    [CoreServiceKeys.ControlSchemeRuntime.Name] = harness.Schemes,
                };
                harness.Runtime = new ClientLocalSeatInputRuntime(harness.Globals, harness.Schemes, inputConfig);
                harness.Globals[CoreServiceKeys.ClientLocalSeatInputRuntime.Name] = harness.Runtime;
                return harness;
            }

            public void PublishDualSeat()
            {
                Seats.Clear();
                Seats.Add(new ClientLocalSeat("seat.0", SchemeA) { PossessedPlayerId = 7, PossessedRep = RepSeven });
                Seats.Add(new ClientLocalSeat("seat.1", SchemeA) { PossessedPlayerId = 8, PossessedRep = RepEight });
                Runtime.PublishSeats(Seats);
            }

            public void Dispose()
            {
                World.Destroy(World);
            }

            private static void InstallGroundIntent(CommandIntentProfileTests.Harness intents, string profileId)
            {
                intents.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
                {
                    Id = profileId,
                    GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                    Rules = new List<CommandIntentRuleDefinition>
                    {
                        CommandIntentProfileTests.Harness.GroundRule(priority: 10, orderTypeKey: "moveTo"),
                    },
                }));
            }

            private static CastDispatchProfileDefinition DispatchDefinition(string id)
            {
                return new CastDispatchProfileDefinition
                {
                    Id = id,
                    Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                    Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                };
            }

            private static ClientCastPreferenceStore CreatePreferenceStore()
            {
                var castCommitIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var castCommitActionIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var contextProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var templateKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var formSetKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                return new ClientCastPreferenceStore(
                    new CastCommitProfileRegistry(
                        castCommitIds,
                        castCommitActionIds,
                        new InteractionContextProfileRegistry(contextProfileIds)),
                    templateKeys.Register,
                    templateKeys.GetName,
                    formSetKeys.Register,
                    formSetKeys.GetName);
            }
        }
    }
}
