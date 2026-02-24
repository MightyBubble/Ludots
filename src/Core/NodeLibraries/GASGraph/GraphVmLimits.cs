namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GraphVmLimits
    {
        public const int MaxFloatRegisters = 32;
        public const int MaxIntRegisters = 32;
        public const int MaxBoolRegisters = 32;
        public const int MaxEntityRegisters = 32;
        public const int MaxTargets = 256;

        /// <summary>
        /// Hard limit on instructions executed per single Execute call.
        /// Prevents runaway programs (infinite jump loops, etc.) from hanging the frame.
        /// </summary>
        public const int MaxInstructionsPerExecution = 4096;

        /// <summary>
        /// Size of the opcode handler table. Must be greater than the highest GraphNodeOp value.
        /// Increased from 256 to 512 to accommodate Blackboard (300-305) and Config (310-312) ops.
        /// </summary>
        public const int HandlerTableSize = 512;
    }
}

