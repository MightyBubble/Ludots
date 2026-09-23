namespace Ludots.Core.GraphRuntime
{
    public readonly record struct GraphExecutionTraceEvent(
        int GraphProgramId,
        int InstructionIndex,
        ushort Op,
        int Step);

    public interface IGraphExecutionTraceSink
    {
        void OnInstruction(in GraphExecutionTraceEvent traceEvent);
    }
}
