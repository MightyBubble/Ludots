using System;
using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Rendering
{
    public readonly struct PresentationVisualRequest
    {
        public PresentationVisualRequest(
            PresentationVisualRequestKind kind,
            int stableId,
            string assetKey,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            Entity source,
            Entity target,
            PresentationPayloadField[] payload,
            string materialKey = "",
            string surfaceLayerKey = "")
        {
            Kind = kind;
            StableId = stableId;
            AssetKey = assetKey ?? string.Empty;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Color = color;
            Source = source;
            Target = target;
            Payload = payload ?? Array.Empty<PresentationPayloadField>();
            MaterialKey = materialKey ?? string.Empty;
            SurfaceLayerKey = surfaceLayerKey ?? string.Empty;
        }

        public PresentationVisualRequestKind Kind { get; }

        public int StableId { get; }

        public string AssetKey { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public Vector4 Color { get; }

        public Entity Source { get; }

        public Entity Target { get; }

        public PresentationPayloadField[] Payload { get; }

        public string MaterialKey { get; }

        public string SurfaceLayerKey { get; }
    }
}
