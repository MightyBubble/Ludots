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
                MakeFrag(@"[{""id"":""B"",""v"":2}, {""id"":""A"",""v"":1}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
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
                MakeFrag(@"[{""id"":""A"",""x"":1,""y"":2}]", "Core:test.json"),
                MakeFrag(@"[{""id"":""A"",""x"":10}]", "ModA:test.json")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
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
                MakeFrag(@"[{""id"":""A"",""v"":1}, {""id"":""B"",""v"":2}]"),
                MakeFrag(@"[{""id"":""A"",""__delete"":true}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(1));
            That(result[0].Id, Is.EqualTo("B"), "Only B remains after A is deleted");
        }

        [Test]
        public void MergeArrayByIdToEntries_DeleteViaDisabled()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""id"":""A"",""v"":1}, {""id"":""B"",""v"":2}]"),
                MakeFrag(@"[{""id"":""B"",""Disabled"":true}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(1));
            That(result[0].Id, Is.EqualTo("A"), "Only A remains after B is disabled");
        }

        [Test]
        public void MergeArrayByIdToEntries_WithReport_RecordsAll()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""id"":""A"",""v"":1}]", "Core:test.json"),
                MakeFrag(@"[{""id"":""A"",""v"":2}, {""id"":""B"",""v"":3}]", "ModA:test.json")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
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
                MakeFrag(@"[{""id"":""A"",""v"":1}]", "Core:test.json"),
                MakeFrag(@"[{""id"":""A"",""__delete"":true}]", "ModA:test.json")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
            var report = new ConfigConflictReport();
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry, report);

            That(result.Count, Is.EqualTo(0));
            var deletions = report.GetDeletions("test.json");
            That(deletions, Is.Not.Null);
            That(deletions.Count, Is.EqualTo(1));
        }

        [Test]
        public void MergeArrayByIdToEntries_MissingId_IsRejectedWithSource()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""id"":""A"",""v"":1}, {""v"":2}]", "ModA:test.json")
            };
            var entry = new ConfigCatalogEntry("GAS/abilities.json", ConfigMergePolicy.ArrayById, "id");

            var ex = Throws<InvalidOperationException>(() => ConfigMerger.MergeArrayByIdToEntries(frags, in entry));

            That(ex!.Message, Does.Contain("GAS/abilities.json"));
            That(ex.Message, Does.Contain("ModA:test.json"));
            That(ex.Message, Does.Contain("id"));
        }

        [Test]
        public void MergeArrayByIdToEntries_EmptyFragments_ReturnsEmpty()
        {
            var frags = new List<ConfigFragment>();
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void MergeArrayByIdToEntries_CaseExactIds()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""id"":""Fireball"",""v"":1}]"),
                MakeFrag(@"[{""id"":""fireball"",""v"":2}]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");
            var result = ConfigMerger.MergeArrayByIdToEntries(frags, in entry);

            That(result.Count, Is.EqualTo(2), "ArrayById ids are case-exact and must not create aliases.");
            That(result[0].Id, Is.EqualTo("Fireball"));
            That(result[1].Id, Is.EqualTo("fireball"));
        }

        [Test]
        public void MergeArrayByIdToEntries_NonObjectNodes_AreRejected()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"[{""id"":""A"",""v"":1}, 42, ""str"", null]")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");

            var ex = Throws<InvalidOperationException>(() => ConfigMerger.MergeArrayByIdToEntries(frags, in entry));

            That(ex!.Message, Does.Contain("test.json"));
            That(ex.Message, Does.Contain("JSON object"));
        }

        [Test]
        public void MergeArrayByIdToEntries_NonArrayFragment_IsRejected()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag(@"{""id"":""A"",""v"":1}")
            };
            var entry = new ConfigCatalogEntry("test.json", ConfigMergePolicy.ArrayById, "id");

            var ex = Throws<InvalidOperationException>(() => ConfigMerger.MergeArrayByIdToEntries(frags, in entry));

            That(ex!.Message, Does.Contain("test.json"));
            That(ex.Message, Does.Contain("JSON array"));
        }

        [Test]
        public void MergeDeepObjectArrayById_MergesNamedArrayByIdAndAppendsSibling()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag("""
                    {
                      "schemes": [
                        { "id": "scheme.default", "inputContexts": [] },
                        { "id": "scheme.aim", "inputContexts": ["Aim.Kbm"], "axisMove": { "throttleTicks": 6 } }
                      ],
                      "allowedSchemes": []
                    }
                    """, "core"),
                MakeFrag("""
                    {
                      "schemes": [
                        { "id": "scheme.aim", "inputContexts": ["Aim.Override"], "axisMove": { "stepDistanceCm": 400 } },
                        { "id": "scheme.wasd", "inputContexts": [] }
                      ],
                      "allowedSchemes": ["scheme.default", "scheme.wasd"]
                    }
                    """, "mod"),
            };
            var entry = new ConfigCatalogEntry(
                "Input/control_schemes.json",
                ConfigMergePolicy.DeepObject,
                arrayAppendFields: new[] { "allowedSchemes" });

            JsonObject merged = ConfigMerger.MergeDeepObjectArrayById(frags, in entry, "schemes");

            JsonArray schemes = merged["schemes"]!.AsArray();
            That(schemes.Count, Is.EqualTo(3));
            That(schemes[0]!["id"]!.GetValue<string>(), Is.EqualTo("scheme.default"));
            That(schemes[1]!["inputContexts"]![0]!.GetValue<string>(), Is.EqualTo("Aim.Override"));
            That(schemes[1]!["axisMove"]!["throttleTicks"]!.GetValue<int>(), Is.EqualTo(6));
            That(schemes[1]!["axisMove"]!["stepDistanceCm"]!.GetValue<int>(), Is.EqualTo(400));
            That(schemes[2]!["id"]!.GetValue<string>(), Is.EqualTo("scheme.wasd"));
            JsonArray allowed = merged["allowedSchemes"]!.AsArray();
            That(allowed.Count, Is.EqualTo(2));
            That(allowed[0]!.GetValue<string>(), Is.EqualTo("scheme.default"));
            That(allowed[1]!.GetValue<string>(), Is.EqualTo("scheme.wasd"));
        }

        [Test]
        public void MergeDeepObjectArrayById_ProfileRulesReplaceWhileGroupPolicyMerges()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag("""
                    {
                      "profiles": [
                        { "id": "intent.command.default", "groupPolicy": { "kind": "independent", "kept": 1 }, "rules": [ { "priority": 10 } ] }
                      ]
                    }
                    """),
                MakeFrag("""
                    {
                      "profiles": [
                        { "id": "intent.command.default", "groupPolicy": { "kind": "ordered" }, "rules": [ { "priority": 90 }, { "priority": 20 } ] }
                      ]
                    }
                    """, "mod"),
            };
            var entry = new ConfigCatalogEntry("Input/command_intent_profiles.json", ConfigMergePolicy.DeepObject);

            JsonObject merged = ConfigMerger.MergeDeepObjectArrayById(frags, in entry, "profiles");

            JsonObject profile = merged["profiles"]![0]!.AsObject();
            That(profile["groupPolicy"]!["kind"]!.GetValue<string>(), Is.EqualTo("ordered"));
            That(profile["groupPolicy"]!["kept"]!.GetValue<int>(), Is.EqualTo(1));
            That(profile["rules"]!.AsArray().Count, Is.EqualTo(2));
            That(profile["rules"]![0]!["priority"]!.GetValue<int>(), Is.EqualTo(90));
        }

        [Test]
        public void MergeDeepObjectArrayById_MissingId_FailsClosed()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag("""{ "schemes": [ { "inputContexts": [] } ] }""", "mod://schemes"),
            };
            var entry = new ConfigCatalogEntry("Input/control_schemes.json", ConfigMergePolicy.DeepObject);

            InvalidOperationException ex = Throws<InvalidOperationException>(
                () => ConfigMerger.MergeDeepObjectArrayById(frags, in entry, "schemes"))!;

            That(ex.Message, Does.Contain("Input/control_schemes.json"));
            That(ex.Message, Does.Contain("mod://schemes"));
            That(ex.Message, Does.Contain("'id'"));
        }

        [Test]
        public void MergeDeepObjectArrayById_NullArray_FailsClosed()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag("""{ "schemes": null }""", "mod://schemes"),
            };
            var entry = new ConfigCatalogEntry("Input/control_schemes.json", ConfigMergePolicy.DeepObject);

            InvalidOperationException ex = Throws<InvalidOperationException>(
                () => ConfigMerger.MergeDeepObjectArrayById(frags, in entry, "schemes"))!;

            That(ex.Message, Does.Contain("schemes"));
            That(ex.Message, Does.Contain("JSON array"));
        }

        [Test]
        public void MergeDeepObjectArrayById_OmittedArray_LeavesTheKeyAbsent()
        {
            var frags = new List<ConfigFragment>
            {
                MakeFrag("""{ "allowedSchemes": ["scheme.default"] }"""),
            };
            var entry = new ConfigCatalogEntry(
                "Input/control_schemes.json",
                ConfigMergePolicy.DeepObject,
                arrayAppendFields: new[] { "allowedSchemes" });

            JsonObject merged = ConfigMerger.MergeDeepObjectArrayById(frags, in entry, "schemes");

            That(merged.ContainsKey("schemes"), Is.False);
            That(merged["allowedSchemes"]!.AsArray()[0]!.GetValue<string>(), Is.EqualTo("scheme.default"));
        }
    }
}
