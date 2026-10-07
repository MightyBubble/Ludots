namespace Ludots.Core.GraphRuntime
{
    /// <summary>Bit flags for <see cref="GraphInstruction.Flags"/>.</summary>
    public static class GraphInstructionFlags
    {
        /// <summary>InvokeScript Imm is a Func Lib name symbol index (patch to GraphId).</summary>
        public const byte FuncLibName = 1;
    }
}
