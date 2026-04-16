using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationBehaviorResolver
    {
        private readonly PresentationBehaviorRegistry _behaviors;
        private readonly MeshAssetRegistry _meshes;

        public PresentationBehaviorResolver(PresentationBehaviorRegistry behaviors, MeshAssetRegistry meshes)
        {
            _behaviors = behaviors ?? throw new ArgumentNullException(nameof(behaviors));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        }

        public void ResolveState(
            int behaviorId,
            string stateId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            PrefabFinalizedVisualBuffer output,
            int maxDepth = PrefabFinalizationPipeline.DefaultMaxDepth)
        {
            ResolveState(
                behaviorId,
                stateId,
                stableId,
                position,
                rotation,
                scale,
                color,
                PrefabFinalizationContext.Empty,
                output,
                maxDepth);
        }

        public void ResolveState(
            int behaviorId,
            string stateId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            in PrefabFinalizationContext context,
            PrefabFinalizedVisualBuffer output,
            int maxDepth = PrefabFinalizationPipeline.DefaultMaxDepth)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                throw new ArgumentException("Presentation behavior stateId must not be empty.", nameof(stateId));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (!_behaviors.TryGet(behaviorId, out PresentationBehaviorDefinition behavior))
            {
                throw new InvalidOperationException($"Presentation behavior id={behaviorId} is not registered.");
            }

            PresentationBehaviorStateDefinition state = ResolveStateDefinition(behavior, stateId);
            if (state.PrefabAssetId <= 0)
            {
                throw new InvalidOperationException(
                    $"Presentation behavior '{_behaviors.GetName(behaviorId)}' state '{state.StateId}' is missing prefabAssetId.");
            }

            PrefabFinalizationPipeline.FinalizeVisuals(
                _meshes,
                state.PrefabAssetId,
                stableId,
                position,
                rotation,
                scale,
                color,
                context,
                output,
                maxDepth);
        }

        private PresentationBehaviorStateDefinition ResolveStateDefinition(PresentationBehaviorDefinition behavior, string stateId)
        {
            PresentationBehaviorStateDefinition[]? states = behavior.States;
            if (states == null || states.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Presentation behavior '{_behaviors.GetName(behavior.BehaviorId)}' does not define any states.");
            }

            for (int i = 0; i < states.Length; i++)
            {
                if (string.Equals(states[i].StateId, stateId, StringComparison.OrdinalIgnoreCase))
                {
                    return states[i];
                }
            }

            throw new InvalidOperationException(
                $"Presentation behavior '{_behaviors.GetName(behavior.BehaviorId)}' does not define state '{stateId}'.");
        }
    }
}
