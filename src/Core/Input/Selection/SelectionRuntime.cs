using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection
{
    public static class SelectionRuntime
    {
        public static bool TryGetController(World world, Dictionary<string, object> globals, out Entity controller)
        {
            controller = default;
            if (!globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj) || obj is not Entity entity)
            {
                return false;
            }

            if (!world.IsAlive(entity) || !world.Has<SelectionBuffer>(entity))
            {
                return false;
            }

            controller = entity;
            return true;
        }

        public static int CollectSelected(World world, Entity controller, List<Entity> selected)
        {
            selected.Clear();
            if (!world.IsAlive(controller) || !world.Has<SelectionBuffer>(controller))
            {
                return 0;
            }

            var buffer = world.Get<SelectionBuffer>(controller);
            for (int i = 0; i < buffer.Count; i++)
            {
                var entity = buffer.Get(i);
                if (world.IsAlive(entity))
                {
                    selected.Add(entity);
                }
            }

            return selected.Count;
        }

        public static int CollectSelected(World world, Dictionary<string, object> globals, List<Entity> selected)
        {
            if (!TryGetController(world, globals, out var controller))
            {
                selected.Clear();
                return 0;
            }

            return CollectSelected(world, controller, selected);
        }

        public static void ClearSelection(World world, Dictionary<string, object> globals, Entity controller)
        {
            if (!world.IsAlive(controller) || !world.Has<SelectionBuffer>(controller))
            {
                globals.Remove(CoreServiceKeys.SelectedEntity.Name);
                return;
            }

            var buffer = world.Get<SelectionBuffer>(controller);
            for (int i = 0; i < buffer.Count; i++)
            {
                var entity = buffer.Get(i);
                if (world.IsAlive(entity) && world.Has<SelectedTag>(entity))
                {
                    world.Remove<SelectedTag>(entity);
                }
            }

            buffer.Clear();
            world.Set(controller, buffer);
            globals.Remove(CoreServiceKeys.SelectedEntity.Name);
        }

        public static void SaveGroup(World world, Entity controller, int groupIndex)
        {
            if (!world.IsAlive(controller))
            {
                return;
            }

            if (!world.Has<SelectionBuffer>(controller))
            {
                world.Add(controller, new SelectionBuffer());
            }

            if (!world.Has<SelectionGroupBuffer>(controller))
            {
                world.Add(controller, new SelectionGroupBuffer());
            }

            var selection = world.Get<SelectionBuffer>(controller);
            var groups = world.Get<SelectionGroupBuffer>(controller);
            groups.SaveGroup(groupIndex, selection);
            world.Set(controller, groups);
        }

        public static void RecallGroup(World world, Dictionary<string, object> globals, Entity controller, int groupIndex)
        {
            if (!world.IsAlive(controller) || !world.Has<SelectionGroupBuffer>(controller))
            {
                return;
            }

            var groups = world.Get<SelectionGroupBuffer>(controller);
            var recalled = new SelectionBuffer();
            groups.RecallGroup(groupIndex, ref recalled);

            Span<Entity> entities = stackalloc Entity[SelectionBuffer.CAPACITY];
            int count = 0;
            for (int i = 0; i < recalled.Count && count < SelectionBuffer.CAPACITY; i++)
            {
                var entity = recalled.Get(i);
                if (world.IsAlive(entity))
                {
                    entities[count++] = entity;
                }
            }

            ApplySelection(world, globals, controller, entities.Slice(0, count), SelectionApplyMode.Replace);
        }

        public static void PruneSelection(
            World world,
            Dictionary<string, object> globals,
            Entity controller,
            ISelectionCandidatePolicy? policy = null)
        {
            if (!world.IsAlive(controller) || !world.Has<SelectionBuffer>(controller))
            {
                globals.Remove(CoreServiceKeys.SelectedEntity.Name);
                return;
            }

            var source = world.Get<SelectionBuffer>(controller);
            var pruned = new SelectionBuffer();

            for (int i = 0; i < source.Count; i++)
            {
                var entity = source.Get(i);
                bool keep = world.IsAlive(entity)
                    && (policy == null || policy.IsSelectable(world, controller, entity));

                if (!keep)
                {
                    if (world.IsAlive(entity) && world.Has<SelectedTag>(entity))
                    {
                        world.Remove<SelectedTag>(entity);
                    }

                    continue;
                }

                pruned.Add(entity);
                if (!world.Has<SelectedTag>(entity))
                {
                    world.Add(entity, new SelectedTag());
                }
            }

            world.Set(controller, pruned);
            SyncPrimarySelectionGlobal(world, globals, in pruned);
        }

        public static void ApplySelection(
            World world,
            Dictionary<string, object> globals,
            Entity controller,
            IReadOnlyList<Entity> entities,
            SelectionApplyMode applyMode)
        {
            if (!world.IsAlive(controller))
            {
                return;
            }

            if (!world.Has<SelectionBuffer>(controller))
            {
                world.Add(controller, new SelectionBuffer());
            }

            var buffer = world.Get<SelectionBuffer>(controller);
            ApplySelectionInternal(world, ref buffer, entities, applyMode);
            world.Set(controller, buffer);
            SyncPrimarySelectionGlobal(world, globals, in buffer);
        }

        public static void ApplySelection(
            World world,
            Dictionary<string, object> globals,
            Entity controller,
            ReadOnlySpan<Entity> entities,
            SelectionApplyMode applyMode)
        {
            if (!world.IsAlive(controller))
            {
                return;
            }

            if (!world.Has<SelectionBuffer>(controller))
            {
                world.Add(controller, new SelectionBuffer());
            }

            var buffer = world.Get<SelectionBuffer>(controller);
            ApplySelectionInternal(world, ref buffer, entities, applyMode);
            world.Set(controller, buffer);
            SyncPrimarySelectionGlobal(world, globals, in buffer);
        }

        public static bool TryFillOrderSelection(World world, Dictionary<string, object> globals, ref OrderEntitySelection entities)
        {
            entities = default;
            if (TryGetController(world, globals, out var controller) && world.IsAlive(controller) && world.Has<SelectionBuffer>(controller))
            {
                var buffer = world.Get<SelectionBuffer>(controller);
                for (int i = 0; i < buffer.Count && entities.Count < OrderEntitySelection.MaxEntities; i++)
                {
                    var entity = buffer.Get(i);
                    if (world.IsAlive(entity))
                    {
                        entities.Add(entity);
                    }
                }

                if (entities.Count > 0)
                {
                    return true;
                }
            }

            if (globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var selectedObj)
                && selectedObj is Entity selected
                && world.IsAlive(selected))
            {
                entities.Add(selected);
                return true;
            }

            return false;
        }

        private static void ApplySelectionInternal(
            World world,
            ref SelectionBuffer buffer,
            IReadOnlyList<Entity> entities,
            SelectionApplyMode applyMode)
        {
            switch (applyMode)
            {
                case SelectionApplyMode.Replace:
                    ClearAliveTags(world, in buffer);
                    buffer.Clear();
                    AddEntities(world, ref buffer, entities);
                    break;
                case SelectionApplyMode.Add:
                    AddEntities(world, ref buffer, entities);
                    break;
                case SelectionApplyMode.Toggle:
                    ToggleEntities(world, ref buffer, entities);
                    break;
            }
        }

        private static void ApplySelectionInternal(
            World world,
            ref SelectionBuffer buffer,
            ReadOnlySpan<Entity> entities,
            SelectionApplyMode applyMode)
        {
            switch (applyMode)
            {
                case SelectionApplyMode.Replace:
                    ClearAliveTags(world, in buffer);
                    buffer.Clear();
                    AddEntities(world, ref buffer, entities);
                    break;
                case SelectionApplyMode.Add:
                    AddEntities(world, ref buffer, entities);
                    break;
                case SelectionApplyMode.Toggle:
                    ToggleEntities(world, ref buffer, entities);
                    break;
            }
        }

        private static void AddEntities(World world, ref SelectionBuffer buffer, IReadOnlyList<Entity> entities)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                TryAddSelectedEntity(world, ref buffer, entities[i]);
            }
        }

        private static void AddEntities(World world, ref SelectionBuffer buffer, ReadOnlySpan<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                TryAddSelectedEntity(world, ref buffer, entities[i]);
            }
        }

        private static void ToggleEntities(World world, ref SelectionBuffer buffer, IReadOnlyList<Entity> entities)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                ToggleSelectedEntity(world, ref buffer, entities[i]);
            }
        }

        private static void ToggleEntities(World world, ref SelectionBuffer buffer, ReadOnlySpan<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                ToggleSelectedEntity(world, ref buffer, entities[i]);
            }
        }

        private static void TryAddSelectedEntity(World world, ref SelectionBuffer buffer, Entity entity)
        {
            if (!world.IsAlive(entity))
            {
                return;
            }

            if (buffer.Add(entity) && !world.Has<SelectedTag>(entity))
            {
                world.Add(entity, new SelectedTag());
            }
        }

        private static void ToggleSelectedEntity(World world, ref SelectionBuffer buffer, Entity entity)
        {
            if (!world.IsAlive(entity))
            {
                return;
            }

            if (buffer.Contains(entity))
            {
                buffer.Remove(entity);
                if (world.Has<SelectedTag>(entity))
                {
                    world.Remove<SelectedTag>(entity);
                }

                return;
            }

            if (buffer.Add(entity) && !world.Has<SelectedTag>(entity))
            {
                world.Add(entity, new SelectedTag());
            }
        }

        private static void ClearAliveTags(World world, in SelectionBuffer buffer)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                var entity = buffer.Get(i);
                if (world.IsAlive(entity) && world.Has<SelectedTag>(entity))
                {
                    world.Remove<SelectedTag>(entity);
                }
            }
        }

        private static void SyncPrimarySelectionGlobal(World world, Dictionary<string, object> globals, in SelectionBuffer buffer)
        {
            if (buffer.Count > 0)
            {
                var primary = buffer.Primary;
                if (world.IsAlive(primary))
                {
                    globals[CoreServiceKeys.SelectedEntity.Name] = primary;
                    return;
                }
            }

            globals.Remove(CoreServiceKeys.SelectedEntity.Name);
        }
    }
}
