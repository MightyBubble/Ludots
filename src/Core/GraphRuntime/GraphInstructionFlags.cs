namespace Ludots.Core.GraphRuntime
{
    /// <summary>Bit flags for <see cref="GraphInstruction.Flags"/>.</summary>
    public static class GraphInstructionFlags
    {
        /// <summary>InvokeScript Imm is a Func Lib name symbol index (patch to GraphId).</summary>
        public const byte FuncLibName = 1;

        /// <summary>
        /// SubmitCommandIntent / SubmitCast / SubmitEngageBatch authored a collectionKey.
        /// Imm (command intent) or ImmF (cast / engage) holds the symbol index until patch
        /// replaces it with the collection key id and clears the flag.
        /// </summary>
        public const byte CollectionKeyAuthored = 2;
    }
}
