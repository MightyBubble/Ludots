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
