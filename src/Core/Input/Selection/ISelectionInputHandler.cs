using Arch.Core;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Selection operation types that the selection system processes.
    /// </summary>
    public enum SelectionOp : byte
    {
        /// <summary>Replace selection with clicked entity.</summary>
        Select = 0,
        /// <summary>Toggle entity in/out of selection (Shift+click).</summary>
        Toggle = 1,
        /// <summary>Select all same-type entities visible (double-click).</summary>
        SelectSameType = 2,
        /// <summary>Box-select start (drag begin).</summary>
        BoxStart = 3,
        /// <summary>Box-select end (drag release).</summary>
        BoxEnd = 4,
        /// <summary>Deselect all.</summary>
        DeselectAll = 5,
        /// <summary>Save current selection to control group.</summary>
        SaveGroup = 6,
        /// <summary>Recall control group.</summary>
        RecallGroup = 7,
    }

    /// <summary>
    /// Interface for handling selection input events.
    /// Implementations are responsible for translating raw input into selection operations.
    /// The selection system processes these operations against the SelectionBuffer.
    /// 
    /// This allows different game genres to have different selection behaviors:
    ///   - RTS: box select, Shift+click toggle, double-click same-type, Ctrl+group
    ///   - MOBA: single select only, Tab to cycle
    ///   - RPG: click to select target, no multi-select
    /// </summary>
    public interface ISelectionInputHandler
    {
        /// <summary>
        /// Called each frame to poll for selection operations.
        /// Implementations should read input state and return the appropriate operation.
        /// </summary>
        /// <param name="op">The selection operation to perform.</param>
        /// <param name="target">Target entity for Select/Toggle operations.</param>
        /// <param name="groupIndex">Group index for SaveGroup/RecallGroup (0-9).</param>
        /// <returns>True if an operation should be processed this frame.</returns>
        bool Poll(out SelectionOp op, out Entity target, out int groupIndex);
    }
}
