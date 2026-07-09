using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Mathematics;
using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class AttributeAggregatorSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _withDirtyFlagsQuery = new QueryDescription()
            .WithAll<AttributeBuffer, ActiveEffectContainer, AttributeAggregateDirty, DirtyFlags>();

        private static readonly QueryDescription _withoutDirtyFlagsQuery = new QueryDescription()
            .WithAll<AttributeBuffer, ActiveEffectContainer, AttributeAggregateDirty>()
            .WithNone<DirtyFlags>();

        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly GasGraphOpHandlerTable _graphHandlers;
        private readonly CommandBuffer _commandBuffer = new();

        public AttributeAggregatorSystem(
            World world,
            GraphProgramRegistry graphPrograms = null,
            IGraphRuntimeApi graphApi = null,
            GasGraphOpHandlerTable graphHandlers = null) : base(world)
        {
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _graphHandlers = graphHandlers;
        }

        public override unsafe void Update(in float dt)
        {
            var withDirtyJob = new AttributeAggregatorWithDirtyJob
            {
                World = World,
                CommandBuffer = _commandBuffer,
                GraphPrograms = _graphPrograms,
                GraphApi = _graphApi,
                GraphHandlers = _graphHandlers,
            };
            World.InlineEntityQuery<AttributeAggregatorWithDirtyJob, AttributeBuffer, ActiveEffectContainer, DirtyFlags>(in _withDirtyFlagsQuery, ref withDirtyJob);

            var withoutDirtyJob = new AttributeAggregatorWithoutDirtyJob
            {
                World = World,
                CommandBuffer = _commandBuffer,
                GraphPrograms = _graphPrograms,
                GraphApi = _graphApi,
                GraphHandlers = _graphHandlers,
            };
            World.InlineEntityQuery<AttributeAggregatorWithoutDirtyJob, AttributeBuffer, ActiveEffectContainer>(in _withoutDirtyFlagsQuery, ref withoutDirtyJob);

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void ExecuteDerivedGraphs(
            World world, Entity entity,
            GraphProgramRegistry graphPrograms, IGraphRuntimeApi graphApi, GasGraphOpHandlerTable graphHandlers)
        {
            if (!world.Has<AttributeDerivedGraphBinding>(entity)) return;

            ref var binding = ref world.Get<AttributeDerivedGraphBinding>(entity);
            if (binding.Count <= 0) return;
            if (binding.Count > AttributeDerivedGraphBinding.MAX_BINDINGS)
            {
                throw new InvalidOperationException(
                    $"AttributeDerivedGraphBinding count {binding.Count} exceeds capacity {AttributeDerivedGraphBinding.MAX_BINDINGS}.");
            }

            if (graphPrograms == null || graphApi == null || graphHandlers == null)
            {
                throw new InvalidOperationException(
                    "AttributeDerivedGraphBinding requires configured graph program registry, graph runtime API, and graph handler table.");
            }

            for (int g = 0; g < binding.Count; g++)
            {
                int programId = binding.GraphProgramIds[g];
                if (programId <= 0)
                {
                    throw new InvalidOperationException(
                        $"AttributeDerivedGraphBinding contains invalid graph program id {programId}.");
                }

                if (!graphPrograms.TryGetProgram(programId, out var program))
                {
                    throw new InvalidOperationException(
                        $"AttributeDerivedGraphBinding references missing graph program {programId}.");
                }

                NodeLibraries.GASGraph.GraphExecutor.Execute(
                    world,
                    caster: entity,        // E[0] = Self
                    explicitTarget: entity, // E[1] = Self (derived graphs operate on self)
                    targetPos: default,
                    program,
                    graphApi,
                    graphHandlers);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong RecomputeEffectiveValues(
            World world,
            Entity entity,
            ref AttributeBuffer attrBuffer,
            ref ActiveEffectContainer effects,
            GraphProgramRegistry graphPrograms,
            IGraphRuntimeApi graphApi,
            GasGraphOpHandlerTable graphHandlers)
        {
            ulong touchedMask = 0UL;

            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                if (attrBuffer.CapValues[i] != attrBuffer.BaseValues[i])
                {
                    touchedMask |= 1UL << i;
                }

                attrBuffer.CurrentValues[i] = attrBuffer.BaseValues[i];
            }

            if (effects.Count > 0)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    Entity effectEntity = effects.GetEntity(i);
                    if (!world.IsAlive(effectEntity))
                    {
                        continue;
                    }

                    if (world.Has<GameplayEffect>(effectEntity))
                    {
                        ref readonly GameplayEffect effect = ref world.Get<GameplayEffect>(effectEntity);
                        if (effect.CancelRequested ||
                            effect.State < EffectState.Committed ||
                            !effect.AggregatesModifiers)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    ref readonly var modifiers = ref world.Get<EffectModifiers>(effectEntity);
                    touchedMask |= BuildTouchedMask(in modifiers);
                    EffectModifierOps.ApplyAggregated(in modifiers, ref attrBuffer);
                }
            }

            Span<float> beforeDerived = stackalloc float[AttributeBuffer.MAX_ATTRS];
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                beforeDerived[i] = attrBuffer.CurrentValues[i];
            }

            ExecuteDerivedGraphs(world, entity, graphPrograms, graphApi, graphHandlers);

            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                if (beforeDerived[i] != attrBuffer.CurrentValues[i])
                {
                    touchedMask |= 1UL << i;
                }
            }

            return touchedMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong BuildTouchedMask(in EffectModifiers modifiers)
        {
            ulong mask = 0UL;
            for (int i = 0; i < modifiers.Count; i++)
            {
                int attributeId = modifiers.Get(i).AttributeId;
                if ((uint)attributeId < AttributeBuffer.MAX_ATTRS)
                {
                    mask |= 1UL << attributeId;
                }
            }

            return mask;
        }

        struct AttributeAggregatorWithDirtyJob : IForEachWithEntity<AttributeBuffer, ActiveEffectContainer, DirtyFlags>
        {
            public World World;
            public CommandBuffer CommandBuffer;
            public GraphProgramRegistry GraphPrograms;
            public IGraphRuntimeApi GraphApi;
            public GasGraphOpHandlerTable GraphHandlers;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void Update(Entity entity, ref AttributeBuffer attrBuffer, ref ActiveEffectContainer effects, ref DirtyFlags dirtyFlags)
            {
                Span<float> oldValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    oldValues[i] = attrBuffer.CurrentValues[i];
                }

                ulong touchedMask = RecomputeEffectiveValues(
                    World,
                    entity,
                    ref attrBuffer,
                    ref effects,
                    GraphPrograms,
                    GraphApi,
                    GraphHandlers);
                RestorePersistentCurrentValues(ref attrBuffer, oldValues, touchedMask);
                bool hasPresentationChanged = World.Has<GameplayAttributeChangedBits>(entity);
                GameplayAttributeChangedBits presentationChangedLocal = default;

                // 4. 标记脏属性（用于延迟触发器）
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    if (oldValues[i] != attrBuffer.CurrentValues[i])
                    {
                        dirtyFlags.MarkAttributeDirty(i);
                        MarkPresentationChanged(World, entity, i, ref presentationChangedLocal, ref hasPresentationChanged);
                    }
                }

                if (!hasPresentationChanged && presentationChangedLocal.IsAnyBitSet())
                {
                    CommandBuffer.Add(entity, presentationChangedLocal);
                }

                CommandBuffer.Remove<AttributeAggregateDirty>(entity);
            }

        }

        struct AttributeAggregatorWithoutDirtyJob : IForEachWithEntity<AttributeBuffer, ActiveEffectContainer>
        {
            public World World;
            public CommandBuffer CommandBuffer;
            public GraphProgramRegistry GraphPrograms;
            public IGraphRuntimeApi GraphApi;
            public GasGraphOpHandlerTable GraphHandlers;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void Update(Entity entity, ref AttributeBuffer attrBuffer, ref ActiveEffectContainer effects)
            {
                Span<float> oldValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    oldValues[i] = attrBuffer.CurrentValues[i];
                }

                ulong touchedMask = RecomputeEffectiveValues(
                    World,
                    entity,
                    ref attrBuffer,
                    ref effects,
                    GraphPrograms,
                    GraphApi,
                    GraphHandlers);
                RestorePersistentCurrentValues(ref attrBuffer, oldValues, touchedMask);

                var dirtyFlags = new DirtyFlags();
                bool anyDirty = false;
                bool hasPresentationChanged = World.Has<GameplayAttributeChangedBits>(entity);
                GameplayAttributeChangedBits presentationChangedLocal = default;
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    if (oldValues[i] != attrBuffer.CurrentValues[i])
                    {
                        dirtyFlags.MarkAttributeDirty(i);
                        MarkPresentationChanged(World, entity, i, ref presentationChangedLocal, ref hasPresentationChanged);
                        anyDirty = true;
                    }
                }

                if (!hasPresentationChanged && presentationChangedLocal.IsAnyBitSet())
                {
                    CommandBuffer.Add(entity, presentationChangedLocal);
                }

                if (anyDirty && World.Has<DirtyFlags>(entity))
                {
                    ref DirtyFlags existingDirty = ref World.Get<DirtyFlags>(entity);
                    for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                    {
                        if (dirtyFlags.IsAttributeDirty(i))
                        {
                            existingDirty.MarkAttributeDirty(i);
                        }
                    }
                }
                else if (anyDirty)
                {
                    CommandBuffer.Add(entity, dirtyFlags);
                }

                CommandBuffer.Remove<AttributeAggregateDirty>(entity);
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void RestorePersistentCurrentValues(ref AttributeBuffer attrBuffer, Span<float> previousCurrentValues, ulong touchedMask)
        {
            ulong definedMask = attrBuffer.DefinedMask;
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                ulong bit = 1UL << i;
                if ((definedMask & bit) == 0UL)
                {
                    continue;
                }

                attrBuffer.CapValues[i] = attrBuffer.CurrentValues[i];
                bool touchedByAggregation = (touchedMask & bit) != 0UL;
                bool clampsToEffectiveCap =
                    AttributeRegistry.TryGetConstraints(i, out var constraints) &&
                    constraints.ClampCurrentToBase;
                if (!touchedByAggregation || clampsToEffectiveCap)
                {
                    attrBuffer.SetCurrent(i, previousCurrentValues[i]);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MarkPresentationChanged(
            World world,
            Entity entity,
            int attributeId,
            ref GameplayAttributeChangedBits presentationChangedLocal,
            ref bool hasPresentationChanged)
        {
            if (hasPresentationChanged)
            {
                world.Get<GameplayAttributeChangedBits>(entity).Mark(attributeId);
                return;
            }

            presentationChangedLocal.Mark(attributeId);
        }
    }
}
