namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Defines what happens to duration when a new stack is added.
    /// </summary>
    public enum StackPolicy : byte
    {
        /// <summary>No stacking — each application creates a separate effect entity.</summary>
        None = 0,
        /// <summary>New stack resets the duration to original value.</summary>
        RefreshDuration = 1,
        /// <summary>New stack adds to the remaining duration.</summary>
        AddDuration = 2,
        /// <summary>Duration does not change on stack.</summary>
        KeepDuration = 3,
    }

    /// <summary>
    /// Defines what happens when a stack reaches its limit.
    /// </summary>
    public enum StackOverflowPolicy : byte
    {
        /// <summary>Reject the new application entirely.</summary>
        RejectNew = 0,
        /// <summary>Remove the oldest stack (reduce count) then add the new one.</summary>
        RemoveOldest = 1,
    }

    /// <summary>
    /// ECS component tracking effect stacking state and policy.
    /// </summary>
    public struct EffectStack
    {
        /// <summary>Current number of stacks.</summary>
        public int Count;
        /// <summary>Maximum number of stacks allowed (0 = no limit).</summary>
        public int Limit;
        /// <summary>What happens to duration when stacking.</summary>
        public StackPolicy Policy;
        /// <summary>What happens when the limit is reached.</summary>
        public StackOverflowPolicy OverflowPolicy;

        /// <summary>
        /// Try to add a stack. Returns true if the stack was accepted.
        /// </summary>
        public bool TryAddStack()
        {
            if (Limit > 0 && Count >= Limit)
            {
                if (OverflowPolicy == StackOverflowPolicy.RemoveOldest)
                {
                    // Count stays the same (remove oldest + add new)
                    return true;
                }
                return false; // RejectNew
            }
            Count++;
            return true;
        }
    }
}
