namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Data-driven policy when registered clients lack input for the next authoritative tick.
/// Neutral/prior-frame input reuse is intentionally absent.
/// </summary>
public enum Physics3DNetMissingInputPolicy : byte
{
    /// <summary>
    /// Do not begin ExecutingTick. Report MissingInputs and leave CommittedTick unchanged.
    /// </summary>
    HoldTick = 1,

    /// <summary>
    /// Throw <see cref="Physics3DNetMissingInputException"/> without starting execution.
    /// </summary>
    FailExplicit = 2
}
