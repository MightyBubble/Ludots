using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// Panel triaxis ownership contract: owner (semantic subject, shared vocabulary with
    /// intent playerSource), audience (which seats may see and operate), surface (which
    /// PresentBinding rects a panel mounts on). Shared panels stay ONE instance with a
    /// multi-seat audience; out-of-audience seats are refused with a reason that flows
    /// back; hotseat rotation overrides the declared audience through the activation
    /// store (SetPanelAudience graph op or direct API).
    /// </summary>
    [TestFixture]
    public sealed class PanelTriaxisOwnershipTests
    {
        private const string SharedTemplateId = "tests.panel.triaxis.shared";
        private const string SharedAnchorId = "tests.anchor.top_left";
        private const string PanelGraphId = "tests.graph.triaxis.values";
        private const string SeatZero = "seat.0";
        private const string SeatOne = "seat.1";
        private const string SeatTwo = "seat.2";

        private static readonly string[] AllOwnerKinds = { "seat", "participant", "team", "world" };

        private World _world = null!;
        private Entity _scope;
        private PanelTemplateRegistry _templates = null!;
        private PanelHost _host = null!;
        private UiPanelActivationStore _activation = null!;
        private PanelActivationApi _activationApi = null!;
        private GraphOutputValueStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
            AttributeRegistry.Register("tests.attr.hp");
            _world = World.Create();
            _scope = _world.Create();
            _world.Add(_scope, new AttributeBuffer());

            _activation = new UiPanelActivationStore();
            _activationApi = new PanelActivationApi(_activation);

            var outputKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _store = new GraphOutputValueStore(outputKeys, initialCapacity: 8);
            _store.SetFloat(_scope, "tests.panel.triaxis.hp", 30f);
            _host = new PanelHost(_templates = new PanelTemplateRegistry(), new PanelProjectionReader(_world, _store));
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
        }

        private static string TemplateJson(string ownerKind = "seat", string audience = "\"all-seats\"") => $$"""
        {
          "id": "{{SharedTemplateId}}",
          "graph": "{{PanelGraphId}}",
          "ownerKind": "{{ownerKind}}",
          "audienceSeats": {{audience}},
          "pins": [
            { "name": "hp", "key": "tests.panel.triaxis.hp", "mode": "realtime", "default": 0 }
          ],
          "events": [
            { "eventId": "ui.triaxis.press", "gesture": "click", "payload": { "amount": "Int" } }
          ]
        }
        """;

        private static JsonObject PressArgs() => new() { ["amount"] = 3 };

        // ── Loader: owner / audience declaration ──

        [Test]
        public void Loader_OwnerKindAcceptsAllFourKinds_SharedVocabulary()
        {
            foreach (string kind in AllOwnerKinds)
            {
                PanelTemplate template = PanelTemplateLoader.Load(TemplateJson(kind));
                Assert.That(template.OwnerKind, Is.EqualTo(PanelOwnerKinds.Parse(kind, "test")), kind);
            }
        }

        [Test]
        public void Loader_UnknownOwnerKind_FailsFastNamingValueAndAllowedSet()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => PanelTemplateLoader.Load(TemplateJson("guild")));

            Assert.That(error!.Message, Does.Contain("guild"));
            Assert.That(error.Message, Does.Contain("seat, participant, team, world"));
        }

        [Test]
        public void Loader_AudienceSeatListAndAllSeats_ParseWithDefaults()
        {
            PanelTemplate listed = PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatOne}\"]"));
            Assert.That(listed.Audience.IsAllSeats, Is.False);
            Assert.That(listed.Audience.SeatIds, Is.EqualTo(new[] { SeatZero, SeatOne }));

            PanelTemplate everyone = PanelTemplateLoader.Load(TemplateJson(audience: "\"all-seats\""));
            Assert.That(everyone.Audience.IsAllSeats, Is.True);

            PanelTemplate defaults = PanelTemplateLoader.Load($$"""
            {
              "id": "{{SharedTemplateId}}",
              "graph": "{{PanelGraphId}}",
              "pins": [ { "name": "hp", "key": "tests.panel.triaxis.hp", "mode": "realtime", "default": 0 } ]
            }
            """);
            Assert.That(defaults.OwnerKind, Is.EqualTo(PanelOwnerKind.Seat), "absent ownerKind keeps today's seat-owner panels");
            Assert.That(defaults.Audience.IsAllSeats, Is.True, "absent audience keeps today's every-seat behavior");
        }

        [Test]
        public void Loader_MalformedAudience_FailsFastNamingTemplate()
        {
            Assert.That(
                () => PanelTemplateLoader.Load(TemplateJson(audience: "[]")),
                Throws.InvalidOperationException.With.Message.Contains("must not be empty"));

            Assert.That(
                () => PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatZero}\"]")),
                Throws.InvalidOperationException.With.Message.Contains("duplicate seat 'seat.0'"));

            Assert.That(
                () => PanelTemplateLoader.Load(TemplateJson(audience: "[42]")),
                Throws.InvalidOperationException.With.Message.Contains("seat id strings"));

            Assert.That(
                () => PanelTemplateLoader.Load(TemplateJson(audience: "\"some-seats\"")),
                Throws.InvalidOperationException.With.Message.Contains("all-seats"));

            Assert.That(
                () => PanelTemplateLoader.Load(TemplateJson(audience: "3")),
                Throws.InvalidOperationException.With.Message.Contains(SharedTemplateId));
        }

        [Test]
        public void Loader_IntentPlayerSource_UsesTheSameOwnerVocabulary()
        {
            string json = TemplateJson().Replace(
                "\"events\": [",
                "\"intents\": [ { \"event\": \"ui.triaxis.press\", \"intent\": \"order.triaxis.tap\", \"args\": { \"n\": \"$payload.amount\" }, \"playerSource\": \"seat\", \"actorSource\": \"commandSource.primary\" } ],\n  \"events\": [");

            foreach (string kind in AllOwnerKinds)
            {
                PanelTemplate template = PanelTemplateLoader.Load(json.Replace("\"playerSource\": \"seat\"", $"\"playerSource\": \"{kind}\""));
                Assert.That(template.Intents[0].PlayerSource, Is.EqualTo(kind), kind);
            }

            Assert.That(
                () => PanelTemplateLoader.Load(json.Replace("\"playerSource\": \"seat\"", "\"playerSource\": \"guild\"")),
                Throws.InvalidOperationException.With.Message.Contains("guild"));
        }

        // ── Admission: seat channel attribution → audience gate ──

        [Test]
        public void Admission_AudienceSeatOperates_OutsideSeatRefusedWithReason()
        {
            _templates.Register(PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatOne}\"]")));
            _templates.Freeze();
            int fired = 0;
            var dispatcher = new PanelEventDispatcher(_templates.Require(SharedTemplateId), (_, _) => fired++, _activation);

            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatZero).Admitted, Is.True);
            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatOne).Admitted, Is.True);
            Assert.That(fired, Is.EqualTo(2));

            PanelEventFireResult refused = dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatTwo);
            Assert.That(refused.Admitted, Is.False);
            Assert.That(refused.Reason, Does.Contain(SharedTemplateId));
            Assert.That(refused.Reason, Does.Contain(SeatTwo));
            Assert.That(refused.Reason, Does.Contain(SeatZero));
            Assert.That(fired, Is.EqualTo(2), "a refused operation never reaches the sink");
        }

        [Test]
        public void Admission_AllSeatsAudience_AdmitsAnySeat_AndMissingAttributionThrows()
        {
            _templates.Register(PanelTemplateLoader.Load(TemplateJson()));
            _templates.Freeze();
            var dispatcher = new PanelEventDispatcher(_templates.Require(SharedTemplateId), static (_, _) => { }, _activation);

            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatTwo).Admitted, Is.True);
            Assert.That(
                () => dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), "  "),
                Throws.ArgumentException.With.Message.Contains("attribution"));
        }

        // ── Shared panel: one instance, multi-seat audience ──

        [Test]
        public void SharedPanel_OneInstance_TwoSeatsOperateThroughTheSameState()
        {
            _templates.Register(PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatOne}\"]")));
            _templates.Freeze();
            foreach (PanelTemplate template in _templates.Snapshot())
            {
                template.GraphId = -1; // light host: store is seeded directly
            }

            Assert.That(_host.Count, Is.EqualTo(0));
            PanelInstanceHandle handle = _host.Instantiate(SharedTemplateId, SharedAnchorId, _scope);
            Assert.That(_host.Count, Is.EqualTo(1), "a shared audience never duplicates the instance");
            Assert.That(_host.SnapshotInstances().Count, Is.EqualTo(1));

            var dispatcher = new PanelEventDispatcher(_templates.Require(SharedTemplateId), static (_, _) => { }, _activation);
            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatZero).Admitted, Is.True);
            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatOne).Admitted, Is.True);
            Assert.That(_host.Count, Is.EqualTo(1), "operating from two seats still reads and writes one instance");
            Assert.That(_host.TryGetValues(handle, out PanelVariableSet values), Is.True);
            Assert.That(values.Get("hp"), Is.EqualTo(30f));
        }

        // ── Hotseat: audience override via the activation store ──

        [Test]
        public void Hotseat_OverrideNarrowsAudience_ClearRestoresDeclaration()
        {
            _templates.Register(PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatOne}\"]")));
            _templates.Freeze();
            var dispatcher = new PanelEventDispatcher(_templates.Require(SharedTemplateId), static (_, _) => { }, _activation);

            _activationApi.SetPanelAudience(SharedTemplateId, PanelAudience.Seats(new[] { SeatZero }));
            Assert.That(PanelAudienceResolution.Effective(_templates.Require(SharedTemplateId), _activation).SeatIds,
                Is.EqualTo(new[] { SeatZero }));
            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatZero).Admitted, Is.True);
            PanelEventFireResult otherSeat = dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatOne);
            Assert.That(otherSeat.Admitted, Is.False, "hotseat turn: the waiting seat is refused");
            Assert.That(otherSeat.Reason, Does.Contain(SeatOne));

            _activationApi.ClearPanelAudience(SharedTemplateId);
            Assert.That(PanelAudienceResolution.Effective(_templates.Require(SharedTemplateId), _activation).IsAllSeats, Is.False);
            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatOne).Admitted, Is.True,
                "turn end: the declared audience rules again");
        }

        [Test]
        public void PlayerSource_NonSeatKind_FailsAtResolveTimePendingAttributionChain()
        {
            string json = TemplateJson(audience: $"[\"{SeatZero}\"]").Replace(
                "\"events\": [",
                "\"intents\": [ { \"event\": \"ui.triaxis.press\", \"intent\": \"order.triaxis.tap\", \"args\": { \"n\": \"$payload.amount\" }, \"playerSource\": \"team\", \"actorSource\": \"commandSource.primary\" } ],\n  \"events\": [");
            PanelTemplate template = PanelTemplateLoader.Load(json);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => new PanelIntentResolver(template).Resolve(
                    "ui.triaxis.press",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["amount"] = 3 },
                    playerId: 2,
                    actor: _scope));

            Assert.That(error!.Message, Does.Contain("playerSource 'team'"));
        }

        // ── Attribution source: the firing seat's input channel ──

        [Test]
        public void DualSeat_ChannelSeatIdDrivesAdmission()
        {
            _templates.Register(PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatOne}\"]")));
            _templates.Freeze();
            var dispatcher = new PanelEventDispatcher(_templates.Require(SharedTemplateId), static (_, _) => { }, _activation);

            using InputHarness harness = InputHarness.CreateDualSeat();
            Assert.That(harness.Runtime.TryGetChannel(SeatZero, out ClientLocalSeatInputChannel channelZero), Is.True);
            Assert.That(harness.Runtime.TryGetChannel(SeatOne, out ClientLocalSeatInputChannel channelOne), Is.True);

            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), channelZero.SeatId).Admitted, Is.True);
            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), channelOne.SeatId).Admitted, Is.True);
            Assert.That(dispatcher.FireFromSeat("ui.triaxis.press", PressArgs(), SeatTwo).Admitted, Is.False,
                "a seat with no channel and no audience membership is refused");
        }

        // ── Graph op: SetPanelAudience ──

        [Test]
        public void GraphApi_SetPanelAudience_OverridesAndClears()
        {
            _templates.Register(PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatOne}\"]")));
            _templates.Freeze();
            var api = new GasGraphRuntimeApi(_world);
            api.BindPanelActivation(_activationApi);

            api.SetPanelAudience(
                ConfigKeyRegistry.Register(SharedTemplateId),
                ConfigKeyRegistry.Register(SeatOne));
            Assert.That(PanelAudienceResolution.Effective(_templates.Require(SharedTemplateId), _activation).SeatIds,
                Is.EqualTo(new[] { SeatOne }));

            api.SetPanelAudience(ConfigKeyRegistry.Register(SharedTemplateId), seatKeyId: 0);
            Assert.That(_activation.TryGetAudienceOverride(SharedTemplateId, out _), Is.False,
                "seat key id 0 clears the override; the declared audience rules again");

            Assert.That(
                () => api.SetPanelAudience(ConfigKeyRegistry.Register(SharedTemplateId), seatKeyId: 9999),
                Throws.InvalidOperationException.With.Message.Contains("9999"));
        }

        [Test]
        public void GraphsJson_SetPanelAudience_CompilesExecutesAndOverrides()
        {
            _templates.Register(PanelTemplateLoader.Load(TemplateJson(audience: $"[\"{SeatZero}\", \"{SeatOne}\"]")));
            _templates.Freeze();
            var api = new GasGraphRuntimeApi(_world);
            api.BindPanelActivation(_activationApi);

            GraphProgramRegistry programs = LoadPrograms("""
[
  {
    "id": "tests.graph.set-panel-audience",
    "kind": "Effect",
    "entry": "rotate",
    "nodes": [
      { "id": "rotate", "op": "SetPanelAudience", "panelType": "tests.panel.triaxis.shared", "panelSeat": "seat.1" },
      { "id": "clear", "op": "SetPanelAudience", "panelType": "tests.panel.triaxis.shared" }
    ],
    "controlEdges": [
      { "from": "rotate", "fromPort": "next", "to": "clear" }
    ],
    "valueEdges": []
  }
]
""");
            Assert.That(programs.TryGetProgram(GraphIdRegistry.GetId("tests.graph.set-panel-audience"), out ReadOnlySpan<GraphInstruction> program), Is.True);
            GraphExecutor.Execute(_world, _scope, Entity.Null, default, program, api);

            Assert.That(_activation.TryGetAudienceOverride(SharedTemplateId, out _), Is.False,
                "rotate then clear leaves the declared audience in charge");
            Assert.That(new PanelEventDispatcher(_templates.Require(SharedTemplateId), static (_, _) => { }, _activation)
                .FireFromSeat("ui.triaxis.press", PressArgs(), SeatOne).Admitted, Is.True);
        }

        // ── Surface: audience seats decide which PresentBinding rects mount ──

        [Test]
        public void SeatSurfacePlacement_DualSplitBinding_AudienceFiltersAndRectsStayPerSeat()
        {
            var bindings = new List<(string SeatId, PresentBinding Binding)>
            {
                (SeatZero, PresentBinding.HorizontalEqualSplit("lv.0", 0, 2, new Vector2(1920f, 1080f))),
                (SeatOne, PresentBinding.HorizontalEqualSplit("lv.1", 1, 2, new Vector2(1920f, 1080f))),
            };
            var surfaces = new List<PanelSeatSurface>();

            Assert.That(PanelSeatSurfacePlacement.TryResolveSeatSurfaces(
                PanelAudience.AllSeats, bindings, 1920f, 1080f, surfaces), Is.True);
            Assert.That(surfaces.Count, Is.EqualTo(2));
            Assert.That(surfaces[0], Is.EqualTo(new PanelSeatSurface(SeatZero, 0f, 0f, 960f, 1080f)));
            Assert.That(surfaces[1], Is.EqualTo(new PanelSeatSurface(SeatOne, 960f, 0f, 960f, 1080f)));

            Assert.That(PanelSeatSurfacePlacement.TryResolveSeatSurfaces(
                PanelAudience.Seats(new[] { SeatOne }), bindings, 1920f, 1080f, surfaces), Is.True);
            Assert.That(surfaces.Count, Is.EqualTo(1), "seat.0's viewport never mounts seat.1's panel");
            Assert.That(surfaces[0].SeatId, Is.EqualTo(SeatOne));
            Assert.That(surfaces[0].X, Is.EqualTo(960f));
        }

        [Test]
        public void SeatSurfacePlacement_NoAudienceBinding_FallsBackToFullWindow()
        {
            var surfaces = new List<PanelSeatSurface>();

            Assert.That(PanelSeatSurfacePlacement.TryResolveSeatSurfaces(
                PanelAudience.Seats(new[] { SeatTwo }),
                new List<(string, PresentBinding)> { (SeatZero, PresentBinding.FullScreen("lv.0", new Vector2(1920f, 1080f))) },
                1920f, 1080f, surfaces), Is.False, "an audience seat without a binding has no surface");

            Assert.That(PanelSeatSurfacePlacement.TryResolveSeatSurfaces(
                PanelAudience.AllSeats, new List<(string, PresentBinding)>(), 1920f, 1080f, surfaces), Is.False,
                "no bindings at all keeps the pre-split full-window mount (sole-seat path)");
        }

        // ── Harness ──

        private GraphProgramRegistry LoadPrograms(string graphJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_PanelTriaxisTests", Guid.NewGuid().ToString("N"));
            try
            {
                string graphDir = Path.Combine(tempRoot, "Core", "GAS");
                Directory.CreateDirectory(graphDir);
                File.WriteAllText(Path.Combine(graphDir, "graphs.json"), graphJson);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", Path.Combine(tempRoot, "Core"));
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = new ConfigCatalog();
                catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));

                var programs = new GraphProgramRegistry();
                var loader = new GraphProgramConfigLoader(pipeline, programs, new NullSymbolResolver());
                var packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
                loader.PatchAndRegister(packages);
                return programs;
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        private sealed class NullSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => TagRegistry.Register(name);
            public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
            public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
            public int ResolveRelationshipType(string name) => 1;
            public int ResolveRelationshipMetric(string name) => 1;
            public int ResolveRelationshipFlag(string name) => 1;
            public int ResolveRelationshipReason(string name) => 1;
            public int ResolveTargetDispatchPreset(string name) => 1;
            public int ResolveEntityTemplate(string name) => 1;
        }

        /// <summary>
        /// Dual-seat input runtime with two channels and no declared schemes — the
        /// channel table is the attribution source; admission consumes channel seat ids.
        /// </summary>
        private sealed class InputHarness : IDisposable
        {
            public ClientLocalSeatRegistry Seats = null!;
            public ClientLocalSeatInputRuntime Runtime = null!;

            public static InputHarness CreateDualSeat()
            {
                var world = World.Create();
                Entity repZero = world.Create();
                Entity repOne = world.Create();

                var orderTypes = new Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry(
                    new Ludots.Core.Gameplay.GAS.Orders.OrderTerminalResultBuffer(
                        capacity: Ludots.Core.Gameplay.GAS.Orders.OrderTerminalResultBuffer.DefaultCapacity));
                var inputConfig = new InputConfigRoot();

                var schemes = new ControlSchemeRuntime(
                    new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                    orderTypes,
                    inputConfig: inputConfig);

                var harness = new InputHarness
                {
                    Seats = new ClientLocalSeatRegistry(),
                };
                harness.Seats.Add(new ClientLocalSeat(SeatZero) { PossessedPlayerId = 7, PossessedRep = repZero });
                harness.Seats.Add(new ClientLocalSeat(SeatOne) { PossessedPlayerId = 8, PossessedRep = repOne });

                var globals = new Dictionary<string, object>();
                harness.Runtime = new ClientLocalSeatInputRuntime(globals, schemes, inputConfig);
                harness.Runtime.PublishSeats(harness.Seats);
                return harness;
            }

            public void Dispose()
            {
            }
        }
    }
}
