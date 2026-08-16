using System;
using System.Numerics;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct CueMarkerAuthoredVisual
    {
        public int MeshAssetId { get; init; }
        public Vector3 Scale { get; init; }
        public Vector3 AnchorOffset { get; init; }
        public float LifetimeSeconds { get; init; }

        public static CueMarkerAuthoredVisual Resolve(
            MeshAssetRegistry meshes,
            PresenterDefinitionRegistry presenters)
        {
            ArgumentNullException.ThrowIfNull(meshes);
            ArgumentNullException.ThrowIfNull(presenters);

            int meshAssetId = WellKnownMeshKeys.RequireCueMarkerId(meshes);
            int definitionId = presenters.GetId(WellKnownMeshKeys.CueMarker);
            if (definitionId <= 0 || !presenters.TryGet(definitionId, out PresenterDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Presenter '{WellKnownMeshKeys.CueMarker}' is required for transient cue markers. Author it in Presentation/presenters.json.");
            }

            if (definition.DefaultLifetime <= 0f || !float.IsFinite(definition.DefaultLifetime))
            {
                throw new InvalidOperationException(
                    $"Presenter '{WellKnownMeshKeys.CueMarker}' lifecycle.durationSeconds must be > 0.");
            }

            Vector3 scale = Vector3.Zero;
            bool foundMesh = false;
            BehaviorSlot[] behaviors = definition.Behaviors ?? Array.Empty<BehaviorSlot>();
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.AssetBinding ||
                    slot.AssetBinding.AssetKind != AssetKind.Mesh ||
                    slot.AssetBinding.AssetId != meshAssetId)
                {
                    continue;
                }

                scale = slot.AssetBinding.LocalScale;
                foundMesh = true;
                break;
            }

            if (!foundMesh)
            {
                throw new InvalidOperationException(
                    $"Presenter '{WellKnownMeshKeys.CueMarker}' must bind AssetKind.Mesh assetId '{WellKnownMeshKeys.CueMarker}'.");
            }

            if (!float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || !float.IsFinite(scale.Z) ||
                scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
            {
                throw new InvalidOperationException(
                    $"Presenter '{WellKnownMeshKeys.CueMarker}' localScale must be finite and > 0 on every axis.");
            }

            return new CueMarkerAuthoredVisual
            {
                MeshAssetId = meshAssetId,
                Scale = scale,
                AnchorOffset = definition.PositionOffset,
                LifetimeSeconds = definition.DefaultLifetime,
            };
        }
    }
}
