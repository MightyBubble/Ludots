namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GraphVmLimits
    {
        public const int MaxFloatRegisters = 32;
        public const int MaxIntRegisters = 32;
        public const int MaxBoolRegisters = 32;
        public const int MaxEntityRegisters = 32;
        public const int MaxTextRegisters = 8;
        public const int MaxTextCharsPerRegister = 128;
        public const int MaxTargets = 256;
        public const int MaxIntIds = 256;
        public const int MaxCallStackDepth = 16;

        /// <summary>
        /// Hard limit on nested InvokeScript frames across one run-to-halt tree.
        /// Distinct from MaxCallStackDepth, which only bounds Call inside one program.
        /// </summary>
        public const int MaxInvokeDepth = 16;

        /// <summary>
        /// Hard limit on instructions executed per single Execute call.
        /// Prevents runaway programs (infinite jump loops, etc.) from hanging the frame.
        /// Shared by the whole InvokeScript tree; nested Execute does not reset it.
        /// </summary>
        public const int MaxInstructionsPerExecution = 4096;

        /// <summary>
        /// Size of the opcode handler table. Must be greater than the highest GraphNodeOp value
        /// and leave room for startup-registered mod graph ops.
        /// </summary>
        public const int HandlerTableSize = 2048;
    }
}
