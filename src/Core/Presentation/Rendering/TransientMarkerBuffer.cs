using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Requests;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class TransientMarkerBuffer
    {
        private TransientMarker[] _buffer;
        private int _count;
        private int _nextStableId = 1;

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public TransientMarkerBuffer(int capacity = 2048)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new TransientMarker[capacity];
        }

        public bool TryAddMesh(int meshAssetId, Vector3 position, Vector3 scale, Vector4 color, float lifetimeSeconds)
        {
            if (_count >= _buffer.Length) return false;
            if (lifetimeSeconds <= 0f) lifetimeSeconds = 0.15f;
            _buffer[_count++] = new TransientMarker
            {
                StableId = AllocateStableId(),
                Kind = TransientMarkerKind.Mesh,
                MeshAssetId = meshAssetId,
                Position = position,
                Scale = scale,
                Color = color,
                Lifetime = lifetimeSeconds,
                TimeLeft = lifetimeSeconds,
                Anchor = default,
                AnchorOffset = default,
            };
            return true;
        }

        public bool TryAddAnchoredMesh(int meshAssetId, Vector3 scale, Vector4 color, float lifetimeSeconds, Entity anchor, Vector3 anchorOffset)
        {
            if (_count >= _buffer.Length) return false;
            if (lifetimeSeconds <= 0f) lifetimeSeconds = 0.15f;
            _buffer[_count++] = new TransientMarker
            {
                StableId = AllocateStableId(),
                Kind = TransientMarkerKind.Mesh,
                MeshAssetId = meshAssetId,
                Position = anchorOffset,
                Scale = scale,
                Color = color,
                Lifetime = lifetimeSeconds,
                TimeLeft = lifetimeSeconds,
                Anchor = anchor,
                AnchorOffset = anchorOffset,
            };
            return true;
        }

        public bool TryAddPrefab(int prefabId, Vector3 position, Vector3 scale, Vector4 color, float lifetimeSeconds)
        {
            if (_count >= _buffer.Length) return false;
            if (lifetimeSeconds <= 0f) lifetimeSeconds = 0.15f;
            _buffer[_count++] = new TransientMarker
            {
                StableId = AllocateStableId(),
                Kind = TransientMarkerKind.Prefab,
                PrefabId = prefabId,
                Position = position,
                Scale = scale,
                Color = color,
                Lifetime = lifetimeSeconds,
                TimeLeft = lifetimeSeconds,
                Anchor = default,
                AnchorOffset = default,
            };
            return true;
        }

        public bool TryAddAnchoredPrefab(int prefabId, Vector3 scale, Vector4 color, float lifetimeSeconds, Entity anchor, Vector3 anchorOffset)
        {
            if (_count >= _buffer.Length) return false;
            if (lifetimeSeconds <= 0f) lifetimeSeconds = 0.15f;
            _buffer[_count++] = new TransientMarker
            {
                StableId = AllocateStableId(),
                Kind = TransientMarkerKind.Prefab,
                PrefabId = prefabId,
                Position = anchorOffset,
                Scale = scale,
                Color = color,
                Lifetime = lifetimeSeconds,
                TimeLeft = lifetimeSeconds,
                Anchor = anchor,
                AnchorOffset = anchorOffset,
            };
            return true;
        }

        public void TickAndRequest(PresentationRequestBuffer requests, float dt, World world)
        {
            ArgumentNullException.ThrowIfNull(requests);
            float delta = dt <= 0f ? 0.016666668f : dt;
            for (int i = 0; i < _count;)
            {
                ref var marker = ref _buffer[i];
                marker.TimeLeft -= delta;
                if (marker.TimeLeft <= 0f)
                {
                    _count--;
                    if (i < _count) _buffer[i] = _buffer[_count];
                    continue;
                }

                Vector3 position = marker.Position;
                bool hasAnchor = marker.Anchor.Id != 0 || marker.Anchor.WorldId != 0;
                if (hasAnchor && world.IsAlive(marker.Anchor) && world.Has<VisualTransform>(marker.Anchor))
                {
                    position = world.Get<VisualTransform>(marker.Anchor).Position + marker.AnchorOffset;
                    marker.Position = position;
                }

                float t = marker.Lifetime <= 0f ? 1f : 1f - (marker.TimeLeft / marker.Lifetime);
                float alpha = 1f - t;
                Vector4 color = marker.Color;
                color.W *= alpha;

                if (marker.Kind == TransientMarkerKind.Prefab)
                {
                    requests.Add(PresentationRequest.FromPrefab(
                        marker.Anchor,
                        marker.PrefabId,
                        marker.StableId,
                        position,
                        Quaternion.Identity,
                        marker.Scale,
                        color,
                        LODLevel.High,
                        PrefabFinalizationContext.Empty));
                    i++;
                    continue;
                }

                requests.Add(PresentationRequest.FromVisualProxy(marker.Anchor, new PresentationVisualProxy
                {
                    ProxyKind = PresentationVisualProxyKind.Presenter,
                    MeshAssetId = marker.MeshAssetId,
                    Position = position,
                    Rotation = Quaternion.Identity,
                    Scale = marker.Scale,
                    Color = color,
                    StableId = marker.StableId,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Movable,
                    Flags = VisualRuntimeFlags.Visible,
                    Visibility = VisualVisibility.Visible,
                    LOD = LODLevel.High,
                }));

                i++;
            }
        }

        private int AllocateStableId()
        {
            return TransientMarkerIdentity.ComposeStableId(_nextStableId++);
        }

        public struct TransientMarker
        {
            public int StableId;
            public TransientMarkerKind Kind;
            public int MeshAssetId;
            public int PrefabId;
            public Vector3 Position;
            public Vector3 Scale;
            public Vector4 Color;
            public float Lifetime;
            public float TimeLeft;
            public Entity Anchor;
            public Vector3 AnchorOffset;
        }

        public enum TransientMarkerKind : byte
        {
            Mesh = 0,
            Prefab = 1,
        }
    }
}
