using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Vision
{
    public readonly struct FogSnapshotHandle : IEquatable<FogSnapshotHandle>
    {
        public FogSnapshotHandle(int value)
        {
            Value = value;
        }

        public readonly int Value;

        public bool Equals(FogSnapshotHandle other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is FogSnapshotHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(FogSnapshotHandle left, FogSnapshotHandle right) => left.Equals(right);
        public static bool operator !=(FogSnapshotHandle left, FogSnapshotHandle right) => !left.Equals(right);
    }

    public readonly struct FogSnapshotHeader
    {
        public FogSnapshotHeader(int scopeKeyId, FogLayerId layerId, int tick, int cellCount)
        {
            ScopeKeyId = scopeKeyId;
            LayerId = layerId;
            Tick = tick;
            CellCount = cellCount;
        }

        public readonly int ScopeKeyId;
        public readonly FogLayerId LayerId;
        public readonly int Tick;
        public readonly int CellCount;
    }

    public sealed class FogSnapshotStore
    {
        private SnapshotEntry[] _entries;
        private int _count;
        private readonly RelationshipRuntime? _relationships;

        public FogSnapshotStore(int initialCapacity = 8, RelationshipRuntime? relationships = null)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _entries = new SnapshotEntry[initialCapacity];
            _relationships = relationships;
        }

        public int Count => _count;

        public FogSnapshotHandle Capture(FogField field, int tick)
        {
            EnsureCapacity(_count + 1);
            int capacity = Math.Max(16, field.ChunkCount * 16 * 16);
            FogCellState[] states = new FogCellState[capacity];
            int count;
            while (true)
            {
                count = field.CopyCells(states);
                if (count < states.Length || field.ChunkCount == 0)
                {
                    break;
                }

                states = new FogCellState[states.Length * 2];
            }

            if (count != states.Length)
            {
                Array.Resize(ref states, count);
            }

            int entryIndex = _count++;
            _entries[entryIndex] = new SnapshotEntry(
                new FogSnapshotHeader(field.ScopeKeyId, field.LayerId, tick, count),
                states);
            return new FogSnapshotHandle(entryIndex + 1);
        }

        public bool TryGetHeader(FogSnapshotHandle handle, out FogSnapshotHeader header)
        {
            if (!TryGetEntry(handle, out SnapshotEntry entry))
            {
                header = default;
                return false;
            }

            header = entry.Header;
            return true;
        }

        public bool TryRestore(FogSnapshotHandle handle, FogField target)
        {
            if (!TryGetEntry(handle, out SnapshotEntry entry))
            {
                return false;
            }

            if (entry.Header.LayerId != target.LayerId || entry.Header.ScopeKeyId != target.ScopeKeyId)
            {
                return false;
            }

            target.ApplySnapshot(entry.States);
            return true;
        }

        public bool TryMergeExplored(FogSnapshotHandle first, FogSnapshotHandle second, FogField target)
        {
            if (!TryGetEntry(first, out SnapshotEntry a) ||
                !TryGetEntry(second, out SnapshotEntry b) ||
                a.Header.LayerId != b.Header.LayerId ||
                a.Header.LayerId != target.LayerId ||
                target.ScopeKeyId != a.Header.ScopeKeyId)
            {
                return false;
            }

            target.ApplySnapshot(a.States);
            for (int i = 0; i < b.States.Length; i++)
            {
                FogCellState state = b.States[i];
                if (state.Visibility == CellVisibility.Explored ||
                    state.Visibility == CellVisibility.Visible)
                {
                    if (target.GetVisibility(state.Cell) == CellVisibility.Unseen)
                    {
                        target.SetExplored(state.Cell);
                    }
                }
            }

            target.ClearDirty();
            return true;
        }

        public bool TryMergeSharedExplored(
            FogSnapshotHandle source,
            FogSnapshotHandle shared,
            FogField target,
            Entity sourceScopeHost,
            Entity sharedScopeHost,
            int relationshipTypeId)
        {
            if (_relationships == null ||
                !_relationships.HasLink(sourceScopeHost, sharedScopeHost, relationshipTypeId))
            {
                return false;
            }

            return TryMergeExplored(source, shared, target);
        }

        public int Diff(FogSnapshotHandle from, FogSnapshotHandle to, Span<FogCell> changedCells)
        {
            if (changedCells.IsEmpty ||
                !TryGetEntry(from, out SnapshotEntry left) ||
                !TryGetEntry(to, out SnapshotEntry right) ||
                left.Header.LayerId != right.Header.LayerId)
            {
                return 0;
            }

            int written = 0;
            for (int i = 0; i < right.States.Length && written < changedCells.Length; i++)
            {
                FogCellState rightState = right.States[i];
                if (!TryFind(left.States, rightState.Cell, out CellVisibility leftVisibility) ||
                    leftVisibility != rightState.Visibility)
                {
                    changedCells[written++] = rightState.Cell;
                }
            }

            for (int i = 0; i < left.States.Length && written < changedCells.Length; i++)
            {
                FogCellState leftState = left.States[i];
                if (!TryFind(right.States, leftState.Cell, out _))
                {
                    changedCells[written++] = leftState.Cell;
                }
            }

            return written;
        }

        private static bool TryFind(FogCellState[] states, FogCell cell, out CellVisibility visibility)
        {
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].Cell == cell)
                {
                    visibility = states[i].Visibility;
                    return true;
                }
            }

            visibility = CellVisibility.Unseen;
            return false;
        }

        private bool TryGetEntry(FogSnapshotHandle handle, out SnapshotEntry entry)
        {
            int index = handle.Value - 1;
            if ((uint)index >= (uint)_count)
            {
                entry = default;
                return false;
            }

            entry = _entries[index];
            return true;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _entries.Length)
            {
                return;
            }

            int next = _entries.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _entries, next);
        }

        private readonly struct SnapshotEntry
        {
            public SnapshotEntry(FogSnapshotHeader header, FogCellState[] states)
            {
                Header = header;
                States = states;
            }

            public readonly FogSnapshotHeader Header;
            public readonly FogCellState[] States;
        }
    }
}
