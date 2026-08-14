namespace GraphAiShowcaseCommon;

public sealed record GraphAiShowcaseSnapshot(
    string ShowcaseId,
    string Mode,
    string Title,
    string GraphProgramId,
    int GraphInstructionCount,
    int Tick,
    int State,
    string StateLabel,
    int Intent,
    string IntentLabel,
    int CompletedTasks,
    string Boundary,
    GraphAiHotPathSnapshot HotPath,
    GraphAiActorSnapshot[] Actors)
{
    public static GraphAiShowcaseSnapshot Inactive(string runtimeKey) =>
        new(
            runtimeKey,
            "Inactive",
            "Inactive",
            string.Empty,
            0,
            0,
            0,
            "Inactive",
            0,
            "Inactive",
            0,
            string.Empty,
            GraphAiHotPathSnapshot.Empty,
            System.Array.Empty<GraphAiActorSnapshot>());
}

public sealed record GraphAiActorSnapshot(
    string Name,
    string InstanceId,
    int State,
    string StateLabel,
    int Intent,
    string IntentLabel,
    string ActionLabel,
    int BtNode,
    int TaskId,
    string TaskLabel,
    int TaskRemainingTicks,
    int Health,
    int EnemyDistanceCm,
    int WorldXCm,
    int WorldYCm);

public sealed record GraphAiHotPathSnapshot(
    int EntityCount,
    long LastGraphExecutions,
    long TotalGraphExecutions,
    long LastElapsedMicroseconds,
    long LastAllocatedBytes,
    int LastGen0Collections,
    int IntentChecksum)
{
    public static GraphAiHotPathSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}
