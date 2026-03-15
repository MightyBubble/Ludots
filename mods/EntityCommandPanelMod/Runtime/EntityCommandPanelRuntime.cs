using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class EntityCommandPanelRuntime : IEntityCommandPanelService
    {
        internal const int MaxInstances = 64;

        private readonly GameEngine _engine;
        private readonly EntityCommandPanelSourceRegistry _sources;
        private readonly EntityCommandPanelAliasStore _aliases;

        private readonly bool[] _occupied = new bool[MaxInstances];
        private readonly bool[] _visible = new bool[MaxInstances];
        private readonly Entity[] _targets = new Entity[MaxInstances];
        private readonly string[] _sourceIds = new string[MaxInstances];
        private readonly string[] _instanceKeys = new string[MaxInstances];
        private readonly EntityCommandPanelAnchor[] _anchors = new EntityCommandPanelAnchor[MaxInstances];
        private readonly EntityCommandPanelSize[] _sizes = new EntityCommandPanelSize[MaxInstances];
        private readonly int[] _groupIndices = new int[MaxInstances];
        private readonly uint[] _generations = new uint[MaxInstances];
        private readonly uint[] _observedRevisions = new uint[MaxInstances];
        private readonly Dictionary<string, int> _slotsByInstanceKey = new(StringComparer.Ordinal);

        private uint _revision = 1;

        public EntityCommandPanelRuntime(
            GameEngine engine,
            EntityCommandPanelSourceRegistry sources,
            EntityCommandPanelAliasStore aliases)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            _aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
        }

        internal uint Revision => _revision;

        internal bool HasVisiblePanels
        {
            get
            {
                for (int i = 0; i < MaxInstances; i++)
                {
                    if (_occupied[i] && _visible[i])
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public EntityCommandPanelHandle Open(in EntityCommandPanelOpenRequest request)
        {
            int slot = ResolveOrAllocateSlot(request.InstanceKey);
            if (slot < 0)
            {
                Log.Warn(in LogChannels.Presentation, "[EntityCommandPanel] No free panel slots remain.");
                return EntityCommandPanelHandle.Invalid;
            }

            bool changed = !_occupied[slot];
            if (!_occupied[slot])
            {
                _occupied[slot] = true;
                _generations[slot]++;
                if (_generations[slot] == 0)
                {
                    _generations[slot] = 1;
                }
            }

            changed |= Assign(ref _targets[slot], request.TargetEntity);
            changed |= Assign(ref _sourceIds[slot], request.SourceId ?? string.Empty);
            changed |= Assign(ref _instanceKeys[slot], request.InstanceKey ?? string.Empty);
            changed |= Assign(ref _anchors[slot], request.Anchor);
            changed |= Assign(ref _sizes[slot], request.Size);
            changed |= Assign(ref _visible[slot], request.StartVisible);
            changed |= Assign(ref _groupIndices[slot], request.InitialGroupIndex);

            if (!string.IsNullOrWhiteSpace(_instanceKeys[slot]))
            {
                _slotsByInstanceKey[_instanceKeys[slot]] = slot;
            }

            NormalizeGroupIndex(slot);
            ObserveSlot(slot, force: true);

            if (changed)
            {
                MarkDirty();
            }

            return CreateHandle(slot);
        }

        public bool Close(EntityCommandPanelHandle handle)
        {
            if (!TryGetSlot(handle, out int slot))
            {
                return false;
            }

            string instanceKey = _instanceKeys[slot];
            if (!string.IsNullOrWhiteSpace(instanceKey))
            {
                _slotsByInstanceKey.Remove(instanceKey);
            }

            _occupied[slot] = false;
            _visible[slot] = false;
            _targets[slot] = Entity.Null;
            _sourceIds[slot] = string.Empty;
            _instanceKeys[slot] = string.Empty;
            _anchors[slot] = default;
            _sizes[slot] = default;
            _groupIndices[slot] = 0;
            _observedRevisions[slot] = 0;
            _aliases.RemoveHandle(handle);
            MarkDirty();
            return true;
        }

        public bool SetVisible(EntityCommandPanelHandle handle, bool visible)
        {
            return TryMutate(handle, static (runtime, slot, value) => runtime.SetVisibleInternal(slot, value), visible);
        }

        public bool RebindTarget(EntityCommandPanelHandle handle, Entity targetEntity)
        {
            return TryMutate(handle, static (runtime, slot, value) => runtime.RebindTargetInternal(slot, value), targetEntity);
        }

        public bool SetGroupIndex(EntityCommandPanelHandle handle, int groupIndex)
        {
            return TryMutate(handle, static (runtime, slot, value) => runtime.SetGroupIndexInternal(slot, value), groupIndex);
        }

        public bool CycleGroup(EntityCommandPanelHandle handle, int delta)
        {
            if (!TryGetSlot(handle, out int slot))
            {
                return false;
            }

            int groupCount = GetGroupCount(slot);
            if (groupCount <= 0 || delta == 0)
            {
                return false;
            }

            int current = Math.Clamp(_groupIndices[slot], 0, groupCount - 1);
            int next = (current + delta) % groupCount;
            if (next < 0)
            {
                next += groupCount;
            }

            if (next == current)
            {
                return false;
            }

            _groupIndices[slot] = next;
            MarkDirty();
            return true;
        }

        public bool SetAnchor(EntityCommandPanelHandle handle, in EntityCommandPanelAnchor anchor)
        {
            return TryMutate(handle, static (runtime, slot, value) => runtime.SetAnchorInternal(slot, value), anchor);
        }

        public bool SetSize(EntityCommandPanelHandle handle, in EntityCommandPanelSize size)
        {
            return TryMutate(handle, static (runtime, slot, value) => runtime.SetSizeInternal(slot, value), size);
        }

        public bool TryGetState(EntityCommandPanelHandle handle, out EntityCommandPanelInstanceState state)
        {
            if (TryGetSlot(handle, out int slot))
            {
                state = CreateState(slot);
                return true;
            }

            state = default;
            return false;
        }

        internal int CopyVisibleSlotIndices(Span<int> destination)
        {
            int count = 0;
            for (int i = 0; i < MaxInstances && count < destination.Length; i++)
            {
                if (_occupied[i] && _visible[i])
                {
                    destination[count++] = i;
                }
            }

            return count;
        }

        internal bool TryGetStateBySlot(int slot, out EntityCommandPanelInstanceState state)
        {
            if ((uint)slot < MaxInstances && _occupied[slot])
            {
                state = CreateState(slot);
                return true;
            }

            state = default;
            return false;
        }

        internal bool TryGetSourceBySlot(int slot, out IEntityCommandPanelSource source)
        {
            if ((uint)slot < MaxInstances &&
                _occupied[slot] &&
                _sources.TryGet(_sourceIds[slot], out source))
            {
                return true;
            }

            source = null!;
            return false;
        }

        internal string ResolveEntityTitle(Entity target)
        {
            if (_engine.World.IsAlive(target) &&
                _engine.World.TryGet(target, out Name name) &&
                !string.IsNullOrWhiteSpace(name.Value))
            {
                return name.Value;
            }

            return target == Entity.Null ? "No Entity" : $"Entity#{target.Id}";
        }

        internal bool RefreshObservedState()
        {
            bool changed = false;
            for (int i = 0; i < MaxInstances; i++)
            {
                if (!_occupied[i])
                {
                    continue;
                }

                changed |= ObserveSlot(i, force: false);
            }

            if (changed)
            {
                MarkDirty();
            }

            return changed;
        }

        private bool ObserveSlot(int slot, bool force)
        {
            if (!_occupied[slot])
            {
                return false;
            }

            NormalizeGroupIndex(slot);

            uint nextRevision = ComputeObservedRevision(slot);
            if (!force && nextRevision == _observedRevisions[slot])
            {
                return false;
            }

            _observedRevisions[slot] = nextRevision;
            return true;
        }

        private uint ComputeObservedRevision(int slot)
        {
            uint revision = 2166136261u;
            revision = HashCombine(revision, (uint)_targets[slot].Id);
            revision = HashCombine(revision, (uint)_targets[slot].Version);
            revision = HashCombine(revision, (uint)_groupIndices[slot]);
            revision = HashCombine(revision, _visible[slot] ? 1u : 0u);

            if (_engine.World.IsAlive(_targets[slot]) &&
                _engine.World.TryGet(_targets[slot], out Name name) &&
                !string.IsNullOrWhiteSpace(name.Value))
            {
                revision = HashCombine(revision, (uint)name.Value.GetHashCode(StringComparison.Ordinal));
            }

            if (_sources.TryGet(_sourceIds[slot], out IEntityCommandPanelSource source) &&
                source.TryGetRevision(_targets[slot], out uint sourceRevision))
            {
                revision = HashCombine(revision, sourceRevision);
            }

            return revision;
        }

        private int ResolveOrAllocateSlot(string instanceKey)
        {
            if (!string.IsNullOrWhiteSpace(instanceKey) &&
                _slotsByInstanceKey.TryGetValue(instanceKey, out int existingSlot) &&
                _occupied[existingSlot])
            {
                return existingSlot;
            }

            for (int i = 0; i < MaxInstances; i++)
            {
                if (!_occupied[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private int GetGroupCount(int slot)
        {
            if (!_occupied[slot] ||
                !_sources.TryGet(_sourceIds[slot], out IEntityCommandPanelSource source))
            {
                return 0;
            }

            return Math.Max(0, source.GetGroupCount(_targets[slot]));
        }

        private void NormalizeGroupIndex(int slot)
        {
            int groupCount = GetGroupCount(slot);
            if (groupCount <= 0)
            {
                _groupIndices[slot] = 0;
                return;
            }

            _groupIndices[slot] = Math.Clamp(_groupIndices[slot], 0, groupCount - 1);
        }

        private bool SetVisibleInternal(int slot, bool visible)
        {
            if (_visible[slot] == visible)
            {
                return false;
            }

            _visible[slot] = visible;
            MarkDirty();
            return true;
        }

        private bool RebindTargetInternal(int slot, Entity targetEntity)
        {
            if (_targets[slot].Equals(targetEntity))
            {
                return false;
            }

            _targets[slot] = targetEntity;
            _observedRevisions[slot] = 0;
            NormalizeGroupIndex(slot);
            MarkDirty();
            return true;
        }

        private bool SetGroupIndexInternal(int slot, int groupIndex)
        {
            int groupCount = GetGroupCount(slot);
            int nextIndex = groupCount <= 0 ? 0 : Math.Clamp(groupIndex, 0, groupCount - 1);
            if (_groupIndices[slot] == nextIndex)
            {
                return false;
            }

            _groupIndices[slot] = nextIndex;
            MarkDirty();
            return true;
        }

        private bool SetAnchorInternal(int slot, EntityCommandPanelAnchor anchor)
        {
            if (_anchors[slot].Equals(anchor))
            {
                return false;
            }

            _anchors[slot] = anchor;
            MarkDirty();
            return true;
        }

        private bool SetSizeInternal(int slot, EntityCommandPanelSize size)
        {
            if (_sizes[slot].Equals(size))
            {
                return false;
            }

            _sizes[slot] = size;
            MarkDirty();
            return true;
        }

        private bool TryGetSlot(EntityCommandPanelHandle handle, out int slot)
        {
            slot = handle.Slot;
            return handle.IsValid &&
                   (uint)slot < MaxInstances &&
                   _occupied[slot] &&
                   _generations[slot] == handle.Generation;
        }

        private EntityCommandPanelInstanceState CreateState(int slot)
        {
            return new EntityCommandPanelInstanceState(
                CreateHandle(slot),
                _targets[slot],
                _sourceIds[slot],
                _instanceKeys[slot],
                _anchors[slot],
                _sizes[slot],
                _groupIndices[slot],
                _visible[slot]);
        }

        private EntityCommandPanelHandle CreateHandle(int slot)
        {
            return new EntityCommandPanelHandle(slot, _generations[slot]);
        }

        private void MarkDirty()
        {
            _revision++;
            if (_revision == 0)
            {
                _revision = 1;
            }
        }

        private static bool Assign<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            return true;
        }

        private static uint HashCombine(uint current, uint value)
        {
            unchecked
            {
                return (current ^ value) * 16777619u;
            }
        }

        private bool TryMutate<TValue>(EntityCommandPanelHandle handle, PanelMutation<TValue> mutation, TValue value)
        {
            if (!TryGetSlot(handle, out int slot))
            {
                return false;
            }

            return mutation(this, slot, value);
        }

        private delegate bool PanelMutation<in TValue>(EntityCommandPanelRuntime runtime, int slot, TValue value);
    }
}
