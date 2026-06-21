using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class InstancedBatchEmissionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription PerformerQuery = new QueryDescription()
            .WithAll<PerformerState>();

        private readonly PerformerDefinitionRegistry _definitions;
        private readonly InstancedBatchAssetRegistry _batchAssets;
        private readonly InstancedBatchRequestBuffer _requests;
        private readonly InstancedBatchSubmissionRuntime _submissionRuntime;
        private readonly PresentationEventStream _events;
        private readonly List<RemovedPerformerKey> _removedThisFrame = new(32);

        public InstancedBatchEmissionSystem(
            World world,
            PerformerDefinitionRegistry definitions,
            InstancedBatchAssetRegistry batchAssets,
            InstancedBatchRequestBuffer requests,
            InstancedBatchSubmissionRuntime submissionRuntime,
            PresentationEventStream events)
            : base(world)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _batchAssets = batchAssets ?? throw new ArgumentNullException(nameof(batchAssets));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _submissionRuntime = submissionRuntime ?? throw new ArgumentNullException(nameof(submissionRuntime));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public override void Update(in float dt)
        {
            _removedThisFrame.Clear();
            EmitRemovals();

            foreach (ref readonly Chunk chunk in World.Query(in PerformerQuery))
            {
                ref Entity firstEntity = ref chunk.Entity(0);
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity performer = System.Runtime.CompilerServices.Unsafe.Add(ref firstEntity, i);
                    ref readonly PerformerState state = ref states[i];
                    if (WasRemovedThisFrame(performer, state.StableId))
                    {
                        continue;
                    }

                    EmitForPerformer(performer, in state);
                }
            }
        }

        private void EmitForPerformer(Entity performer, in PerformerState state)
        {
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition) ||
                !definition.HasInstancedBatchBindings)
            {
                return;
            }

            InstancedBatchBinding[] bindings = definition.InstancedBatches;
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                int batchAssetId = bindings[bindingIndex].BatchAssetId;
                if (!_batchAssets.TryGet(batchAssetId, out InstancedBatchAsset asset))
                {
                    throw new InvalidOperationException(
                        $"Performer definition '{definition.Key}' references missing instanced batch asset id={batchAssetId}.");
                }

                InstancedBatchGroup[] groups = asset.Groups;
                for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    ref readonly InstancedBatchGroup group = ref groups[groupIndex];
                    int totalInstances = group.InstanceCount;
                    int budget = asset.ProgressiveSubmission.MaxInstancesPerFlush;
                    if (!_submissionRuntime.ShouldSubmit(
                            performer,
                            state.StableId,
                            batchAssetId,
                            groupIndex,
                            totalInstances,
                            out int start,
                            out int count,
                            budget))
                    {
                        continue;
                    }

                    InstancedBatchAddress address = group.Address;
                    if (!address.IsValid)
                    {
                        throw new InvalidOperationException(
                            $"Instanced batch asset '{asset.Key}' group '{group.Id}' has not been compiled to a stable address.");
                    }

                    _requests.Add(new InstancedBatchRequest(
                        InstancedBatchRequestKind.CreateOrUpdate,
                        batchAssetId,
                        state.StableId,
                        state.OwnerEntity,
                        performer,
                        address,
                        asset.RenderPath,
                        group.MeshAssetId,
                        group.MaterialId,
                        start,
                        count,
                        finalChunk: start + count >= totalInstances));
                }
            }
        }

        private void EmitRemovals()
        {
            ReadOnlySpan<PresentationEvent> events = _events.GetSpan();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly PresentationEvent evt = ref events[i];
                if (evt.Kind != PresentationEventKind.PerformerDestroyed ||
                    evt.PerformerEntity == Entity.Null ||
                    !_definitions.TryGet(evt.KeyId, out PerformerDefinition definition) ||
                    !definition.HasInstancedBatchBindings)
                {
                    continue;
                }

                _submissionRuntime.Remove(evt.PerformerEntity, (int)evt.Magnitude);
                _removedThisFrame.Add(new RemovedPerformerKey(evt.PerformerEntity, (int)evt.Magnitude));
                InstancedBatchBinding[] bindings = definition.InstancedBatches;
                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    int batchAssetId = bindings[bindingIndex].BatchAssetId;
                    if (!_batchAssets.TryGet(batchAssetId, out InstancedBatchAsset asset))
                    {
                        continue;
                    }

                    InstancedBatchGroup[] groups = asset.Groups;
                    for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                    {
                        ref readonly InstancedBatchGroup group = ref groups[groupIndex];
                        InstancedBatchAddress address = group.Address;
                        if (!address.IsValid)
                        {
                            throw new InvalidOperationException(
                                $"Instanced batch asset '{asset.Key}' group '{group.Id}' has not been compiled to a stable address.");
                        }

                        _requests.Add(new InstancedBatchRequest(
                            InstancedBatchRequestKind.Remove,
                            batchAssetId,
                            (int)evt.Magnitude,
                            evt.Source,
                            evt.PerformerEntity,
                            address,
                            asset.RenderPath,
                            group.MeshAssetId,
                            group.MaterialId,
                            0,
                            0,
                            finalChunk: true));
                    }
                }
            }
        }

        private bool WasRemovedThisFrame(Entity performer, int performerStableId)
        {
            for (int i = 0; i < _removedThisFrame.Count; i++)
            {
                if (_removedThisFrame[i].Performer == performer &&
                    _removedThisFrame[i].PerformerStableId == performerStableId)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct RemovedPerformerKey
        {
            public readonly Entity Performer;
            public readonly int PerformerStableId;

            public RemovedPerformerKey(Entity performer, int performerStableId)
            {
                Performer = performer;
                PerformerStableId = performerStableId;
            }
        }
    }
}
