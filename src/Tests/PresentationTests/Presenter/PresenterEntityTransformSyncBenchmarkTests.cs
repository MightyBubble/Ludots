using System;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Headless Stopwatch evidence for the chunk-batched PresenterEntityTransformSyncSystem anchored loop (#1501):
    /// massnav production shape — 10K owners, each with 1 anchored root presenter plus 2 Parent-attached
    /// children (attachment fast path), every owner moving every frame; prints per-frame median/p95 and
    /// asserts steady-state zero allocation.
    /// </summary>
    [TestFixture]
    public sealed class PresenterEntityTransformSyncBenchmarkTests
    {
        private const int OwnerCount = 10_000;
        private const int WarmupFrames = 16;
        private const int MeasuredFrames = 200;

        [Test]
        public void HeadlessBenchmark_AnchoredTransformSync_RootsWithAttachedChildren_PrintsMedianP95()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int rootDefId = definitions.Register("benchmark.anchored.transform.sync.root", new PresenterDefinition
            {
                PositionOffset = new Vector3(0f, 2f, 0f),
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 91,
                            MaterialId = 92,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });
            int childDefId = definitions.Register("benchmark.anchored.transform.sync.child", new PresenterDefinition
            {
                PositionOffset = Vector3.Zero,
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 93,
                            MaterialId = 94,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Parent,
                            Offset = new Vector3(0f, 1f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = true,
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);

            var owners = new Entity[OwnerCount];
            var roots = new Entity[OwnerCount];
            int stableId = 20_000;
            for (int i = 0; i < OwnerCount; i++)
            {
                owners[i] = world.Create(
                    new VisualTransform
                    {
                        Position = new Vector3(10f + i, 0f, 20f - i),
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                    },
                    new CullState { IsVisible = true, LOD = LODLevel.High });

                Entity root = runtime.CreateHierarchy(
                    definitions, rootDefId, owners[i], scopeId: 1, PresentationAnchorKind.Entity,
                    worldPosition: Vector3.Zero, stableId: stableId++, parent: Entity.Null,
                    definitions.Get(rootDefId));
                roots[i] = root;
                runtime.CreateHierarchy(
                    definitions, childDefId, owners[i], scopeId: 1, PresentationAnchorKind.Entity,
                    worldPosition: Vector3.Zero, stableId: stableId++, parent: root,
                    definitions.Get(childDefId));
                runtime.CreateHierarchy(
                    definitions, childDefId, owners[i], scopeId: 1, PresentationAnchorKind.Entity,
                    worldPosition: Vector3.Zero, stableId: stableId++, parent: root,
                    definitions.Get(childDefId));
            }

            using var syncSystem = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            using var structuralCommands = new CommandBuffer();

            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                MoveOwners(world, owners, frame);
                SyncOnce(world, runtime, syncSystem, structuralCommands);
            }

            var samples = new double[MeasuredFrames];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                MoveOwners(world, owners, frame);
                long start = Stopwatch.GetTimestamp();
                SyncOnce(world, runtime, syncSystem, structuralCommands);
                samples[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.That(allocated, Is.Zero, "steady-state anchored transform sync must not allocate");

            Vector3 spotPosition = world.Get<PresenterWorldPosition>(roots[0]).Value;
            Vector3 expectedSpot = world.Get<VisualTransform>(owners[0]).Position + definitions.Get(rootDefId).PositionOffset;
            Assert.That(spotPosition, Is.EqualTo(expectedSpot), "benchmark must keep syncing real transforms");

            Array.Sort(samples);
            TestContext.Out.WriteLine(
                $"PresenterEntityTransformSyncSystem headless {OwnerCount} owners x (1 anchored root + 2 attached children), all owners moving: median={samples[MeasuredFrames / 2]:F4}ms p95={samples[(int)(MeasuredFrames * 0.95)]:F4}ms");
        }

        private static void MoveOwners(World world, Entity[] owners, int frame)
        {
            float delta = (frame % 7) * 0.25f + 0.5f;
            for (int i = 0; i < owners.Length; i++)
            {
                ref VisualTransform transform = ref world.Get<VisualTransform>(owners[i]);
                transform.Position = new Vector3(transform.Position.X + delta, 0f, transform.Position.Z - delta * 0.5f);
            }
        }

        private static void SyncOnce(
            World world,
            PresenterEntityRuntime runtime,
            PresenterEntityTransformSyncSystem syncSystem,
            CommandBuffer structuralCommands)
        {
            runtime.BeginDeferredStructuralChanges(structuralCommands);
            try
            {
                syncSystem.Update(0.016f);
            }
            finally
            {
                runtime.EndDeferredStructuralChanges(structuralCommands);
            }

            structuralCommands.Playback(world);
        }
    }
}
