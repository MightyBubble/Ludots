using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// End-to-end tests for the unified config merge pipeline.
    /// Simulates real mod development scenarios: Core + Mod overlay, delete semantics,
    /// conflict reporting, DeepObject merging, and multi-mod layering.
    /// </summary>
    [TestFixture]
    public class ConfigMergeE2ETests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_ConfigMergeE2E", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        // ── Helpers ──

        private static (VirtualFileSystem vfs, ModLoader modLoader, ConfigPipeline pipeline, ConfigCatalog catalog)
            BuildPipeline(string root, string[] modIds = null)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            if (modIds != null)
            {
                for (int i = 0; i < modIds.Length; i++)
                {
                    string modPath = Path.Combine(root, modIds[i]);
                    vfs.Mount(modIds[i], modPath);
                    modLoader.LoadedModIds.Add(modIds[i]);
                }
            }
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return (vfs, modLoader, pipeline, catalog);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, "Configs", Path.GetDirectoryName(relativePath) ?? "");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private void WriteAssetFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, "assets", "Configs", Path.GetDirectoryName(relativePath) ?? "");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 1: Mod overrides a single effect property (ArrayById merge)
        // A mod developer wants to change the damage of a Core-defined effect.
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_ModOverridesEffectProperty_MergedCorrectly()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""Fireball"", ""presetType"": ""Damage"", ""damage"": 100 }]");
            WriteAssetFile("ModA", "GAS/effects.json",
                @"[{ ""Id"": ""Fireball"", ""damage"": 200 }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var report = new ConfigConflictReport();
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry, report);

            That(merged.Count, Is.EqualTo(1), "Should have exactly 1 merged entry");
            That(merged[0].Id, Is.EqualTo("Fireball"));
            That(merged[0].Node["damage"]?.GetValue<int>(), Is.EqualTo(200), "Mod should override damage to 200");
            That(merged[0].Node["presetType"]?.GetValue<string>(), Is.EqualTo("Damage"), "Core property should be preserved");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 2: Mod adds a new ability without touching Core abilities
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_ModAddsNewAbility_BothExist()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/abilities.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "GAS/abilities.json",
                @"[{ ""id"": ""Slash"", ""cooldown"": 5 }]");
            WriteAssetFile("ModA", "GAS/abilities.json",
                @"[{ ""id"": ""Fireball"", ""cooldown"": 8 }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/abilities.json", ConfigMergePolicy.ArrayById, "id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(2));
            That(merged[0].Id, Is.EqualTo("Slash"));
            That(merged[1].Id, Is.EqualTo("Fireball"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 3: Mod deletes a Core-defined tag rule via __delete
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_ModDeletesTagRule_ViaDeleteField()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/tag_rules.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "GAS/tag_rules.json",
                @"[{ ""id"": ""Stun"", ""blocks"": [""Move""] }, { ""id"": ""Silence"", ""blocks"": [""Cast""] }]");
            WriteAssetFile("ModA", "GAS/tag_rules.json",
                @"[{ ""id"": ""Stun"", ""__delete"": true }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/tag_rules.json", ConfigMergePolicy.ArrayById, "id");
            var report = new ConfigConflictReport();
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry, report);

            That(merged.Count, Is.EqualTo(1), "Stun should be deleted, only Silence remains");
            That(merged[0].Id, Is.EqualTo("Silence"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 4: Mod deletes via Disabled field
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_ModDeletesEntry_ViaDisabled()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""Heal"", ""amount"": 50 }, { ""Id"": ""Poison"", ""amount"": 10 }]");
            WriteAssetFile("ModA", "GAS/effects.json",
                @"[{ ""Id"": ""Heal"", ""Disabled"": true }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(1));
            That(merged[0].Id, Is.EqualTo("Poison"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 5: DeepObject merge — mod adds a new clock field
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_DeepObjectMerge_ModAddsField()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/clock.json"", ""Policy"": ""DeepObject"" }]");
            WriteFile("Core", "GAS/clock.json",
                @"{ ""StepEveryFixedTicks"": 1, ""Mode"": ""Auto"" }");
            WriteAssetFile("ModA", "GAS/clock.json",
                @"{ ""StepEveryFixedTicks"": 2 }");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/clock.json", ConfigMergePolicy.DeepObject);
            var merged = pipeline.MergeDeepObjectFromCatalog(in entry);

            That(merged, Is.Not.Null);
            That(merged["StepEveryFixedTicks"]?.GetValue<int>(), Is.EqualTo(2), "Mod overrides tick count");
            That(merged["Mode"]?.GetValue<string>(), Is.EqualTo("Auto"), "Core field preserved");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 6: Three-mod layering — priority ordering
        // ModB overrides ModA which overrides Core.
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_ThreeModLayering_LastModWins()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""Fireball"", ""damage"": 100, ""element"": ""fire"" }]");
            WriteAssetFile("ModA", "GAS/effects.json",
                @"[{ ""Id"": ""Fireball"", ""damage"": 150 }]");
            WriteAssetFile("ModB", "GAS/effects.json",
                @"[{ ""Id"": ""Fireball"", ""damage"": 200, ""splash"": true }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA", "ModB" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(1));
            That(merged[0].Node["damage"]?.GetValue<int>(), Is.EqualTo(200), "Last mod (ModB) wins");
            That(merged[0].Node["element"]?.GetValue<string>(), Is.EqualTo("fire"), "Core field preserved");
            That(merged[0].Node["splash"]?.GetValue<bool>(), Is.True, "ModB field added");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 7: ConflictReport records all fragment sources and winners
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_ConflictReport_RecordsFragmentsAndWinners()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""A"", ""v"": 1 }, { ""Id"": ""B"", ""v"": 2 }]");
            WriteAssetFile("ModA", "GAS/effects.json",
                @"[{ ""Id"": ""A"", ""v"": 10 }, { ""Id"": ""C"", ""v"": 3 }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var report = new ConfigConflictReport();
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry, report);

            That(merged.Count, Is.EqualTo(3));
            // Report should have recorded fragments
            var fragments = report.GetFragments("GAS/effects.json");
            That(fragments, Is.Not.Null);
            That(fragments.Count, Is.GreaterThanOrEqualTo(2), "Should record at least Core + ModA fragments");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 8: Single source — Core only, no mods
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_SingleSource_CoreOnly()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/abilities.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "GAS/abilities.json",
                @"[{ ""id"": ""Slash"", ""cooldown"": 5 }, { ""id"": ""Heal"", ""cooldown"": 10 }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/abilities.json", ConfigMergePolicy.ArrayById, "id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(2));
            That(merged[0].Id, Is.EqualTo("Slash"));
            That(merged[1].Id, Is.EqualTo("Heal"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 9: GetEntryOrDefault fallback when not in catalog
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_GetEntryOrDefault_FallbackWhenNotInCatalog()
        {
            WriteFile("Core", "config_catalog.json", "[]");
            WriteFile("Core", "Custom/my_config.json",
                @"[{ ""Id"": ""X"", ""v"": 1 }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Custom/my_config.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(1));
            That(merged[0].Id, Is.EqualTo("X"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 10: DeepObject — attribute constraints merge from multiple mods
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_DeepObject_AttributeConstraints_MultiMod()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/attribute_constraints.json"", ""Policy"": ""DeepObject"" }]");
            WriteFile("Core", "GAS/attribute_constraints.json",
                @"{ ""Health"": { ""min"": 0, ""max"": 1000 } }");
            WriteAssetFile("ModA", "GAS/attribute_constraints.json",
                @"{ ""Mana"": { ""min"": 0, ""max"": 500 }, ""Health"": { ""max"": 2000 } }");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/attribute_constraints.json", ConfigMergePolicy.DeepObject);
            var merged = pipeline.MergeDeepObjectFromCatalog(in entry);

            That(merged, Is.Not.Null);
            // Health.max should be overridden by ModA
            var health = merged["Health"]?.AsObject();
            That(health, Is.Not.Null);
            That(health["max"]?.GetValue<int>(), Is.EqualTo(2000), "ModA overrides Health.max");
            // Mana should be added by ModA
            var mana = merged["Mana"]?.AsObject();
            That(mana, Is.Not.Null);
            That(mana["max"]?.GetValue<int>(), Is.EqualTo(500));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 11: Mod re-adds a previously deleted entry
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_ModReAddsDeletedEntry()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""Heal"", ""amount"": 50 }]");
            // ModA deletes Heal
            WriteAssetFile("ModA", "GAS/effects.json",
                @"[{ ""Id"": ""Heal"", ""__delete"": true }]");
            // ModB re-adds Heal with different values
            WriteAssetFile("ModB", "GAS/effects.json",
                @"[{ ""Id"": ""Heal"", ""amount"": 100, ""type"": ""holy"" }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA", "ModB" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(1));
            That(merged[0].Id, Is.EqualTo("Heal"));
            That(merged[0].Node["amount"]?.GetValue<int>(), Is.EqualTo(100), "Re-added with new value");
            That(merged[0].Node["type"]?.GetValue<string>(), Is.EqualTo("holy"), "New field from ModB");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 12: PresetTypeLoader via ConfigPipeline (full integration)
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_PresetTypeLoader_LoadsViaPipeline()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/preset_types.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "GAS/preset_types.json", @"[
                {
                    ""id"": ""InstantDamage"",
                    ""components"": [""ModifierParams""],
                    ""activePhases"": [""OnApply""],
                    ""allowedLifetimes"": [""Instant""]
                }
            ]");
            WriteAssetFile("ModA", "GAS/preset_types.json", @"[
                {
                    ""id"": ""InstantDamage"",
                    ""components"": [""ModifierParams"", ""PhaseGraphBindings""]
                }
            ]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var registry = new PresetTypeRegistry();
            var loader = new PresetTypeLoader(pipeline, registry);
            loader.Load(catalog);

            That(registry.IsRegistered(EffectPresetType.InstantDamage), Is.True);
            ref readonly var def = ref registry.Get(EffectPresetType.InstantDamage);
            That(def.HasComponent(ComponentFlags.ModifierParams), Is.True, "Core component preserved");
            That(def.HasComponent(ComponentFlags.PhaseGraphBindings), Is.True, "ModA component added");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 13: GasClockConfig DeepObject merge
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_GasClockConfig_LoadsViaPipeline()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/clock.json"", ""Policy"": ""DeepObject"" }]");
            WriteFile("Core", "GAS/clock.json",
                @"{ ""StepEveryFixedTicks"": 1, ""Mode"": ""Auto"" }");
            WriteAssetFile("ModA", "GAS/clock.json",
                @"{ ""StepEveryFixedTicks"": 3 }");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var loader = new GasClockConfigLoader(pipeline);
            var config = loader.Load(catalog);

            That(config.StepEveryFixedTicks, Is.EqualTo(3), "Mod overrides tick count");
            That(config.Mode, Is.EqualTo(GasStepMode.Auto), "Core mode preserved");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 14: Empty mod fragment — no crash, Core preserved
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_EmptyModFragment_CorePreserved()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""Fireball"", ""damage"": 100 }]");
            WriteAssetFile("ModA", "GAS/effects.json", "[]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(1));
            That(merged[0].Node["damage"]?.GetValue<int>(), Is.EqualTo(100));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 15: No config file exists anywhere — empty result
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_NoConfigExists_EmptyResult()
        {
            WriteFile("Core", "config_catalog.json", "[]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "NonExistent/stuff.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(0));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 16: Performer definition with numeric ID — ArrayById merge
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_PerformerDefinition_NumericId_MergesCorrectly()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json", @"[
                { ""id"": 1, ""visualKind"": ""GroundOverlay"", ""defaultScale"": 1.0 },
                { ""id"": 2, ""visualKind"": ""GroundOverlay"", ""defaultScale"": 2.0 }
            ]");
            WriteAssetFile("ModA", "Presentation/performers.json", @"[
                { ""id"": 1, ""defaultScale"": 1.5 }
            ]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/performers.json", ConfigMergePolicy.ArrayById, "id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(2));
            // id=1 should have merged scale
            That(merged[0].Node["defaultScale"]?.GetValue<float>(), Is.EqualTo(1.5f).Within(0.01f));
            // id=2 should be unchanged
            That(merged[1].Node["defaultScale"]?.GetValue<float>(), Is.EqualTo(2.0f).Within(0.01f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 17: MergeArrayByIdToEntries preserves insertion order
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_InsertionOrder_Preserved()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""C"" }, { ""Id"": ""A"" }, { ""Id"": ""B"" }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(3));
            That(merged[0].Id, Is.EqualTo("C"), "Insertion order preserved");
            That(merged[1].Id, Is.EqualTo("A"));
            That(merged[2].Id, Is.EqualTo("B"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Scenario 18: Case-insensitive ID matching
        // ═══════════════════════════════════════════════════════════════════
        [Test]
        public void Scenario_CaseInsensitiveId_MergesCorrectly()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""Id"" }]");
            WriteFile("Core", "GAS/effects.json",
                @"[{ ""Id"": ""Fireball"", ""damage"": 100 }]");
            WriteAssetFile("ModA", "GAS/effects.json",
                @"[{ ""Id"": ""fireball"", ""damage"": 200 }]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "ModA" });
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "GAS/effects.json", ConfigMergePolicy.ArrayById, "Id");
            var merged = pipeline.MergeArrayByIdFromCatalog(in entry);

            That(merged.Count, Is.EqualTo(1), "Case-insensitive: should merge as one entry");
            That(merged[0].Node["damage"]?.GetValue<int>(), Is.EqualTo(200));
        }
    }
}
