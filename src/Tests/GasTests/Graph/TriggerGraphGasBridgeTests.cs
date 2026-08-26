using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// GAS → TriggerManager bridges (#1031 D4/D5): post-swap visibility, payload contract,
    /// map routing, drop counters, moment mirroring without stealing presentation events,
    /// and the presentation-side Fire guard (#1031 D6).
    /// </summary>
    [NonParallelizable]
    public sealed class TriggerGraphGasBridgeTests
    {
        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void GasEventBridge_FiresOnlyAfterBufferSwap_WithPayloadContract()
        {
            int hitTag = TagRegistry.Register("Combat.Hit");
            using World world = World.Create();
            Entity target = world.Create();
            var mapId = new MapId("bridge_map");
            world.Add(target, new MapEntity { MapId = mapId });

            var bus = new GameplayEventBus();
            var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
            var bridge = new GasEventTriggerBridgeSystem(bus, manager, world, () => new ScriptContext());

            GameplayEvent? seen = null;
            MapId seenMap = default;
            manager.RegisterEventHandler(new EventKey("Gas.Event.Combat.Hit"), ctx =>
            {
                seen = new GameplayEvent
                {
                    TagId = ctx.Get<int>(MapTriggerEventPayloadKeys.TagId),
                    Source = ctx.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity),
                    Target = ctx.Get<Entity>(MapTriggerEventPayloadKeys.TargetEntity),
                    Magnitude = ctx.Get<float>(MapTriggerEventPayloadKeys.Magnitude),
                };
                seenMap = ctx.Get<MapId>(ContextKeys.MapId);
                return Task.CompletedTask;
            });

            bus.Publish(new GameplayEvent { TagId = hitTag, Source = Entity.Null, Target = target, Magnitude = 12.5f });

            bridge.Update(default);
            Assert.That(seen, Is.Null, "Bridge must not observe the event before the EventDispatch swap.");

            new GameplayEventDispatchSystem(bus).Update(default);
            bridge.Update(default);

            Assert.That(seen, Is.Not.Null);
            Assert.That(seen!.Value.TagId, Is.EqualTo(hitTag));
            Assert.That(seen.Value.Target, Is.EqualTo(target));
            Assert.That(seen.Value.Magnitude, Is.EqualTo(12.5f));
            Assert.That(seenMap.Value, Is.EqualTo(mapId.Value));
        }

        [Test]
        public void GasEventBridge_FallsBackToSourceMap_ThenDropsAndCounts()
        {
            int tag = TagRegistry.Register("Combat.Fallback");
            using World world = World.Create();
            Entity source = world.Create();
            var mapId = new MapId("source_map");
            world.Add(source, new MapEntity { MapId = mapId });

            var bus = new GameplayEventBus();
            var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
            var bridge = new GasEventTriggerBridgeSystem(bus, manager, world, () => new ScriptContext());

            var fired = new List<string>();
            manager.RegisterEventHandler(new EventKey("Gas.Event.Combat.Fallback"), _ =>
            {
                fired.Add("fallback");
                return Task.CompletedTask;
            });

            bus.Publish(new GameplayEvent { TagId = tag, Source = source, Target = Entity.Null });
            bus.Publish(new GameplayEvent { TagId = tag, Source = Entity.Null, Target = Entity.Null });
            new GameplayEventDispatchSystem(bus).Update(default);
            bridge.Update(default);

            Assert.That(fired.Count, Is.EqualTo(1), "Source-map fallback routes exactly the routable event.");
            Assert.That(bridge.DroppedNoMapEvents, Is.EqualTo(1));
        }

        [Test]
        public void GasEventBridge_UnknownTag_SkippedAndCounted()
        {
            using World world = World.Create();
            var bus = new GameplayEventBus();
            var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
            var bridge = new GasEventTriggerBridgeSystem(bus, manager, world, () => new ScriptContext());

            bus.Publish(new GameplayEvent { TagId = 4127, Source = Entity.Null, Target = Entity.Null });
            new GameplayEventDispatchSystem(bus).Update(default);
            bridge.Update(default);

            Assert.That(bridge.DroppedUnknownTagEvents, Is.EqualTo(1));
        }

        [Test]
        public void MomentBridge_MirrorsAbilityMoments_WithoutConsumingBuffer()
        {
            using World world = World.Create();
            Entity caster = world.Create();
            var mapId = new MapId("moment_map");
            world.Add(caster, new MapEntity { MapId = mapId });

            var buffer = new GasPresentationEventBuffer(8);
            var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
            var bridge = new TriggerGraphMomentBridgeSystem(buffer, manager, world, () => new ScriptContext());

            GasPresentationEvent? seen = null;
            manager.RegisterEventHandler(new EventKey("Ability.CastStarted"), ctx =>
            {
                seen = new GasPresentationEvent
                {
                    AbilityId = ctx.Get<int>(MapTriggerEventPayloadKeys.AbilityId),
                    Actor = ctx.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity),
                };
                return Task.CompletedTask;
            });

            buffer.Publish(new GasPresentationEvent { Kind = GasPresentationEventKind.CastStarted, Actor = caster, AbilityId = 77 });
            bridge.Update(default);

            Assert.That(seen, Is.Not.Null);
            Assert.That(seen!.Value.AbilityId, Is.EqualTo(77));
            Assert.That(seen.Value.Actor, Is.EqualTo(caster));
            Assert.That(buffer.Count, Is.EqualTo(1), "The bridge is read-only; the presentation projection still owns consumption.");
        }

        [Test]
        public void MomentBridge_EventNameTable_CoversEveryMoment()
        {
            foreach (GasPresentationEventKind kind in Enum.GetValues<GasPresentationEventKind>())
            {
                Assert.That(
                    TriggerGraphMomentBridgeSystem.EventNameFor(kind),
                    Is.Not.Null,
                    $"Every GasPresentationEventKind must map to a trigger event name ({kind}).");
            }
        }

        [Test]
        public void PresentationSources_NeverFireTriggerManagerEvents()
        {
            string repoRoot = FindRepoRoot();
            string[] forbiddenRoots =
            {
                Path.Combine(repoRoot, "src", "Core", "Presentation"),
                Path.Combine(repoRoot, "src", "Client"),
                Path.Combine(repoRoot, "src", "Adapters"),
                Path.Combine(repoRoot, "src", "Platforms"),
            };
            var firePattern = new Regex(@"\.(FireEvent|FireMapEvent|FireEventAsync|FireMapEventAsync)\s*\(");

            var offenders = new List<string>();
            foreach (string root in forbiddenRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    {
                        continue;
                    }

                    foreach (string line in File.ReadAllLines(file))
                    {
                        if (firePattern.IsMatch(line))
                        {
                            offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: {line.Trim()}");
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "TriggerManager fires are simulation-side only (#1031 D6); presentation sources must not fire events:\n" +
                string.Join("\n", offenders));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                    File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent!;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root.");
        }
    }
}
