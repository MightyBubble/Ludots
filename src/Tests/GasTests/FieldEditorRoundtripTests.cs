using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Tools.FieldEditor;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class FieldEditorRoundtripTests
    {
        [Test]
        public void EditorPaint_Save_EngineLoad_EditorReload_PreservesSemantics()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_FieldEditorRoundtrip", Guid.NewGuid().ToString("N"));
            string mod = Path.Combine(root, "DemoMod");
            try
            {
                Directory.CreateDirectory(Path.Combine(mod, "assets", "Fields", "cells"));
                string catalogPath = CatalogDocument.AssetPath(mod);
                var catalog = new System.Text.Json.Nodes.JsonArray();
                CatalogDocument.AppendLayer(catalog, "layer.demo", 100, 16, 16, "map.field.demo");
                CatalogDocument.Save(catalogPath, catalog);

                string cellsPath = CellsDocument.AssetPath(mod, "layer.demo");
                var editor = new CellsDocument("layer.demo");
                editor.AddRegion("paint.a");
                editor.AddRegion("paint.b");
                editor.PaintRect("paint.a", 0, 0, 3, 3);
                editor.PaintRect("paint.b", 6, 0, 9, 3);
                editor.Save(cellsPath, maxRegionIds: 16);

                FieldCellsAsset asset = LoadAsset(mod, "layer.demo")!;
                Assert.That(asset.RegionKeys, Is.EquivalentTo(new[] { "paint.a", "paint.b" }));
                Assert.That(asset.Rects.Length, Is.EqualTo(2));

                FieldLayerRegistry registry = new();
                registry.Register(
                    "layer.demo", FieldLayerKind.DiscreteId, 100, 16,
                    FieldLayerDefaultValue.None, true, "map.field.demo", 16);
                FieldSessionStore store = FieldSessionStore.Create(
                    registry, new[] { "layer.demo" }, new FieldCellsConfigLoader(CreatePipeline(mod)));
                var layer = store.Get<DiscreteIdFieldLayerData>(registry.GetId("layer.demo"));
                Assert.That(layer.Field.Get(new FieldCell2D(1, 1)), Is.EqualTo(layer.Regions.GetId("paint.a")));
                Assert.That(layer.Field.Get(new FieldCell2D(7, 1)), Is.EqualTo(layer.Regions.GetId("paint.b")));
                Assert.That(layer.Field.NonDefaultCount, Is.EqualTo(32));

                CellsDocument reloaded = CellsDocument.LoadOrNew(cellsPath, "layer.demo");
                Assert.That(reloaded.Regions.Keys.ToArray(), Is.EqualTo(editor.Regions.Keys.ToArray()));
                Assert.That(reloaded.CellCount, Is.EqualTo(editor.CellCount));
                foreach ((FieldCell2D cell, string regionKey) in editor.EnumerateCells())
                {
                    Assert.That(
                        reloaded.TryGetCellKey(cell.X, cell.Y, out string? reloadedKey),
                        Is.True);
                    Assert.That(reloadedKey, Is.EqualTo(regionKey));
                }
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadSave_LargeRect_StaysChunkedAndRectNative()
        {
            string root = CreateTempRoot();
            string path = Path.Combine(root, "layer.large.json");
            try
            {
                File.WriteAllText(
                    path,
                    """
                    {
                      "layer": "layer.large",
                      "regions": ["province"],
                      "rects": [[0, 0, 511, 511, 1]]
                    }
                    """);

                CellsDocument document = CellsDocument.LoadOrNew(
                    path,
                    "layer.large",
                    chunkSizeCells: 16);

                Assert.That(document.CellCount, Is.EqualTo(512 * 512));
                Assert.That(document.Field.ChunkCount, Is.EqualTo(32 * 32));
                Assert.That(
                    document.TryGetCellKey(511, 511, out string? regionKey),
                    Is.True);
                Assert.That(regionKey, Is.EqualTo("province"));

                document.Save(path, maxRegionIds: 8);

                JsonObject saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                JsonArray rects = saved["rects"]!.AsArray();
                Assert.That(rects, Has.Count.EqualTo(1));
                Assert.That(rects[0]!.AsArray().Select(node => node!.GetValue<int>()), Is.EqualTo(
                    new[] { 0, 0, 511, 511, 1 }));
                Assert.That(new FileInfo(path).Length, Is.LessThan(512));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void HistoryStore_UndoRedo_AcrossDocumentReload_PersistsOnDisk()
        {
            string root = CreateTempRoot();
            string path = Path.Combine(root, "layer.history.json");
            try
            {
                var document = new CellsDocument("layer.history");
                document.AddRegion("paint");
                document.Save(path, maxRegionIds: 8);

                HistoryStore.Push(path, document);
                document.PaintRect("paint", 10, 20, 13, 23);
                document.Save(path, maxRegionIds: 8);

                CellsDocument reloaded = CellsDocument.LoadOrNew(path, "layer.history");
                CellsDocument? undone = HistoryStore.Undo(path, reloaded);
                Assert.That(undone, Is.Not.Null);
                undone!.Save(path, maxRegionIds: 8);

                CellsDocument afterUndo = CellsDocument.LoadOrNew(path, "layer.history");
                Assert.That(afterUndo.CellCount, Is.Zero);
                Assert.That(File.Exists(HistoryStore.HistoryPath(path)), Is.True);

                CellsDocument? redone = HistoryStore.Redo(path, afterUndo);
                Assert.That(redone, Is.Not.Null);
                redone!.Save(path, maxRegionIds: 8);

                CellsDocument afterRedo = CellsDocument.LoadOrNew(path, "layer.history");
                Assert.That(afterRedo.CellCount, Is.EqualTo(16));
                Assert.That(afterRedo.TryGetCellKey(12, 22, out string? key), Is.True);
                Assert.That(key, Is.EqualTo("paint"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void RegionKeyChanges_ReindexFieldWithoutChangingPaintedMeaning()
        {
            var document = new CellsDocument("layer.reindex");
            document.AddRegion("zulu");
            document.PaintCell("zulu", 4, 5);

            document.AddRegion("alpha");
            Assert.That(document.TryGetCellKey(4, 5, out string? afterAdd), Is.True);
            Assert.That(afterAdd, Is.EqualTo("zulu"));

            document.RenameRegion("zulu", "bravo");
            Assert.That(document.TryGetCellKey(4, 5, out string? afterRename), Is.True);
            Assert.That(afterRename, Is.EqualTo("bravo"));

            document.RemoveRegion("alpha");
            Assert.That(document.TryGetCellKey(4, 5, out string? afterRemove), Is.True);
            Assert.That(afterRemove, Is.EqualTo("bravo"));
            Assert.That(document.Field.Get(new FieldCell2D(4, 5)), Is.EqualTo(1));
        }

        [Test]
        public void RegionColors_StayInEditorSidecar_AndEngineAssetRemainsStrict()
        {
            string root = CreateTempRoot();
            string mod = Path.Combine(root, "DemoMod");
            try
            {
                Directory.CreateDirectory(Path.Combine(mod, "assets", "Fields", "cells"));
                string path = CellsDocument.AssetPath(mod, "layer.colors");
                var document = new CellsDocument("layer.colors");
                document.AddRegion("paint");
                document.PaintCell("paint", 0, 0);
                document.Save(path, maxRegionIds: 8);
                HistoryStore.Push(path, document);

                string color = FieldEditorMetadataStore.SetColor(
                    path,
                    document,
                    "paint",
                    "#12abEF");

                Assert.That(color, Is.EqualTo("#12ABEF"));
                JsonObject cells = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                Assert.That(cells.ContainsKey("regionColors"), Is.False);
                Assert.That(File.Exists(FieldEditorMetadataStore.MetadataPath(path)), Is.True);
                Assert.That(LoadAsset(mod, "layer.colors"), Is.Not.Null);

                CellsDocument colorUndo = CellsDocument.LoadOrNew(path, "layer.colors");
                HistoryStore.Undo(path, colorUndo);
                Assert.That(
                    FieldEditorMetadataStore.GetColors(path, colorUndo),
                    Is.Empty);

                HistoryStore.Redo(path, colorUndo);
                Assert.That(
                    FieldEditorMetadataStore.GetColors(path, colorUndo)["paint"],
                    Is.EqualTo("#12ABEF"));

                HistoryStore.Push(path, colorUndo);
                colorUndo.RenameRegion("paint", "renamed");
                colorUndo.Save(path, maxRegionIds: 8);
                FieldEditorMetadataStore.RenameRegion(
                    path,
                    colorUndo.LayerKey,
                    "paint",
                    "renamed");

                CellsDocument renameUndo = CellsDocument.LoadOrNew(path, "layer.colors");
                HistoryStore.Undo(path, renameUndo);
                renameUndo.Save(path, maxRegionIds: 8);
                Assert.That(renameUndo.Regions.Keys, Is.EqualTo(new[] { "paint" }));
                Assert.That(
                    FieldEditorMetadataStore.GetColors(path, renameUndo)["paint"],
                    Is.EqualTo("#12ABEF"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_FieldEditorRoundtrip",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static FieldCellsAsset? LoadAsset(string modRoot, string layerKey)
        {
            return new FieldCellsConfigLoader(CreatePipeline(modRoot)).Load(layerKey);
        }

        private static ConfigPipeline CreatePipeline(string modRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("DemoMod", modRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("DemoMod");
            return new ConfigPipeline(vfs, modLoader);
        }
    }
}
