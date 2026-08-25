using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Runtime equivalence between the bindSpawn load-time sugar and its hand-written
    /// canonical rules: the same spawn/destroy lifecycle must produce identical presenter
    /// instances through the compiled bootstrap path.
    /// </summary>
    [TestFixture]
    public sealed class PresenterBindSpawnEquivalenceTests
    {
        private const int TemplateKeyId = 515;
        private const int OwnerStableId = 411;

        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterBindSpawnTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
            TagRegistry.Clear();
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
            TagRegistry.Clear();
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failures in test teardown.
            }
        }

        [Test]
        public void Lifecycle_BindSpawnAndHandWrittenRules_ProduceIdenticalInstances()
        {
            PresenterDefinitionRegistry sugar = LoadDefinitions(
                """
                [
                  {
                    "id": "eqa.actor",
                    "bindSpawn": {
                      "template": "eqa.unit",
                      "scopeSource": "EventPayloadA",
                      "condition": { "inline": "SourceHasVisualTransform" }
                    }
                  }
                ]
                """);

            PresenterDefinitionRegistry canonical = LoadDefinitions(
                """
                [
                  {
                    "id": "eqa.actor",
                    "rules": [
                      {
                        "event": { "kind": "EntitySpawned", "key": "eqa.unit" },
                        "condition": { "inline": "SourceHasVisualTransform" },
                        "command": {
                          "kind": "CreatePresenter",
                          "definitionId": "eqa.actor",
                          "scopeSource": "EventPayloadA"
                        }
                      },
                      {
                        "event": { "kind": "EntityDestroyed", "key": "eqa.unit" },
                        "command": { "kind": "DestroyPresenterScope", "scopeSource": "EventPayloadA" }
                      }
                    ]
                  }
                ]
                """);

            using var sugarRun = new BindSpawnRuntimeFixture(sugar);
            using var canonicalRun = new BindSpawnRuntimeFixture(canonical);

            BindSpawnSpawnResult sugarSpawn = sugarRun.SpawnTemplateEntity();
            BindSpawnSpawnResult canonicalSpawn = canonicalRun.SpawnTemplateEntity();

            Assert.That(sugarSpawn.PresenterState.DefId, Is.EqualTo(canonicalSpawn.PresenterState.DefId));
            Assert.That(sugarSpawn.PresenterState.ScopeId, Is.EqualTo(canonicalSpawn.PresenterState.ScopeId));
            Assert.That(sugarSpawn.PresenterState.ScopeId, Is.EqualTo(OwnerStableId));
            Assert.That(sugarSpawn.PresenterState.OwnerEntity, Is.EqualTo(sugarRun.OwnerEntity));
            Assert.That(canonicalSpawn.PresenterState.OwnerEntity, Is.EqualTo(canonicalRun.OwnerEntity));
            Assert.That(sugarSpawn.PresenterState.AnchorKind, Is.EqualTo(canonicalSpawn.PresenterState.AnchorKind));
            Assert.That(sugarRun.CountPresentersOfDefinition(sugarSpawn.PresenterState.DefId), Is.EqualTo(1));
            Assert.That(canonicalRun.CountPresentersOfDefinition(canonicalSpawn.PresenterState.DefId), Is.EqualTo(1));

            sugarRun.DestroyTemplateEntity();
            canonicalRun.DestroyTemplateEntity();

            Assert.That(sugarRun.CountPresentersOfDefinition(sugarSpawn.PresenterState.DefId), Is.EqualTo(0));
            Assert.That(canonicalRun.CountPresentersOfDefinition(canonicalSpawn.PresenterState.DefId), Is.EqualTo(0));
        }

        private PresenterDefinitionRegistry LoadDefinitions(string presentersJson)
        {
            WriteFile(
                "Core",
                "config_catalog.json",
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/presenters.json", presentersJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);

            var registry = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveEntityTemplateKey: key => string.Equals(key, "eqa.unit", StringComparison.Ordinal) ? TemplateKeyId : 0)
                .Load(catalog);
            return registry;
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private readonly struct BindSpawnSpawnResult
        {
            public readonly PresenterState PresenterState;

            public BindSpawnSpawnResult(PresenterState presenterState)
            {
                PresenterState = presenterState;
            }
        }

        private sealed class BindSpawnRuntimeFixture : IDisposable
        {
            public readonly World World;
            private readonly PresenterCommandBuffer _commands;
            private readonly PresentationEventStream _events;
            private readonly PresenterEntityRuntime _instances;
            private readonly PresentationStableIdAllocator _stableIds;
            private readonly PresenterRuntimeSystem _runtime;
            private readonly PresentationEntityLifecycleSystem _lifecycle;
            private readonly Entity _ownerEntity;
            private readonly QueryDescription _presenterQuery = new QueryDescription().WithAll<PresenterState>();

            public BindSpawnRuntimeFixture(PresenterDefinitionRegistry definitions)
            {
                World = Arch.Core.World.Create();
                _commands = new PresenterCommandBuffer();
                _events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                _instances = new PresenterEntityRuntime(World);
                _stableIds = new PresentationStableIdAllocator();
                _runtime = new PresenterRuntimeSystem(
                    World,
                    _commands,
                    _events,
                    new TransientMarkerBuffer(),
                    new PresentationRequestBuffer(),
                    _instances,
                    _stableIds,
                    definitions);
                _lifecycle = new PresentationEntityLifecycleSystem(
                    World,
                    _events,
                    _instances,
                    definitions,
                    _stableIds,
                    _commands);
                _ownerEntity = World.Create(
                    new PresentationStableId { Value = OwnerStableId },
                    new EntityTemplateKeyRef { TemplateKeyId = TemplateKeyId },
                    VisualTransform.Default);
            }

            public Entity OwnerEntity => _ownerEntity;

            public BindSpawnSpawnResult SpawnTemplateEntity()
            {
                _lifecycle.Update(0.016f);

                foreach (var chunk in World.Query(in _presenterQuery))
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        Entity presenter = chunk.Entity(i);
                        PresenterState state = World.Get<PresenterState>(presenter);
                        if (state.OwnerEntity == _ownerEntity)
                        {
                            return new BindSpawnSpawnResult(state);
                        }
                    }
                }

                throw new InvalidOperationException("EntitySpawned bootstrap did not create a presenter for the spawned template entity.");
            }

            public void DestroyTemplateEntity()
            {
                World.Add<PresentationDestroyPending>(_ownerEntity);
                _lifecycle.Update(0.016f);
                _runtime.Update(0.016f);
            }

            public int CountPresentersOfDefinition(int definitionId)
            {
                int count = 0;
                foreach (var chunk in World.Query(in _presenterQuery))
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (World.Get<PresenterState>(chunk.Entity(i)).DefId == definitionId)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }

            public void Dispose()
            {
                _runtime.Dispose();
                _lifecycle.Dispose();
                World.Dispose();
            }
        }
    }
}
