using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.MapTriggers;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Mod-contributed TriggerGraph mount table: deterministic arbitration contract and
    /// the explicit Replace override, failing closed on any ambiguous or dangling case.
    /// Mod mounts are authored with domain "mod" (applies to all maps) or domain "map"
    /// with a target map filter; map-owned mounts are passed in per resolve.
    /// </summary>
    [TestFixture]
    public sealed class TriggerGraphMountTableTests
    {
        private const string MapId = "table_probe_map";
        private const string BaseGraph = "Graph.Base.Reward";

        [Test]
        public void ResolveForMap_NoMapOwnedMounts_ReturnsModMountsInArbitrationOrder()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            table.AddMounts("ModA", new[]
            {
                ModMount("first", GraphName(2), priority: 2),
                ModMount("second", GraphName(1), priority: 1),
            });

            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, null);

            That(resolved.Count, Is.EqualTo(2));
            That(resolved[0].Mount.Id, Is.EqualTo("second"), "Lower priority must execute first.");
            That(resolved[1].Mount.Id, Is.EqualTo("first"));
            That(resolved[0].OwnerModId, Is.EqualTo("ModA"));
            That(resolved[0].ModRank, Is.EqualTo(1));
            That(resolved[0].DeclarationIndex, Is.EqualTo(1));
        }

        [Test]
        public void ResolveForMap_ModDomainWithoutMapFilter_AppliesToAllMaps()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            table.AddMounts("ModA", new[] { ModMount("global", BaseGraph) });

            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, null);

            That(resolved.Count, Is.EqualTo(1));
            That(resolved[0].Mount.Id, Is.EqualTo("global"));
            That(resolved[0].Mount.Domain, Is.EqualTo(TriggerGraphMountDomain.Mod));
        }

        [Test]
        public void ResolveForMap_MapDomainMount_OnlyAppliesToTargetedMap()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            table.AddMounts("ModA", new[]
            {
                Mount($$"""{ "graph": "{{BaseGraph}}", "id": "local", "domain": "map", "map": "{{MapId}}" }"""),
            });

            IReadOnlyList<ResolvedTriggerGraphMount> onTarget = table.ResolveForMap(MapId, null);
            That(onTarget.Count, Is.EqualTo(1));
            That(onTarget[0].Mount.Id, Is.EqualTo("local"));

            IReadOnlyList<ResolvedTriggerGraphMount> offTarget = table.ResolveForMap("other_map", null);
            That(offTarget.Count, Is.EqualTo(0), "Map-domain mount must not apply to another map.");
        }

        [Test]
        public void ResolveForMap_DisabledMount_DroppedAndCannotReplace()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            var baseMount = Mount($$"""{ "graph": "{{BaseGraph}}", "id": "base" }""");
            table.AddMounts("ModA", new[]
            {
                Mount($$"""{ "graph": "{{BaseGraph}}r", "id": "disabled", "domain": "mod", "enabled": false, "replaces": "{{MapId}}.base" }"""),
            });

            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, new[] { baseMount });
            That(resolved.Count, Is.EqualTo(1));
            That(resolved[0].Mount.Id, Is.EqualTo("base"), "Disabled mount must be dropped and the base kept.");
        }

        [Test]
        public void ResolveForMap_SingleReplace_DropsBaseAndKeepsReplacer()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            var baseMount = Mount($$"""{ "graph": "{{BaseGraph}}", "id": "base" }""");
            table.AddMounts("ModA", new[]
            {
                Mount($$"""{ "graph": "{{BaseGraph}}r", "id": "replacer", "domain": "mod", "replaces": "{{MapId}}.base" }"""),
            });

            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, new[] { baseMount });

            That(resolved.Count, Is.EqualTo(1));
            That(resolved[0].Mount.Id, Is.EqualTo("replacer"), "Base must be dropped by an explicit replaces.");
            That(resolved[0].Mount.Graph, Is.EqualTo($"{BaseGraph}r"));
        }

        [Test]
        public void ResolveForMap_ReplaceDanglingTarget_FailsClosed()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            table.AddMounts("ModA", new[]
            {
                Mount($$"""{ "graph": "{{BaseGraph}}", "id": "replacer", "domain": "mod", "replaces": "{{MapId}}.nope" }"""),
            });

            var ex = Throws<InvalidOperationException>(() => table.ResolveForMap(MapId, null));
            That(ex!.Message, Does.Contain($"{MapId}.nope"));
        }

        [Test]
        public void ResolveForMap_SelfReplace_FailsClosed()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            table.AddMounts("ModA", new[]
            {
                Mount($$"""{ "graph": "{{BaseGraph}}", "id": "self", "domain": "mod", "replaces": "ModA.self" }"""),
            });

            var ex = Throws<InvalidOperationException>(() => table.ResolveForMap(MapId, null));
            That(ex!.Message, Does.Contain("cannot replace itself"));
        }

        [Test]
        public void ResolveForMap_DuplicateMountKey_FailsClosed()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA" }, EmptyClosure());
            table.AddMounts("ModA", new[]
            {
                ModMount("dup", BaseGraph),
                ModMount("dup", GraphName(2)),
            });

            var ex = Throws<InvalidOperationException>(() => table.ResolveForMap(MapId, null));
            That(ex!.Message, Does.Contain("ModA.dup"));
        }

        [Test]
        public void ResolveForMap_SameGraphTwiceWithoutReplace_FailsClosed()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA", "ModB" }, EmptyClosure());
            table.AddMounts("ModA", new[] { ModMount("a", BaseGraph) });
            table.AddMounts("ModB", new[] { ModMount("b", BaseGraph) });

            var ex = Throws<InvalidOperationException>(() => table.ResolveForMap(MapId, null));
            That(ex!.Message, Does.Contain(BaseGraph));
            That(ex.Message, Does.Contain("replaces"));
        }

        [Test]
        public void ResolveForMap_TwoIndependentReplacers_FailsClosedAsDiamond()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(new[] { "ModA", "ModB" }, EmptyClosure());
            var baseMount = Mount($$"""{ "graph": "{{BaseGraph}}", "id": "base" }""");
            table.AddMounts("ModA", new[] { ReplaceMount("ra", $"{BaseGraph}A") });
            table.AddMounts("ModB", new[] { ReplaceMount("rb", $"{BaseGraph}B") });

            var ex = Throws<InvalidOperationException>(() => table.ResolveForMap(MapId, new[] { baseMount }));
            That(ex!.Message, Does.Contain("both replace"));
            That(ex.Message, Does.Contain($"{MapId}.base"));
        }

        [Test]
        public void ResolveForMap_DownstreamReplacerWins_BaseDropped()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(
                new[] { "ModA", "ModB" },
                Closure("ModB", "ModA")); // ModB depends on ModA → ModB is downstream.
            var baseMount = Mount($$"""{ "graph": "{{BaseGraph}}", "id": "base" }""");
            table.AddMounts("ModA", new[] { ReplaceMount("ra", $"{BaseGraph}A") });
            table.AddMounts("ModB", new[] { ReplaceMount("rb", $"{BaseGraph}B") });

            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, new[] { baseMount });

            That(resolved.Count, Is.EqualTo(1));
            That(resolved[0].Mount.Id, Is.EqualTo("rb"), "Downstream mod's replace must win and base must be dropped.");
        }

        [Test]
        public void ResolveForMap_UpstreamAfterDownstreamReplace_StillResolvesToDownstream()
        {
            var table = new TriggerGraphMountTable();
            table.SetModOrder(
                new[] { "ModA", "ModB" },
                Closure("ModB", "ModA"));
            var baseMount = Mount($$"""{ "graph": "{{BaseGraph}}", "id": "base" }""");
            table.AddMounts("ModB", new[] { ReplaceMount("rb", $"{BaseGraph}B") });
            table.AddMounts("ModA", new[] { ReplaceMount("ra", $"{BaseGraph}A") });

            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, new[] { baseMount });

            That(resolved.Count, Is.EqualTo(1));
            That(resolved[0].Mount.Id, Is.EqualTo("rb"), "Dependency order, not registration order, must decide the winner.");
        }

        private static TriggerGraphMount ModMount(string id, string graph, int priority = 0)
            => Mount($$"""{ "graph": "{{graph}}", "id": "{{id}}", "domain": "mod", "priority": {{priority}} }""");

        private static TriggerGraphMount ReplaceMount(string id, string graph)
            => Mount($$"""{ "graph": "{{graph}}", "id": "{{id}}", "domain": "mod", "replaces": "{{MapId}}.base" }""");

        private static TriggerGraphMount Mount(string json)
            => TriggerGraphMount.ParseObject(JsonNode.Parse(json)!.AsObject(), "ctx");

        private static string GraphName(int suffix)
            => $"Graph.Base.Reward{suffix}";

        private static IReadOnlyDictionary<string, IReadOnlySet<string>> EmptyClosure()
            => new Dictionary<string, IReadOnlySet<string>>();

        private static IReadOnlyDictionary<string, IReadOnlySet<string>> Closure(string modId, params string[] upstream)
            => new Dictionary<string, IReadOnlySet<string>>
            {
                [modId] = new HashSet<string>(upstream),
            };
    }
}
