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
        private readonly TagOps _tagOps;
        private readonly CommandBuffer _commandBuffer = new();

        public AttributeAggregatorSystem(World world, GraphProgramRegistry graphPrograms = null, IGraphRuntimeApi graphApi = null, TagOps tagOps = null) : base(world)
        {
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
        }

        public override unsafe void Update(in float dt)
        {
            var withDirtyJob = new AttributeAggregatorWithDirtyJob
            {
                World = World,
                CommandBuffer = _commandBuffer,
                GraphPrograms = _graphPrograms,
                GraphApi = _graphApi,
                TagOps = _tagOps,
            };
            World.InlineEntityQuery<AttributeAggregatorWithDirtyJob, AttributeBuffer, ActiveEffectContainer, DirtyFlags>(in _withDirtyFlagsQuery, ref withDirtyJob);

            var withoutDirtyJob = new AttributeAggregatorWithoutDirtyJob();
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
            World world, Entity entity, ref AttributeBuffer attributes,
            GraphProgramRegistry graphPrograms, IGraphRuntimeApi graphApi)
        {
            if (!world.Has<AttributeDerivedGraphBinding>(entity)) return;

            ref var binding = ref world.Get<AttributeDerivedGraphBinding>(entity);
            if (binding.Count <= 0) return;
            if (binding.Count > AttributeDerivedGraphBinding.MAX_BINDINGS)
            {
                throw new InvalidOperationException(
                    $"AttributeDerivedGraphBinding count {binding.Count} exceeds capacity {AttributeDerivedGraphBinding.MAX_BINDINGS}.");
            }

            if (graphPrograms == null || graphApi == null)
            {
                throw new InvalidOperationException(
                    "AttributeDerivedGraphBinding requires configured graph program registry and graph runtime API.");
            }
            if (graphApi is not IDerivedAttributeGraphRuntimeApi derivedAttributeApi)
            {
                throw new InvalidOperationException(IDerivedAttributeGraphRuntimeApi.MissingContractError);
            }

            derivedAttributeApi.BeginDerivedAttributeWrites(entity, in attributes);
            bool commit = false;
            try
            {
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

                    GraphKind kind = graphPrograms.RequireKind(programId, GraphKind.Derived);
                    NodeLibraries.GASGraph.GraphExecutor.ExecuteDerived(
                        world,
                        entity,
                        program,
                        graphApi,
                        kind,
                        graphPrograms);
                }

                commit = true;
            }
            finally
            {
                derivedAttributeApi.EndDerivedAttributeWrites(entity, ref attributes, commit);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong RecomputeEffectiveValues(
            World world,
            Entity entity,
            ref AttributeBuffer attrBuffer,
            ref ActiveEffectContainer effects,
            GraphProgramRegistry graphPrograms,
            IGraphRuntimeApi graphApi)
        {
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
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
                    EffectModifierOps.ApplyAggregated(in modifiers, ref attrBuffer);
                }
            }

            Span<float> beforeDerived = stackalloc float[AttributeBuffer.MAX_ATTRS];
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                beforeDerived[i] = attrBuffer.CurrentValues[i];
            }

            ExecuteDerivedGraphs(world, entity, ref attrBuffer, graphPrograms, graphApi);

            ulong derivedWrittenMask = 0UL;
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                if (beforeDerived[i] != attrBuffer.CurrentValues[i])
                {
                    derivedWrittenMask |= 1UL << i;
                }
            }

            return derivedWrittenMask;
        }

        struct AttributeAggregatorWithDirtyJob : IForEachWithEntity<AttributeBuffer, ActiveEffectContainer, DirtyFlags>
        {
            public World World;
            public CommandBuffer CommandBuffer;
            public GraphProgramRegistry GraphPrograms;
            public IGraphRuntimeApi GraphApi;
            public TagOps TagOps;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void Update(Entity entity, ref AttributeBuffer attrBuffer, ref ActiveEffectContainer effects, ref DirtyFlags dirtyFlags)
            {
                AttributeBuffer attributesBefore = attrBuffer;
                DirtyFlags dirtyBefore = dirtyFlags;
                Span<float> oldValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
                Span<float> oldCaps = stackalloc float[AttributeBuffer.MAX_ATTRS];
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    oldValues[i] = attrBuffer.CurrentValues[i];
                    oldCaps[i] = attrBuffer.CapValues[i];
                }

                ulong derivedWrittenMask = RecomputeEffectiveValues(
                    World,
                    entity,
                    ref attrBuffer,
                    ref effects,
                    GraphPrograms,
                    GraphApi);
                RestorePersistentCurrentValues(ref attrBuffer, oldValues, derivedWrittenMask);
                bool hasPresentationChanged = World.Has<GameplayAttributeChangedBits>(entity);
                GameplayAttributeChangedBits presentationChangedLocal = default;

                // 4. 标记脏属性（用于延迟触发器）
                ulong changedMask = 0UL;
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    if (oldValues[i] != attrBuffer.CurrentValues[i] ||
                        oldCaps[i] != attrBuffer.CapValues[i])
                    {
                        dirtyFlags.MarkAttributeDirty(i);
                        changedMask |= 1UL << i;
                    }
                }

                if (changedMask != 0UL)
                {
                    try
                    {
                        TagOps.MarkDirtyEntity(World, entity);
                    }
                    catch
                    {
                        attrBuffer = attributesBefore;
                        dirtyFlags = dirtyBefore;
                        throw;
                    }

                    for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                    {
                        if ((changedMask & (1UL << i)) != 0UL)
                        {
                            MarkPresentationChanged(World, entity, i, ref presentationChangedLocal, ref hasPresentationChanged);
                        }
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void Update(Entity entity, ref AttributeBuffer attrBuffer, ref ActiveEffectContainer effects)
            {
                throw new InvalidOperationException(
                    $"{TagOps.MissingDirtyFlagsError}: entity={entity.Id}, system=AttributeAggregatorSystem.");
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void RestorePersistentCurrentValues(
            ref AttributeBuffer attrBuffer,
            Span<float> previousCurrentValues,
            ulong derivedWrittenMask)
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
                if ((derivedWrittenMask & bit) != 0UL)
                {
                    continue;
                }

                attrBuffer.SetCurrent(i, previousCurrentValues[i]);
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
