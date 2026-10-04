using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// RFC-0067 P1 世界级属性列存：<see cref="GasLoadTimeCapacityPlan"/> 冻结后按
    /// AttributeSlotCount × rowCapacity 一次性分配，对局内零扩容、零分配；行分配只是写索引。
    /// 容量真相分层：槽位上限 = Plan.AttributeSlotCount；实体内嵌 <see cref="AttributeBuffer"/>
    /// 仍是槽位 [0,64) 的镜像（迁移期双轨，P4 拆），槽位 [64, Plan) 只存在于本表。
    /// 每帧属性读写禁止字典：行号经 _rowByEntityId 稀疏数组直查，代际校验防实体 id 复用误绑。
    /// </summary>
    public unsafe sealed class WorldAttributeStore
    {
        public const int DefaultRowCapacity = 65_536;

        private readonly int _slotCount;
        private readonly int _rowCapacity;
        private readonly float[] _base;
        private readonly float[] _cap;
        private readonly float[] _current;
        private readonly float[] _lastSnapshot;
        private readonly ulong[] _definedWords;
        private readonly ulong[] _aggregateDirtyRows;
        private readonly ulong[] _attributeDirtyRows;
        private readonly ulong[] _tagBits;
        private readonly ulong[] _tagLastSnapshot;
        private readonly ulong[] _tagDirtyRows;
        private readonly int _tagWordCount;

        private readonly int[] _rowByEntityId;
        private readonly int[] _worldByEntityId;
        private readonly uint[] _versionByEntityId;
        private readonly Entity[] _entityByRow;
        private int _rowCount;

        public WorldAttributeStore(GasLoadTimeCapacityPlan plan, int rowCapacity = DefaultRowCapacity)
        {
            plan.VerifyFrozen();
            if (rowCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rowCapacity));
            }

            _slotCount = plan.AttributeSlotCount;
            _tagWordCount = plan.TagWordCount;
            _rowCapacity = rowCapacity;
            int cells = checked(_slotCount * rowCapacity);
            _base = new float[cells];
            _cap = new float[cells];
            _current = new float[cells];
            _lastSnapshot = new float[cells];
            int definedWordCount = WordCount * rowCapacity;
            _definedWords = new ulong[definedWordCount];
            _aggregateDirtyRows = new ulong[(rowCapacity + 63) >> 6];
            _attributeDirtyRows = new ulong[(rowCapacity + 63) >> 6];
            _tagBits = new ulong[checked(_tagWordCount * rowCapacity)];
            _tagLastSnapshot = new ulong[checked(_tagWordCount * rowCapacity)];
            _tagDirtyRows = new ulong[(rowCapacity + 63) >> 6];

            int entityIdCapacity = 1 << 20;
            _rowByEntityId = new int[entityIdCapacity];
            _worldByEntityId = new int[entityIdCapacity];
            _versionByEntityId = new uint[entityIdCapacity];
            Array.Fill(_rowByEntityId, -1);
            _entityByRow = new Entity[rowCapacity];
            _rowCount = 0;
        }

        public int SlotCount => _slotCount;
        public int TagIdSpace => _tagWordCount * 64;
        public int TagWordCount => _tagWordCount;
        public int RowCapacity => _rowCapacity;
        public int RowCount => _rowCount;

        private static int WordCountFor(int slots) => (slots + 63) >> 6;
        private int WordCount => WordCountFor(_slotCount);

        /// <summary>装载期容量语义：本表只为槽位 [0, SlotCount) 服务，越界失败关闭。</summary>
        private int ValidateSlot(int attributeId)
        {
            if ((uint)attributeId >= (uint)_slotCount)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.AttributeSlotOutOfRange: attributeId={attributeId} 超出本局容量计划 {_slotCount} 槽（RFC-0067 §3.1）。");
            }

            return attributeId;
        }

        public bool HasRow(Entity entity)
        {
            return TryGetRow(entity, out _);
        }

        public bool TryGetRow(Entity entity, out int row)
        {
            row = -1;
            int id = entity.Id;
            if ((uint)id >= (uint)_rowByEntityId.Length)
            {
                return false;
            }

            if (_rowByEntityId[id] < 0 ||
                _worldByEntityId[id] != entity.WorldId ||
                _versionByEntityId[id] != entity.Version)
            {
                return false;
            }

            row = _rowByEntityId[id];
            return true;
        }

        /// <summary>为实体建行（幂等）。行容量在装载期定死，耗尽即失败关闭——对局零扩容。</summary>
        public int EnsureRow(Entity entity)
        {
            int id = entity.Id;
            if ((uint)id >= (uint)_rowByEntityId.Length)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.AttributeRowEntityIdOutOfRange: entity id {id} 超出本表实体索引容量 {_rowByEntityId.Length}。");
            }

            if (_rowByEntityId[id] >= 0 &&
                _worldByEntityId[id] == entity.WorldId &&
                _versionByEntityId[id] == entity.Version)
            {
                return _rowByEntityId[id];
            }

            if (_rowCount >= _rowCapacity)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.AttributeRowCapacityExceeded: 属性行容量 {_rowCapacity} 已满（装载期定容，RFC-0067 §2）。");
            }

            int row = _rowCount++;
            _rowByEntityId[id] = row;
            _worldByEntityId[id] = entity.WorldId;
            _versionByEntityId[id] = unchecked((uint)entity.Version);
            _entityByRow[row] = entity;
            return row;
        }

        public Entity GetEntity(int row) => _entityByRow[row];

        private int Cell(int row, int slot) => row * _slotCount + slot;

        public bool IsDefined(int row, int attributeId)
        {
            ValidateSlot(attributeId);
            int word = attributeId >> 6;
            return (_definedWords[row * WordCount + word] & (1UL << (attributeId & 63))) != 0UL;
        }

        public void MarkDefined(int row, int attributeId)
        {
            ValidateSlot(attributeId);
            _definedWords[row * WordCount + (attributeId >> 6)] |= 1UL << (attributeId & 63);
        }

        public float GetBase(int row, int attributeId) => _base[Cell(row, ValidateSlot(attributeId))];
        public float GetCap(int row, int attributeId) => _cap[Cell(row, ValidateSlot(attributeId))];
        public float GetCurrent(int row, int attributeId) => _current[Cell(row, ValidateSlot(attributeId))];
        public float GetLastSnapshot(int row, int attributeId) => _lastSnapshot[Cell(row, ValidateSlot(attributeId))];

        public void SetBase(int row, int attributeId, float value)
        {
            ValidateSlot(attributeId);
            MarkDefined(row, attributeId);
            int cell = Cell(row, attributeId);
            _base[cell] = value;
            _cap[cell] = value;
            _current[cell] = value;
        }

        /// <summary>镜像写：把内嵌侧已 settle 的最终值同步进列存（含钳制结果），不重复做约束。</summary>
        public void MirrorCurrent(int row, int attributeId, float baseValue, float capValue, float currentValue)
        {
            ValidateSlot(attributeId);
            MarkDefined(row, attributeId);
            int cell = Cell(row, attributeId);
            _base[cell] = baseValue;
            _cap[cell] = capValue;
            _current[cell] = currentValue;
        }

        /// <summary>高槽位（≥64）写通道：与 AttributeBuffer.SetCurrentInternal 同一套约束语义。</summary>
        public void SetCurrentHigh(int row, int attributeId, float value)
        {
            ValidateSlot(attributeId);
            MarkDefined(row, attributeId);
            if (AttributeRegistry.TryGetConstraints(attributeId, out var constraints))
            {
                if (constraints.ClampCurrentToBase && value > _base[Cell(row, attributeId)])
                {
                    value = _base[Cell(row, attributeId)];
                }

                if (constraints.HasMin && value < constraints.Min)
                {
                    value = constraints.Min;
                }

                if (constraints.HasMax && value > constraints.Max)
                {
                    value = constraints.Max;
                }
            }

            _current[Cell(row, attributeId)] = value;
        }

        public void SetCurrentRaw(int row, int attributeId, float value)
        {
            ValidateSlot(attributeId);
            _current[Cell(row, attributeId)] = value;
        }

        public void SetCapRaw(int row, int attributeId, float value)
        {
            ValidateSlot(attributeId);
            _cap[Cell(row, attributeId)] = value;
        }

        public void SetLastSnapshot(int row, int attributeId, float value)
        {
            _lastSnapshot[Cell(row, attributeId)] = value;
        }

        // —— 脏位：高槽位专属（[64, SlotCount)），内嵌 DirtyFlags 的 64 位掩码覆盖不到的部分 ——

        public void MarkAttributeDirtyHigh(int row, int attributeId)
        {
            if (attributeId < AttributeBuffer.MAX_ATTRS || (uint)attributeId >= (uint)_slotCount)
            {
                return;
            }

            _attributeDirtyRows[row >> 6] |= 1UL << (row & 63);
        }

        public bool HasAttributeDirtyHigh(int row) => (_attributeDirtyRows[row >> 6] & (1UL << (row & 63))) != 0UL;

        public void ClearAttributeDirtyHigh(int row) => _attributeDirtyRows[row >> 6] &= ~(1UL << (row & 63));

        public void MarkAggregateDirty(int row)
        {
            _aggregateDirtyRows[row >> 6] |= 1UL << (row & 63);
        }

        public bool HasAggregateDirty(int row) => (_aggregateDirtyRows[row >> 6] & (1UL << (row & 63))) != 0UL;
        public void ClearAggregateDirty(int row) => _aggregateDirtyRows[row >> 6] &= ~(1UL << (row & 63));

        // ── 标签位列（RFC-0067 P2）：[0,256) 内嵌镜像 + [256, TagIdSpace) 唯一真相 ──

        private int TagCell(int row, int word) => row * _tagWordCount + word;

        private int ValidateTagId(int tagId)
        {
            if ((uint)tagId >= (uint)TagIdSpace)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.TagSlotOutOfRange: tagId={tagId} 超出本局容量计划 {TagIdSpace} 位（RFC-0067 §3.1）。");
            }

            return tagId;
        }

        public bool HasTag(int row, int tagId)
        {
            ValidateTagId(tagId);
            return (_tagBits[TagCell(row, tagId >> 6)] & (1UL << (tagId & 63))) != 0UL;
        }

        public void SetTag(int row, int tagId)
        {
            ValidateTagId(tagId);
            _tagBits[TagCell(row, tagId >> 6)] |= 1UL << (tagId & 63);
        }

        public void ClearTag(int row, int tagId)
        {
            ValidateTagId(tagId);
            _tagBits[TagCell(row, tagId >> 6)] &= ~(1UL << (tagId & 63));
        }

        /// <summary>低槽位镜像：内嵌容器 settle 后同步（全 4 字整体镜像最省分支）。</summary>
        public void MirrorTagWords(int row, in GameplayTagContainer container)
        {
            for (int w = 0; w < 4 && w < _tagWordCount; w++)
            {
                _tagBits[TagCell(row, w)] = container.Bits[w];
            }
        }

        public bool GetTagLastSnapshot(int row, int tagId)
        {
            ValidateTagId(tagId);
            return (_tagLastSnapshot[TagCell(row, tagId >> 6)] & (1UL << (tagId & 63))) != 0UL;
        }

        public void SetTagLastSnapshot(int row, int tagId, bool present)
        {
            ValidateTagId(tagId);
            if (present)
            {
                _tagLastSnapshot[TagCell(row, tagId >> 6)] |= 1UL << (tagId & 63);
            }
            else
            {
                _tagLastSnapshot[TagCell(row, tagId >> 6)] &= ~(1UL << (tagId & 63));
            }
        }

        /// <summary>建行时按当前位图播种标签快照（与内嵌快照初始化对齐）。</summary>
        public void SeedTagSnapshotFromBits(int row)
        {
            for (int w = 0; w < _tagWordCount; w++)
            {
                _tagLastSnapshot[TagCell(row, w)] = _tagBits[TagCell(row, w)];
            }
        }

        public void MarkTagDirtyHigh(int row, int tagId)
        {
            if (tagId < GameplayTagContainer.MAX_TAG_ID + 1 || (uint)tagId >= (uint)TagIdSpace)
            {
                return;
            }

            _tagDirtyRows[row >> 6] |= 1UL << (row & 63);
        }

        public bool HasTagDirtyHigh(int row) => (_tagDirtyRows[row >> 6] & (1UL << (row & 63))) != 0UL;
        public void ClearTagDirtyHigh(int row) => _tagDirtyRows[row >> 6] &= ~(1UL << (row & 63));

        /// <summary>整行拷贝（事务影子/存档快照用）。高槽位语义：<paramref name="length"/> 从 fromSlot 起。</summary>
        public void CopyRowTo(int row, float[] baseOut, float[] capOut, float[] currentOut, int fromSlot)
        {
            int cell = Cell(row, fromSlot);
            int length = _slotCount - fromSlot;
            Array.Copy(_base, cell, baseOut, 0, length);
            Array.Copy(_cap, cell, capOut, 0, length);
            Array.Copy(_current, cell, currentOut, 0, length);
        }

        public void RestoreRowFrom(int row, float[] baseIn, float[] capIn, float[] currentIn, int fromSlot)
        {
            int cell = Cell(row, fromSlot);
            int length = _slotCount - fromSlot;
            Array.Copy(baseIn, 0, _base, cell, length);
            Array.Copy(capIn, 0, _cap, cell, length);
            Array.Copy(currentIn, 0, _current, cell, length);
        }
    }
}
