namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Effect lifecycle phases. Each phase supports Pre/Main/Post Graph execution.
    /// Phases are ordered: OnPropose → (ResponseChain) → OnCalculate → OnResolve → OnHit → OnApply → OnPeriod → OnExpire → OnRemove.
    /// </summary>
    public enum EffectPhaseId : byte
    {
        /// <summary>Effect enters Proposal window, before ResponseChain.</summary>
        OnPropose = 0,
        /// <summary>After ResponseChain settles, compute final Modifier values.</summary>
        OnCalculate = 1,
        /// <summary>Target resolution (spatial query / graph query) to collect candidates.</summary>
        OnResolve = 2,
        /// <summary>Per-target hit validation (evasion / shield / immunity).</summary>
        OnHit = 3,
        /// <summary>Apply Modifiers to each validated target.</summary>
        OnApply = 4,
        /// <summary>Periodic tick for duration effects.</summary>
        OnPeriod = 5,
        /// <summary>Natural expiration.</summary>
        OnExpire = 6,
        /// <summary>Forced removal.</summary>
        OnRemove = 7,
    }

    /// <summary>
    /// Slot within a Phase's three-stage execution: Pre → Main → Post.
    /// Inspired by Trigger framework's InsertBefore / AnchorCommand / InsertAfter.
    /// </summary>
    public enum PhaseSlot : byte
    {
        /// <summary>User Graph that runs before the Preset Main step (InsertBefore).</summary>
        Pre = 0,
        /// <summary>Preset built-in step determined by PresetType (AnchorCommand).</summary>
        Main = 1,
        /// <summary>User Graph that runs after the Preset Main step (InsertAfter).</summary>
        Post = 2,
    }

    public static class EffectPhaseConstants
    {
        /// <summary>Total number of phases.</summary>
        public const int PhaseCount = 8;
    }
}
