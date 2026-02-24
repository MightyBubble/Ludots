using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Rendering
{
    /// <summary>
    /// 通用的瞬态视觉标记缓冲区。
    /// 
    /// 管理具有固定生命周期的 3D 图元标记（点击标记、技能特效、伤害飘字等）。
    /// 支持可选的实体锚点跟随。每帧 Tick 后自动衰减透明度并在超时后移除。
    /// 
    /// 取代 Mod 层的 MobaMarkerBuffer，提供统一的 Core 级抽象。
    /// PerformerRuntimeSystem 也使用此缓冲区存储事件管线触发的瞬态实例。
    /// </summary>
    public sealed class TransientMarkerBuffer
    {
        private TransientMarker[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public TransientMarkerBuffer(int capacity = 2048)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new TransientMarker[capacity];
        }

        /// <summary>
        /// 添加一个不跟随实体的瞬态标记。
        /// </summary>
        public bool TryAdd(int meshAssetId, Vector3 position, Vector3 scale, Vector4 color, float lifetimeSeconds)
        {
            if (_count >= _buffer.Length) return false;
            if (lifetimeSeconds <= 0f) lifetimeSeconds = 0.15f;
            _buffer[_count++] = new TransientMarker
            {
                MeshAssetId = meshAssetId,
                Position = position,
                Scale = scale,
                Color = color,
                Lifetime = lifetimeSeconds,
                TimeLeft = lifetimeSeconds,
                Anchor = default,
                AnchorOffset = default
            };
            return true;
        }

        /// <summary>
        /// 添加一个跟随实体锚点的瞬态标记。
        /// 锚点有效时，每帧用 anchor.VisualTransform.Position + offset 更新位置。
        /// </summary>
        public bool TryAddAnchored(int meshAssetId, Vector3 scale, Vector4 color, float lifetimeSeconds, Entity anchor, Vector3 anchorOffset)
        {
            if (_count >= _buffer.Length) return false;
            if (lifetimeSeconds <= 0f) lifetimeSeconds = 0.15f;
            _buffer[_count++] = new TransientMarker
            {
                MeshAssetId = meshAssetId,
                Position = anchorOffset, // 初始位置用 offset，Tick 时用锚点更新
                Scale = scale,
                Color = color,
                Lifetime = lifetimeSeconds,
                TimeLeft = lifetimeSeconds,
                Anchor = anchor,
                AnchorOffset = anchorOffset
            };
            return true;
        }

        /// <summary>
        /// 每帧 Tick：衰减生命周期，移除过期标记，更新锚点位置，输出到 PrimitiveDrawBuffer。
        /// </summary>
        public void TickAndEmit(PrimitiveDrawBuffer draw, float dt, World world)
        {
            float delta = dt <= 0f ? 0.016666668f : dt;
            for (int i = 0; i < _count;)
            {
                ref var m = ref _buffer[i];
                m.TimeLeft -= delta;
                if (m.TimeLeft <= 0f)
                {
                    _count--;
                    if (i < _count) _buffer[i] = _buffer[_count];
                    continue;
                }

                // 锚点跟随
                var pos = m.Position;
                bool hasAnchor = m.Anchor.Id != 0 || m.Anchor.WorldId != 0;
                if (hasAnchor && world.IsAlive(m.Anchor) && world.Has<VisualTransform>(m.Anchor))
                {
                    pos = world.Get<VisualTransform>(m.Anchor).Position + m.AnchorOffset;
                    m.Position = pos; // 持续更新
                }

                // 透明度衰减
                float t = m.Lifetime <= 0f ? 1f : 1f - (m.TimeLeft / m.Lifetime);
                float alpha = 1f - t;
                var c = m.Color;
                c.W *= alpha;

                draw.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = m.MeshAssetId,
                    Position = pos,
                    Scale = m.Scale,
                    Color = c
                });

                i++;
            }
        }

        public struct TransientMarker
        {
            public int MeshAssetId;
            public Vector3 Position;
            public Vector3 Scale;
            public Vector4 Color;
            public float Lifetime;
            public float TimeLeft;
            public Entity Anchor;
            public Vector3 AnchorOffset;
        }
    }
}
