using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// T1.1(#1842/#1847):内容表装载走官方 config_catalog(SangoContentMod 逐表
    /// DeepObject、SangoSimMod GAS 三表 ArrayById)。验收面:①catalog 声明与
    /// Data/Common 实际文件零漂移(新表未声明即红);②官方 fail-fast 语义——未声明
    /// 路径 RequireEntry 抛、声明了不存在文件的条目装载抛(平台行为文档化);
    /// ③GAS 三表与内容表同在共享 catalog(战斗运行时不再私搭管线)。
    /// </summary>
    [TestFixture]
    public sealed class SangoConfigCatalogTests
    {
        private static string RepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate Ludots repo root (showcase.registry.json).");
        }

        [Test]
        public void ContentCatalog_DeclaresExactlyTheTableFiles_NoDrift()
        {
            // T1.2 起内容面三个目录都进 catalog(Common 表、外交事件表、剧本事件表):
            // 逐目录断言"声明 = 实际文件",新表未声明即红。
            string assetsRoot = Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod", "assets");
            string[] tableDirs = { "Data/Common", "Data/DiplomacyEvent", "Data/ScenarioEvent" };

            var declared = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.Json.Nodes.JsonNode? node in
                     System.Text.Json.Nodes.JsonNode.Parse(
                         File.ReadAllText(Path.Combine(assetsRoot, "config_catalog.json")))!.AsArray())
            {
                Assert.That((string)node!["Policy"]!, Is.EqualTo("DeepObject"),
                    $"table {(string)node!["Path"]!} must declare DeepObject (single-root table shape)");
                declared.Add((string)node!["Path"]!);
            }

            var onDisk = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string dir in tableDirs)
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(assetsRoot, dir), "*.json", SearchOption.TopDirectoryOnly))
                {
                    onDisk.Add(dir + "/" + Path.GetFileName(file));
                }
            }

            Assert.That(declared, Is.EqualTo(onDisk),
                "catalog declarations and table files must stay in lockstep (new tables must be declared)");
        }

        [Test]
        public void ContentCatalog_UndeclaredOrMissingEntries_FailFastViaOfficialPipeline()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "sango-t11-" + Path.GetRandomFileName());
            try
            {
                // mod 布局:catalog 与表都在 <modRoot>/assets/ 下(官方 ModAssets uri 约定)。
                Directory.CreateDirectory(Path.Combine(tempRoot, "assets", "Tables"));
                File.WriteAllText(Path.Combine(tempRoot, "assets", "config_catalog.json"),
                    "[{ \"Path\": \"Tables/exists.json\", \"Policy\": \"DeepObject\" }," +
                    " { \"Path\": \"Tables/ghost.json\", \"Policy\": \"DeepObject\" }]");
                File.WriteAllText(Path.Combine(tempRoot, "assets", "Tables", "exists.json"), "{\"a\":1}");

                var vfs = new VirtualFileSystem();
                vfs.Mount("ProbeMod", tempRoot);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                modLoader.LoadedModIds.Add("ProbeMod");
                var pipeline = new ConfigPipeline(vfs, modLoader);
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);

                // 官方语义:声明在案的条目可 RequireEntry 取用。
                ConfigCatalogEntry exists =
                    ConfigPipeline.RequireEntry(catalog, "Tables/exists.json", ConfigMergePolicy.DeepObject);
                Assert.That(pipeline.MergeDeepObjectFromCatalog(in exists)["a"]!.GetValue<int>(), Is.EqualTo(1));

                // 官方语义:catalog 未声明的路径即抛(不存在静默兜底)。
                Assert.That(() => ConfigPipeline.RequireEntry(catalog, "Tables/undeclared.json", ConfigMergePolicy.DeepObject),
                    Throws.InvalidOperationException);

                // 官方语义:声明了但文件缺失的 DeepObject 条目合并返回 null(缺源=空,平台
                // 不抛);把 null 当不可接受是消费方的决定——GameData.LoadCommonTablesInto
                // 对内容表 null 即 fail-fast。
                ConfigCatalogEntry ghost =
                    ConfigPipeline.RequireEntry(catalog, "Tables/ghost.json", ConfigMergePolicy.DeepObject);
                Assert.That(pipeline.MergeDeepObjectFromCatalog(in ghost), Is.Null);
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void SharedCatalog_CarriesGasThreeTablesAndCommonTables()
        {
            System.Reflection.Assembly sim = LoadSangoSimMod();
            BootKernel(sim);
            object catalog = sim.GetType("Sango.Runtime.SangoConfigRuntime", throwOnError: true)!
                .GetMethod("Catalog", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object?[] { null, null })!;

            // ConfigCatalog/ConfigCatalogEntry 是 Ludots.Core 类型,sim 程序集与测试共享同一份。
            var paths = new SortedSet<string>(
                ((IEnumerable<ConfigCatalogEntry>)catalog.GetType()
                    .GetProperty("Entries")!.GetValue(catalog)!)
                .Select(entry => entry.RelativePath),
                StringComparer.Ordinal);

            Assert.That(paths, Is.SupersetOf(new[]
            {
                "GAS/preset_types.json",
                "GAS/effects.json",
                "GAS/abilities.json",
                "Data/Common/TerrainTypes.json",
                "Data/Common/ai.json",
            }), "shared catalog must carry both mods' declarations (no private pipeline)");
        }

        private static System.Reflection.Assembly LoadSangoSimMod()
        {
            string dll = Path.Combine(
                RepoRoot(), "mods", "sango", "SangoSimMod", "bin", "net9.0", "SangoSimMod.dll");
            Assert.That(File.Exists(dll), Is.True, $"SangoSimMod build output missing: {dll} (run dotnet build first)");
            return System.Reflection.Assembly.LoadFrom(dll);
        }

        private static void BootKernel(System.Reflection.Assembly sim)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
            sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                .GetMethod("Boot", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { vfs, "SangoContentMod", 20260902, "Scenario/Scenario.json" });
        }
    }
}
