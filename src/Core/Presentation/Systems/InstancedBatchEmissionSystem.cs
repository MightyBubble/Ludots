using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class InstancedBatchEmissionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription PresenterQuery = new QueryDescription()
            .WithAll<PresenterState>();

        private readonly PresenterDefinitionRegistry _definitions;
        private readonly InstancedBatchAssetRegistry _batchAssets;
        private readonly InstancedBatchRequestBuffer _requests;
        private readonly InstancedBatchSubmissionRuntime _submissionRuntime;
        private readonly PresentationEventStream _events;
        private readonly List<RemovedPresenterKey> _removedThisFrame = new(32);

        public InstancedBatchEmissionSystem(
            World world,
            PresenterDefinitionRegistry definitions,
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

            foreach (ref readonly Chunk chunk in World.Query(in PresenterQuery))
            {
                ref Entity firstEntity = ref chunk.Entity(0);
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity presenter = System.Runtime.CompilerServices.Unsafe.Add(ref firstEntity, i);
                    ref readonly PresenterState state = ref states[i];
                    if (WasRemovedThisFrame(presenter, state.StableId))
                    {
                        continue;
                    }

                    EmitForPresenter(presenter, in state);
                }
            }
        }

        private void EmitForPresenter(Entity presenter, in PresenterState state)
        {
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition) ||
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
                        $"Presenter definition '{definition.Key}' references missing instanced batch asset id={batchAssetId}.");
                }

                InstancedBatchGroup[] groups = asset.Groups;
                for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    ref readonly InstancedBatchGroup group = ref groups[groupIndex];
                    int totalInstances = group.InstanceCount;
                    int budget = asset.ProgressiveSubmission.MaxInstancesPerFlush;
                    if (!_submissionRuntime.ShouldSubmit(
                            presenter,
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
                        presenter,
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
                if (evt.Kind != PresentationEventKind.PresenterDestroyed ||
                    evt.PresenterEntity == Entity.Null ||
                    !_definitions.TryGet(evt.KeyId, out PresenterDefinition definition) ||
                    !definition.HasInstancedBatchBindings)
                {
                    continue;
                }

                _submissionRuntime.Remove(evt.PresenterEntity, (int)evt.Magnitude);
                _removedThisFrame.Add(new RemovedPresenterKey(evt.PresenterEntity, (int)evt.Magnitude));
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
                            evt.PresenterEntity,
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

        private bool WasRemovedThisFrame(Entity presenter, int presenterStableId)
        {
            for (int i = 0; i < _removedThisFrame.Count; i++)
            {
                if (_removedThisFrame[i].Presenter == presenter &&
                    _removedThisFrame[i].PresenterStableId == presenterStableId)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct RemovedPresenterKey
        {
            public readonly Entity Presenter;
            public readonly int PresenterStableId;

            public RemovedPresenterKey(Entity presenter, int presenterStableId)
            {
                Presenter = presenter;
                PresenterStableId = presenterStableId;
            }
        }
    }
}
