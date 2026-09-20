using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterTrailMeshBehaviorTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void TrailMesh_CueActivatesSampling_WeaponSwingWeavesArc_DeactivateFadesOut()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int parentDefId = definitions.Register("trail.parent", new PresenterDefinition());
            int childDefId = definitions.Register("trail.blade", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Parent,
                            Offset = new Vector3(0f, 1.4f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 17,
                        Kind = BehaviorKind.TrailMesh,
                        ActiveByDefault = false,
                        TrailMesh = new TrailMeshConfig
                        {
                            BaseOffset = Vector3.Zero,
                            TipOffset = new Vector3(0f, 0f, 1.2f),
                            MaxSamples = 8,
                            SampleIntervalSeconds = 0f,
                            SampleLifetimeSeconds = 0.3f,
                            HeadColor = Vector4.One,
                            TailColor = new Vector4(1f, 1f, 1f, 0f),
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var commands = new PresenterCommandBuffer();
            var events = new PresentationEventStream(64);
            var buffer = new TrailMeshBuffer(capacity: 8);
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity parent = runtime.CreateHierarchy(
                definitions, parentDefId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7100, parent: Entity.Null,
                definitions.Get(parentDefId));
            Entity child = runtime.CreateHierarchy(
                definitions, childDefId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7101, parent,
                definitions.Get(childDefId));

            using var commandRuntime = new PresenterRuntimeSystem(
                world, commands, events, new TransientMarkerBuffer(), new PresentationRequestBuffer(),
                runtime, new PresentationStableIdAllocator(), definitions);
            using var syncSystem = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            using var behaviorSystem = new PresenterBehaviorSystem(
                world, runtime, definitions, events, new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer(), trailMeshBuffer: buffer);

            syncSystem.Update(Dt);
            behaviorSystem.Update(Dt);
            AssertVec3(
                world.Get<PresenterWorldPosition>(child).Value,
                new Vector3(10f, 1.4f, 20f), 0.001f);
            Assert.That(buffer.Count, Is.EqualTo(0), "trail slot starts inactive; no sampling before the cue");

            var activate = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.ActivateBehavior,
                PresenterEntity = child,
                TargetBehaviorSlot = 17,
            };
            Assert.That(commands.TryAdd(in activate), Is.True);
            commandRuntime.Update(Dt);
            behaviorSystem.Update(Dt);
            // 激活帧只做 re-bootstrap（同 Spline）；首个采样落在下一帧 tick-driven pass。
            behaviorSystem.Update(Dt);

            Assert.That(buffer.Count, Is.EqualTo(1), "ActivateBehavior cue must start trail sampling");
            ReadOnlySpan<TrailMeshSample> first = buffer.GetSamples(0);
            Assert.That(buffer.GetStableId(0), Is.EqualTo(7101));
            Assert.That(first.Length, Is.EqualTo(1));
            AssertVec3(first[0].Base, new Vector3(10f, 1.4f, 20f), 0.001f);
            AssertVec3(first[0].Tip, new Vector3(10f, 1.4f, 21.2f), 0.001f);

            world.Get<VisualTransform>(owner).Rotation =
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
            syncSystem.Update(Dt);
            behaviorSystem.Update(Dt);

            ReadOnlySpan<TrailMeshSample> swung = buffer.GetSamples(0);
            Assert.That(swung.Length, Is.EqualTo(2), "weapon swing must append a new arc segment");
            AssertVec3(
                swung[0].Tip,
                new Vector3(11.2f, 1.4f, 20f), 0.01f);
            Assert.That(swung[0].Age01, Is.EqualTo(0f));
            Assert.That(swung[1].Age01, Is.GreaterThan(0f).And.LessThan(0.2f));

            var deactivate = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DeactivateBehavior,
                PresenterEntity = child,
                TargetBehaviorSlot = 17,
            };
            Assert.That(commands.TryAdd(in deactivate), Is.True);
            commandRuntime.Update(Dt);

            for (int i = 0; i < 30; i++)
            {
                behaviorSystem.Update(Dt);
            }

            Assert.That(buffer.Count, Is.EqualTo(0), "after the cue ends, retained samples must age out and the trail must leave the buffer");
        }

        [Test]
        public void TrailMesh_SampleIntervalGatesAppends_HeadPinnedWithinInterval()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int parentDefId = definitions.Register("trail.parent", new PresenterDefinition());
            int childDefId = definitions.Register("trail.blade.interval", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Parent,
                            Offset = new Vector3(0f, 1.4f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 17,
                        Kind = BehaviorKind.TrailMesh,
                        ActiveByDefault = false,
                        TrailMesh = new TrailMeshConfig
                        {
                            BaseOffset = Vector3.Zero,
                            TipOffset = new Vector3(0f, 0f, 1.2f),
                            MaxSamples = 8,
                            SampleIntervalSeconds = 0.27f,
                            SampleLifetimeSeconds = 1f,
                            HeadColor = Vector4.One,
                            TailColor = new Vector4(1f, 1f, 1f, 0f),
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var commands = new PresenterCommandBuffer();
            var events = new PresentationEventStream(64);
            var buffer = new TrailMeshBuffer(capacity: 8);
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity parent = runtime.CreateHierarchy(
                definitions, parentDefId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7200, parent: Entity.Null,
                definitions.Get(parentDefId));
            Entity child = runtime.CreateHierarchy(
                definitions, childDefId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7201, parent,
                definitions.Get(childDefId));

            using var commandRuntime = new PresenterRuntimeSystem(
                world, commands, events, new TransientMarkerBuffer(), new PresentationRequestBuffer(),
                runtime, new PresentationStableIdAllocator(), definitions);
            using var syncSystem = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            using var behaviorSystem = new PresenterBehaviorSystem(
                world, runtime, definitions, events, new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer(), trailMeshBuffer: buffer);

            syncSystem.Update(Dt);
            behaviorSystem.Update(Dt);

            var activate = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.ActivateBehavior,
                PresenterEntity = child,
                TargetBehaviorSlot = 17,
            };
            Assert.That(commands.TryAdd(in activate), Is.True);
            commandRuntime.Update(Dt);
            behaviorSystem.Update(Dt);
            behaviorSystem.Update(Dt);
            Assert.That(buffer.Count, Is.EqualTo(1), "first sample lands one frame after activation re-bootstrap");

            world.Get<VisualTransform>(owner).Rotation =
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
            syncSystem.Update(Dt);
            for (int i = 0; i < 16; i++)
            {
                behaviorSystem.Update(Dt);
            }

            ReadOnlySpan<TrailMeshSample> pinned = buffer.GetSamples(0);
            Assert.That(pinned.Length, Is.EqualTo(1), "sub-interval resamples must pin the head in place");
            AssertVec3(
                pinned[0].Tip,
                new Vector3(11.2f, 1.4f, 20f), 0.01f);

            // Appends every 17 frames (17/60 = 0.2833 >= interval 0.27):
            // t=20/60 appends, then 15 frames of sub-interval pinning (16/60 = 0.2667 < 0.27),
            // then t=37/60 appends again.
            behaviorSystem.Update(Dt);
            ReadOnlySpan<TrailMeshSample> grown = buffer.GetSamples(0);
            Assert.That(grown.Length, Is.EqualTo(2), "once the interval elapses a new arc segment appends");

            for (int i = 0; i < 15; i++)
            {
                behaviorSystem.Update(Dt);
            }

            ReadOnlySpan<TrailMeshSample> stillPinned = buffer.GetSamples(0);
            Assert.That(stillPinned.Length, Is.EqualTo(2), "interval gating holds until the next append boundary");
            for (int i = 0; i < 2; i++)
            {
                behaviorSystem.Update(Dt);
            }

            Assert.That(buffer.GetSamples(0).Length, Is.EqualTo(3), "past the interval the head appends again");
        }

        [Test]
        public void TrailMesh_DefinitionWithNullBehaviors_DoesNotFailAtSetup()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("trail.no.behaviors", new PresenterDefinition
            {
                Behaviors = null!,
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = Vector3.Zero,
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7103, parent: Entity.Null,
                definitions.Get(defId));

            // 定义没有 TrailMesh 行为：即使没有接线 TrailMeshBuffer，setup 也必须正常完成，
            // 只有“存在 TrailMesh 行为且 buffer 缺失”才 fail-fast。
            Assert.DoesNotThrow(
                () => new PresenterBehaviorSystem(
                    world, runtime, definitions, new PresentationEventStream(64),
                    new PresentationOwnerChangeBuffer(8), new SoundRequestBuffer()));
        }

        [Test]
        public void TrailMesh_DefinitionWithoutWiredBuffer_FailsAtSetup()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("trail.unwired", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 17,
                        Kind = BehaviorKind.TrailMesh,
                        ActiveByDefault = true,
                        TrailMesh = new TrailMeshConfig
                        {
                            BaseOffset = Vector3.Zero,
                            TipOffset = Vector3.UnitZ,
                            MaxSamples = 4,
                            SampleIntervalSeconds = 0f,
                            SampleLifetimeSeconds = 0.3f,
                            HeadColor = Vector4.One,
                            TailColor = Vector4.Zero,
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = Vector3.Zero,
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7102, parent: Entity.Null,
                definitions.Get(defId));

            Assert.Throws<InvalidOperationException>(
                () => new PresenterBehaviorSystem(
                    world, runtime, definitions, new PresentationEventStream(64),
                    new PresentationOwnerChangeBuffer(8), new SoundRequestBuffer()),
                "TrailMesh definitions without a wired buffer must fail at service composition, not on first tick");
        }

        private static void AssertVec3(Vector3 actual, Vector3 expected, float tolerance)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance));
        }
    }
}
