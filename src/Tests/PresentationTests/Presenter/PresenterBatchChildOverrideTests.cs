using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterBatchChildOverrideTests
    {
        private const int OwnerCount = 6;
        private const int ParamKey = 5;

        private static readonly PresenterInstanceTransformOverride InheritOverride = new()
        {
            LocalPosition = new Vector3(2.4f, 0f, 0.6f),
            LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f),
            LocalScale = new Vector3(0.6f, 0.6f, 0.6f),
            HasOverride = true,
        };

        private static readonly PresenterInstanceTransformOverride AttachedOverride = new()
        {
            LocalPosition = new Vector3(0.5f, 0f, 0f),
            LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            LocalScale = new Vector3(2f, 3f, 4f),
            HasOverride = true,
        };

        [TestCase(false)]
        [TestCase(true)]
        public void BatchEntry_ChildrenWithPoseAndParamOverrides_StayOnBatchPathAndMatchPerEntityCreate(bool staticOwners)
        {
            using var batch = Scene.Build(staticOwners, useBatchEntry: true);
            using var single = Scene.Build(staticOwners, useBatchEntry: false);

            Assert.That(batch.Runtime.LastChildBatchCreatedCount, Is.EqualTo(OwnerCount * 4),
                "pose and param overrides must not push children off the batch create path");
            for (int i = 0; i < OwnerCount; i++)
            {
                Entity child = batch.FindChild(i, batch.InheritId);
                Assert.That(batch.World.Has<PresenterInstanceTransformOverride>(child), Is.True);
                Assert.That(batch.World.Has<PresenterInstanceTransformOverride>(batch.FindChild(i, batch.PlainId)), Is.False);
            }

            AssertScenesMatch(batch, single, "after create");
            for (int frame = 0; frame < 3; frame++)
            {
                if (!staticOwners)
                {
                    batch.MoveOwners(frame);
                    single.MoveOwners(frame);
                }

                batch.Tick();
                single.Tick();
                AssertScenesMatch(batch, single, $"frame {frame}");
            }

            if (staticOwners)
            {
                return;
            }

            batch.MoveOwners(3);
            batch.Tick();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            batch.MoveOwners(4);
            batch.Tick();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.That(allocated, Is.Zero, "steady-state transform sync over batch-created override children must not allocate");
        }

        [Test]
        public void BatchEntry_InstanceSubtreeOverride_StillUsesPerEntityPlan()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int leafId = definitions.Register("batch.override.leaf", MeshDefinition(81));
            int childId = definitions.Register("batch.override.child", MeshDefinition(82));
            int rootId = definitions.Register("batch.override.subtree.root", new PresenterDefinition
            {
                Behaviors = [MeshSlot(0, 80)],
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = childId,
                        InstanceOverride = new PresenterChildInstanceOverride
                        {
                            ChildrenMode = PresenterChildrenMode.Instance,
                            InstanceChildren = [new ChildPresenterRef { DefinitionId = leafId }],
                        },
                    },
                ],
            });
            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = CreateOwner(world, new Vector3(1f, 0f, 2f), staticOwner: false);
            var created = new Entity[1];
            runtime.CreateEntityAnchoredRootBatch(
                definitions, rootId,
                new[] { owner }, new[] { 1 }, new[] { 9500 },
                new[] { world.Get<VisualTransform>(owner) },
                new[] { world.Get<CullState>(owner) },
                definitions.Get(rootId), created);

            Assert.That(runtime.LastChildBatchCreatedCount, Is.Zero);
            Entity child = world.Get<PresenterChildren>(created[0]).Get(0);
            Assert.That(world.Has<PresenterInstanceChildren>(child), Is.True);
        }

        private static void AssertScenesMatch(Scene batch, Scene single, string phase)
        {
            int[] childIds = [batch.InheritId, batch.AttachedId, batch.ParamId, batch.PlainId];
            for (int i = 0; i < OwnerCount; i++)
            {
                AssertPoseEqual(batch.World, batch.Roots[i], single.World, single.Roots[i], $"{phase} root[{i}]");
                for (int c = 0; c < childIds.Length; c++)
                {
                    Entity a = batch.FindChild(i, childIds[c]);
                    Entity b = single.FindChild(i, childIds[c]);
                    string message = $"{phase} owner[{i}] child def={childIds[c]}";
                    AssertPoseEqual(batch.World, a, single.World, b, message);
                    Assert.That(batch.World.Get<PresenterTransformSource>(a).Value,
                        Is.EqualTo(single.World.Get<PresenterTransformSource>(b).Value), message + " source");
                    Assert.That(batch.Runtime.ResolveFloat(a, ParamKey, -1f),
                        Is.EqualTo(single.Runtime.ResolveFloat(b, ParamKey, -1f)), message + " param");
                }

                Assert.That(batch.Runtime.ResolveFloat(batch.FindChild(i, batch.ParamId), ParamKey, -1f), Is.EqualTo(7f));
            }
        }

        private static void AssertPoseEqual(World worldA, Entity a, World worldB, Entity b, string message)
        {
            AssertClose(worldA.Get<PresenterWorldPosition>(a).Value, worldB.Get<PresenterWorldPosition>(b).Value, message + " position");
            AssertClose(worldA.Get<PresenterWorldScale>(a).Value, worldB.Get<PresenterWorldScale>(b).Value, message + " scale");
            Quaternion ra = VisualMath.NormalizeOrIdentity(worldA.Get<PresenterWorldRotation>(a).Value);
            Quaternion rb = VisualMath.NormalizeOrIdentity(worldB.Get<PresenterWorldRotation>(b).Value);
            Assert.That(MathF.Abs(Quaternion.Dot(ra, rb)), Is.EqualTo(1f).Within(0.0001f), message + $" rotation {ra} vs {rb}");
        }

        private static void AssertClose(Vector3 actual, Vector3 expected, string message)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f), message + $" actual={actual} expected={expected}");
        }

        private static PresenterDefinition MeshDefinition(int assetId, params BehaviorSlot[] extra)
        {
            var behaviors = new BehaviorSlot[1 + extra.Length];
            behaviors[0] = MeshSlot(0, assetId);
            Array.Copy(extra, 0, behaviors, 1, extra.Length);
            return new PresenterDefinition { Behaviors = behaviors };
        }

        private static BehaviorSlot MeshSlot(int slot, int assetId)
        {
            return new BehaviorSlot
            {
                SlotIndex = slot,
                Kind = BehaviorKind.AssetBinding,
                ActiveByDefault = true,
                AssetBinding = new AssetBindingConfig
                {
                    AssetKind = AssetKind.Mesh,
                    AssetId = assetId,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Movable,
                    LocalRotation = Quaternion.Identity,
                    LocalScale = Vector3.One,
                    AssetIdParamKey = -1,
                },
            };
        }

        private static BehaviorSlot ParentAttachmentSlot(int slot)
        {
            return new BehaviorSlot
            {
                SlotIndex = slot,
                Kind = BehaviorKind.Attachment,
                ActiveByDefault = true,
                Attachment = new AttachmentConfig
                {
                    Target = AttachmentTarget.Parent,
                    Offset = new Vector3(0f, 1.5f, 0f),
                    RotationOffset = Quaternion.Identity,
                    InheritScale = false,
                },
            };
        }

        private static Entity CreateOwner(World world, Vector3 position, bool staticOwner)
        {
            Entity owner = world.Create(
                new VisualTransform { Position = position, Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            if (staticOwner)
            {
                world.Add(owner, new PresentationStaticTransform());
            }

            return owner;
        }

        private sealed class Scene : IDisposable
        {
            public World World = null!;
            public PresenterEntityRuntime Runtime = null!;
            public PresenterDefinitionRegistry Definitions = null!;
            public PresenterEntityTransformSyncSystem Sync = null!;
            public PresenterBehaviorSystem Behaviors = null!;
            public Entity[] Owners = null!;
            public Entity[] Roots = null!;
            public int InheritId;
            public int AttachedId;
            public int ParamId;
            public int PlainId;

            public static Scene Build(bool staticOwners, bool useBatchEntry)
            {
                var scene = new Scene { World = World.Create(), Definitions = new PresenterDefinitionRegistry() };
                PresenterDefinitionRegistry definitions = scene.Definitions;
                scene.InheritId = definitions.Register("batch.override.inherit", MeshDefinition(91));
                scene.AttachedId = definitions.Register("batch.override.attached", MeshDefinition(92, ParentAttachmentSlot(1)));
                PresenterDefinition paramDefinition = MeshDefinition(93);
                paramDefinition.ParamDefaults = [new ParamDefault { ParamKey = ParamKey, Lane = ParamLane.Float, FloatValue = 1f }];
                scene.ParamId = definitions.Register("batch.override.param", paramDefinition);
                scene.PlainId = definitions.Register("batch.override.plain", MeshDefinition(94));
                int rootId = definitions.Register("batch.override.root", new PresenterDefinition
                {
                    Behaviors = [MeshSlot(0, 90)],
                    Children =
                    [
                        new ChildPresenterRef { DefinitionId = scene.InheritId, TransformOverride = InheritOverride },
                        new ChildPresenterRef { DefinitionId = scene.AttachedId, TransformOverride = AttachedOverride },
                        new ChildPresenterRef
                        {
                            DefinitionId = scene.ParamId,
                            ParamOverrides = [new ParamDefault { ParamKey = ParamKey, Lane = ParamLane.Float, FloatValue = 7f }],
                        },
                        new ChildPresenterRef { DefinitionId = scene.PlainId },
                    ],
                });

                scene.Runtime = new PresenterEntityRuntime(scene.World);
                scene.Runtime.BindDefinitions(definitions);
                scene.Owners = new Entity[OwnerCount];
                scene.Roots = new Entity[OwnerCount];
                var scopeIds = new int[OwnerCount];
                var stableIds = new int[OwnerCount];
                var transforms = new VisualTransform[OwnerCount];
                var culls = new CullState[OwnerCount];
                for (int i = 0; i < OwnerCount; i++)
                {
                    scene.Owners[i] = CreateOwner(scene.World, new Vector3(3f * i, 0f, 10f - i), staticOwners);
                    scopeIds[i] = 1;
                    stableIds[i] = 9300 + i;
                    transforms[i] = scene.World.Get<VisualTransform>(scene.Owners[i]);
                    culls[i] = scene.World.Get<CullState>(scene.Owners[i]);
                }

                if (useBatchEntry)
                {
                    scene.Runtime.CreateEntityAnchoredRootBatch(
                        definitions, rootId, scene.Owners, scopeIds, stableIds, transforms, culls,
                        definitions.Get(rootId), scene.Roots);
                }
                else
                {
                    for (int i = 0; i < OwnerCount; i++)
                    {
                        scene.Roots[i] = scene.Runtime.CreateHierarchy(
                            definitions, rootId, scene.Owners[i], scopeIds[i], PresentationAnchorKind.Entity,
                            Vector3.Zero, stableIds[i], Entity.Null, definitions.Get(rootId));
                    }
                }

                scene.Sync = new PresenterEntityTransformSyncSystem(scene.World, scene.Runtime, definitions);
                scene.Behaviors = new PresenterBehaviorSystem(
                    scene.World,
                    scene.Runtime,
                    definitions,
                    new PresentationEventStream(16),
                    new PresentationOwnerChangeBuffer(4),
                    new SoundRequestBuffer(),
                    heightmap: null);
                return scene;
            }

            public Entity FindChild(int ownerIndex, int definitionId)
            {
                PresenterChildren children = World.Get<PresenterChildren>(Roots[ownerIndex]);
                for (int i = 0; i < children.Count; i++)
                {
                    Entity child = children.Get(i);
                    if (World.Get<PresenterState>(child).DefId == definitionId)
                    {
                        return child;
                    }
                }

                Assert.Fail($"Child definition {definitionId} missing under owner {ownerIndex}.");
                return Entity.Null;
            }

            public void MoveOwners(int frame)
            {
                for (int i = 0; i < Owners.Length; i++)
                {
                    ref VisualTransform visual = ref World.Get<VisualTransform>(Owners[i]);
                    visual.Position = new Vector3(3f * i + frame * 1.25f, 0.5f * frame, 10f - i - frame);
                    visual.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f * (frame + 1) + 0.05f * i);
                    visual.Scale = new Vector3(1f + 0.25f * frame, 1f, 1f);
                }
            }

            public void Tick()
            {
                Sync.Update(0.016f);
                Behaviors.Update(0.016f);
            }

            public void Dispose()
            {
                Behaviors.Dispose();
                Sync.Dispose();
                World.Dispose();
            }
        }
    }
}
