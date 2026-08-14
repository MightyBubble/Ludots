using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterCompiledLaneTests
    {
        [Test]
        public void Register_CompilesAttributeAndTagBindings_WithSortedThresholds()
        {
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("compiled.binding.table", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = 7,
                            TargetParamKey = 100,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds =
                            [
                                new ThresholdMapping { Threshold = 0.66f, OutputParamKey = 101, OutputValue = 1f },
                                new ThresholdMapping { Threshold = 0f, OutputParamKey = 101, OutputValue = 2f },
                            ],
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig
                        {
                            TagId = 3,
                            TargetParamKey = 110,
                            InvertLogic = true,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 2,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1,
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            Assert.That(definitions.TryGet(defId, out PresenterDefinition definition), Is.True);
            Assert.That(definition.BehaviorPresenceMask, Is.EqualTo((1u << 0) | (1u << 1) | (1u << 2)));
            Assert.That(definition.CompiledBindings.Length, Is.EqualTo(2));

            ref readonly CompiledBinding attribute = ref definition.CompiledBindings[0];
            Assert.That(attribute.IsAttributeBound, Is.True);
            Assert.That(attribute.SourceAttributeId, Is.EqualTo(7));
            Assert.That(attribute.SourceTagId, Is.EqualTo(CompiledBinding.UnboundSourceId));
            Assert.That(attribute.TargetParamKey, Is.EqualTo(100));
            Assert.That(attribute.Mode, Is.EqualTo(ValueSourceKind.AttributeRatio));
            Assert.That(attribute.Thresholds.Length, Is.EqualTo(2));
            Assert.That(attribute.Thresholds[0].Threshold, Is.EqualTo(0f));
            Assert.That(attribute.Thresholds[0].OutputValue, Is.EqualTo(2f));
            Assert.That(attribute.Thresholds[1].Threshold, Is.EqualTo(0.66f));
            Assert.That(attribute.Thresholds[1].OutputValue, Is.EqualTo(1f));

            ref readonly CompiledBinding tag = ref definition.CompiledBindings[1];
            Assert.That(tag.IsTagBound, Is.True);
            Assert.That(tag.SourceTagId, Is.EqualTo(3));
            Assert.That(tag.InvertLogic, Is.True);
            Assert.That(tag.TargetParamKey, Is.EqualTo(110));

            Assert.That(definition.TryGetOwnerAttributeWork(7, out PresenterDefinition.OwnerAttributeWorkItem attributeWork), Is.True);
            Assert.That(attributeWork.CompiledBindingIndices, Is.EqualTo(new[] { 0 }));
            Assert.That(definition.TryGetOwnerTagWork(3, out PresenterDefinition.OwnerTagWorkItem tagWork), Is.True);
            Assert.That(tagWork.CompiledBindingIndices, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void DirtySync_ReadsCompiledThresholdTable_NotAuthoredOrder()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 0f);
            Entity owner = world.Create(attributes);

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("compiled.threshold.dirty", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = 7,
                            TargetParamKey = 200,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds =
                            [
                                new ThresholdMapping { Threshold = 0.66f, OutputParamKey = 201, OutputValue = 1f },
                                new ThresholdMapping { Threshold = 0f, OutputParamKey = 201, OutputValue = 2f },
                            ],
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9301, Entity.Null, default);
            world.Add(presenter, new PresenterBootstrapPending());
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveFloat(presenter, 200), Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                instances.ResolveInt(presenter, 201),
                Is.EqualTo(2),
                "Compiled thresholds sort low-to-high, so ratio 0 must hit the ruined mapping instead of the first authored row.");

            ref AttributeBuffer updated = ref world.Get<AttributeBuffer>(owner);
            updated.SetCurrent(7, 50f);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Attribute, 7)), Is.True);
            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(presenter, 200), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(instances.ResolveInt(presenter, 201), Is.EqualTo(1));
        }

        [Test]
        public void SetBehaviorActive_IgnoresSlotsMissingFromPresenceMask()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("compiled.presence.mask", new PresenterDefinition
            {
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
                            AssetId = 1,
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            PresenterDefinition definition = definitions.Get(defId);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9302, Entity.Null, definition);
            Assert.That(definition.BehaviorPresenceMask, Is.EqualTo(1u));
            Assert.That(instances.SetBehaviorActive(presenter, definition, slotIndex: 3, active: true), Is.False);
            Assert.That(world.Get<PresenterState>(presenter).BehaviorActiveMask & (1u << 3), Is.EqualTo(0u));
        }

        [Test]
        public void StaticStallsStayPut_WhileWalkersKeepMoving()
        {
            const int stallCount = 48;
            const int walkerCount = 4;

            using var world = World.Create();
            var runtime = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PresenterVisualStableIdTable(stableIds, capacity: stallCount + walkerCount);
            var stableDrawCache = new StableDrawCache(stallCount + walkerCount);

            int stallDefId = definitions.Register("compiled.lane.stall", CreateStaticStallDefinition());
            int walkerDefId = definitions.Register("compiled.lane.walker", CreateWalkerDefinition());
            PresenterDefinition stallDefinition = definitions.Get(stallDefId);
            PresenterDefinition walkerDefinition = definitions.Get(walkerDefId);

            Assert.That(stallDefinition.UsesEventDrivenStaticEmit, Is.True);
            Assert.That(stallDefinition.TickBehaviorIndices, Is.Empty);
            Assert.That(walkerDefinition.UsesEventDrivenStaticEmit, Is.False);

            var stallPresenters = new Entity[stallCount];
            var stallPositions = new Vector3[stallCount];
            for (int i = 0; i < stallCount; i++)
            {
                Vector3 position = new(i * 2f, 0f, 0f);
                Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                stallPresenters[i] = runtime.Create(
                    stallDefId,
                    owner,
                    i,
                    PresentationAnchorKind.WorldPosition,
                    position,
                    20_000 + i,
                    Entity.Null,
                    stallDefinition);
                world.Get<PresenterState>(stallPresenters[i]).BehaviorActiveMask = 1u;
                stallPositions[i] = position;
            }

            var walkerPresenters = new Entity[walkerCount];
            var walkerOwners = new Entity[walkerCount];
            for (int i = 0; i < walkerCount; i++)
            {
                Vector3 start = new(i * 3f, 0f, 8f);
                walkerOwners[i] = world.Create(
                    WorldPositionCm.FromCm((int)(start.X * 100f), (int)(start.Z * 100f)),
                    new VisualTransform { Position = start, Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                walkerPresenters[i] = runtime.Create(
                    walkerDefId,
                    walkerOwners[i],
                    100 + i,
                    PresentationAnchorKind.Entity,
                    start,
                    30_000 + i,
                    Entity.Null,
                    walkerDefinition);
                world.Get<PresenterState>(walkerPresenters[i]).BehaviorActiveMask = 1u;
            }

            using var behavior = new PresenterBehaviorSystem(
                world,
                runtime,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());
            using var transformSync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            using var emit = new PresenterEmitSystem(
                world,
                runtime,
                definitions,
                requests,
                new Dictionary<string, object>(),
                stableDrawCache: stableDrawCache,
                visualStableIds: visualStableIds);

            behavior.Update(0.016f);
            transformSync.Update(0.016f);
            emit.Update(0.016f);

            Assert.That(CountStaticStable(world, stallPresenters), Is.EqualTo(stallCount), "Stalls must freeze onto the event-driven static lane after the first draw.");
            Assert.That(CountStaticStable(world, walkerPresenters), Is.EqualTo(0), "Walkers must stay on the continuous projection lane.");
            int firstCacheCount = stableDrawCache.Count;
            int firstRevision = stableDrawCache.ContentRevision;
            Assert.That(firstCacheCount, Is.EqualTo(stallCount), "Only the standing stalls belong in the frozen draw cache.");

            requests.Clear();
            for (int frame = 0; frame < 8; frame++)
            {
                for (int i = 0; i < walkerCount; i++)
                {
                    Vector3 next = new((i * 3f) + ((frame + 1) * 0.4f), 0f, 8f);
                    world.Get<VisualTransform>(walkerOwners[i]).Position = next;
                    world.Get<WorldPositionCm>(walkerOwners[i]).Value = WorldPositionCm.FromCm((int)(next.X * 100f), (int)(next.Z * 100f)).Value;
                }

                behavior.Update(0.016f);
                transformSync.Update(0.016f);
                emit.Update(0.016f);
            }

            for (int i = 0; i < stallCount; i++)
            {
                Assert.That(world.Get<PresenterWorldPosition>(stallPresenters[i]).Value, Is.EqualTo(stallPositions[i]));
                Assert.That(world.Has<PerfStaticStableVisual>(stallPresenters[i]), Is.True);
                Assert.That(world.Has<PerfTransformSyncTick>(stallPresenters[i]), Is.False);
            }

            for (int i = 0; i < walkerCount; i++)
            {
                Vector3 expected = new((i * 3f) + (8 * 0.4f), 0f, 8f);
                Assert.That(world.Get<PresenterWorldPosition>(walkerPresenters[i]).Value, Is.EqualTo(expected));
            }

            Assert.That(stableDrawCache.Count, Is.EqualTo(firstCacheCount), "Standing stalls must not flicker out of the frozen draw cache.");
            Assert.That(stableDrawCache.ContentRevision, Is.EqualTo(firstRevision), "Standing stalls must not be rewritten while nobody touches them.");
            Assert.That(CountTickDriven(world), Is.EqualTo(0), "Frozen stalls have no continuous-tick work; walkers move through transform sync, not behavior rescan.");
        }

        private static PresenterDefinition CreateStaticStallDefinition()
        {
            return new PresenterDefinition
            {
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
                            AssetId = 501,
                            MaterialId = 601,
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            };
        }

        private static PresenterDefinition CreateWalkerDefinition()
        {
            return new PresenterDefinition
            {
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
                            AssetId = 502,
                            MaterialId = 602,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            };
        }

        private static int CountStaticStable(World world, Entity[] presenters)
        {
            int count = 0;
            for (int i = 0; i < presenters.Length; i++)
            {
                if (world.Has<PerfStaticStableVisual>(presenters[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountTickDriven(World world)
        {
            var query = new QueryDescription()
                .WithAll<PresenterState, PresenterWorldPosition, PresenterWorldPlanePosition>()
                .WithAny<PerfHasSpline, PerfHasAttachmentTick, PerfHasGrounding, PerfHasSound, PerfHasOwnerFacingBinding>()
                .WithNone<PresenterBootstrapPending>();
            int count = 0;
            world.Query(in query, (ref PresenterState _) => count++);
            return count;
        }
    }
}
