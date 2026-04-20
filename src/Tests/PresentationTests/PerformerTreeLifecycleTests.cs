using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerTreeLifecycleTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PerformerTreeLifecycle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PerformerScopeTagRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PerformerScopeTagRegistry.Clear();
            TagRegistry.Clear();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup races.
            }
        }

        [Test]
        public void CreateRoot_AutoCreatesDeclaredChildren()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  { "id": "child_a" },
                  { "id": "child_b" },
                  {
                    "id": "root",
                    "children": [
                      { "definitionId": "child_a", "scopeTag": 101 },
                      { "definitionId": "child_b", "scopeTag": 102 }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            using var fixture = PerformerTreeFixture.Create();
            var loader = new PerformerDefinitionConfigLoader(pipeline, fixture.Definitions);
            loader.Load(catalog);

            int childAId = fixture.Definitions.GetId("child_a");
            int childBId = fixture.Definitions.GetId("child_b");
            int rootId = fixture.Definitions.GetId("root");

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 100);
            fixture.TickRuleThenRuntime();

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(3));

            ref readonly PerformerChildren children = ref fixture.World.Get<PerformerChildren>(rootEntity);
            Assert.That(children.Count, Is.GreaterThan(0));

            int childCount = 0;
            for (int i = 0; i < children.Count; i++)
            {
                Entity childEntity = children.Get(i);
                childCount++;
                ref readonly PerformerState childState = ref fixture.World.Get<PerformerState>(childEntity);
                ref readonly PerformerParent childParent = ref fixture.World.Get<PerformerParent>(childEntity);
                Assert.That(childParent.Parent, Is.EqualTo(rootEntity));
                Assert.That(childState.DefId, Is.EqualTo(childAId).Or.EqualTo(childBId));
                Assert.That(childState.ScopeId, Is.EqualTo(101).Or.EqualTo(102));
            }

            Assert.That(childCount, Is.EqualTo(2));
        }

        [Test]
        public void DestroyPerformerScope_ReleasesOnlyThatScope()
        {
            using var fixture = PerformerTreeFixture.Create();
            int defId = fixture.Definitions.Register("scoped", new PerformerDefinition());
            Entity sharedOwner = fixture.World.Create();

            fixture.Create(defId, sharedOwner, scopeTag: 400);
            fixture.Create(defId, sharedOwner, scopeTag: 400);
            Entity survivorEntity = fixture.Create(defId, sharedOwner, scopeTag: 401);

            fixture.Commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformerScope,
                ScopeTag = 400,
            });
            fixture.Runtime.Update(0.016f);

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(1));
            Assert.That(fixture.World.IsAlive(survivorEntity), Is.True);
            Assert.That(fixture.World.Get<PerformerState>(survivorEntity).ScopeId, Is.EqualTo(401));
        }

        [Test]
        public void RuleTriggeredCreatePerformer_WithParent_AttachesToRoot()
        {
            using var fixture = PerformerTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PerformerDefinition());
            int rootId = fixture.Definitions.Register("root", new PerformerDefinition
            {
                Rules =
                [
                    new PerformerRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.TagEffectiveChanged,
                            KeyId = 77,
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = childId,
                            ScopeTag = 500,
                        },
                    },
                ],
            });

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 500);
            fixture.Events.Clear();

            Assert.That(fixture.Events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.TagEffectiveChanged,
                KeyId = 77,
                Source = fixture.Owner,
                Target = fixture.Owner,
                Magnitude = 1f,
            }), Is.True);

            fixture.TickRuleThenRuntime();

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(2));
            ref readonly PerformerChildren rootChildren = ref fixture.World.Get<PerformerChildren>(rootEntity);
            Assert.That(rootChildren.Count, Is.GreaterThan(0));
            Entity childEntity = rootChildren.Get(0);
            ref readonly PerformerParent childParent = ref fixture.World.Get<PerformerParent>(childEntity);
            ref readonly PerformerState childState = ref fixture.World.Get<PerformerState>(childEntity);
            Assert.That(childParent.Parent, Is.EqualTo(rootEntity));
            Assert.That(childState.ScopeId, Is.EqualTo(500));
            Assert.That(childState.DefId, Is.EqualTo(childId));
        }

        [Test]
        public void DestroyRoot_ReleasesWholeSubtree()
        {
            using var fixture = PerformerTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PerformerDefinition());
            int grandChildId = fixture.Definitions.Register("grand_child", new PerformerDefinition());
            int rootId = fixture.Definitions.Register("root", new PerformerDefinition());

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 600);
            Entity childEntity = fixture.Create(childId, fixture.Owner, scopeTag: 601, parentEntity: rootEntity);
            Entity grandChildEntity = fixture.Create(grandChildId, fixture.Owner, scopeTag: 602, parentEntity: childEntity);

            fixture.Commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformer,
                PerformerEntity = rootEntity,
            });
            fixture.Runtime.Update(0.016f);

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(0));
            Assert.That(fixture.World.IsAlive(rootEntity), Is.False);
            Assert.That(fixture.World.IsAlive(childEntity), Is.False);
            Assert.That(fixture.World.IsAlive(grandChildEntity), Is.False);
        }

        [Test]
        public void Blackboard_ChildInheritsParentParam()
        {
            using var fixture = PerformerTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PerformerDefinition());
            int rootId = fixture.Definitions.Register("root", new PerformerDefinition());

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 700);
            Entity childEntity = fixture.Create(childId, fixture.Owner, scopeTag: 701, parentEntity: rootEntity);

            fixture.Instances.SetParam(rootEntity, 300, ParamLane.Float, 1.5f, 0, Vector4.Zero);

            Assert.That(fixture.Instances.ResolveFloat(childEntity, 300, -1f), Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void Blackboard_ChildOverride_ShadowsParent_PropagatesToGrandchild()
        {
            using var fixture = PerformerTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PerformerDefinition());
            int grandChildId = fixture.Definitions.Register("grand_child", new PerformerDefinition());
            int rootId = fixture.Definitions.Register("root", new PerformerDefinition());

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 800);
            Entity childEntity = fixture.Create(childId, fixture.Owner, scopeTag: 801, parentEntity: rootEntity);
            Entity grandChildEntity = fixture.Create(grandChildId, fixture.Owner, scopeTag: 802, parentEntity: childEntity);

            fixture.Instances.SetParam(rootEntity, 300, ParamLane.Float, 1f, 0, Vector4.Zero);
            fixture.Instances.SetParam(childEntity, 300, ParamLane.Float, 2f, 0, Vector4.Zero);

            Assert.That(fixture.Instances.ResolveFloat(rootEntity, 300, -1f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(fixture.Instances.ResolveFloat(childEntity, 300, -1f), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(fixture.Instances.ResolveFloat(grandChildEntity, 300, -1f), Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void Loader_RejectsCircularChildReference()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  { "id": "a", "children": [ { "definitionId": "b" } ] },
                  { "id": "b", "children": [ { "definitionId": "a" } ] },
                  { "id": "ok_root" }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            Assert.DoesNotThrow(() => loader.Load(catalog));
            Assert.That(registry.TryGet(registry.GetId("a"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("b"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("ok_root"), out _), Is.True);
        }

        [Test]
        public void Loader_RejectsMoreThan32Behaviors()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "too_many_behaviors",
                    "behaviors": [
                      { "slot": 0, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 1, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 2, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 3, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 4, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 5, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 6, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 7, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 8, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 9, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 10, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 11, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 12, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 13, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 14, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 15, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 16, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 17, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 18, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 19, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 20, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 21, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 22, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 23, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 24, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 25, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 26, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 27, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 28, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 29, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 30, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 31, "kind": "Sound", "sound": { "soundAssetId": 1 } },
                      { "slot": 32, "kind": "Sound", "sound": { "soundAssetId": 1 } }
                    ]
                  },
                  { "id": "ok_root" }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            Assert.DoesNotThrow(() => loader.Load(catalog));
            Assert.That(registry.TryGet(registry.GetId("too_many_behaviors"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("ok_root"), out _), Is.True);
        }

        private (VirtualFileSystem Vfs, ModLoader ModLoader, ConfigPipeline Pipeline, ConfigCatalog Catalog) BuildPipeline()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return (vfs, modLoader, pipeline, catalog);
        }

        private void WriteCatalog()
        {
            WriteFile(
                "Core",
                "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
        }

        private void WritePerformers(string content)
        {
            WriteFile("Core", "Presentation/performers.json", content);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, "Configs", Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private sealed class PerformerTreeFixture : IDisposable
        {
            public readonly World World;
            public readonly PerformerCommandBuffer Commands;
            public readonly PresentationEventStream Events;
            public readonly PerformerEntityRuntime Instances;
            public readonly PerformerDefinitionRegistry Definitions;
            public readonly PerformerRuntimeSystem Runtime;
            public readonly PerformerRuleSystem Rules;
            public readonly Entity Owner;

            private PerformerTreeFixture()
            {
                World = Arch.Core.World.Create();
                Commands = new PerformerCommandBuffer();
                Events = new PresentationEventStream();
                Instances = new PerformerEntityRuntime(World);
                Definitions = new PerformerDefinitionRegistry();
                Owner = this.World.Create();
                Runtime = new PerformerRuntimeSystem(
                    this.World,
                    Commands,
                    Events,
                    new TransientMarkerBuffer(),
                    new PresentationRequestBuffer(),
                    Instances,
                    new PresentationStableIdAllocator(),
                    Definitions);
                Rules = new PerformerRuleSystem(
                    this.World,
                    Events,
                    Commands,
                    Definitions,
                    Instances,
                    new Ludots.Core.GraphRuntime.GraphProgramRegistry(),
                    new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(this.World, spatialQueries: null, coords: null, eventBus: null),
                    new System.Collections.Generic.Dictionary<string, object>());
            }

            public static PerformerTreeFixture Create() => new();

            public Entity CreateRoot(int definitionId, int scopeTag)
            {
                return Create(definitionId, Owner, scopeTag, parentEntity: Entity.Null);
            }

            public Entity Create(int definitionId, Entity owner, int scopeTag, Entity parentEntity = default)
            {
                Commands.TryAdd(new PerformerCommand
                {
                    CommandKind = PerformerCommandKind.CreatePerformer,
                    PerformerDefinitionId = definitionId,
                    ParentEntity = parentEntity,
                    ScopeTag = scopeTag,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = owner,
                    Target = owner,
                });
                Runtime.Update(0.016f);

                ReadOnlySpan<PresentationEvent> events = Events.GetSpan();
                Assert.That(events.Length, Is.GreaterThan(0));
                Entity performer = events[^1].PerformerEntity;
                return performer;
            }

            public void TickRuleThenRuntime()
            {
                Rules.Update(0.016f);
                Runtime.Update(0.016f);
            }

            public void Dispose()
            {
                Rules.Dispose();
                Runtime.Dispose();
                World.Dispose();
            }
        }
    }
}
