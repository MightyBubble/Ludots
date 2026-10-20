using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sango.Content;

namespace Sango.Tests
{
    [TestFixture]
    public sealed class SangoContentCatalogTests
    {
        [Test]
        public void Catalog_DataTables_ContainsAtLeastTwentyTables()
        {
            IReadOnlyDictionary<string, int> rowsByTable = SangoContentCatalog.CountRows(ModRoot());

            int dataTables = 0;
            foreach (KeyValuePair<string, int> table in rowsByTable)
            {
                if (table.Key.StartsWith("Data/", StringComparison.Ordinal))
                    dataTables++;
            }

            Assert.That(dataTables, Is.GreaterThanOrEqualTo(20), $"Data tables found: {dataTables}");
        }

        [Test]
        public void Catalog_CoreTables_HaveRows()
        {
            IReadOnlyDictionary<string, int> rowsByTable = SangoContentCatalog.CountRows(ModRoot());
            string[] coreTables =
            {
                "Data/Common/TroopTypes.json",
                "Data/Common/Skills.json",
                "Data/Common/Features.json",
                "Data/Common/Techniques.json",
                "Data/Common/ai.json",
            };

            foreach (string table in coreTables)
            {
                Assert.That(rowsByTable.ContainsKey(table), Is.True, $"Missing table: {table}");
                Assert.That(rowsByTable[table], Is.GreaterThan(0), $"{table} should have rows");
            }
        }

        [Test]
        public void Catalog_ScenarioAsset_ExistsAndIsNonEmpty()
        {
            IReadOnlyDictionary<string, int> rowsByTable = SangoContentCatalog.CountRows(ModRoot());

            Assert.That(rowsByTable.ContainsKey("Scenario/Scenario.json"), Is.True, "Scenario.json should be cataloged");
            Assert.That(rowsByTable["Scenario/Scenario.json"], Is.GreaterThan(0), "Scenario.json should contain scenario sets");
        }

        [Test]
        public void Catalog_RootIdKeyedTable_CountsRowsNotMaxFieldCount()
        {
            // T0.5:根即 id 键表(原 ModelConfig 形状,行对象带 Id)的行数 = 根字段数,
            // 不是最大行属性数(旧逻辑报 4 实为 22 一类)。
            string modRoot = Path.Combine(Path.GetTempPath(), "sango-t05-" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(Path.Combine(modRoot, "assets", "Data"));
                var rows = new System.Text.StringBuilder();
                rows.AppendLine("{");
                for (int id = 0; id < 22; id++)
                {
                    rows.Append("  \"").Append(id).Append("\": {\"Id\": ").Append(id)
                        .Append(", \"Name\": \"n\", \"prefab\": \"p\", \"bind\": 0}");
                    rows.AppendLine(id < 21 ? "," : "");
                }

                rows.AppendLine("}");
                File.WriteAllText(Path.Combine(modRoot, "assets", "Data", "IdKeyed.json"), rows.ToString());

                IReadOnlyDictionary<string, int> rowsByTable = SangoContentCatalog.CountRows(modRoot);
                Assert.That(rowsByTable["Data/IdKeyed.json"], Is.EqualTo(22),
                    "root id-keyed table must count its rows, not the max row field count");
            }
            finally
            {
                if (Directory.Exists(modRoot)) Directory.Delete(modRoot, recursive: true);
            }
        }

        [Test]
        public void Catalog_WrapperTables_KeepLargestCollectionSemantics()
        {
            // 既有形状回归:wrapper 键下的 id 键表(如 TerrainTypes)仍按内层属性数计行,
            // 不被根即 id 键表的新分支误伤。
            IReadOnlyDictionary<string, int> rowsByTable = SangoContentCatalog.CountRows(ModRoot());
            Assert.That(rowsByTable["Data/Common/TerrainTypes.json"], Is.EqualTo(32),
                "TerrainTypes wrapper table keeps its nested id-keyed row count (ids 0..31; 32+ is out-of-table open sea)");
        }

        private static string ModRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                    return Path.Combine(dir, "mods", "sango", "SangoContentMod");
                dir = Path.GetDirectoryName(dir);
            }
            throw new DirectoryNotFoundException("Could not locate repo root.");
        }
    }
}
