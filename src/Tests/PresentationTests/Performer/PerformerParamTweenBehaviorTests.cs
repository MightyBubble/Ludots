using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Tweening;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerParamTweenBehaviorTests
    {
        [Test]
        public void FloatTween_TransitionsThroughStartDelayActiveAndCompletedStages()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = definitions.Register(
                "param_tween.float_stages",
                DefinitionWithTween(
                    slotIndex: 3,
                    paramKey: 701,
                    from: 2f,
                    to: 10f,
                    durationSeconds: 1f,
                    delaySeconds: 0.25f));
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                world.Create(),
                scopeId: 0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                stableId: 1,
                Entity.Null,
                definitions.Get(definitionId));

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);

            system.Update(0f);
            Assert.That(runtime.ResolveFloat(performer, 701), Is.EqualTo(2f));

            system.Update(0.25f);
            Assert.That(runtime.ResolveFloat(performer, 701), Is.EqualTo(2f));

            system.Update(0.5f);
            Assert.That(runtime.ResolveFloat(performer, 701), Is.EqualTo(6f).Within(0.0001f));

            system.Update(0.5f);
            Assert.That(runtime.ResolveFloat(performer, 701), Is.EqualTo(10f));

            runtime.SetParam(performer, 701, ParamLane.Float, 4f, 0, Vector4.Zero);
            system.Update(0.1f);
            Assert.That(runtime.ResolveFloat(performer, 701), Is.EqualTo(10f),
                "An active completed tween owns its lane and holds the declared end value.");
        }

        [Test]
        public void VectorTween_LoopPingPongAlternatesDirectionWithoutEndpointDiscontinuity()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = definitions.Register(
                "param_tween.vector_ping_pong",
                new PerformerDefinition
                {
                    Behaviors =
                    [
                        new BehaviorSlot
                        {
                            SlotIndex = 1,
                            Kind = BehaviorKind.ParamTween,
                            ActiveByDefault = true,
                            ParamTween = new ParamTweenConfig
                            {
                                ParamKey = 702,
                                Lane = ParamLane.Vector,
                                FromVector = Vector4.Zero,
                                ToVector = new Vector4(2f, 4f, 6f, 8f),
                                DurationSeconds = 1f,
                                Easing = TweenEasing.Linear,
                                Loop = true,
                                PingPong = true,
                            },
                        },
                    ],
                });
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                world.Create(),
                0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                2,
                Entity.Null,
                definitions.Get(definitionId));

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            system.Update(0f);
            system.Update(0.5f);
            Assert.That(runtime.ResolveVector(performer, 702, new Vector4(-1f)), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));

            system.Update(0.5f);
            Assert.That(runtime.ResolveVector(performer, 702, Vector4.Zero), Is.EqualTo(new Vector4(2f, 4f, 6f, 8f)));

            system.Update(0.5f);
            Assert.That(runtime.ResolveVector(performer, 702, Vector4.Zero), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));

            system.Update(0.5f);
            Assert.That(runtime.ResolveVector(performer, 702, new Vector4(-1f)), Is.EqualTo(Vector4.Zero));
        }

        [Test]
        public void CutTween_HoldsStartDuringDelayThenSnapsToEnd()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = definitions.Register(
                "param_tween.cut_delay",
                new PerformerDefinition
                {
                    Behaviors =
                    [
                        new BehaviorSlot
                        {
                            SlotIndex = 2,
                            Kind = BehaviorKind.ParamTween,
                            ActiveByDefault = true,
                            ParamTween = new ParamTweenConfig
                            {
                                ParamKey = 709,
                                Lane = ParamLane.Float,
                                FromFloat = 3f,
                                ToFloat = 9f,
                                DurationSeconds = 0f,
                                DelaySeconds = 0.5f,
                                Easing = TweenEasing.Cut,
                            },
                        },
                    ],
                });
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                world.Create(),
                0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                20,
                Entity.Null,
                definitions.Get(definitionId));

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            system.Update(0f);
            system.Update(0.49f);
            Assert.That(runtime.ResolveFloat(performer, 709), Is.EqualTo(3f));

            system.Update(0.01f);
            Assert.That(runtime.ResolveFloat(performer, 709), Is.EqualTo(9f));
        }

        [Test]
        public void DeactivateThenActivate_RestartsTweenFromDeclaredStartValue()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = definitions.Register(
                "param_tween.restart",
                DefinitionWithTween(4, 703, 0f, 10f, 1f));
            PerformerDefinition definition = definitions.Get(definitionId);
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                world.Create(),
                0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                3,
                Entity.Null,
                definition);

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            system.Update(0f);
            system.Update(0.5f);
            Assert.That(runtime.ResolveFloat(performer, 703), Is.EqualTo(5f));

            Assert.That(runtime.SetBehaviorActive(performer, definition, 4, active: false), Is.True);
            system.Update(0.25f);
            Assert.That(runtime.ResolveFloat(performer, 703), Is.EqualTo(5f));

            Assert.That(runtime.SetBehaviorActive(performer, definition, 4, active: true), Is.True);
            system.Update(0f);
            Assert.That(runtime.ResolveFloat(performer, 703), Is.Zero);
        }

        [Test]
        public void DefinitionReplacement_ClearsExistingTweenStateAndReplaysFromNewDeclaredStart()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            const string definitionKey = "param_tween.hot_reload";
            const int slotIndex = 5;
            const int paramKey = 711;
            int definitionId = definitions.Register(
                definitionKey,
                DefinitionWithTween(slotIndex, paramKey, 2f, 10f, 1f));
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                world.Create(),
                0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                31,
                Entity.Null,
                definitions.Get(definitionId));

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            system.Update(0f);
            system.Update(1f);

            uint slotBit = 1u << slotIndex;
            PerformerParamTweenState completedState = world.Get<PerformerParamTweenState>(performer);
            Assert.Multiple(() =>
            {
                Assert.That(completedState.StartedMask & slotBit, Is.EqualTo(slotBit));
                Assert.That(completedState.CompletedMask & slotBit, Is.EqualTo(slotBit));
                Assert.That(completedState.ValidatedTargetMask & slotBit, Is.EqualTo(slotBit));
                Assert.That(completedState.GetElapsedSeconds(slotIndex), Is.EqualTo(1f));
                Assert.That(runtime.ResolveFloat(performer, paramKey), Is.EqualTo(10f));
            });

            int replacedDefinitionId = definitions.Register(
                definitionKey,
                DefinitionWithTween(slotIndex, paramKey, 20f, 40f, 2f));
            runtime.BindDefinitions(definitions);

            PerformerParamTweenState resetState = world.Get<PerformerParamTweenState>(performer);
            Assert.Multiple(() =>
            {
                Assert.That(replacedDefinitionId, Is.EqualTo(definitionId));
                Assert.That(resetState.StartedMask, Is.Zero);
                Assert.That(resetState.CompletedMask, Is.Zero);
                Assert.That(resetState.ValidatedTargetMask, Is.Zero);
                Assert.That(resetState.GetElapsedSeconds(slotIndex), Is.Zero);
            });

            system.Update(0f);
            Assert.That(runtime.ResolveFloat(performer, paramKey), Is.EqualTo(20f));

            system.Update(0.5f);
            Assert.That(runtime.ResolveFloat(performer, paramKey), Is.EqualTo(25f).Within(0.0001f));
        }

        [Test]
        public void TwoActiveTweensForSameLaneAndKey_FailBeforeEitherWrites()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = definitions.Register(
                "param_tween.conflict",
                new PerformerDefinition
                {
                    Behaviors =
                    [
                        TweenSlot(0, 704, 0f, 1f),
                        TweenSlot(1, 704, 1f, 2f),
                    ],
                });
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                world.Create(),
                0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                4,
                Entity.Null,
                definitions.Get(definitionId));

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(exception.Message, Does.Contain("slots 0 and 1"));
            Assert.That(runtime.TryResolveFloat(performer, 704, out _), Is.False);
        }

        [Test]
        [Description(
            "Feature: Performer parameter stage lifecycle\n" +
            "Scenario: A scoped parent finishes a presentation tween and is removed\n" +
            "Given a parent performer with a child sharing its parameter scope\n" +
            "When the tween reaches its end and the scope is destroyed\n" +
            "Then the child sees the final value and both performers are removed")]
        public void Feature_ScopedParentTween_GivenChild_WhenCompletedAndScopeDestroyed_ThenChildReadsEndValueAndTreeIsCleaned()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int childDefinitionId = definitions.Register("param_tween.scope_child", new PerformerDefinition());
            int rootDefinitionId = definitions.Register(
                "param_tween.scope_root",
                new PerformerDefinition
                {
                    Behaviors = [TweenSlot(2, 705, 0f, 1f)],
                    Children =
                    [
                        new ChildPerformerRef
                        {
                            DefinitionId = childDefinitionId,
                            ScopeTag = 0,
                            ParamOverrides = Array.Empty<ParamDefault>(),
                        },
                    ],
                });
            runtime.BindDefinitions(definitions);
            Entity root = runtime.CreateHierarchy(
                definitions,
                rootDefinitionId,
                world.Create(),
                scopeId: 42,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                stableId: 5,
                Entity.Null,
                definitions.Get(rootDefinitionId),
                allocateStableId: () => 6);
            Entity child = world.Get<PerformerChildren>(root).Get(0);

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            system.Update(0f);
            system.Update(1f);

            Assert.That(runtime.ResolveFloat(child, 705), Is.EqualTo(1f));
            runtime.DestroyScope(42);
            Assert.Multiple(() =>
            {
                Assert.That(world.IsAlive(root), Is.False);
                Assert.That(world.IsAlive(child), Is.False);
                Assert.That(runtime.ActiveCount, Is.Zero);
            });
        }

        [Test]
        public void Tick_ProcessesPerformerChunksWithZeroManagedAllocationsAfterWarmup()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            PerformerDefinition definition = DefinitionWithTween(0, 706, 0f, 1f, 1f, loop: true);
            int definitionId = definitions.Register("param_tween.chunk_zero_alloc", definition);
            runtime.BindDefinitions(definitions);
            Entity owner = world.Create();
            var performers = new Entity[256];
            for (int i = 0; i < performers.Length; i++)
            {
                performers[i] = runtime.Create(
                    definitionId,
                    owner,
                    0,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    i + 10,
                    Entity.Null,
                    definition);
            }

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            system.Update(0f);
            // Measure the steady-state Tier 1 path, not tiered-JIT promotion work.
            for (int i = 0; i < 128; i++)
            {
                system.Update(1f / 60f);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                system.Update(1f / 60f);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero);
                Assert.That(runtime.ResolveFloat(performers[0], 706), Is.InRange(0f, 1f));
                Assert.That(runtime.ResolveFloat(performers[^1], 706), Is.EqualTo(runtime.ResolveFloat(performers[0], 706)));
            });
        }

        [Test]
        public void EntityAnchoredRootBatch_CreatesTweenStateAndTicksEveryCreatedPerformer()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            PerformerDefinition definition = DefinitionWithTween(0, 710, 0f, 1f, 1f);
            int definitionId = definitions.Register("param_tween.batch", definition);
            runtime.BindDefinitions(definitions);

            const int count = 64;
            var owners = new Entity[count];
            var scopes = new int[count];
            var stableIds = new int[count];
            var transforms = new VisualTransform[count];
            var culls = new CullState[count];
            var created = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                transforms[i] = new VisualTransform
                {
                    Position = new Vector3(i, 0f, 0f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                };
                culls[i] = new CullState { IsVisible = true, LOD = LODLevel.High };
                owners[i] = world.Create(transforms[i], culls[i]);
                scopes[i] = 100 + i;
                stableIds[i] = 1000 + i;
            }

            int createdCount = runtime.CreateEntityAnchoredRootBatch(
                definitions,
                definitionId,
                owners,
                scopes,
                stableIds,
                transforms,
                culls,
                definition,
                created);

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            system.Update(0f);
            system.Update(0.5f);

            Assert.That(createdCount, Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(world.Has<PerformerParamTweenState>(created[i]), Is.True);
                    Assert.That(world.Has<PerfHasParamTween>(created[i]), Is.True);
                    Assert.That(runtime.ResolveFloat(created[i], 710), Is.EqualTo(0.5f).Within(0.0001f));
                });
            }
        }

        [Test]
        public void TweenStart_FailsExplicitlyWhenTargetLaneCapacityIsFull()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = definitions.Register(
                "param_tween.capacity",
                DefinitionWithTween(0, 999, 0f, 1f, 1f));
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                world.Create(),
                0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                30,
                Entity.Null,
                definitions.Get(definitionId));
            for (int key = 1; key <= PerformerFloatParams.MAX_ENTRIES; key++)
            {
                runtime.SetParam(performer, key, ParamLane.Float, key, 0, Vector4.Zero);
            }

            using PerformerBehaviorSystem system = CreateSystem(world, runtime, definitions);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(exception.Message, Does.Contain("float param capacity 16 is full"));
            Assert.That(runtime.TryResolveFloat(performer, 999, out _), Is.False);
        }

        [Test]
        public void Registry_RejectsDiscreteIntTweenAndTweenDrivenMaterialSelection()
        {
            var definitions = new PerformerDefinitionRegistry();
            var intTween = new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.ParamTween,
                        ActiveByDefault = true,
                        ParamTween = new ParamTweenConfig
                        {
                            ParamKey = 707,
                            Lane = ParamLane.Int,
                            DurationSeconds = 1f,
                            Easing = TweenEasing.Linear,
                        },
                    },
                ],
            };

            InvalidOperationException intException = Assert.Throws<InvalidOperationException>(
                () => definitions.Register("param_tween.invalid_int", intTween))!;
            Assert.That(intException.Message, Does.Contain("SetParam commands"));

            var materialTween = new PerformerDefinition
            {
                Behaviors =
                [
                    TweenSlot(0, 708, 0f, 1f),
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Material,
                        ActiveByDefault = true,
                        Material = new MaterialConfig
                        {
                            BaseMaterialId = 1,
                            MaterialSwapParamKey = 708,
                            SwapTable = [new MaterialSwapEntry { ParamValue = 1f, MaterialId = 2 }],
                        },
                    },
                ],
            };

            InvalidOperationException materialException = Assert.Throws<InvalidOperationException>(
                () => definitions.Register("param_tween.invalid_material", materialTween))!;
            Assert.That(materialException.Message, Does.Contain("discrete material selection"));
        }

        private static PerformerBehaviorSystem CreateSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions)
        {
            return new PerformerBehaviorSystem(
                world,
                runtime,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());
        }

        private static PerformerDefinition DefinitionWithTween(
            int slotIndex,
            int paramKey,
            float from,
            float to,
            float durationSeconds,
            float delaySeconds = 0f,
            bool loop = false)
        {
            BehaviorSlot slot = TweenSlot(slotIndex, paramKey, from, to);
            slot.ParamTween.DurationSeconds = durationSeconds;
            slot.ParamTween.DelaySeconds = delaySeconds;
            slot.ParamTween.Loop = loop;
            return new PerformerDefinition { Behaviors = [slot] };
        }

        private static BehaviorSlot TweenSlot(int slotIndex, int paramKey, float from, float to)
        {
            return new BehaviorSlot
            {
                SlotIndex = slotIndex,
                Kind = BehaviorKind.ParamTween,
                ActiveByDefault = true,
                ParamTween = new ParamTweenConfig
                {
                    ParamKey = paramKey,
                    Lane = ParamLane.Float,
                    FromFloat = from,
                    ToFloat = to,
                    DurationSeconds = 1f,
                    Easing = TweenEasing.Linear,
                },
            };
        }
    }
}
