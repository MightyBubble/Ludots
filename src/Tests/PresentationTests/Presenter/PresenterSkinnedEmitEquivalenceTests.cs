using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterSkinnedEmitEquivalenceTests
    {
        private const int Count = 512;
        private const int WarmupFrames = 4;
        private const string LaneDefinitionKey = "equivalence.agent.skinned_lane";
        private const string GenericDefinitionKey = "equivalence.agent.skinned_generic";

        [Test]
        public void SkinnedStaticBatchLane_MatchesGenericPerEntityPath()
        {
            using World laneWorldHolder = CreateSkinnedWorld(singleAssetSlot: true, out SkinnedEmitWorldState laneState);
            using World genericWorldHolder = CreateSkinnedWorld(singleAssetSlot: false, out SkinnedEmitWorldState genericState);

            EmitFrames(laneState);
            EmitFrames(genericState);

            ReadOnlySpan<SkinnedVisualBatchItem> laneItems = laneState.Batch.GetSpan();
            ReadOnlySpan<SkinnedVisualBatchItem> genericItems = genericState.Batch.GetSpan();
            int laneDefId = laneState.Definitions.GetId(LaneDefinitionKey);
            int genericDefId = genericState.Definitions.GetId(GenericDefinitionKey);

            Assert.That(laneItems.Length, Is.EqualTo(Count), "Direct batch lane must emit one item per presenter.");
            Assert.That(genericItems.Length, Is.EqualTo(Count), "Generic per-entity path must emit one item per presenter.");
            Assert.That(laneState.Requests.Count, Is.EqualTo(0), "Skinned-lane presenters must not fall back to the proxy request lane.");
            Assert.That(genericState.Requests.Count, Is.EqualTo(0));

            var genericByOwner = new Dictionary<int, SkinnedVisualBatchItem>(Count);
            foreach (ref readonly SkinnedVisualBatchItem genericItem in genericItems)
            {
                genericByOwner[genericItem.OwnerStableId] = genericItem;
            }

            Assert.That(genericByOwner.Count, Is.EqualTo(Count), "Generic items must cover every presenter owner.");

            for (int i = 0; i < Count; i++)
            {
                ref readonly SkinnedVisualBatchItem laneItem = ref laneItems[i];
                Assert.That(genericByOwner.TryGetValue(laneItem.OwnerStableId, out SkinnedVisualBatchItem genericItem), Is.True, $"lane item {i} owner must exist in generic output");
                int presenterStableId = laneItem.OwnerStableId - 10_000 + 90_000;

                AssertPayloadsBitEqual(in laneItem.Payload, in genericItem.Payload, $"item {i} dynamic fields");
                Assert.That(laneItem.LOD, Is.EqualTo(genericItem.LOD), $"item {i} lod");

                Assert.That(laneItem.StableId,
                    Is.EqualTo(PresenterBehaviorRuntimeUtility.ComposeVisualStableId(presenterStableId, 1, AssetKind.SkinnedMesh, laneDefId)),
                    $"item {i} lane stable id must follow the per-slot composition contract");
                Assert.That(genericItem.StableId,
                    Is.EqualTo(PresenterBehaviorRuntimeUtility.ComposeVisualStableId(presenterStableId, 1, AssetKind.SkinnedMesh, genericDefId)),
                    $"item {i} generic stable id must follow the per-slot composition contract");
                Assert.That(laneItem.TemplateId, Is.EqualTo(laneDefId), $"item {i} lane template id");
                Assert.That(genericItem.TemplateId, Is.EqualTo(genericDefId), $"item {i} generic template id");
                Assert.That(laneItem.StableId, Is.Not.EqualTo(presenterStableId), $"item {i} stable id must differ from the presenter stable id");
            }

            AssertEmitCachesMatchContract(laneState.World);
        }

        [Test]
        public void SkinnedStaticBatchLane_RawBytes_MatchDeterministicSnapshot()
        {
            using World worldHolder = CreateSkinnedWorld(singleAssetSlot: true, out SkinnedEmitWorldState state);
            EmitFrames(state);

            ReadOnlySpan<SkinnedVisualBatchItem> items = state.Batch.GetSpan();
            Assert.That(items.Length, Is.EqualTo(Count));
            long hash = Fnv1a(items);
            TestContext.Out.WriteLine($"skinned-batch-lane-fnv1a={hash} items={items.Length}");
            Assert.That(hash, Is.Not.EqualTo(0L));
        }

        [Test]
        public void SkinnedStaticBatchLane_InactiveSlot_AndLodCull_SkipBatchWrite()
        {
            using World worldHolder = CreateSkinnedWorld(singleAssetSlot: true, out SkinnedEmitWorldState state, addContractEdges: true);
            EmitFrames(state);

            Assert.That(state.Batch.Count, Is.EqualTo(Count - 2), "Inactive slot and LOD-culled presenters must not write batch items.");
            Assert.That(state.Batch.DroppedSinceClear, Is.EqualTo(0));
        }

        private static void AssertPayloadsBitEqual(in VisualRenderPayload lane, in VisualRenderPayload generic, string context)
        {
            Assert.That(FloatBits(in lane.Position), Is.EqualTo(FloatBits(in generic.Position)), $"{context} position");
            Assert.That(FloatBits(in lane.Rotation), Is.EqualTo(FloatBits(in generic.Rotation)), $"{context} rotation");
            Assert.That(FloatBits(in lane.Scale), Is.EqualTo(FloatBits(in generic.Scale)), $"{context} scale");
            Assert.That(FloatBits(in lane.Color), Is.EqualTo(FloatBits(in generic.Color)), $"{context} color");
            Assert.That(lane.MeshAssetId, Is.EqualTo(generic.MeshAssetId), $"{context} mesh asset");
            Assert.That(lane.OwnerStableId, Is.EqualTo(generic.OwnerStableId), $"{context} owner stable id");
            Assert.That(lane.MaterialId, Is.EqualTo(generic.MaterialId), $"{context} material");
            Assert.That(lane.AnimationProfileId, Is.EqualTo(generic.AnimationProfileId), $"{context} animation profile");
            Assert.That(lane.RenderPath, Is.EqualTo(generic.RenderPath), $"{context} render path");
            Assert.That(lane.AssetKind, Is.EqualTo(generic.AssetKind), $"{context} asset kind");
            Assert.That(lane.SortId, Is.EqualTo(generic.SortId), $"{context} sort id");
            Assert.That(ReferenceEquals(lane.SurfaceLayerKey, generic.SurfaceLayerKey), Is.True, $"{context} surface layer key must be the same def-owned reference");
            Assert.That( lane.MaterialCustomData.Count, Is.EqualTo(0), $"{context} material custom data count");
            AssertPayloadSlotsZero(in lane.MaterialCustomData, context);
            Assert.That(lane.Animator, Is.EqualTo(generic.Animator), $"{context} animator packed state");
            Assert.That(lane.AnimationOverlay, Is.EqualTo(generic.AnimationOverlay), $"{context} animation overlay");
            Assert.That(lane.Visibility, Is.EqualTo(generic.Visibility), $"{context} visibility");
        }

        private static void AssertPayloadSlotsZero(in MaterialCustomDataPayload payload, string context)
        {
            Assert.That(payload.Slot0, Is.EqualTo(Vector4.Zero), $"{context} custom slot 0");
            Assert.That(payload.Slot1, Is.EqualTo(Vector4.Zero), $"{context} custom slot 1");
            Assert.That(payload.Slot2, Is.EqualTo(Vector4.Zero), $"{context} custom slot 2");
            Assert.That(payload.Slot3, Is.EqualTo(Vector4.Zero), $"{context} custom slot 3");
        }

        private static int[] FloatBits(in Vector3 value)
        {
            return
            [
                BitConverter.SingleToInt32Bits(value.X),
                BitConverter.SingleToInt32Bits(value.Y),
                BitConverter.SingleToInt32Bits(value.Z),
            ];
        }

        private static int[] FloatBits(in Vector4 value)
        {
            return
            [
                BitConverter.SingleToInt32Bits(value.X),
                BitConverter.SingleToInt32Bits(value.Y),
                BitConverter.SingleToInt32Bits(value.Z),
                BitConverter.SingleToInt32Bits(value.W),
            ];
        }

        private static int[] FloatBits(in Quaternion value)
        {
            return
            [
                BitConverter.SingleToInt32Bits(value.X),
                BitConverter.SingleToInt32Bits(value.Y),
                BitConverter.SingleToInt32Bits(value.Z),
                BitConverter.SingleToInt32Bits(value.W),
            ];
        }

        private static long Fnv1a(ReadOnlySpan<SkinnedVisualBatchItem> items)
        {
            ulong hash = 0xcbf29ce484222325UL;
            foreach (ref readonly SkinnedVisualBatchItem item in items)
            {
                Mix(ref hash, FloatBits(in item.Payload.Position));
                Mix(ref hash, FloatBits(in item.Payload.Rotation));
                Mix(ref hash, FloatBits(in item.Payload.Scale));
                Mix(ref hash, FloatBits(in item.Payload.Color));
                Mix(ref hash, FloatBits(in item.Payload.AnimationOverlay.BaseClip));
                Mix(ref hash, FloatBits(in item.Payload.AnimationOverlay.LayerClip));
                Mix(ref hash, FloatBits(in item.Payload.AnimationOverlay.OverlayClip));
                Mix(ref hash,
                [
                    item.StableId,
                    item.OwnerStableId,
                    item.MeshAssetId,
                    item.MaterialId,
                    item.TemplateId,
                    item.AnimationProfileId,
                    (int)item.RenderPath,
                    (int)item.AssetKind,
                    item.SortId,
                    (int)item.Visibility,
                    (int)item.LOD,
                    item.Payload.MaterialCustomData.Count,
                    unchecked((int)item.Animator.Word0),
                    unchecked((int)(item.Animator.Word0 >> 32)),
                    unchecked((int)item.Animator.Word1),
                    unchecked((int)(item.Animator.Word1 >> 32)),
                ]);
            }

            return unchecked((long)hash);
        }

        private static int[] FloatBits(in AnimationChannelState channel)
        {
            return
            [
                channel.ChannelId,
                BitConverter.SingleToInt32Bits(channel.NormalizedTime01),
                BitConverter.SingleToInt32Bits(channel.Weight01),
                BitConverter.SingleToInt32Bits(channel.Scalar0),
                BitConverter.SingleToInt32Bits(channel.Scalar1),
            ];
        }

        private static void Mix(ref ulong hash, int[] values)
        {
            foreach (int value in values)
            {
                hash ^= (ulong)(uint)value;
                hash *= 0x100000001b3UL;
            }
        }

        private static void EmitFrames(in SkinnedEmitWorldState state)
        {
            using var animatorSystem = new AnimatorRuntimeSystem(state.World, state.Controllers, state.Runtime, state.Definitions, state.AnimatorStates);
            using var emitSystem = new PresenterEmitSystem(
                state.World,
                state.Runtime,
                state.Definitions,
                state.Requests,
                new Dictionary<string, object>(),
                state.AnimatorStates,
                soundRequests: null,
                timingDiagnostics: null,
                stableDrawCache: null,
                skinnedVisualBatchBuffer: state.Batch);

            const float dt = 1f / 30f;
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                animatorSystem.Update(dt);
                emitSystem.Update(dt);
            }
        }

        private static World CreateSkinnedWorld(bool singleAssetSlot, out SkinnedEmitWorldState state, bool addContractEdges = false)
        {
            World world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "equivalence.agent.locomotion",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 3, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                    ],
                    Transitions = [],
                });

            var definitions = new PresenterDefinitionRegistry();
            string definitionKey = singleAssetSlot ? LaneDefinitionKey : GenericDefinitionKey;
            int defId = definitions.Register(
                definitionKey,
                new PresenterDefinition
                {
                    AnimationProfileId = 55,
                    Behaviors = BuildBehaviors(singleAssetSlot, controllerId),
                });

            var instances = new PresenterEntityRuntime(world);
            var animatorStates = new PresenterAnimatorStateBuffer(Count + 8);
            var requests = new PresentationRequestBuffer();
            var batch = new SkinnedVisualBatchBuffer(Count + 64);

            instances.BindDefinitions(definitions);
            instances.BindAnimatorStates(animatorStates);

            uint random = 0x2545F491u;
            int created = 0;
            while (created < Count - (addContractEdges ? 2 : 0))
            {
                Entity presenter = CreatePresenter(world, instances, defId, controllerId: 0, ref random, created, LODLevel.High);
                _ = presenter;
                created++;
            }

            if (addContractEdges)
            {
                CreatePresenter(world, instances, defId, controllerId: 0, ref random, created, LODLevel.Low);
                created++;
                Entity inactivePresenter = CreatePresenter(world, instances, defId, controllerId: 0, ref random, created, LODLevel.High);
                ref PresenterState inactiveState = ref world.Get<PresenterState>(inactivePresenter);
                inactiveState.BehaviorActiveMask &= ~(1u << 1);
                created++;
            }

            state = new SkinnedEmitWorldState(
                world,
                controllers,
                definitions,
                instances,
                animatorStates,
                requests,
                batch,
                defId,
                controllerId);
            return world;
        }

        private static Entity CreatePresenter(World world, PresenterEntityRuntime instances, int defId, int controllerId, ref uint random, int index, LODLevel lod)
        {
            random = Next(random);
            float x = ((random >> 8) % 400) * 0.5f - 100f;
            float z = ((random >> 20) % 400) * 0.5f - 100f;
            Entity owner = world.Create(
                new PresentationStableId { Value = 10_000 + index },
                new VisualTransform
                {
                    Position = new Vector3(x, 0f, z),
                    Rotation = Quaternion.CreateFromYawPitchRoll((random % 360) * MathF.PI / 180f, 0f, 0f),
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity presenter = instances.Create(
                defId,
                owner,
                scopeId: index + 1,
                PresentationAnchorKind.Entity,
                new Vector3(x, 0f, z),
                stableId: 90_000 + index,
                Entity.Null,
                definition: null);
            world.Get<PresenterWorldRotation>(presenter).Value =
                Quaternion.CreateFromYawPitchRoll((random % 360) * MathF.PI / 180f, 0f, 0f);
            world.Get<PresenterCullState>(presenter).LOD = lod;
            return presenter;
        }

        private static BehaviorSlot[] BuildBehaviors(bool singleAssetSlot, int controllerId)
        {
            var assetSlot = new BehaviorSlot
            {
                SlotIndex = 1,
                Kind = BehaviorKind.AssetBinding,
                ActiveByDefault = true,
                Style = new BehaviorStyleConfig { Color = new Vector4(0.38f, 0.72f, 1f, 1f), HasColor = true },
                AssetBinding = new AssetBindingConfig
                {
                    AssetKind = AssetKind.SkinnedMesh,
                    AssetId = 401,
                    MaterialId = 402,
                    RenderPath = VisualRenderPath.GpuSkinnedInstance,
                    Mobility = VisualMobility.Movable,
                    LocalScale = new Vector3(0.45f, 0.45f, 0.45f),
                    HasMaxLod = true,
                    MaxLod = LODLevel.Medium,
                },
            };

            if (singleAssetSlot)
            {
                return [BuildAnimatorSlot(controllerId), assetSlot];
            }

            return
            [
                BuildAnimatorSlot(controllerId),
                assetSlot,
                new BehaviorSlot
                {
                    SlotIndex = 2,
                    Kind = BehaviorKind.AssetBinding,
                    ActiveByDefault = false,
                    AssetBinding = new AssetBindingConfig
                    {
                        AssetKind = AssetKind.SkinnedMesh,
                        AssetId = 403,
                        MaterialId = 404,
                        RenderPath = VisualRenderPath.GpuSkinnedInstance,
                        Mobility = VisualMobility.Movable,
                        LocalScale = Vector3.One,
                    },
                },
            ];
        }

        private static BehaviorSlot BuildAnimatorSlot(int controllerId)
        {
            return new BehaviorSlot
            {
                SlotIndex = 0,
                Kind = BehaviorKind.Animator,
                ActiveByDefault = true,
                Animator = new AnimatorConfig
                {
                    AnimatorControllerId = controllerId,
                    AnimationProfileId = 55,
                    SpeedParamKey = -1,
                    StateParamKey = -1,
                },
            };
        }

        private static void AssertEmitCachesMatchContract(World world)
        {
            var query = new QueryDescription().WithAll<PresenterState, PresenterEmitCache>();
            int checkedPresenters = 0;
            foreach (ref var chunk in world.Query(in query))
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterEmitCache> emitCaches = chunk.GetSpan<PresenterEmitCache>();
                foreach (int index in chunk)
                {
                    Assert.That(states[index].Elapsed, Is.GreaterThan(0f), "emit must advance elapsed time");
                    ref readonly PresenterEmitCache emitCache = ref emitCaches[index];
                    Assert.That(emitCache.CachedVersion, Is.EqualTo(states[index].Version), "emit cache must record the state version");
                    Assert.That(emitCache.LastOwnerCullVisible, Is.EqualTo(1));
                    Assert.That(emitCache.StableVisualPresent, Is.EqualTo(0));
                    Assert.That(emitCache.RetainedRequestPresent, Is.EqualTo(0));
                    checkedPresenters++;
                }
            }

            Assert.That(checkedPresenters, Is.EqualTo(Count));
        }

        private static uint Next(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private readonly record struct SkinnedEmitWorldState(
            World World,
            AnimatorControllerRegistry Controllers,
            PresenterDefinitionRegistry Definitions,
            PresenterEntityRuntime Runtime,
            PresenterAnimatorStateBuffer AnimatorStates,
            PresentationRequestBuffer Requests,
            SkinnedVisualBatchBuffer Batch,
            int DefId,
            int ControllerId);
    }
}
