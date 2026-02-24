using System;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public sealed class HtnRootTable
    {
        private readonly (int GoalPresetId, int RootTaskId)[] _items;

        public HtnRootTable((int GoalPresetId, int RootTaskId)[] items)
        {
            _items = items ?? Array.Empty<(int, int)>();
        }

        public bool TryGetRootTask(int goalPresetId, out int rootTaskId)
        {
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].GoalPresetId != goalPresetId) continue;
                rootTaskId = _items[i].RootTaskId;
                return true;
            }
            rootTaskId = -1;
            return false;
        }
    }
}

