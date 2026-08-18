using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// Entity attribute panel, end to end, driven purely by JSON artifacts (#1010/#1012/#1013/#1014):
    /// panel template JSON + visibility orchestration graph JSON + lookup table JSON.
    /// Chain: signal → orchestration graph → activation → variables (attribute/graph/table)
    /// → markup render → declared event → validated payload → signal bridge → intent resolution.
    /// </summary>
    [TestFixture]
    public sealed class EntityAttributePanelShowcaseTests
    {
        private const string PanelTemplateJson = """
        {
          "id": "tests.panel.entity_attributes",
          "variables": [
            { "name": "hp", "kind": "Float",
              "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.attr.hp" } },
            { "name": "attack", "kind": "Float",
              "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.attr.attack" } },
            { "name": "squadAttack", "kind": "Float",
              "source": { "sourceKind": "AggregateProjection", "graphOutputKey": "tests.panel.squad.attack.total" } },
            { "name": "rank.badge", "kind": "Int",
              "source": { "sourceKind": "TableLookup", "lookupTable": "tests.rank_display", "lookupField": "displayToken", "keyAttribute": "tests.attr.level" } }
          ],
          "binds": [
            { "control": "lbl.hp", "variable": "hp" },
            { "control": "lbl.attack", "variable": "attack" },
            { "control": "lbl.squad", "variable": "squadAttack" },
            { "control": "lbl.rank", "variable": "rank.badge" }
          ],
          "events": [
            { "eventId": "ui.entity.inspect", "gesture": "click", "control": "row.entity",
              "payload": { "entityId": "Int", "verbose": "Bool" } }
          ],
          "intents": [
            { "event": "ui.entity.inspect", "intent": "order.entity.inspect",
              "args": { "target": "$payload.entityId", "verbose": "$payload.verbose" },
              "playerSource": "seat", "actorSource": "commandSource.primary" }
          ]
        }
        """;

        // visible = blackboard("tests.signal.selection") != 0 — pure JSON instructions.
        private const string OrchestrationJson = """
        [
          {
            "panelType": "tests.panel.entity_attributes",
            "instructions": [
              { "op": "LoadCaster", "dst": 0 },
              { "op": "ReadBlackboardInt", "dst": 1, "a": 0, "imm": 0 },
              { "op": "HaltReturnInt", "a": 1 }
            ],
            "symbols": [ "tests.signal.selection" ]
          }
        ]
        """;

        private const string RankTableJson = """
        [
          {
            "id": "tests.rank_display",
            "keyKind": "Int",
            "columns": [
              { "id": "displayToken", "kind": "Int" },
              { "id": "powerScale", "kind": "Float" }
            ],
            "rows": [ { "key": 2, "displayToken": 11, "powerScale": 1.2 } ]
          }
        ]
        """;

        private World _world = null!;
        private Entity _soldier;
        private Entity _context;
        private GraphLookupTableRegistry _tables = null!;
        private UiPanelActivationStore _activation = null!;
        private PanelOrchestrationRuntime _orchestration = null!;
        private GasGraphRuntimeApi _api = null!;
        private int _hpId;
        private int _attackId;
        private int _levelId;

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            _hpId = AttributeRegistry.Register("tests.attr.hp");
            _attackId = AttributeRegistry.Register("tests.attr.attack");
            _levelId = AttributeRegistry.Register("tests.attr.level");

            _world = World.Create();
            _soldier = _world.Create();
            _world.Add(_soldier, new AttributeBuffer());
            ref AttributeBuffer buffer = ref _world.Get<AttributeBuffer>(_soldier);
            buffer.SetBase(_hpId, 87f);
            buffer.SetBase(_attackId, 12f);
            buffer.SetBase(_levelId, 2f);

            _context = _world.Create();
            _world.Add(_context, new BlackboardIntBuffer());

            _tables = LoadTables(RankTableJson);
            _activation = new UiPanelActivationStore();
            _orchestration = new PanelOrchestrationRuntime(PanelOrchestrationJson.Load(OrchestrationJson), _activation);
            _api = new GasGraphRuntimeApi(_world);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void FullChain_SelectionSignalOrchestratesVisibility_VariablesRenderAndEventsResolve()
        {
            PanelTemplate template = PanelTemplateLoader.Load(PanelTemplateJson);

            // 1) hidden until the selection signal fires (orchestration graph decides).
            PanelActivationDiff hiddenState = _orchestration.EvaluateAll(_world, _api, _context);
            Assert.That(_activation.IsVisible("tests.panel.entity_attributes"), Is.False);
            Assert.That(hiddenState.Activated, Is.Empty);

            // 2) declared event fires with a validated payload; the signal bridge turns it
            //    into the orchestration blackboard signal (zero-code path B wiring).
            var fired = new List<string>();
            var dispatcher = new PanelEventDispatcher(template, (eventId, payload) =>
            {
                fired.Add(eventId);
                PanelSignalBridge.WriteSignal(_world, _context, "tests.signal.selection", Convert.ToInt32(payload["entityId"]));
            });
            dispatcher.Fire(
                "ui.entity.inspect",
                new JsonObject { ["entityId"] = 5, ["verbose"] = true });
            Assert.That(fired, Is.EqualTo(new[] { "ui.entity.inspect" }));

            // 3) orchestration re-evaluates from the signal → panel visible.
            PanelActivationDiff shown = _orchestration.EvaluateAll(_world, _api, _context);
            Assert.That(_activation.IsVisible("tests.panel.entity_attributes"), Is.True);
            Assert.That(shown.Activated, Does.Contain("tests.panel.entity_attributes"));

            // 4) variables: attribute + graph aggregate + lookup table, all fail-closed.
            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);
            Entity factionOwner = _world.Create();
            _world.Add(factionOwner, new AttributeBuffer());
            ref AttributeBuffer ownerBuffer = ref _world.Get<AttributeBuffer>(factionOwner);
            ownerBuffer.SetBase(_hpId, 430f);
            ownerBuffer.SetBase(_attackId, 96f);
            ownerBuffer.SetBase(_levelId, 2f);
            outputs.SetFloat(_soldier, "tests.panel.squad.attack.total", 36f);
            outputs.SetFloat(factionOwner, "tests.panel.squad.attack.total", 36f);
            var reader = new PanelProjectionReader(_world, outputs, AttributeRegistry.GetId, _tables);

            PanelVariableSet itemScope = new PanelInstance(template, _soldier).Evaluate(reader);
            Assert.That(itemScope.Get("hp"), Is.EqualTo(87f));
            Assert.That(itemScope.Get("attack"), Is.EqualTo(12f));
            Assert.That(itemScope.Get("squadAttack"), Is.EqualTo(36f));
            Assert.That(itemScope.Get("rank.badge"), Is.EqualTo(11f));

            // 5) same template serves the collection scope (#1012): the faction owner
            //    reads the same squad aggregate while the item scope reads soldier attrs.
            PanelVariableSet collectionScope = new PanelInstance(template, factionOwner).Evaluate(reader);
            Assert.That(collectionScope.Get("squadAttack"), Is.EqualTo(36f));

            // 6) the declared event resolves to a seat-attributed intent (#1013).
            var intentResolver = new PanelIntentResolver(template);
            PanelIntent intent = intentResolver.Resolve(
                "ui.entity.inspect",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["entityId"] = 5,
                    ["verbose"] = true,
                },
                playerId: 2,
                actor: _soldier);
            Assert.That(intent.Intent, Is.EqualTo("order.entity.inspect"));
            Assert.That(intent.Args["target"], Is.EqualTo(5));
            Assert.That(intent.Args["verbose"], Is.True);
            Assert.That(intent.PlayerId, Is.EqualTo(2));
            Assert.That(intent.Actor, Is.EqualTo(_soldier));

            // 7) zero-selection signal hides the panel again — still the graph deciding.
            PanelSignalBridge.WriteSignal(_world, _context, "tests.signal.selection", 0);
            _orchestration.EvaluateAll(_world, _api, _context);
            Assert.That(_activation.IsVisible("tests.panel.entity_attributes"), Is.False);
        }

        [Test]
        public void BadEventPayload_FailsNamingField()
        {
            PanelTemplate template = PanelTemplateLoader.Load(PanelTemplateJson);
            var dispatcher = new PanelEventDispatcher(template, static (_, _) => { });

            Assert.That(
                () => dispatcher.Fire("ui.entity.inspect", new JsonObject { ["entityId"] = 5 }),
                Throws.InvalidOperationException.With.Message.Contains("verbose"));

            Assert.That(
                () => dispatcher.Fire("ui.entity.inspect", new JsonObject { ["entityId"] = 5, ["verbose"] = true, ["ghost"] = 1 }),
                Throws.InvalidOperationException.With.Message.Contains("ghost"));
        }

        [Test]
        public void OrchestrationJson_UnknownOpOrField_FailsLoudly()
        {
            Assert.That(
                () => PanelOrchestrationJson.Load("""[{"panelType":"p","instructions":[{"op":"Magic"}]}]"""),
                Throws.InvalidOperationException.With.Message.Contains("Magic"));

            Assert.That(
                () => PanelOrchestrationJson.Load("""[{"panelType":"p","frob":1,"instructions":[{"op":"HaltReturnInt","a":0}]}]"""),
                Throws.InvalidOperationException.With.Message.Contains("frob"));
        }

        private static GraphLookupTableRegistry LoadTables(string json)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_PanelRankTable", Guid.NewGuid().ToString("N"));
            try
            {
                string tableDir = Path.Combine(tempRoot, "GraphTables");
                Directory.CreateDirectory(tableDir);
                File.WriteAllText(Path.Combine(tableDir, "lookup_tables.json"), json);
                var vfs = new Ludots.Core.Modding.VirtualFileSystem();
                vfs.Mount("Core", tempRoot);
                var modLoader = new Ludots.Core.Modding.ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                var pipeline = new Ludots.Core.Config.ConfigPipeline(vfs, modLoader);
                var catalog = new Ludots.Core.Config.ConfigCatalog();
                catalog.Add(new Ludots.Core.Config.ConfigCatalogEntry(
                    GraphLookupTableLoader.ConfigPath,
                    Ludots.Core.Config.ConfigMergePolicy.ArrayById,
                    "id"));
                return new GraphLookupTableLoader(pipeline).Load(catalog);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }
    }
}
