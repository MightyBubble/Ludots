using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorNavState
{
    public bool HasStart { get; set; }
    public WorldCmInt2 Start { get; set; }
    public bool HasGoal { get; set; }
    public WorldCmInt2 Goal { get; set; }
    public NavPathStatus PathStatus { get; set; } = NavPathStatus.NotReady;
    public int[] PathXcm { get; set; } = Array.Empty<int>();
    public int[] PathZcm { get; set; } = Array.Empty<int>();
    public long LastQueryElapsedMicroseconds { get; set; }
    public int LastRebuiltTiles { get; set; }
    public int LastFailedTiles { get; set; }
    public int PendingTiles { get; set; }
    public string LastMessage { get; set; } = "idle";

    public void SetPath(NavPathResult result, long elapsedMicroseconds)
    {
        PathStatus = result.Status;
        PathXcm = result.PathXcm ?? Array.Empty<int>();
        PathZcm = result.PathZcm ?? Array.Empty<int>();
        LastQueryElapsedMicroseconds = elapsedMicroseconds;
        LastMessage = result.Status.ToString();
    }
}
