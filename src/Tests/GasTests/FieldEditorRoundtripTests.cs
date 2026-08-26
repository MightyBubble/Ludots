using System;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Tools.FieldEditor;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Editor → engine → editor semantic roundtrip for discrete-id cells (schema v2 rects).
    /// </summary>
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
                Assert.That(reloaded.Cells.Count, Is.EqualTo(editor.Cells.Count));
                foreach (var pair in editor.Cells)
                {
                    Assert.That(reloaded.Cells[pair.Key], Is.EqualTo(pair.Value));
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch
                {
                }
            }
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
