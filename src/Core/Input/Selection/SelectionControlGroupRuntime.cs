using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection
{
    public static class SelectionControlGroupRuntime
    {
        public static bool TrySaveViewedSelectionToGroup(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            Entity viewer,
            int groupIndex,
            bool mirrorToFormation)
        {
            ArgumentNullException.ThrowIfNull(globals);
            ArgumentNullException.ThrowIfNull(selection);

            if (!world.IsAlive(viewer) ||
                !SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out _, out _, out Entity sourceContainer))
            {
                return false;
            }

            bool saved = TryCopyContainerToAlias(selection, viewer, sourceContainer, SelectionSetKeys.ControlGroup(groupIndex), SelectionContainerKind.Group);
            if (!saved || !mirrorToFormation)
            {
                return saved;
            }

            if (!TryCopyContainerToAlias(selection, viewer, sourceContainer, SelectionSetKeys.FormationPrimary, SelectionContainerKind.Formation))
            {
                return false;
            }

            selection.TryBindView(viewer, SelectionViewKeys.Formation, viewer, SelectionSetKeys.FormationPrimary);
            return true;
        }

        public static bool TryRecallGroupToLive(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            Entity viewer,
            int groupIndex,
            bool mirrorToFormation)
        {
            ArgumentNullException.ThrowIfNull(globals);
            ArgumentNullException.ThrowIfNull(selection);

            if (!world.IsAlive(viewer) ||
                !selection.TryGetSelectionEntity(viewer, SelectionSetKeys.ControlGroup(groupIndex), out Entity groupContainer))
            {
                return false;
            }

            bool recalled = TryCopyContainerToAlias(selection, viewer, groupContainer, SelectionSetKeys.LivePrimary, SelectionContainerKind.Live);
            if (!recalled)
            {
                return false;
            }

            selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary);
            globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
            globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;

            if (!mirrorToFormation)
            {
                return true;
            }

            if (!TryCopyContainerToAlias(selection, viewer, groupContainer, SelectionSetKeys.FormationPrimary, SelectionContainerKind.Formation))
            {
                return false;
            }

            selection.TryBindView(viewer, SelectionViewKeys.Formation, viewer, SelectionSetKeys.FormationPrimary);
            return true;
        }

        public static bool TryDescribeControlGroup(
            World world,
            SelectionRuntime selection,
            Entity viewer,
            int groupIndex,
            out SelectionContainerDescriptor descriptor)
        {
            descriptor = default;
            return world.IsAlive(viewer) &&
                   selection.TryDescribeSelection(viewer, SelectionSetKeys.ControlGroup(groupIndex), out descriptor);
        }

        private static bool TryCopyContainerToAlias(
            SelectionRuntime selection,
            Entity owner,
            Entity sourceContainer,
            string aliasKey,
            SelectionContainerKind kind)
        {
            int count = selection.GetSelectionCount(sourceContainer);
            if (count <= 0)
            {
                return selection.TryGetOrCreateContainer(owner, aliasKey, kind, out Entity emptyTarget) &&
                       (!selection.ClearSelection(emptyTarget) || selection.GetSelectionCount(emptyTarget) == 0);
            }

            var snapshot = new Entity[count];
            int written = selection.CopySelection(sourceContainer, snapshot);
            if (written <= 0)
            {
                return false;
            }

            return selection.TryGetOrCreateContainer(owner, aliasKey, kind, out Entity targetContainer) &&
                   selection.ReplaceSelection(targetContainer, snapshot.AsSpan(0, written));
        }
    }
}
