using Arch.Core;
using Ludots.Core.Input.Selection;

namespace Ludots.Core.Gameplay.Morph
{
    internal static class MorphSelectionRewire
    {
        public static void ReplaceSource(SelectionRuntime? selection, Entity source, Entity target)
        {
            if (selection == null)
            {
                return;
            }

            selection.ReplaceMemberTargetEverywhere(source, target);
        }
    }
}
