using System;
using System.Numerics;
using NUnit.Framework;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Bit-level exactness guard for the chunk-batched PresenterEntityTransformSyncSystem anchored loop:
    /// every presenter output must stay bit-identical to the per-entity evaluation
    /// (owner VisualTransform + definition offset through VisualMath/WorldPlane2D) across batched chunks,
    /// with and without FacingDirection, and across repeated syncs.
    /// </summary>
    [TestFixture]
    public sealed class PresenterEntityTransformSyncBatchExactnessTests
    {
        private const int PresenterCount = 64;

        [Test]
        public void AnchoredBatchSync_MatchesPerEntityEvaluationBitExactly()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("batch.exactness.anchor", new PresenterDefinition
            {
                PositionOffset = new Vector3(0.25f, 2f, -1.5f),
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

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);

            var owners = new Entity[PresenterCount];
            var presenters = new Entity[PresenterCount];
            for (int i = 0; i < PresenterCount; i++)
            {
                owners[i] = world.Create(
                    new VisualTransform
                    {
                        Position = new Vector3(10f + i, 0f, 20f - i),
                        Rotation = Quaternion.CreateFromYawPitchRoll(0.3f + i * 0.01f, 0.1f, -0.2f),
                        Scale = new Vector3(1.5f, 2.5f, 0.75f),
                    },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                if ((i & 1) == 0)
                {
                    world.Add(owners[i], new FacingDirection { AngleRad = 0.7f + i * 0.001f });
                }

                presenters[i] = runtime.CreateHierarchy(
                    definitions, defId, owners[i], scopeId: 1, PresentationAnchorKind.Entity,
                    worldPosition: Vector3.Zero, stableId: 9100 + i, parent: Entity.Null,
                    definitions.Get(defId));
            }

            using var syncSystem = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            using var structuralCommands = new CommandBuffer();

            SyncOnce(world, runtime, syncSystem, structuralCommands);
            MoveOwners(world, owners);
            SyncOnce(world, runtime, syncSystem, structuralCommands);
            AssertAllPresentersMatchPerEntityEvaluation(world, definitions, defId, owners, presenters);

            long allocated = GC.GetAllocatedBytesForCurrentThread();
            MoveOwners(world, owners);
            SyncOnce(world, runtime, syncSystem, structuralCommands);
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.That(allocated, Is.Zero, "steady-state anchored transform sync must not allocate");
            AssertAllPresentersMatchPerEntityEvaluation(world, definitions, defId, owners, presenters);

            SyncOnce(world, runtime, syncSystem, structuralCommands);
            AssertAllPresentersMatchPerEntityEvaluation(world, definitions, defId, owners, presenters);
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

        private static void MoveOwners(World world, Entity[] owners)
        {
            for (int i = 0; i < owners.Length; i++)
            {
                ref VisualTransform transform = ref world.Get<VisualTransform>(owners[i]);
                transform.Position = new Vector3(-40f + i * 1.25f, 3.5f, 60f - i * 0.5f);
                transform.Rotation = Quaternion.CreateFromYawPitchRoll(-1.1f + i * 0.02f, 0.4f, 0.9f);
                transform.Scale = new Vector3(0.5f + (i & 3), 1.5f, 2f);
            }
        }

        private static void AssertAllPresentersMatchPerEntityEvaluation(
            World world,
            PresenterDefinitionRegistry definitions,
            int defId,
            Entity[] owners,
            Entity[] presenters)
        {
            PresenterDefinition definition = definitions.Get(defId);
            for (int i = 0; i < presenters.Length; i++)
            {
                VisualTransform ownerTransform = world.Get<VisualTransform>(owners[i]);
                Vector3 expectedPosition = ownerTransform.Position + definition.PositionOffset;
                Vector2 expectedPlanePosition = WorldPlane2D.VisualMetersToLogicCm(in expectedPosition);
                Quaternion expectedRotation = VisualMath.NormalizeOrIdentity(ownerTransform.Rotation);
                Vector3 expectedScale = VisualMath.NormalizeScale(ownerTransform.Scale);
                PresenterWorldFacing expectedFacing = world.TryGet(owners[i], out FacingDirection ownerFacing)
                    ? new PresenterWorldFacing { AngleRad = ownerFacing.AngleRad, HasValue = 1 }
                    : default;

                AssertBits(world.Get<PresenterWorldPosition>(presenters[i]).Value, expectedPosition, $"presenter {i} position");
                AssertBits(world.Get<PresenterWorldPlanePosition>(presenters[i]).ValueCm, expectedPlanePosition, $"presenter {i} plane position");
                AssertBits(world.Get<PresenterWorldRotation>(presenters[i]).Value, expectedRotation, $"presenter {i} rotation");
                AssertBits(world.Get<PresenterWorldScale>(presenters[i]).Value, expectedScale, $"presenter {i} scale");
                Assert.That(
                    BitConverter.SingleToInt32Bits(world.Get<PresenterWorldFacing>(presenters[i]).AngleRad),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(expectedFacing.AngleRad)),
                    $"presenter {i} facing angle");
                Assert.That(world.Get<PresenterWorldFacing>(presenters[i]).HasValue, Is.EqualTo(expectedFacing.HasValue), $"presenter {i} facing presence");
            }
        }

        private static void AssertBits(Vector3 actual, Vector3 expected, string label)
        {
            Assert.That(BitConverter.SingleToInt32Bits(actual.X), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.X)), label + " X");
            Assert.That(BitConverter.SingleToInt32Bits(actual.Y), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Y)), label + " Y");
            Assert.That(BitConverter.SingleToInt32Bits(actual.Z), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Z)), label + " Z");
        }

        private static void AssertBits(Vector2 actual, Vector2 expected, string label)
        {
            Assert.That(BitConverter.SingleToInt32Bits(actual.X), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.X)), label + " X");
            Assert.That(BitConverter.SingleToInt32Bits(actual.Y), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Y)), label + " Y");
        }

        private static void AssertBits(Quaternion actual, Quaternion expected, string label)
        {
            Assert.That(BitConverter.SingleToInt32Bits(actual.X), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.X)), label + " X");
            Assert.That(BitConverter.SingleToInt32Bits(actual.Y), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Y)), label + " Y");
            Assert.That(BitConverter.SingleToInt32Bits(actual.Z), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Z)), label + " Z");
            Assert.That(BitConverter.SingleToInt32Bits(actual.W), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.W)), label + " W");
        }
    }
}
