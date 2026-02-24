using Arch.Core;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Per-player selection buffer. Stores which entities are currently selected.
    /// This component lives on the player/controller entity, not on selected entities.
    /// 
    /// Architecture:
    ///   - SelectionBuffer (this): which entities are selected (persistent ECS component)
    ///   - SelectedTag (on targets): marker on selected entities for query filtering
    ///   - ISelectionInputHandler: input → selection operations
    ///   - SelectionGroupBuffer: control groups (Ctrl+1..9)
    /// 
    /// Note: Storage pattern (EntityIds/WorldIds/Versions + Count) parallels
    /// <c>OrderEntitySelection</c> in the GAS Orders layer. These are intentionally
    /// separate types to avoid Input↔GAS coupling. <c>OrderEntitySelection</c> is
    /// transient per-order data; this is a persistent per-player component with
    /// interactive operations (Add/Remove/Contains).
    /// </summary>
    public unsafe struct SelectionBuffer
    {
        public const int CAPACITY = 64;
        
        public fixed int EntityIds[CAPACITY];
        public fixed int EntityWorldIds[CAPACITY];
        public fixed int EntityVersions[CAPACITY];
        public int Count;
        
        /// <summary>
        /// The "primary" selected entity (first in buffer, used for UI portrait).
        /// </summary>
        public Entity Primary
        {
            get
            {
                if (Count <= 0) return default;
                return Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(
                    EntityIds[0], EntityWorldIds[0], EntityVersions[0]);
            }
        }
        
        /// <summary>
        /// Add an entity to the selection.
        /// </summary>
        public bool Add(Entity entity)
        {
            if (Count >= CAPACITY) return false;
            // Check for duplicates
            for (int i = 0; i < Count; i++)
            {
                if (EntityIds[i] == entity.Id && EntityWorldIds[i] == entity.WorldId)
                    return false;
            }
            EntityIds[Count] = entity.Id;
            EntityWorldIds[Count] = entity.WorldId;
            EntityVersions[Count] = entity.Version;
            Count++;
            return true;
        }
        
        /// <summary>
        /// Remove an entity from the selection.
        /// </summary>
        public bool Remove(Entity entity)
        {
            for (int i = 0; i < Count; i++)
            {
                if (EntityIds[i] == entity.Id && EntityWorldIds[i] == entity.WorldId)
                {
                    Count--;
                    if (i != Count)
                    {
                        EntityIds[i] = EntityIds[Count];
                        EntityWorldIds[i] = EntityWorldIds[Count];
                        EntityVersions[i] = EntityVersions[Count];
                    }
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Clear the entire selection.
        /// </summary>
        public void Clear()
        {
            Count = 0;
        }
        
        /// <summary>
        /// Get an entity from the selection by index.
        /// </summary>
        public Entity Get(int index)
        {
            if ((uint)index >= (uint)Count) return default;
            return Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(
                EntityIds[index], EntityWorldIds[index], EntityVersions[index]);
        }
        
        /// <summary>
        /// Check if an entity is in the selection.
        /// </summary>
        public bool Contains(Entity entity)
        {
            for (int i = 0; i < Count; i++)
            {
                if (EntityIds[i] == entity.Id && EntityWorldIds[i] == entity.WorldId)
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Replace the entire selection with a single entity.
        /// </summary>
        public void SetSingle(Entity entity)
        {
            Count = 1;
            EntityIds[0] = entity.Id;
            EntityWorldIds[0] = entity.WorldId;
            EntityVersions[0] = entity.Version;
        }
    }

    /// <summary>
    /// Control group storage (Ctrl+1 through Ctrl+9).
    /// Each group stores a set of entity references that can be recalled.
    /// This component lives on the player/controller entity.
    /// </summary>
    public unsafe struct SelectionGroupBuffer
    {
        public const int MAX_GROUPS = 10;        // groups 0-9
        public const int MAX_PER_GROUP = 24;     // max entities per group
        
        /// <summary>Flat storage: [group * MAX_PER_GROUP + offset]</summary>
        public fixed int EntityIds[MAX_GROUPS * MAX_PER_GROUP];
        public fixed int EntityWorldIds[MAX_GROUPS * MAX_PER_GROUP];
        public fixed int EntityVersions[MAX_GROUPS * MAX_PER_GROUP];
        public fixed int GroupCounts[MAX_GROUPS];
        
        /// <summary>
        /// Save the current selection to a group.
        /// </summary>
        public void SaveGroup(int groupIndex, in SelectionBuffer selection)
        {
            if ((uint)groupIndex >= MAX_GROUPS) return;
            int count = selection.Count;
            if (count > MAX_PER_GROUP) count = MAX_PER_GROUP;
            int baseIdx = groupIndex * MAX_PER_GROUP;
            for (int i = 0; i < count; i++)
            {
                EntityIds[baseIdx + i] = selection.EntityIds[i];
                EntityWorldIds[baseIdx + i] = selection.EntityWorldIds[i];
                EntityVersions[baseIdx + i] = selection.EntityVersions[i];
            }
            GroupCounts[groupIndex] = count;
        }
        
        /// <summary>
        /// Recall a group into a selection buffer.
        /// </summary>
        public void RecallGroup(int groupIndex, ref SelectionBuffer selection)
        {
            if ((uint)groupIndex >= MAX_GROUPS) return;
            selection.Clear();
            int count = GroupCounts[groupIndex];
            int baseIdx = groupIndex * MAX_PER_GROUP;
            for (int i = 0; i < count && i < SelectionBuffer.CAPACITY; i++)
            {
                var entity = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(
                    EntityIds[baseIdx + i], EntityWorldIds[baseIdx + i], EntityVersions[baseIdx + i]);
                selection.Add(entity);
            }
        }
        
        /// <summary>
        /// Get the count of entities in a group.
        /// </summary>
        public int GetGroupCount(int groupIndex)
        {
            if ((uint)groupIndex >= MAX_GROUPS) return 0;
            return GroupCounts[groupIndex];
        }
    }
}
