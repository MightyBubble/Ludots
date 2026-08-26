using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class FieldSessionStoreTests
    {
        [Test]
        public void Create_IsCatalogIntersectedWithEnablement()
        {
            FieldLayerRegistry catalog = CreateCatalog(
                Layer("layerX", FieldLayerKind.DiscreteId),
                Layer("layerY", FieldLayerKind.Scalar32));

            FieldSessionStore store = FieldSessionStore.Create(catalog, new[] { "layerX" });

            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(store.TryGet(catalog.GetId("layerX"), out FieldLayerData enabled), Is.True);
            Assert.That(enabled, Is.InstanceOf<DiscreteIdFieldLayerData>());
            Assert.That(store.TryGet(catalog.GetId("layerY"), out _), Is.False, "declared but not enabled layers stay absent");
        }

        [Test]
        public void Create_NoEnablement_YieldsEmptyStore()
        {
            FieldLayerRegistry catalog = CreateCatalog(Layer("layerX", FieldLayerKind.DiscreteId));
            Assert.That(FieldSessionStore.Create(catalog, null).Count, Is.EqualTo(0));
            Assert.That(FieldSessionStore.Create(catalog, Array.Empty<string>()).Count, Is.EqualTo(0));
        }

        [Test]
        public void Create_EnabledLayerMissingFromCatalog_FailsClosed()
        {
            FieldLayerRegistry catalog = CreateCatalog(Layer("layerX", FieldLayerKind.DiscreteId));

            var exception = Assert.Throws<InvalidOperationException>(
                () => FieldSessionStore.Create(catalog, new[] { "layerMissing" }));
            Assert.That(exception!.Message, Does.Contain("layerMissing"));
            Assert.That(exception.Message, Does.Contain("Fields/layers.json"));
        }

        [Test]
        public void Create_AppliesAuthoredCells_RegionsRegisteredInOrdinalOrder()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCells(root, """
                { "schemaVersion": 1, "layer": "layerX", "regions": [ "r2", "r1" ], "cells": [ [7, 7, 1], [8, 8, 2] ] }
                """);
                FieldLayerRegistry catalog = CreateCatalog(Layer("layerX", FieldLayerKind.DiscreteId));

                FieldSessionStore store = FieldSessionStore.Create(
                    catalog, new[] { "layerX" }, new FieldCellsConfigLoader(CreatePipeline(root)));

                var layer = store.Get<DiscreteIdFieldLayerData>(catalog.GetId("layerX"));
                Assert.That(layer.Regions.GetId("r1"), Is.EqualTo(1), "ids follow the Ordinal-sorted union");
                Assert.That(layer.Regions.GetId("r2"), Is.EqualTo(2));
                Assert.That(layer.Field.Get(new FieldCell2D(7, 7)), Is.EqualTo(1), "fragment-local id 1 = 'r1': ids are sorted-ordinal, not file order");
                Assert.That(layer.Field.Get(new FieldCell2D(8, 8)), Is.EqualTo(2), "fragment-local id 2 = 'r2'");
                Assert.That(layer.Field.NonDefaultCount, Is.EqualTo(2));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void TypedAccessor_MismatchedKind_FailsClosed()
        {
            FieldLayerRegistry catalog = CreateCatalog(Layer("layerS", FieldLayerKind.Scalar32));
            FieldSessionStore store = FieldSessionStore.Create(catalog, new[] { "layerS" });

            Assert.Throws<InvalidOperationException>(
                () => store.Get<DiscreteIdFieldLayerData>(catalog.GetId("layerS")));
        }

        [Test]
        public void FieldLayersParticipant_RoundTripsDiscreteAndScalarLayers()
        {
            var manager = new MapSessionManager();
            var mapId = new MapId("mapA");
            FieldLayerRegistry catalog = CreateCatalog(
                Layer("layerD", FieldLayerKind.DiscreteId),
                Layer("layerS", FieldLayerKind.Scalar32),
                Layer("layerV", FieldLayerKind.Vector2, persistent: false));

            MapSession session = manager.CreateSession(mapId, new MapConfig());
            var discrete = FieldSessionStore.Create(catalog, new[] { "layerD", "layerS", "layerV" });
            var discreteLayer = discrete.Get<DiscreteIdFieldLayerData>(catalog.GetId("layerD"));
            discreteLayer.Regions.Register("r1");
            discreteLayer.Regions.Register("r2");
            discreteLayer.Field.Set(new FieldCell2D(1, 2), discreteLayer.Regions.GetId("r2"));
            discrete.Get<Scalar32FieldLayerData>(catalog.GetId("layerS")).Field.Set(new FieldCell2D(3, 4), 5.5f);
            discrete.Get<Vector2FieldLayerData>(catalog.GetId("layerV")).Field.Set(new FieldCell2D(9, 9), new System.Numerics.Vector2(1f, 2f));
            session.Fields = discrete;

            ISaveParticipant participant = CoreSaveParticipants.CreateFieldLayersParticipant(manager);
            JsonNode captured = participant.CaptureState();

            manager.CreateSession(mapId, new MapConfig());
            MapSession restored = manager.GetSession(mapId);
            restored.Fields = FieldSessionStore.Create(catalog, new[] { "layerD", "layerS", "layerV" });
            restored.Fields.Get<Vector2FieldLayerData>(catalog.GetId("layerV"))
                .Field.Set(new FieldCell2D(9, 9), new System.Numerics.Vector2(1f, 2f));
            participant.RestoreState(JsonNode.Parse(captured.ToJsonString())!);

            var restoredDiscrete = restored.Fields.Get<DiscreteIdFieldLayerData>(catalog.GetId("layerD"));
            Assert.That(restoredDiscrete.Regions.GetId("r2"), Is.EqualTo(discreteLayer.Regions.GetId("r2")), "region ids survive via the key table");
            Assert.That(restoredDiscrete.Field.Get(new FieldCell2D(1, 2)), Is.EqualTo(discreteLayer.Regions.GetId("r2")));
            Assert.That(
                restored.Fields.Get<Scalar32FieldLayerData>(catalog.GetId("layerS")).Field.Get(new FieldCell2D(3, 4)),
                Is.EqualTo(5.5f));
            Assert.That(
                restored.Fields.Get<Vector2FieldLayerData>(catalog.GetId("layerV")).Field.NonDefaultCount,
                Is.EqualTo(1),
                "non-persistent layers skip capture, so authored state remains");
        }

        [Test]
        public void FieldLayersParticipant_UnknownLayerInSave_FailsClosed()
        {
            var manager = new MapSessionManager();
            var mapId = new MapId("mapA");
            FieldLayerRegistry catalog = CreateCatalog(Layer("layerD", FieldLayerKind.DiscreteId));
            MapSession session = manager.CreateSession(mapId, new MapConfig());
            session.Fields = FieldSessionStore.Create(catalog, new[] { "layerD" });

            ISaveParticipant participant = CoreSaveParticipants.CreateFieldLayersParticipant(manager);
            JsonNode save = JsonNode.Parse("""
            { "sessions": [ { "mapId": "mapA", "layers": [ { "layer": "layerGone", "regions": [ "r1" ], "rects": [] } ] } ] }
            """)!;

            var exception = Assert.Throws<InvalidOperationException>(() => participant.RestoreState(save));
            Assert.That(exception!.Message, Does.Contain("layerGone"));
        }

        [Test]
        public void FieldLayersParticipant_UnloadedMap_FailsClosed()
        {
            var manager = new MapSessionManager();
            ISaveParticipant participant = CoreSaveParticipants.CreateFieldLayersParticipant(manager);
            JsonNode save = JsonNode.Parse("""
            { "sessions": [ { "mapId": "mapMissing", "layers": [] } ] }
            """)!;

            var exception = Assert.Throws<InvalidOperationException>(() => participant.RestoreState(save));
            Assert.That(exception!.Message, Does.Contain("mapMissing"));
        }

        private static FieldLayerRegistry CreateCatalog(params (string Key, FieldLayerKind Kind, bool Persistent)[] layers)
        {
            var registry = new FieldLayerRegistry();
            foreach ((string key, FieldLayerKind kind, bool persistent) in layers)
            {
                registry.Register(
                    key, kind, cellSizeCm: 100, chunkSizeCells: 8,
                    FieldLayerDefaultValue.None, persistent, "test.writer",
                    maxRegionIds: kind == FieldLayerKind.DiscreteId ? 16 : 0);
            }

            return registry;
        }

        private static (string Key, FieldLayerKind Kind, bool Persistent) Layer(string key, FieldLayerKind kind, bool persistent = true)
        {
            return (key, kind, persistent);
        }

        private static ConfigPipeline CreatePipeline(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }

        private static void WriteCells(string root, string json)
        {
            Directory.CreateDirectory(Path.Combine(root, "Fields", "cells"));
            File.WriteAllText(Path.Combine(root, "Fields", "cells", "layerX.json"), json);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_FieldSessionStoreTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
