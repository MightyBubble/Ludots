using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// HUD 属主的每帧快照：按 Arch chunk 存储序遍历 (CullState, AttributeBuffer)，
    /// 产出 SoA 行与 Entity.Id → 行号索引。值刷新与可见性判定经索引一次跳转读快照，
    /// 投影热循环零 World 随机访问，成本不随 archetype 布局漂移。
    /// 属主可见性沿用旧语义：存活且（无 CullState 或 CullState.IsVisible）；
    /// 不在快照中的属主（两类组件皆无）由调用方回退 World.IsAlive。
    /// </summary>
    public sealed class HudOwnerFrameSnapshot
    {
        private static readonly QueryDescription CullAndAttributesQuery = new QueryDescription()
            .WithAll<CullState, AttributeBuffer>();
        private static readonly QueryDescription AttributesOnlyQuery = new QueryDescription()
            .WithAll<AttributeBuffer>()
            .WithNone<CullState>();
        private static readonly QueryDescription CullOnlyQuery = new QueryDescription()
            .WithAll<CullState>()
            .WithNone<AttributeBuffer>();

        private int[] _ownerIds = Array.Empty<int>();
        private int[] _ownerVersions = Array.Empty<int>();
        private byte[] _visible = Array.Empty<byte>();
        private int _count;
        private int[] _rowByOwnerId = Array.Empty<int>();

        // 每个被值绑定引用的属性一列；列集由投影系统按需登记。
        private int[] _attributeIds = Array.Empty<int>();
        private float[][] _currentByAttribute = Array.Empty<float[]>();
        private float[][] _baseByAttribute = Array.Empty<float[]>();

        public void RegisterTrackedAttribute(int attributeId)
        {
            if (ColumnOf(attributeId) >= 0)
            {
                return;
            }

            int index = _attributeIds.Length;
            Array.Resize(ref _attributeIds, index + 1);
            Array.Resize(ref _currentByAttribute, index + 1);
            Array.Resize(ref _baseByAttribute, index + 1);
            _attributeIds[index] = attributeId;
            // 列长对齐行容量而非当前行数：登记可能发生在 Rebuild 增行中途，
            // 迟登记列必须容得下后续 AppendRow 的满行写入
            int rowCapacity = Math.Max(_count, _ownerIds.Length);
            _currentByAttribute[index] = new float[rowCapacity];
            _baseByAttribute[index] = new float[rowCapacity];
        }

        public bool IsTracked(int attributeId) => ColumnOf(attributeId) >= 0;

        public void Rebuild(World world)
        {
            _count = 0;
            AppendQueryRows(world, CullAndAttributesQuery, hasCull: true, withAttributes: true);
            AppendQueryRows(world, AttributesOnlyQuery, hasCull: false, withAttributes: true);
            AppendQueryRows(world, CullOnlyQuery, hasCull: true, withAttributes: false);
            RebuildIndex();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRow(in Entity owner, out int row)
        {
            row = 0;
            if (!IsAssignedOwner(owner) || (uint)owner.Id >= (uint)_rowByOwnerId.Length)
            {
                return false;
            }

            int candidate = _rowByOwnerId[owner.Id] - 1;
            if ((uint)candidate >= (uint)_count || _ownerVersions[candidate] != owner.Version)
            {
                return false;
            }

            row = candidate;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsVisible(int row)
        {
            return _visible[row] != 0;
        }

        /// <summary>未跟踪属性返回 false，由调用方回退 World 现读；属主不在快照同样返回 false。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAttributeValues(int row, int attributeId, out float current, out float baseValue)
        {
            current = 0f;
            baseValue = 0f;
            int column = ColumnOf(attributeId);
            if (column < 0 || (uint)row >= (uint)_count)
            {
                return false;
            }

            current = _currentByAttribute[column][row];
            baseValue = _baseByAttribute[column][row];
            return true;
        }

        private int ColumnOf(int attributeId)
        {
            for (int i = 0; i < _attributeIds.Length; i++)
            {
                if (_attributeIds[i] == attributeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void AppendQueryRows(World world, QueryDescription query, bool hasCull, bool withAttributes)
        {
            AttributeBuffer noBuffer = default;
            foreach (ref Chunk chunk in world.Query(in query))
            {
                ReadOnlySpan<Entity> entities = chunk.Entities;
                ReadOnlySpan<CullState> cullStates = hasCull ? chunk.GetSpan<CullState>() : default;
                ReadOnlySpan<AttributeBuffer> buffers = withAttributes ? chunk.GetSpan<AttributeBuffer>() : default;
                for (int i = 0; i < chunk.Count; i++)
                {
                    bool visible = !hasCull || cullStates[i].IsVisible;
                    if (withAttributes)
                    {
                        AppendRow(entities[i], visible, true, in buffers[i]);
                    }
                    else
                    {
                        AppendRow(entities[i], visible, false, in noBuffer);
                    }
                }
            }
        }

        private void AppendRow(in Entity owner, bool visible, bool withAttributes, in AttributeBuffer buffer)
        {
            if (_count == _ownerIds.Length)
            {
                EnsureRowCapacity(_count == 0 ? 64 : _count * 2);
            }

            _ownerIds[_count] = owner.Id;
            _ownerVersions[_count] = owner.Version;
            _visible[_count] = visible ? (byte)1 : (byte)0;
            for (int column = 0; column < _attributeIds.Length; column++)
            {
                int attributeId = _attributeIds[column];
                _currentByAttribute[column][_count] = withAttributes ? buffer.GetCurrent(attributeId) : 0f;
                _baseByAttribute[column][_count] = withAttributes ? buffer.GetBase(attributeId) : 0f;
            }

            _count++;
        }

        private void RebuildIndex()
        {
            int maxId = 0;
            for (int i = 0; i < _count; i++)
            {
                maxId = Math.Max(maxId, _ownerIds[i]);
            }

            if (_rowByOwnerId.Length <= maxId)
            {
                int next = Math.Max(1024, _rowByOwnerId.Length);
                while (next <= maxId)
                {
                    next *= 2;
                }

                _rowByOwnerId = new int[next];
            }
            else
            {
                Array.Clear(_rowByOwnerId, 0, _rowByOwnerId.Length);
            }

            for (int i = 0; i < _count; i++)
            {
                _rowByOwnerId[_ownerIds[i]] = i + 1;
            }
        }

        private void EnsureRowCapacity(int capacity)
        {
            if (_ownerIds.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref _ownerIds, capacity);
            Array.Resize(ref _ownerVersions, capacity);
            Array.Resize(ref _visible, capacity);
            for (int column = 0; column < _currentByAttribute.Length; column++)
            {
                Array.Resize(ref _currentByAttribute[column], capacity);
                Array.Resize(ref _baseByAttribute[column], capacity);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAssignedOwner(in Entity owner)
        {
            return owner.Id >= 0 && owner.Version > 0;
        }
    }
}
