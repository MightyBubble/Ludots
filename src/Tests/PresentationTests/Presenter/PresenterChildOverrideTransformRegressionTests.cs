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
    public sealed class PresenterChildOverrideTransformRegressionTests
    {
        [Test]
        public void InheritedStaticChildren_KeepAnchorOffsetAndTransformOverrideAfterOwnerMoves()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int overrideChildId = definitions.Register(
                "c1.override.child",
                StaticMeshDefinition(assetId: 11, VisualMobility.Static));
            int anchorChildId = definitions.Register(
                "c1.anchor.child",
                StaticMeshDefinition(assetId: 12, VisualMobility.Static, positionOffset: new Vector3(1f, 0f, 2f)));
            int rootId = definitions.Register("c1.root", new PresenterDefinition
            {
                Behaviors = [MeshSlot(0, 10, VisualMobility.Movable)],
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = overrideChildId,
                        TransformOverride = TransformOverride(new Vector3(3f, 0f, 0f), new Vector3(0.5f, 0.5f, 0.5f)),
                    },
                    new ChildPresenterRef { DefinitionId = anchorChildId },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = CreateOwner(world, new Vector3(10f, 0f, 20f), Quaternion.Identity);
            Entity root = runtime.CreateHierarchy(
                definitions, rootId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 8100, parent: Entity.Null,
                definitions.Get(rootId));
            (Entity overrideChild, Entity anchorChild) = FindOverrideAndOtherChild(world, root);

            Assert.That(world.Get<PresenterTransformSource>(root).Value, Is.EqualTo(TransformSource.EntityTransform));
            Assert.That(world.Get<PresenterTransformSource>(overrideChild).Value, Is.EqualTo(TransformSource.InheritParent));
            Assert.That(world.Get<PresenterTransformSource>(anchorChild).Value, Is.EqualTo(TransformSource.InheritParent));
            Assert.That(world.Has<PerfHasAttachmentTick>(overrideChild), Is.False);
            Assert.That(world.Has<PerfHasAttachmentTick>(anchorChild), Is.False);
            Assert.That(world.Has<PresenterInstanceTransformOverride>(overrideChild), Is.True);
            Assert.That(world.Has<PresenterInstanceTransformOverride>(anchorChild), Is.False);

            using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            Vector3[] ownerPositions =
            [
                new Vector3(30f, 0f, 40f),
                new Vector3(50f, 1f, 60f),
                new Vector3(12f, -2f, -8f),
            ];
            for (int frame = 0; frame < ownerPositions.Length; frame++)
            {
                MoveOwner(world, owner, ownerPositions[frame], Quaternion.Identity, Vector3.One);
                sync.Update(0.016f);
                AssertInheritedChild(
                    world,
                    root,
                    overrideChild,
                    definitions.Get(overrideChildId).PositionOffset,
                    world.Get<PresenterInstanceTransformOverride>(overrideChild),
                    $"frame {frame} transform override");
                AssertInheritedChild(
                    world,
                    root,
                    anchorChild,
                    definitions.Get(anchorChildId).PositionOffset,
                    default,
                    $"frame {frame} anchor offset");
            }

            MoveOwner(world, owner, new Vector3(4f, 1f, 6f), YawRadians(MathF.PI / 2f), new Vector3(2f, 2f, 2f));
            sync.Update(0.016f);
            AssertInheritedChild(
                world,
                root,
                overrideChild,
                definitions.Get(overrideChildId).PositionOffset,
                world.Get<PresenterInstanceTransformOverride>(overrideChild),
                "rotated parent keeps the child local override");
            AssertInheritedChild(
                world,
                root,
                anchorChild,
                definitions.Get(anchorChildId).PositionOffset,
                default,
                "rotated parent keeps the child anchor offset in the same space as per-entity resolve");
        }

        [Test]
        public void ProcessPresenter_ParentAttachmentComposesTransformOverride()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            var attached = AttachmentDefinition(assetId: 21, extraTickSlot: InactiveSoundSlot(2));
            int attachedId = definitions.Register("b3.process.attached", attached);
            int rootId = definitions.Register("b3.process.root", new PresenterDefinition
            {
                Behaviors = [MeshSlot(0, 20, VisualMobility.Movable)],
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = attachedId,
                        TransformOverride = TransformOverride(
                            new Vector3(0.5f, 0f, 0f),
                            new Vector3(2f, 2f, 2f),
                            YawRadians(MathF.PI / 2f)),
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = CreateOwner(world, new Vector3(1f, 0f, 1f), Quaternion.Identity);
            Entity root = runtime.CreateHierarchy(
                definitions, rootId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                Vector3.Zero, stableId: 8200, Entity.Null, definitions.Get(rootId));
            Entity child = world.Get<PresenterChildren>(root).Get(0);
            PresenterDefinition attachedDefinition = definitions.Get(attachedId);

            Assert.That(attachedDefinition.SupportsFastParentAttachmentTick, Is.False);
            Assert.That(world.Has<PerfHasAttachmentTick>(child), Is.True);
            Assert.That(world.Has<PerfOwnerPayloadAttachedTransformSync>(child), Is.False);
            Assert.That(world.Has<PresenterInstanceBehaviors>(child), Is.False);

            using var behaviors = CreateBehaviors(world, runtime, definitions);
            Quaternion parentRotation = YawRadians(MathF.PI / 2f);
            PoseParent(world, root, new Vector3(10f, 0f, 20f), parentRotation, Vector3.One);
            behaviors.Update(0.016f);
            AssertAttachmentPose(
                world,
                root,
                child,
                attachedDefinition.Behaviors[1].Attachment,
                world.Get<PresenterInstanceTransformOverride>(child),
                "ProcessPresenter attachment keeps the child local override");

            PoseParent(world, root, new Vector3(-4f, 2f, 7f), YawRadians(-MathF.PI / 2f), new Vector3(1f, 1f, 1f));
            behaviors.Update(0.016f);
            AssertAttachmentPose(
                world,
                root,
                child,
                attachedDefinition.Behaviors[1].Attachment,
                world.Get<PresenterInstanceTransformOverride>(child),
                "ProcessPresenter attachment tracks a later parent pose");
        }

        [Test]
        public void BatchTick_ParentAttachmentComposesTransformOverride()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            var attached = AttachmentDefinition(assetId: 31);
            int attachedId = definitions.Register("b3.batch.attached", attached);
            int rootId = definitions.Register("b3.batch.root", new PresenterDefinition
            {
                Behaviors = [MeshSlot(0, 30, VisualMobility.Movable)],
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = attachedId,
                        TransformOverride = TransformOverride(new Vector3(0.5f, 0f, 0f), new Vector3(2f, 3f, 4f)),
                    },
                    new ChildPresenterRef { DefinitionId = attachedId },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = CreateOwner(world, Vector3.Zero, Quaternion.Identity);
            Entity root = runtime.CreateHierarchy(
                definitions, rootId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                Vector3.Zero, stableId: 8300, Entity.Null, definitions.Get(rootId));
            (Entity overrideChild, Entity plainChild) = FindOverrideAndOtherChild(world, root);
            PresenterDefinition attachedDefinition = definitions.Get(attachedId);

            Assert.That(attachedDefinition.SupportsFastParentAttachmentTick, Is.True);
            Assert.That(world.Has<PerfHasAttachmentTick>(overrideChild), Is.True);
            Assert.That(world.Has<PerfHasAttachmentTick>(plainChild), Is.True);
            Assert.That(world.Has<PerfOwnerPayloadAttachedTransformSync>(overrideChild), Is.False);
            Assert.That(world.Has<PresenterInstanceBehaviors>(overrideChild), Is.False);
            Assert.That(world.Has<PresenterBootstrapPending>(overrideChild), Is.False);

            using var behaviors = CreateBehaviors(world, runtime, definitions);
            AttachmentConfig attachment = attachedDefinition.Behaviors[1].Attachment;
            PoseParent(world, root, new Vector3(8f, 0f, -3f), YawRadians(MathF.PI / 2f), Vector3.One);
            behaviors.Update(0.016f);
            AssertAttachmentPose(
                world, root, overrideChild, attachment,
                world.Get<PresenterInstanceTransformOverride>(overrideChild),
                "batch attachment keeps the child local override");
            AssertAttachmentPose(
                world, root, plainChild, attachment, default,
                "batch attachment leaves a child without an override on the attachment pose");

            PoseParent(world, root, new Vector3(2f, 5f, 9f), YawRadians(MathF.PI), new Vector3(1f, 1f, 1f));
            behaviors.Update(0.016f);
            AssertAttachmentPose(
                world, root, overrideChild, attachment,
                world.Get<PresenterInstanceTransformOverride>(overrideChild),
                "batch attachment tracks a later parent pose");
            AssertAttachmentPose(
                world, root, plainChild, attachment, default,
                "batch attachment still ignores a missing override after the parent moves");
        }

        [Test]
        public void FastSync_PerPresenter_ComposesTransformOverride()
        {
            (World world, PresenterEntityRuntime runtime, PresenterDefinitionRegistry definitions, Entity owner, Entity root, Entity child, AttachmentConfig attachment) =
                CreateSyncAttachmentHierarchy(includeWorldPosition: false, stableId: 8400, childAssetId: 41, rootAssetId: 40);
            using (world)
            {
                Assert.That(world.Has<PerfTransformSyncTick>(root), Is.True);
                Assert.That(world.Has<PerfOwnerPayloadTransformSync>(root), Is.False);
                Assert.That(world.Has<PerfHasAttachmentTick>(child), Is.True);
                Assert.That(world.Has<PerfOwnerPayloadAttachedTransformSync>(child), Is.False);
                Assert.That(world.Get<PresenterChildren>(root).Count, Is.EqualTo(1));

                using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
                MoveOwner(world, owner, new Vector3(6f, 1f, 4f), YawRadians(MathF.PI / 2f), Vector3.One);
                sync.Update(0.016f);
                AssertAttachmentPose(
                    world, root, child, attachment,
                    world.Get<PresenterInstanceTransformOverride>(child),
                    "per-presenter fast attachment keeps the child local override");

                MoveOwner(world, owner, new Vector3(-2f, 0f, 11f), YawRadians(MathF.PI), Vector3.One);
                sync.Update(0.016f);
                AssertAttachmentPose(
                    world, root, child, attachment,
                    world.Get<PresenterInstanceTransformOverride>(child),
                    "per-presenter fast attachment tracks a later owner pose");
            }
        }

        [Test]
        public void FastSync_OwnerPayload_ComposesTransformOverride()
        {
            (World world, PresenterEntityRuntime runtime, PresenterDefinitionRegistry definitions, Entity owner, Entity root, Entity child, AttachmentConfig attachment) =
                CreateSyncAttachmentHierarchy(includeWorldPosition: true, stableId: 8500, childAssetId: 51, rootAssetId: 50);
            using (world)
            {
                Assert.That(world.Has<PerfOwnerPayloadTransformSync>(root), Is.True);
                Assert.That(world.Has<PerfOwnerPayloadAttachedTransformSync>(child), Is.True);
                Assert.That(world.Has<PerfHasAttachmentTick>(child), Is.False);

                using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
                MoveOwner(world, owner, new Vector3(15f, -1f, 3f), YawRadians(MathF.PI / 2f), Vector3.One);
                sync.Update(0.016f);
                AssertAttachmentPose(
                    world, root, child, attachment,
                    world.Get<PresenterInstanceTransformOverride>(child),
                    "owner-payload fast attachment keeps the child local override");

                MoveOwner(world, owner, new Vector3(1f, 4f, -5f), Quaternion.Identity, Vector3.One);
                sync.Update(0.016f);
                AssertAttachmentPose(
                    world, root, child, attachment,
                    world.Get<PresenterInstanceTransformOverride>(child),
                    "owner-payload fast attachment tracks a later owner pose");
            }
        }

        [Test]
        public void Propagation_ParentAttachmentComposesTransformOverride()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            var attached = AttachmentDefinition(assetId: 61);
            int attachedId = definitions.Register("b3.propagate.attached", attached);
            int siblingId = definitions.Register(
                "b3.propagate.sibling",
                StaticMeshDefinition(assetId: 62, VisualMobility.Static));
            int rootId = definitions.Register("b3.propagate.root", new PresenterDefinition
            {
                Behaviors = [MeshSlot(0, 60, VisualMobility.Movable)],
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = attachedId,
                        TransformOverride = TransformOverride(new Vector3(0.5f, 0f, 0f), new Vector3(2f, 2f, 2f)),
                    },
                    new ChildPresenterRef { DefinitionId = siblingId },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = CreateOwner(world, new Vector3(3f, 0f, 3f), Quaternion.Identity);
            Entity root = runtime.CreateHierarchy(
                definitions, rootId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                Vector3.Zero, stableId: 8600, Entity.Null, definitions.Get(rootId));
            (Entity child, Entity sibling) = FindOverrideAndOtherChild(world, root);

            Assert.That(definitions.Get(attachedId).SupportsFastParentAttachmentTick, Is.True);
            Assert.That(world.Has<PerfHasAttachmentTick>(child), Is.True);
            Assert.That(world.Has<PerfOwnerPayloadAttachedTransformSync>(child), Is.False);
            Assert.That(world.Get<PresenterTransformSource>(sibling).Value, Is.EqualTo(TransformSource.InheritParent));
            Assert.That(world.Has<PerfHasAttachmentTick>(sibling), Is.False);

            using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            AttachmentConfig attachment = definitions.Get(attachedId).Behaviors[1].Attachment;
            MoveOwner(world, owner, new Vector3(9f, 2f, -1f), YawRadians(MathF.PI / 2f), Vector3.One);
            sync.Update(0.016f);
            AssertAttachmentPose(
                world, root, child, attachment,
                world.Get<PresenterInstanceTransformOverride>(child),
                "parent propagation keeps the attached child's local override");
            Assert.That(
                world.Get<PresenterWorldPosition>(sibling).Value,
                Is.EqualTo(world.Get<PresenterWorldPosition>(root).Value),
                "the static sibling follows the moved parent and is what forces propagation");

            MoveOwner(world, owner, new Vector3(0f, 0f, 5f), YawRadians(MathF.PI), Vector3.One);
            sync.Update(0.016f);
            AssertAttachmentPose(
                world, root, child, attachment,
                world.Get<PresenterInstanceTransformOverride>(child),
                "parent propagation tracks a later owner pose");
        }

        [Test]
        public void BoneAttachment_ComposesTransformOverride()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int attachedId = definitions.Register("b3.bone.attached", new PresenterDefinition
            {
                Behaviors =
                [
                    MeshSlot(0, 71, VisualMobility.Movable),
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Bone,
                            BoneId = 7,
                            Offset = new Vector3(0f, 1f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            });
            int rootId = definitions.Register("b3.bone.root", new PresenterDefinition
            {
                Behaviors = [MeshSlot(0, 70, VisualMobility.Movable)],
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = attachedId,
                        TransformOverride = TransformOverride(new Vector3(0.5f, 0f, 0f), new Vector3(2f, 2f, 2f)),
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = CreateOwner(world, Vector3.Zero, Quaternion.Identity);
            const int parentStableId = 8700;
            Entity root = runtime.CreateHierarchy(
                definitions, rootId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                Vector3.Zero, parentStableId, Entity.Null, definitions.Get(rootId));
            Entity child = world.Get<PresenterChildren>(root).Get(0);
            var bones = new StubBoneTransformProvider(parentStableId, boneId: 7);
            bones.Position = new Vector3(2f, 3f, 4f);
            bones.Rotation = YawRadians(MathF.PI / 2f);
            bones.Scale = Vector3.One;

            using var behaviors = CreateBehaviors(world, runtime, definitions, bones);
            behaviors.Update(0.016f);
            AssertBonePose(
                world,
                child,
                bones,
                definitions.Get(attachedId).Behaviors[1].Attachment,
                world.Get<PresenterInstanceTransformOverride>(child),
                "bone attachment keeps the child local override");

            bones.Position = new Vector3(-6f, 1f, 8f);
            bones.Rotation = YawRadians(MathF.PI);
            behaviors.Update(0.016f);
            AssertBonePose(
                world,
                child,
                bones,
                definitions.Get(attachedId).Behaviors[1].Attachment,
                world.Get<PresenterInstanceTransformOverride>(child),
                "bone attachment tracks a later bone pose");
        }

        private static void AssertInheritedChild(
            World world,
            Entity parent,
            Entity child,
            Vector3 anchorOffset,
            in PresenterInstanceTransformOverride instanceOverride,
            string message)
        {
            PresenterTransformSnapshot parentSnapshot = Snapshot(world, parent);
            PresenterTransformSnapshot childSnapshot = Snapshot(world, child);
            childSnapshot.TransformSource = TransformSource.InheritParent;
            PresenterResolvedTransform expected = PresenterGroundingUtility.ResolveTransform(
                childSnapshot,
                parentSnapshot,
                hasParent: true,
                ownerTransform: default,
                hasOwnerTransform: false,
                anchorOffset,
                instanceOverride);
            AssertClose(world.Get<PresenterWorldPosition>(child).Value, expected.Position, message);
            AssertClose(world.Get<PresenterWorldScale>(child).Value, expected.Scale, message);
            AssertRotation(world.Get<PresenterWorldRotation>(child).Value, expected.Rotation, message);
        }

        private static void AssertAttachmentPose(
            World world,
            Entity parent,
            Entity child,
            in AttachmentConfig attachment,
            in PresenterInstanceTransformOverride instanceOverride,
            string message)
        {
            Vector3 parentPosition = world.Get<PresenterWorldPosition>(parent).Value;
            Quaternion parentRotation = world.Get<PresenterWorldRotation>(parent).Value;
            Vector3 parentScale = world.Get<PresenterWorldScale>(parent).Value;
            ComposeAttachment(
                parentPosition,
                parentRotation,
                parentScale,
                attachment,
                instanceOverride,
                out Vector3 position,
                out Quaternion rotation,
                out Vector3 scale);
            Assert.That(world.Get<PresenterTransformSource>(child).Value, Is.EqualTo(TransformSource.AttachedToParent), message);
            AssertClose(world.Get<PresenterWorldPosition>(child).Value, position, message);
            AssertClose(world.Get<PresenterWorldScale>(child).Value, scale, message);
            AssertRotation(world.Get<PresenterWorldRotation>(child).Value, rotation, message);
        }

        private static void AssertBonePose(
            World world,
            Entity child,
            StubBoneTransformProvider bones,
            in AttachmentConfig attachment,
            in PresenterInstanceTransformOverride instanceOverride,
            string message)
        {
            ComposeAttachment(
                bones.Position,
                bones.Rotation,
                bones.Scale,
                attachment,
                instanceOverride,
                out Vector3 position,
                out Quaternion rotation,
                out Vector3 scale);
            Assert.That(world.Get<PresenterTransformSource>(child).Value, Is.EqualTo(TransformSource.BoneAttached), message);
            AssertClose(world.Get<PresenterWorldPosition>(child).Value, position, message);
            AssertClose(world.Get<PresenterWorldScale>(child).Value, scale, message);
            AssertRotation(world.Get<PresenterWorldRotation>(child).Value, rotation, message);
        }

        private static void ComposeAttachment(
            Vector3 parentPosition,
            Quaternion parentRotation,
            Vector3 parentScale,
            in AttachmentConfig attachment,
            in PresenterInstanceTransformOverride instanceOverride,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            Quaternion normalizedParentRotation = VisualMath.NormalizeOrIdentity(parentRotation);
            Vector3 normalizedParentScale = VisualMath.NormalizeScale(parentScale);
            Vector3 scaledOffset = attachment.InheritScale
                ? normalizedParentScale * attachment.Offset
                : attachment.Offset;
            position = parentPosition + Vector3.Transform(scaledOffset, normalizedParentRotation);
            rotation = VisualMath.NormalizeOrIdentity(
                normalizedParentRotation * VisualMath.NormalizeOrIdentity(attachment.RotationOffset));
            scale = attachment.InheritScale ? normalizedParentScale : Vector3.One;
            if (!instanceOverride.HasOverride)
            {
                return;
            }

            position += Vector3.Transform(instanceOverride.LocalPosition, rotation);
            rotation = VisualMath.NormalizeOrIdentity(
                rotation * VisualMath.NormalizeOrIdentity(instanceOverride.LocalRotation));
            scale *= VisualMath.NormalizeScale(instanceOverride.LocalScale);
        }

        private static (
            World world,
            PresenterEntityRuntime runtime,
            PresenterDefinitionRegistry definitions,
            Entity owner,
            Entity root,
            Entity child,
            AttachmentConfig attachment) CreateSyncAttachmentHierarchy(
            bool includeWorldPosition,
            int stableId,
            int childAssetId,
            int rootAssetId)
        {
            var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            var attached = AttachmentDefinition(childAssetId);
            int attachedId = definitions.Register($"b3.sync.{stableId}.attached", attached);
            int rootId = definitions.Register($"b3.sync.{stableId}.root", new PresenterDefinition
            {
                Behaviors = [MeshSlot(0, rootAssetId, VisualMobility.Movable)],
                Children =
                [
                    new ChildPresenterRef
                    {
                        DefinitionId = attachedId,
                        TransformOverride = TransformOverride(new Vector3(0.5f, 0f, 0f), new Vector3(2f, 2f, 2f)),
                    },
                ],
            });
            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = CreateOwner(world, new Vector3(10f, 0f, 20f), Quaternion.Identity, includeWorldPosition);
            Entity root = runtime.CreateHierarchy(
                definitions, rootId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                Vector3.Zero, stableId, Entity.Null, definitions.Get(rootId));
            Entity child = world.Get<PresenterChildren>(root).Get(0);
            return (world, runtime, definitions, owner, root, child, attached.Behaviors[1].Attachment);
        }

        private static PresenterDefinition AttachmentDefinition(int assetId, BehaviorSlot? extraTickSlot = null)
        {
            BehaviorSlot[] behaviors = extraTickSlot == null
                ? [MeshSlot(0, assetId, VisualMobility.Movable), ParentAttachmentSlot(1)]
                : [MeshSlot(0, assetId, VisualMobility.Movable), ParentAttachmentSlot(1), extraTickSlot.Value];
            return new PresenterDefinition { Behaviors = behaviors };
        }

        private static PresenterDefinition StaticMeshDefinition(int assetId, VisualMobility mobility, Vector3 positionOffset = default)
        {
            return new PresenterDefinition
            {
                PositionOffset = positionOffset,
                Behaviors = [MeshSlot(0, assetId, mobility)],
            };
        }

        private static BehaviorSlot MeshSlot(int slot, int assetId, VisualMobility mobility)
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
                    Mobility = mobility,
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
                    Offset = new Vector3(0f, 1f, 0f),
                    RotationOffset = Quaternion.Identity,
                    InheritScale = false,
                },
            };
        }

        private static BehaviorSlot InactiveSoundSlot(int slot)
        {
            return new BehaviorSlot
            {
                SlotIndex = slot,
                Kind = BehaviorKind.Sound,
                ActiveByDefault = true,
                Sound = new SoundConfig { SoundAssetId = 0 },
            };
        }

        private static PresenterInstanceTransformOverride TransformOverride(Vector3 localPosition, Vector3 localScale, Quaternion? localRotation = null)
        {
            return new PresenterInstanceTransformOverride
            {
                LocalPosition = localPosition,
                LocalRotation = localRotation ?? Quaternion.Identity,
                LocalScale = localScale,
                HasOverride = true,
            };
        }

        private static Entity CreateOwner(World world, Vector3 position, Quaternion rotation, bool includeWorldPosition = false)
        {
            var visual = new VisualTransform
            {
                Position = position,
                Rotation = rotation,
                Scale = Vector3.One,
            };
            var cull = new CullState { IsVisible = true, LOD = LODLevel.High };
            if (!includeWorldPosition)
            {
                return world.Create(visual, cull);
            }

            return world.Create(visual, cull, WorldPositionCm.FromCm(1000, 2000));
        }

        private static void MoveOwner(World world, Entity owner, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            ref VisualTransform visual = ref world.Get<VisualTransform>(owner);
            visual.Position = position;
            visual.Rotation = rotation;
            visual.Scale = scale;
        }

        private static void PoseParent(World world, Entity parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            world.Get<PresenterWorldPosition>(parent).Value = position;
            world.Get<PresenterWorldRotation>(parent).Value = rotation;
            world.Get<PresenterWorldScale>(parent).Value = scale;
        }

        private static (Entity overrideChild, Entity otherChild) FindOverrideAndOtherChild(World world, Entity parent)
        {
            PresenterChildren children = world.Get<PresenterChildren>(parent);
            Entity overrideChild = Entity.Null;
            Entity otherChild = Entity.Null;
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (world.Has<PresenterInstanceTransformOverride>(child))
                {
                    overrideChild = child;
                }
                else
                {
                    otherChild = child;
                }
            }

            Assert.That(overrideChild, Is.Not.EqualTo(Entity.Null));
            Assert.That(otherChild, Is.Not.EqualTo(Entity.Null));
            return (overrideChild, otherChild);
        }

        private static PresenterTransformSnapshot Snapshot(World world, Entity presenter)
        {
            return new PresenterTransformSnapshot
            {
                WorldPosition = world.Get<PresenterWorldPosition>(presenter).Value,
                WorldRotation = world.Get<PresenterWorldRotation>(presenter).Value,
                WorldScale = world.Get<PresenterWorldScale>(presenter).Value,
                WorldFacing = world.Get<PresenterWorldFacing>(presenter),
                TransformSource = world.Get<PresenterTransformSource>(presenter).Value,
            };
        }

        private static PresenterBehaviorSystem CreateBehaviors(
            World world,
            PresenterEntityRuntime runtime,
            PresenterDefinitionRegistry definitions,
            IBoneTransformProvider? bones = null)
        {
            return new PresenterBehaviorSystem(
                world,
                runtime,
                definitions,
                new PresentationEventStream(16),
                new PresentationOwnerChangeBuffer(4),
                new SoundRequestBuffer(),
                heightmap: null,
                boneTransformProvider: bones);
        }

        private static Quaternion YawRadians(float radians)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians);
        }

        private static void AssertClose(Vector3 actual, Vector3 expected, string message)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f), message + $" x actual={actual} expected={expected}");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f), message + $" y actual={actual} expected={expected}");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f), message + $" z actual={actual} expected={expected}");
        }

        private static void AssertRotation(Quaternion actual, Quaternion expected, string message)
        {
            float dot = MathF.Abs(Quaternion.Dot(VisualMath.NormalizeOrIdentity(actual), VisualMath.NormalizeOrIdentity(expected)));
            Assert.That(dot, Is.EqualTo(1f).Within(0.0001f), message + $" rotation actual={actual} expected={expected}");
        }

        private sealed class StubBoneTransformProvider : IBoneTransformProvider
        {
            private readonly int _presenterStableId;
            private readonly int _boneId;

            public StubBoneTransformProvider(int presenterStableId, int boneId)
            {
                _presenterStableId = presenterStableId;
                _boneId = boneId;
            }

            public Vector3 Position { get; set; }

            public Quaternion Rotation { get; set; } = Quaternion.Identity;

            public Vector3 Scale { get; set; } = Vector3.One;

            public bool TryGetBoneWorldTransform(int presenterStableId, int boneId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
            {
                position = Position;
                rotation = Rotation;
                scale = Scale;
                return presenterStableId == _presenterStableId && boneId == _boneId;
            }
        }
    }
}
