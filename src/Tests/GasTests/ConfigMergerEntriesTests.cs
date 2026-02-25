using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Unit tests for ConfigMerger.MergeArrayByIdToEntries — the core merge primitive.
    /// </summary>
    [TestFixture]
    public class ConfigMergerEntriesTests
    {
        private static ConfigFragment MakeFrag(string json, string source = "test://test")
        {
            return new ConfigFragment(JsonNode.Parse(json), source);
        }

        [Test]
        public void MergeArrayByIdToEntries_SingleFragment_ReturnsOrderedEntries()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""B"",""v"":2}, {""Id"":""A"",""v"":1}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(2));
            That(result[0].Id, Is.EqualTo("B"), "Insertion order preserved");
            That(result[1].Id, Is.EqualTo("A"));
            That(result[0].Node["v"]?.GetValue<int>(), Is.EqualTo(2));
            That(result[1].Node["v"]?.GetValue<int>(), Is.EqualTo(1));
        }

        [Test]
        public void MergeArrayByIdToEntries_TwoFragments_MergesById()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""A"",""x"":1,""y"":2}]", "Core:test.json"),
                MakeFrag(@"[{""Id"":""A"",""x"":10}]", "ModA:test.json")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(1));
            That(result[0].Node["x"]?.GetValue<int>(), Is.EqualTo(10), "Overridden by second fragment");
            That(result[0].Node["y"]?.GetValue<int>(), Is.EqualTo(2), "Original field preserved");
        }

        [Test]
        public void MergeArrayByIdToEntries_DeleteViaDeleteField()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""A"",""v"":1}, {""Id"":""B"",""v"":2}]"),
                MakeFrag(@"[{""Id"":""A"",""__delete"":true}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(1));
            That(result[0].Id, Is.EqualTo("B"), "Only B remains after A is deleted");
        }

        [Test]
        public void MergeArrayByIdToEntries_DeleteViaDisabled()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""A"",""v"":1}, {""Id"":""B"",""v"":2}]"),
                MakeFrag(@"[{""Id"":""B"",""Disabled"":true}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(1));
            That(result[0].Id, Is.EqualTo("A"), "Only A remains after B is disabled");
        }

        [Test]
        public void MergeArrayByIdToEntries_WithReport_RecordsAll()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""A"",""v"":1}]", "Core:test.json"),
                MakeFrag(@"[{""Id"":""A"",""v"":2}, {""Id"":""B"",""v"":3}]", "ModA:test.json")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var report = new ConfigConflictReport();
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry, report);

            That(result.Count, Is.EqualTo(2));
            var fragments = report.GetFragments("test.json");
            That(fragments, Is.Not.Null);
            That(fragments.Count, Is.EqualTo(2));
        }

        [Test]
        public void MergeArrayByIdToEntries_WithReport_RecordsDeletions()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""A"",""v"":1}]", "Core:test.json"),
                MakeFrag(@"[{""Id"":""A"",""__delete"":true}]", "ModA:test.json")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var report = new ConfigConflictReport();
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry, report);

            That(result.Count, Is.EqualTo(0));
            var deletions = report.GetDeletions("test.json");
            That(deletions, Is.Not.Null);
            That(deletions.Count, Is.EqualTo(1));
        }

        [Test]
        public void MergeArrayByIdToEntries_EmptyFragments_ReturnsEmpty()
        {
            var frags = new List<ConfigFragment>();
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void MergeArrayByIdToEntries_CaseInsensitiveIds()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""Fireball"",""v"":1}]"),
                MakeFrag(@"[{""Id"":""fireball"",""v"":2}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(1), "Case-insensitive merge");
            That(result[0].Node["v"]?.GetValue<int>(), Is.EqualTo(2));
        }

        [Test]
        public void MergeArrayByIdToEntries_NonObjectNodes_Skipped()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""Id"":""A"",""v"":1}, 42, ""str"", null]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(1));
            That(result[0].Id, Is.EqualTo("A"));
        }

        [Test]
        public void MergeArrayByIdToEntries_NonArrayFragment_Skipped()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"{""Id"":""A"",""v"":1}")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "Id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(0), "Non-array fragments are skipped");
        }
    }
}
