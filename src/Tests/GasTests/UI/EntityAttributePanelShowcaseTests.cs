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
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// Entity attribute panel, end to end, driven purely by JSON artifacts:
    /// panel template JSON + lookup table JSON; visibility via ShowPanel/HidePanel
    /// graph ops (any graph can fire them). The panel only manages data (variables)
    /// and interaction routing (events/intents) — visibility is decided by whoever
    /// calls the op.
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
        private GraphLookupTableRegistry _tables = null!;
        private UiPanelActivationStore _activation = null!;
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

            _tables = LoadTables(RankTableJson);
            _activation = new UiPanelActivationStore();
            _api = new GasGraphRuntimeApi(_world, lookupTables: _tables);
            _api.BindPanelActivation(new PanelActivationApi(_activation));
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void FullChain_GraphOpsDriveVisibility_VariablesEvaluate_EventsResolve()
        {
            PanelTemplate template = PanelTemplateLoader.Load(PanelTemplateJson);

            // 1) panel starts hidden — nobody called ShowPanel yet.
            Assert.That(_activation.IsVisible("tests.panel.entity_attributes"), Is.False);

            // 2) some graph (level blueprint, selection handler, whatever) fires ShowPanel.
            //    Direct API call here for test simplicity — same effect as the graph op.
            _api.ShowPanel(ConfigKeyRegistry.Register("tests.panel.entity_attributes"));
            Assert.That(_activation.IsVisible("tests.panel.entity_attributes"), Is.True);

            // 3) variables: attribute + graph aggregate + lookup table, all fail-closed.
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
            PanelVariableSet values = new PanelInstance(template, _soldier).Evaluate(reader);

            Assert.That(values.Get("hp"), Is.EqualTo(87f));
            Assert.That(values.Get("attack"), Is.EqualTo(12f));
            Assert.That(values.Get("squadAttack"), Is.EqualTo(36f));
            Assert.That(values.Get("rank.badge"), Is.EqualTo(11f));

            // 4) same template serves the collection scope (#1012).
            PanelVariableSet collectionScope = new PanelInstance(template, factionOwner).Evaluate(reader);
            Assert.That(collectionScope.Get("squadAttack"), Is.EqualTo(36f));

            // 5) the declared event resolves to a seat-attributed intent (#1013).
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
            Assert.That(intent.PlayerId, Is.EqualTo(2));
            Assert.That(intent.Actor, Is.EqualTo(_soldier));

            // 6) whoever showed the panel can hide it — same op, reverse direction.
            _api.HidePanel(ConfigKeyRegistry.Register("tests.panel.entity_attributes"));
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
