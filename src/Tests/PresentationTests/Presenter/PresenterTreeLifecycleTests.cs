using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
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
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterTreeLifecycleTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterTreeLifecycle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PresenterScopeTagRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterScopeTagRegistry.Clear();
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
            WritePresenters(
                """
                [
                  { "id": "child_a" },
                  { "id": "child_b" },
                  {
                    "id": "root",
                    "children": [
                      { "definitionId": "child_a", "scopeTag": "childA" },
                      { "definitionId": "child_b", "scopeTag": "childB" }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            using var fixture = PresenterTreeFixture.Create();
            var loader = new PresenterDefinitionConfigLoader(pipeline, fixture.Definitions);
            loader.Load(catalog);

            int childAId = fixture.Definitions.GetId("child_a");
            int childBId = fixture.Definitions.GetId("child_b");
            int rootId = fixture.Definitions.GetId("root");
            int childAScopeId = PresenterScopeTagRegistry.GetId("childA");
            int childBScopeId = PresenterScopeTagRegistry.GetId("childB");

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 100);
            fixture.TickRuleThenRuntime();

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(3));

            ref readonly PresenterChildren children = ref fixture.World.Get<PresenterChildren>(rootEntity);
            Assert.That(children.Count, Is.GreaterThan(0));

            int childCount = 0;
            for (int i = 0; i < children.Count; i++)
            {
                Entity childEntity = children.Get(i);
                childCount++;
                ref readonly PresenterState childState = ref fixture.World.Get<PresenterState>(childEntity);
                ref readonly PresenterParent childParent = ref fixture.World.Get<PresenterParent>(childEntity);
                Assert.That(childParent.Parent, Is.EqualTo(rootEntity));
                Assert.That(childState.DefId, Is.EqualTo(childAId).Or.EqualTo(childBId));
                Assert.That(childState.ScopeId, Is.EqualTo(childAScopeId).Or.EqualTo(childBScopeId));
            }

            Assert.That(childCount, Is.EqualTo(2));
        }

        [Test]
        public void DestroyPresenterScope_ReleasesOnlyThatScope()
        {
            using var fixture = PresenterTreeFixture.Create();
            int defId = fixture.Definitions.Register("scoped", new PresenterDefinition());
            Entity sharedOwner = fixture.World.Create();

            fixture.Create(defId, sharedOwner, scopeTag: 400);
            fixture.Create(defId, sharedOwner, scopeTag: 400);
            Entity survivorEntity = fixture.Create(defId, sharedOwner, scopeTag: 401);

            fixture.Commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenterScope,
                ScopeTag = 400,
            });
            fixture.Runtime.Update(0.016f);

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(1));
            Assert.That(fixture.World.IsAlive(survivorEntity), Is.True);
            Assert.That(fixture.World.Get<PresenterState>(survivorEntity).ScopeId, Is.EqualTo(401));
        }

        [Test]
        public void DestroyPresenterScope_NamedTags_ReleasesOnlyThatScope()
        {
            using var fixture = PresenterTreeFixture.Create();
            int defId = fixture.Definitions.Register("scoped", new PresenterDefinition());
            Entity sharedOwner = fixture.World.Create();
            int structureId = PresenterScopeTagRegistry.Register("structure");
            int workingId = PresenterScopeTagRegistry.Register("working");

            fixture.Create(defId, sharedOwner, scopeTag: structureId);
            fixture.Create(defId, sharedOwner, scopeTag: workingId);

            fixture.Commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenterScope,
                ScopeTag = workingId,
            });
            fixture.Runtime.Update(0.016f);

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(1));
            Entity survivor = Entity.Null;
            var query = new QueryDescription().WithAll<PresenterState>();
            fixture.World.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                survivor = entity;
                Assert.That(state.ScopeId, Is.EqualTo(structureId));
            });
            Assert.That(survivor, Is.Not.EqualTo(Entity.Null));
        }

        [Test]
        public void RuleTriggeredCreatePresenter_WithParent_AttachesToRoot()
        {
            using var fixture = PresenterTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PresenterDefinition());
            int rootId = fixture.Definitions.Register("root", new PresenterDefinition
            {
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.TagEffectiveChanged,
                            KeyId = 77,
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = childId,
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
            ref readonly PresenterChildren rootChildren = ref fixture.World.Get<PresenterChildren>(rootEntity);
            Assert.That(rootChildren.Count, Is.GreaterThan(0));
            Entity childEntity = rootChildren.Get(0);
            ref readonly PresenterParent childParent = ref fixture.World.Get<PresenterParent>(childEntity);
            ref readonly PresenterState childState = ref fixture.World.Get<PresenterState>(childEntity);
            Assert.That(childParent.Parent, Is.EqualTo(rootEntity));
            Assert.That(childState.ScopeId, Is.EqualTo(500));
            Assert.That(childState.DefId, Is.EqualTo(childId));
        }

        [Test]
        public void RuleCarrierScopedCreate_EmitsWithoutOwnerInstance()
        {
            using var fixture = PresenterTreeFixture.Create();
            int markerId = fixture.Definitions.Register("carrier_marker", new PresenterDefinition());
            _ = fixture.Definitions.Register("carrier_rules", new PresenterDefinition
            {
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.TagEffectiveChanged,
                            KeyId = 91,
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = markerId,
                            ScopeTag = 700,
                        },
                    },
                ],
            });

            Assert.That(fixture.Events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.TagEffectiveChanged,
                KeyId = 91,
                Source = fixture.Owner,
                Target = fixture.Owner,
                Magnitude = 1f,
            }), Is.True);

            fixture.Rules.Update(0.016f);

            Assert.That(fixture.Commands.Count, Is.EqualTo(1));
            ref readonly PresenterCommand command = ref fixture.Commands.GetSpan()[0];
            Assert.That(command.CommandKind, Is.EqualTo(PresenterCommandKind.CreatePresenter));
            Assert.That(command.PresenterDefinitionId, Is.EqualTo(markerId));
            Assert.That(command.Source, Is.EqualTo(fixture.Owner));
            Assert.That(command.ScopeTag, Is.EqualTo(700));
        }

        [Test]
        public void ScopedCreateRules_EmitOnlyForMatchingOwnerDefinitionInstance()
        {
            using var fixture = PresenterTreeFixture.Create();
            int markerAId = fixture.Definitions.Register("marker_a", new PresenterDefinition());
            int markerBId = fixture.Definitions.Register("marker_b", new PresenterDefinition());
            int rootAId = fixture.Definitions.Register("root_a", new PresenterDefinition
            {
                Children = [new ChildPresenterRef { DefinitionId = markerAId, ScopeTag = 1 }],
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.TagEffectiveChanged,
                            KeyId = 92,
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = markerAId,
                            ScopeTag = 800,
                        },
                    },
                ],
            });
            int rootBId = fixture.Definitions.Register("root_b", new PresenterDefinition
            {
                Children = [new ChildPresenterRef { DefinitionId = markerBId, ScopeTag = 1 }],
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.TagEffectiveChanged,
                            KeyId = 92,
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = markerBId,
                            ScopeTag = 800,
                        },
                    },
                ],
            });

            Entity rootA = fixture.CreateRoot(rootAId, scopeTag: 101);
            Entity otherOwner = fixture.World.Create();
            _ = fixture.Create(rootBId, otherOwner, scopeTag: 202);
            fixture.Events.Clear();

            Assert.That(fixture.Events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.TagEffectiveChanged,
                KeyId = 92,
                Source = fixture.Owner,
                Target = fixture.Owner,
                Magnitude = 1f,
            }), Is.True);

            fixture.Rules.Update(0.016f);

            Assert.That(fixture.Commands.Count, Is.EqualTo(1));
            ref readonly PresenterCommand command = ref fixture.Commands.GetSpan()[0];
            Assert.That(command.CommandKind, Is.EqualTo(PresenterCommandKind.CreatePresenter));
            Assert.That(command.PresenterDefinitionId, Is.EqualTo(markerAId));
            Assert.That(command.ParentEntity, Is.EqualTo(rootA));
            Assert.That(command.ScopeTag, Is.EqualTo(800));
        }

        [Test]
        public void EntityCollectionMemberRemoved_DestroysScopedMarker_WithoutDestroyingRoot()
        {
            using var fixture = PresenterTreeFixture.Create();
            var collectionKeys = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var collections = new EntityCollectionStore(collectionKeys);
            int commandSourceKeyId = collectionKeys.Register(EntityCollectionKeys.CommandSource);
            const int sourceStableId = 9001;

            int markerId = fixture.Definitions.Register("selection_marker", new PresenterDefinition());
            int rootId = fixture.Definitions.Register("agent_root", new PresenterDefinition
            {
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntityCollectionMemberAdded,
                            KeyId = commandSourceKeyId,
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = markerId,
                            ScopeSource = PresenterCommandScopeSource.SourceStableId,
                        },
                    },
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntityCollectionMemberRemoved,
                            KeyId = commandSourceKeyId,
                        },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.DestroyScopedPresenter,
                            PresenterDefinitionId = markerId,
                            ScopeSource = PresenterCommandScopeSource.SourceStableId,
                        },
                    },
                ],
            });

            fixture.World.Add(fixture.Owner, new PresentationStableId { Value = sourceStableId });
            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: sourceStableId);
            fixture.Events.Clear();

            Entity player = fixture.World.Create();
            using var collectionEvents = new EntityCollectionPresentationEventSystem(fixture.World, collections, fixture.Events);
            ReplaceCommandSource(collections, player, fixture.Owner);

            collectionEvents.Update(0.016f);
            fixture.TickRuleThenRuntime();

            Assert.That(
                fixture.Instances.TryGetActiveScopedInstance(
                    markerId,
                    fixture.Owner,
                    sourceStableId,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    out Entity markerEntity),
                Is.True);
            Assert.That(fixture.World.Get<PresenterState>(markerEntity).ScopeId, Is.EqualTo(sourceStableId));
            Assert.That(fixture.World.Get<PresenterParent>(markerEntity).Parent, Is.EqualTo(rootEntity));
            Assert.That(fixture.World.IsAlive(rootEntity), Is.True);

            fixture.Events.Clear();
            Assert.That(collections.Remove(player, EntityCollectionKeys.CommandSource), Is.True);
            collectionEvents.Update(0.016f);
            fixture.TickRuleThenRuntime();

            Assert.That(fixture.World.IsAlive(markerEntity), Is.False);
            Assert.That(fixture.World.IsAlive(rootEntity), Is.True);
            Assert.That(
                fixture.Instances.TryGetActiveScopedInstance(
                    markerId,
                    fixture.Owner,
                    sourceStableId,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    out _),
                Is.False);
            Assert.That(fixture.Instances.GetActiveByOwnerDefinition(rootId, fixture.Owner).Count, Is.EqualTo(1));
            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(1));
        }

        private static void ReplaceCommandSource(EntityCollectionStore collections, Entity owner, Entity member)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                owner,
                member,
                "Command source",
                "1 entity");
            collections.Replace(owner, descriptor, new[] { member }, owner);
        }

        [Test]
        public void DestroyRoot_ReleasesWholeSubtree()
        {
            using var fixture = PresenterTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PresenterDefinition());
            int grandChildId = fixture.Definitions.Register("grand_child", new PresenterDefinition());
            int rootId = fixture.Definitions.Register("root", new PresenterDefinition());

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 600);
            Entity childEntity = fixture.Create(childId, fixture.Owner, scopeTag: 601, parentEntity: rootEntity);
            Entity grandChildEntity = fixture.Create(grandChildId, fixture.Owner, scopeTag: 602, parentEntity: childEntity);

            fixture.Commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenter,
                PresenterEntity = rootEntity,
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
            using var fixture = PresenterTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PresenterDefinition());
            int rootId = fixture.Definitions.Register("root", new PresenterDefinition());

            Entity rootEntity = fixture.CreateRoot(rootId, scopeTag: 700);
            Entity childEntity = fixture.Create(childId, fixture.Owner, scopeTag: 701, parentEntity: rootEntity);

            fixture.Instances.SetParam(rootEntity, 300, ParamLane.Float, 1.5f, 0, Vector4.Zero);

            Assert.That(fixture.Instances.ResolveFloat(childEntity, 300, -1f), Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void Blackboard_ChildOverride_ShadowsParent_PropagatesToGrandchild()
        {
            using var fixture = PresenterTreeFixture.Create();
            int childId = fixture.Definitions.Register("child", new PresenterDefinition());
            int grandChildId = fixture.Definitions.Register("grand_child", new PresenterDefinition());
            int rootId = fixture.Definitions.Register("root", new PresenterDefinition());

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
            WritePresenters(
                """
                [
                  { "id": "a", "children": [ { "definitionId": "b" } ] },
                  { "id": "b", "children": [ { "definitionId": "a" } ] },
                  { "id": "ok_root" }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("Circular child reference"));
        }

        [Test]
        public void Loader_RejectsMoreThan32Behaviors()
        {
            WriteCatalog();
            WritePresenters(
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
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("exceeds the max 32 behaviors"));
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
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
        }

        private void WritePresenters(string content)
        {
            WriteFile("Core", "Presentation/presenters.json", content);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private sealed class PresenterTreeFixture : IDisposable
        {
            public readonly World World;
            public readonly PresenterCommandBuffer Commands;
            public readonly PresentationEventStream Events;
            public readonly PresenterEntityRuntime Instances;
            public readonly PresenterDefinitionRegistry Definitions;
            public readonly PresenterRuntimeSystem Runtime;
            public readonly PresenterRuleSystem Rules;
            public readonly Entity Owner;

            private PresenterTreeFixture()
            {
                World = Arch.Core.World.Create();
                Commands = new PresenterCommandBuffer();
                Events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                Instances = new PresenterEntityRuntime(World);
                Definitions = new PresenterDefinitionRegistry();
                Owner = this.World.Create();
                Runtime = new PresenterRuntimeSystem(
                    this.World,
                    Commands,
                    Events,
                    new TransientMarkerBuffer(),
                    new PresentationRequestBuffer(),
                    Instances,
                    new PresentationStableIdAllocator(),
                    Definitions);
                Rules = new PresenterRuleSystem(
                    this.World,
                    Events,
                    Commands,
                    Definitions,
                    Instances,
                    new Ludots.Core.GraphRuntime.GraphProgramRegistry(),
                    new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(this.World, spatialQueries: null, coords: null, eventBus: null),
                    new System.Collections.Generic.Dictionary<string, object>());
            }

            public static PresenterTreeFixture Create() => new();

            public Entity CreateRoot(int definitionId, int scopeTag)
            {
                return Create(definitionId, Owner, scopeTag, parentEntity: Entity.Null);
            }

            public Entity Create(int definitionId, Entity owner, int scopeTag, Entity parentEntity = default)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    PresenterDefinitionId = definitionId,
                    ParentEntity = parentEntity,
                    ScopeTag = scopeTag,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = owner,
                    Target = owner,
                });
                Runtime.Update(0.016f);

                ReadOnlySpan<PresentationEvent> events = Events.GetSpan();
                Assert.That(events.Length, Is.GreaterThan(0));
                Entity presenter = events[^1].PresenterEntity;
                return presenter;
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
