using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.World;

namespace CrowdSimulationTests.Parity;

/// <summary>Web 端烘焙的跳跃候选快照，只作行为对拍参照；入库 .navsurface 由 Ludots 烘焙。</summary>
internal static class S1JumpTruth
{
    public const string FileName = "s1-jump-truth.json";

    public static NavSurfaceJumpCandidate[] Read(string seedAssetsDir)
    {
        string path = Path.Combine(seedAssetsDir, "CrowdSimulation", "parity", FileName);
        JsonArray rows = JsonNode.Parse(File.ReadAllText(path))!["jumpCandidates"]!.AsArray();
        var jumps = new NavSurfaceJumpCandidate[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            JsonArray row = rows[i]!.AsArray();
            jumps[i] = new NavSurfaceJumpCandidate
            {
                FromX = row[0]!.GetValue<ushort>(),
                FromY = row[1]!.GetValue<ushort>(),
                ToX = row[2]!.GetValue<ushort>(),
                ToY = row[3]!.GetValue<ushort>(),
                DropCm = row[4]!.GetValue<int>(),
                LengthCells = row[5]!.GetValue<float>(),
            };
        }

        return jumps;
    }
}
