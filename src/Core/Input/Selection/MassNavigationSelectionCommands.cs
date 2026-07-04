using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection;

public static class MassNavigationSelectionCommands
{
    public static void ClearLocalCommandSelectionSets(World world, Dictionary<string, object> globals)
    {
        if (!SelectionContextRuntime.TryGetRuntime(globals, out SelectionRuntime selection))
        {
            return;
        }

        if (!globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localPlayerObj) ||
            localPlayerObj is not Entity owner ||
            !world.IsAlive(owner))
        {
            return;
        }

        selection.ClearSelection(owner, SelectionSetKeys.LivePrimary);
        selection.ClearSelection(owner, SelectionSetKeys.FormationPrimary);
        selection.ClearSelection(owner, SelectionSetKeys.CommandPreview);
        selection.ClearSelection(owner, SelectionSetKeys.CommandSnapshot);
    }
}
