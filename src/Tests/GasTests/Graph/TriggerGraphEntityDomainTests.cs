using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Entity-domain TriggerGraph mounts end to end on a real engine: an entity
    /// template declares "TriggerGraphs", the mounted graph reacts to its own
    /// lifecycle (EntitySpawned same tick, EntityDied on the destroy tick,
    /// MapHeartbeat with self scope), reads its own attributes, writes its
    /// map's variables, goes inert after death, and is swept and cleaned up.
    /// A second entity mount declared through map JSON (domain "entity") follows
    /// the same lifecycle contract.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphEntityDomainTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "trigger_graph_entity_domain_probe";
        private const string ProbeTemplateId = "entity_domain_probe";
        private const string WatcherTemplateId = "entity_domain_watcher";
        private const string ProbeGraphName = "Graph.EntityDomain.Probe";
        private const string WatcherGraphName = "Graph.EntityDomain.Watcher";
        private const int HeartbeatIntervalTicks = 2;

        [Test]
        public void EntitySpawnedEntry_RunsSameTick_WithSelfScope_AndOwnAttributesAndMapVarsResolve()
        {
            using EntityDomainFixture fixture = EntityDomainFixture.Create();
            using GameEngine engine = fixture.CreateEngine();
            engine.Start();
            engine.LoadMap(MapId);

            MapVariableStore variables = RequireVariables(engine);
            Assert.Multiple(() =>
            {
                Assert.That(variables.ReadInt("spawned"), Is.EqualTo(1),
                    "The EntitySpawned entry must run during map load (same tick as the spawn), not at wave granularity.");
                Assert.That(variables.ReadFloat("spawn_health"), Is.EqualTo(77f).Within(0.001f),
                    "LoadSelfAttribute must read the mounted entity's own attribute: caster = E[0] = self.");
                Assert.That(variables.ReadInt("watcher_spawned"), Is.EqualTo(1),
                    "A map-JSON entity-domain mount must also run its EntitySpawned entry at mount creation.");
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            });

            TriggerGraphMountTrigger? mount = FindMountTrigger(engine, ProbeGraphName, "on_spawn");
            Assert.That(mount, Is.Not.Null);
            Assert.That(mount!.Domain, Is.EqualTo(TriggerGraphMountDomain.Entity));
            Assert.That(mount.LastSliceResult.Halted, Is.True);
        }

        [Test]
        public void MapHeartbeatEntry_FiresWithSelfScope()
        {
            using EntityDomainFixture fixture = EntityDomainFixture.Create();
            using GameEngine engine = fixture.CreateEngine();
            engine.Start();
            engine.LoadMap(MapId);
            MapVariableStore variables = RequireVariables(engine);
            Assert.That(variables.ReadInt("wave_ran"), Is.EqualTo(0), "No wave may fire the entry before the interval elapses.");

            TickUntil(engine, () => variables.ReadInt("wave_ran") == 1, HeartbeatIntervalTicks * 4,
                () => $"MapHeartbeat entry never ran (wave_ran={variables.ReadInt("wave_ran")}).");

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        [Test]
        public void EntityDestroy_RunsOwnEntityDiedEntryOnDestroyTick_ThenMountInertAndSwept()
        {
            using EntityDomainFixture fixture = EntityDomainFixture.Create();
            using GameEngine engine = fixture.CreateEngine();
            engine.Start();
            engine.LoadMap(MapId);
            MapVariableStore variables = RequireVariables(engine);
            World world = engine.World;
            Entity probe = RequireEntity(world, "EntityDomainProbe");
            Entity watcher = RequireEntity(world, "EntityDomainWatcher");

            world.Destroy(probe);
            world.Destroy(watcher);

            Assert.Multiple(() =>
            {
                Assert.That(variables.ReadInt("died"), Is.EqualTo(1),
                    "The own EntityDied entry must run on the destroy tick, without waiting for a wave.");
                Assert.That(variables.ReadInt("watcher_died"), Is.EqualTo(1),
                    "A map-JSON entity-domain mount must run its EntityDied entry on the destroy tick.");
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            });

            int waveRuns = variables.ReadInt("wave_ran");
            Tick(engine, HeartbeatIntervalTicks * 4);
            Assert.Multiple(() =>
            {
                Assert.That(variables.ReadInt("wave_ran"), Is.EqualTo(waveRuns),
                    "A dead entity's MapHeartbeat mount must stay inert.");
                Assert.That(variables.ReadInt("died"), Is.EqualTo(1),
                    "The wave-granularity EntityDied broadcast must not re-fire the entity-domain entry.");
            });

            TickUntil(engine,
                () => engine.EntityTriggerGraphMounts.GetDeadMountCount(new MapId(MapId)) == 0,
                HeartbeatIntervalTicks * 6,
                () => "Dead entity mounts must be swept at think waves.");
        }

        [Test]
        public void MapUnload_CleansUpEntityMounts()
        {
            using EntityDomainFixture fixture = EntityDomainFixture.Create();
            using GameEngine engine = fixture.CreateEngine();
            engine.Start();
            engine.LoadMap(MapId);
            Assert.That(FindMountTrigger(engine, ProbeGraphName, "on_spawn"), Is.Not.Null);

            engine.UnloadMap(MapId);

            Assert.Multiple(() =>
            {
                Assert.That(engine.TriggerManager.Get<TriggerGraphMountTrigger>(), Is.Null,
                    "Map unload must unregister entity-domain mount triggers.");
                Assert.That(engine.EntityTriggerGraphMounts.GetDeadMountCount(new MapId(MapId)), Is.EqualTo(0));
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void LoadMap_UnknownTemplateGraphName_FailsClosedNamingTemplateAndGraph()
        {
            using EntityDomainFixture fixture = EntityDomainFixture.Create(templateGraphNames: new[] { "Graph.EntityDomain.Missing" });
            using GameEngine engine = fixture.CreateEngine();
            engine.Start();

            string? message = null;
            try
            {
                engine.LoadMap(MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(ProbeTemplateId));
            Assert.That(message, Does.Contain("Graph.EntityDomain.Missing"));
        }

        [Test]
        public void TemplateTriggerGraphs_UntrimmedEntry_FailsTemplateLoad()
        {
            using EntityDomainFixture fixture = EntityDomainFixture.Create(templateGraphNames: new[] { "  " });
            string? message = null;
            try
            {
                using GameEngine engine = fixture.CreateEngine();
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(ProbeTemplateId));
            Assert.That(message, Does.Contain("TriggerGraphs"));
        }

        private static MapVariableStore RequireVariables(GameEngine engine)
            => engine.CurrentMapSession?.Variables
               ?? throw new InvalidOperationException($"Map '{MapId}' must declare map variables.");

        private static Entity RequireEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Ludots.Core.Components.Name>();
            world.Query(in query, (Entity entity, ref Ludots.Core.Components.Name name) =>
            {
                if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    result = entity;
                }
            });

            return result == Entity.Null
                ? throw new InvalidOperationException($"Missing entity '{entityName}'.")
                : result;
        }

        private static TriggerGraphMountTrigger? FindMountTrigger(GameEngine engine, string graphName, string entryLabel)
        {
            IReadOnlyList<Trigger> triggers = engine.CurrentMapSession?.Triggers ?? Array.Empty<Trigger>();
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i] is TriggerGraphMountTrigger mount &&
                    string.Equals(mount.Name, $"TriggerGraph:{graphName}:{entryLabel}", StringComparison.Ordinal))
                {
                    return mount;
                }
            }

            return null;
        }

        private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, Func<string> describeFailure)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                Tick(engine, 1);
                if (condition())
                {
                    return;
                }
            }

            Assert.Fail(describeFailure());
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private sealed class EntityDomainFixture : IDisposable
        {
            private const string ModId = "TriggerGraphEntityDomainFixtureMod";

            private EntityDomainFixture(string root)
            {
                Root = root;
            }

            public string Root { get; }

            public static EntityDomainFixture Create(string[]? templateGraphNames = null)
            {
                string[] graphs = templateGraphNames ?? new[] { ProbeGraphName };
                string root = Path.Combine(Path.GetTempPath(), "Ludots_TriggerGraphEntityDomainTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Entities"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Maps"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "GAS"));

                File.WriteAllText(
                    Path.Combine(root, ModId, "mod.json"),
                    $$"""
                    {
                      "name": "{{ModId}}",
                      "version": "1.0.0",
                      "description": "Entity-domain TriggerGraph fixture",
                      "priority": 0,
                      "dependencies": {}
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "game.json"),
                    """
                    {
                      "startupMapId": "trigger_graph_entity_domain_probe",
                      "startupInputContexts": [],
                      "presentation": {
                        "presenterInstanceCapacity": 16,
                        "gasPresentationEventCapacity": 16,
                        "presentationEventStreamCapacity": 16,
                        "presentationOwnerChangeCapacity": 16,
                        "presenterCommandCapacity": 16,
                        "presenterTimerCapacity": 16,
                        "primitiveDrawBufferCapacity": 16,
                        "visualSnapshotBufferCapacity": 16,
                        "visualProxyBufferCapacity": 16,
                        "skinnedVisualBatchCapacity": 16,
                        "presentationRequestCapacity": 16,
                        "instancedBatchRequestCapacity": 16,
                        "instancedBatchOperationCapacity": 16,
                        "groundOverlayCapacity": 16,
                        "splineRibbonCapacity": 16,
                        "worldHudCapacity": 16,
                        "screenHudCapacity": 16,
                        "minimapMarkerCapacity": 16,
                        "runtimeEntitySpawnQueueCapacity": 16,
                        "runtimeEntitySpawnReceiptQueueCapacity": 16,
                        "cameraCulling": {
                          "highLodDistanceCm": 1000.0,
                          "mediumLodDistanceCm": 2000.0,
                          "lowLodDistanceCm": 3000.0
                        },
                        "minimap": {
                          "initialZoomNormalized": 1.0,
                          "wheelZoomNormalizedStep": 0.1,
                          "buttonZoomNormalizedStep": 0.2,
                          "zoomSliderEnabled": true,
                          "modeToggleEnabled": true,
                          "rotateToggleEnabled": true,
                          "debugMarkerSampleCapacity": 0,
                          "minZoomExtentMode": "OneChunk",
                          "maxZoomExtentMode": "FullMap",
                          "minZoomExplicitHalfExtentCm": 0.0,
                          "maxZoomExplicitHalfExtentCm": 0.0
                        }
                      },
                      "constants": {
                        "orderTypeIds": {
                          "castAbility": 100,
                          "moveTo": 101,
                          "attackTarget": 102,
                          "stop": 103
                        },
                        "responseChainOrderTypeIds": {
                          "chainPass": 1,
                          "chainNegate": 2,
                          "chainActivateEffect": 3
                        },
                        "attributes": {
                          "health": "Health"
                        }
                      }
                    }
                    """);
                string graphList = string.Join(", ", graphs.Select(g => $"\"{g}\""));
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "Entities", "templates.json"),
                    $$"""
                    [
                      {
                        "id": "{{ProbeTemplateId}}",
                        "components": {
                          "Name": { "Value": "EntityDomainProbe" },
                          "Team": { "Id": 1 },
                          "WorldPositionCm": { "Value": { "X": -300, "Y": 0 } },
                          "AttributeBuffer": { "base": { "Health": 77 } }
                        },
                        "TriggerGraphs": [ {{graphList}} ]
                      },
                      {
                        "id": "{{WatcherTemplateId}}",
                        "components": {
                          "Name": { "Value": "EntityDomainWatcher" },
                          "Team": { "Id": 2 },
                          "WorldPositionCm": { "Value": { "X": 300, "Y": 0 } }
                        }
                      }
                    ]
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "GAS", "graphs.json"),
                    $$"""
                    [
                      {
                        "id": "{{ProbeGraphName}}",
                        "kind": "TriggerGraph",
                        "entries": [
                          { "label": "on_spawn", "event": "EntitySpawned", "start": "spawn_begin", "once": true },
                          { "label": "on_wave", "event": "MapHeartbeat", "start": "wave_begin" },
                          { "label": "on_death", "event": "EntityDied", "start": "death_begin", "once": true }
                        ],
                        "nodes": [
                          { "id": "spawn_begin", "op": "LoadExplicitTarget" },
                          { "id": "spawn_one", "op": "ConstInt", "intValue": 1 },
                          { "id": "spawn_write", "op": "WriteMapVarInt", "var": "spawned" },
                          { "id": "spawn_health", "op": "LoadSelfAttribute", "attribute": "Health" },
                          { "id": "spawn_health_write", "op": "WriteMapVarFloat", "var": "spawn_health" },
                          { "id": "spawn_done", "op": "HaltReturnInt" },

                          { "id": "wave_begin", "op": "LoadExplicitTarget" },
                          { "id": "wave_one", "op": "ConstInt", "intValue": 1 },
                          { "id": "wave_write", "op": "WriteMapVarInt", "var": "wave_ran" },
                          { "id": "wave_done", "op": "HaltReturnInt" },

                          { "id": "death_begin", "op": "LoadExplicitTarget" },
                          { "id": "death_one", "op": "ConstInt", "intValue": 1 },
                          { "id": "death_write", "op": "WriteMapVarInt", "var": "died" },
                          { "id": "death_done", "op": "HaltReturnInt" }
                        ],
                        "controlEdges": [
                          { "from": "spawn_begin", "fromPort": "next", "to": "spawn_one" },
                          { "from": "spawn_one", "fromPort": "next", "to": "spawn_write" },
                          { "from": "spawn_write", "fromPort": "next", "to": "spawn_health" },
                          { "from": "spawn_health", "fromPort": "next", "to": "spawn_health_write" },
                          { "from": "spawn_health_write", "fromPort": "next", "to": "spawn_done" },

                          { "from": "wave_begin", "fromPort": "next", "to": "wave_one" },
                          { "from": "wave_one", "fromPort": "next", "to": "wave_write" },
                          { "from": "wave_write", "fromPort": "next", "to": "wave_done" },

                          { "from": "death_begin", "fromPort": "next", "to": "death_one" },
                          { "from": "death_one", "fromPort": "next", "to": "death_write" },
                          { "from": "death_write", "fromPort": "next", "to": "death_done" }
                        ],
                        "valueEdges": [
                          { "from": "spawn_begin", "fromPort": "value", "to": "spawn_write", "toPort": "source" },
                          { "from": "spawn_one", "fromPort": "value", "to": "spawn_write", "toPort": "value" },
                          { "from": "spawn_one", "fromPort": "value", "to": "spawn_done", "toPort": "value" },
                          { "from": "spawn_begin", "fromPort": "value", "to": "spawn_health_write", "toPort": "source" },
                          { "from": "spawn_health", "fromPort": "value", "to": "spawn_health_write", "toPort": "value" },

                          { "from": "wave_begin", "fromPort": "value", "to": "wave_write", "toPort": "source" },
                          { "from": "wave_one", "fromPort": "value", "to": "wave_write", "toPort": "value" },
                          { "from": "wave_one", "fromPort": "value", "to": "wave_done", "toPort": "value" },

                          { "from": "death_begin", "fromPort": "value", "to": "death_write", "toPort": "source" },
                          { "from": "death_one", "fromPort": "value", "to": "death_write", "toPort": "value" },
                          { "from": "death_one", "fromPort": "value", "to": "death_done", "toPort": "value" }
                        ]
                      },
                      {
                        "id": "{{WatcherGraphName}}",
                        "kind": "TriggerGraph",
                        "entries": [
                          { "label": "watcher_spawn", "event": "EntitySpawned", "start": "w_spawn_begin", "once": true },
                          { "label": "watcher_death", "event": "EntityDied", "start": "w_death_begin", "once": true }
                        ],
                        "nodes": [
                          { "id": "w_spawn_begin", "op": "LoadExplicitTarget" },
                          { "id": "w_spawn_one", "op": "ConstInt", "intValue": 1 },
                          { "id": "w_spawn_write", "op": "WriteMapVarInt", "var": "watcher_spawned" },
                          { "id": "w_spawn_done", "op": "HaltReturnInt" },

                          { "id": "w_death_begin", "op": "LoadExplicitTarget" },
                          { "id": "w_death_one", "op": "ConstInt", "intValue": 1 },
                          { "id": "w_death_write", "op": "WriteMapVarInt", "var": "watcher_died" },
                          { "id": "w_death_done", "op": "HaltReturnInt" }
                        ],
                        "controlEdges": [
                          { "from": "w_spawn_begin", "fromPort": "next", "to": "w_spawn_one" },
                          { "from": "w_spawn_one", "fromPort": "next", "to": "w_spawn_write" },
                          { "from": "w_spawn_write", "fromPort": "next", "to": "w_spawn_done" },
                          { "from": "w_death_begin", "fromPort": "next", "to": "w_death_one" },
                          { "from": "w_death_one", "fromPort": "next", "to": "w_death_write" },
                          { "from": "w_death_write", "fromPort": "next", "to": "w_death_done" }
                        ],
                        "valueEdges": [
                          { "from": "w_spawn_begin", "fromPort": "value", "to": "w_spawn_write", "toPort": "source" },
                          { "from": "w_spawn_one", "fromPort": "value", "to": "w_spawn_write", "toPort": "value" },
                          { "from": "w_spawn_one", "fromPort": "value", "to": "w_spawn_done", "toPort": "value" },
                          { "from": "w_death_begin", "fromPort": "value", "to": "w_death_write", "toPort": "source" },
                          { "from": "w_death_one", "fromPort": "value", "to": "w_death_write", "toPort": "value" },
                          { "from": "w_death_one", "fromPort": "value", "to": "w_death_done", "toPort": "value" }
                        ]
                      }
                    ]
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "Maps", $"{MapId}.json"),
                    $$"""
                    {
                      "Id": "{{MapId}}",
                      "Tags": [ "camera.skip_default_on_load" ],
                      "HeartbeatIntervalTicks": {{HeartbeatIntervalTicks}},
                      "Variables": [
                        { "name": "spawned", "type": "int", "initial": 0 },
                        { "name": "wave_ran", "type": "int", "initial": 0 },
                        { "name": "died", "type": "int", "initial": 0 },
                        { "name": "spawn_health", "type": "float", "initial": 0 },
                        { "name": "watcher_spawned", "type": "int", "initial": 0 },
                        { "name": "watcher_died", "type": "int", "initial": 0 }
                      ],
                      "TriggerGraphs": [
                        { "graph": "{{WatcherGraphName}}", "scopeInstanceId": "entity-domain-watcher", "domain": "entity" }
                      ],
                      "Entities": [
                        { "InstanceId": "entity-domain-probe", "Template": "{{ProbeTemplateId}}" },
                        { "InstanceId": "entity-domain-watcher", "Template": "{{WatcherTemplateId}}" }
                      ]
                    }
                    """);
                return new EntityDomainFixture(root);
            }

            public GameEngine CreateEngine()
            {
                string repoRoot = FindRepoRoot();
                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    new List<string>
                    {
                        Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                        Path.Combine(Root, ModId),
                    },
                    Path.Combine(repoRoot, "assets"));
                return engine;
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }

            private static string FindRepoRoot()
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "assets")) &&
                        Directory.Exists(Path.Combine(dir.FullName, "src")))
                    {
                        return dir.FullName;
                    }

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
            }
        }
    }
}
