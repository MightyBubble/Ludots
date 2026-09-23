using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.MapTriggers;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Data model / parser contracts for the mod-domain TriggerGraph mount family
    /// (GAS/map_trigger_mounts.json). Covers field parsing, type strictness, and the
    /// boundary between the map-config mount family and the mod mount family.
    /// </summary>
    [TestFixture]
    public sealed class TriggerGraphMountModDomainTests
    {
        private const string GraphName = "Graph.TriggerGraph.ModProbe";
        private const string MapId = "mod_domain_probe_map";

        [Test]
        public void ParseObject_ModDomainWithAllArbitrationFields_Parses()
        {
            TriggerGraphMount mount = Parse(
                $$"""
                {
                  "graph": "{{GraphName}}",
                  "id": "raid-wave",
                  "priority": 7,
                  "enabled": false,
                  "replaces": "base.reward",
                  "map": "{{MapId}}",
                  "domain": "mod"
                }
                """);

            That(mount.Graph, Is.EqualTo(GraphName));
            That(mount.Domain, Is.EqualTo(TriggerGraphMountDomain.Mod));
            That(mount.Id, Is.EqualTo("raid-wave"));
            That(mount.Priority, Is.EqualTo(7));
            That(mount.Enabled, Is.False);
            That(mount.Replaces, Is.EqualTo("base.reward"));
            That(mount.MapFilter, Is.EqualTo(MapId));
            That(mount.ScopeInstanceId, Is.Null);
        }

        [Test]
        public void ParseObject_DefaultDomainIsMap_AndArbitrationDefaults()
        {
            TriggerGraphMount mount = Parse(
                $$"""{ "graph": "{{GraphName}}", "id": "plain" }""");

            That(mount.Domain, Is.EqualTo(TriggerGraphMountDomain.Map));
            That(mount.Priority, Is.EqualTo(0));
            That(mount.Enabled, Is.True);
            That(mount.Replaces, Is.EqualTo(string.Empty));
            That(mount.MapFilter, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ParseObject_UnknownField_Rejected()
        {
            var obj = JsonNode.Parse(
                $$"""{ "graph": "{{GraphName}}", "id": "x", "bogus": 1 }""")!.AsObject();

            var ex = Throws<InvalidOperationException>(() => TriggerGraphMount.ParseObject(obj, "ctx"));
            That(ex!.Message, Does.Contain("bogus"));
        }

        [Test]
        public void ParseObject_PriorityNonInteger_Rejected()
        {
            var obj = JsonNode.Parse(
                $$"""{ "graph": "{{GraphName}}", "id": "x", "priority": "high" }""")!.AsObject();

            var ex = Throws<InvalidOperationException>(() => TriggerGraphMount.ParseObject(obj, "ctx"));
            That(ex!.Message, Does.Contain("priority"));
            That(ex.Message, Does.Contain("integer"));
        }

        [Test]
        public void ParseObject_EnabledNonBoolean_Rejected()
        {
            var obj = JsonNode.Parse(
                $$"""{ "graph": "{{GraphName}}", "id": "x", "enabled": "yes" }""")!.AsObject();

            var ex = Throws<InvalidOperationException>(() => TriggerGraphMount.ParseObject(obj, "ctx"));
            That(ex!.Message, Does.Contain("enabled"));
            That(ex.Message, Does.Contain("boolean"));
        }

        [Test]
        public void ParseObject_AbilityDomainStillRejected()
        {
            var obj = JsonNode.Parse(
                $$"""{ "graph": "{{GraphName}}", "domain": "ability" }""")!.AsObject();

            var ex = Throws<InvalidOperationException>(() => TriggerGraphMount.ParseObject(obj, "ctx"));
            That(ex!.Message, Does.Contain("ability"));
        }

        [Test]
        public void ParseList_ModDomainMount_RejectedInMapConfigFamily()
        {
            var node = JsonNode.Parse(
                $$"""[ { "graph": "{{GraphName}}", "domain": "mod", "id": "x" } ]""")!;

            var ex = Throws<InvalidOperationException>(() => TriggerGraphMount.ParseList(node, MapId));
            That(ex!.Message, Does.Contain(MapId));
            That(ex.Message, Does.Contain("mod"));
        }

        [Test]
        public void ParseList_ModFamilyOnlyField_RejectedInMapConfigFamily()
        {
            // 'map' is a mod-family-only field; map-config mounts must not carry it.
            var node = JsonNode.Parse(
                $$"""[ { "graph": "{{GraphName}}", "map": "{{MapId}}" } ]""")!;

            var ex = Throws<InvalidOperationException>(() => TriggerGraphMount.ParseList(node, MapId));
            That(ex!.Message, Does.Contain(MapId));
            That(ex.Message, Does.Contain("map"));
        }

        [Test]
        public void ParseList_RejectsEveryModFamilyOnlyField()
        {
            string[] modFields = { "id", "priority", "enabled", "replaces", "map" };
            foreach (string field in modFields)
            {
                var node = JsonNode.Parse(
                    $$"""[ { "graph": "{{GraphName}}", "{{field}}": 1 } ]""")!;

                var ex = Throws<InvalidOperationException>(() => TriggerGraphMount.ParseList(node, MapId));
                That(ex!.Message, Does.Contain(field), $"map-config family must reject field '{field}'.");
            }
        }

        private static TriggerGraphMount Parse(string json)
        {
            return TriggerGraphMount.ParseObject(JsonNode.Parse(json)!.AsObject(), "ctx");
        }
    }
}
