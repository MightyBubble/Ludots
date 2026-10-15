using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.World;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// Web 端烘焙的链接候选快照，只作行为对拍参照；入库 .navsurface 由 Ludots 烘焙。
/// 每行依次是 fromX、fromY、toX、toY、dropCm、lengthCells。lengthCells 不进候选记录，
/// 读入时由位移推出 steps 和斜向位，再核对按 v2 公式算出的格距和快照里的 float 逐位相同。
/// </summary>
internal static class S1JumpTruth
{
    public const string FileName = "s1-jump-truth.json";

    public static NavSurfaceLinkCandidate[] Read(string seedAssetsDir)
    {
        string path = Path.Combine(seedAssetsDir, "CrowdSimulation", "parity", FileName);
        JsonArray rows = JsonNode.Parse(File.ReadAllText(path))!["linkCandidates"]!.AsArray();
        var candidates = new NavSurfaceLinkCandidate[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            JsonArray row = rows[i]!.AsArray();
            ushort fromX = row[0]!.GetValue<ushort>();
            ushort fromY = row[1]!.GetValue<ushort>();
            ushort toX = row[2]!.GetValue<ushort>();
            ushort toY = row[3]!.GetValue<ushort>();
            int dx = Math.Abs(toX - fromX);
            int dy = Math.Abs(toY - fromY);
            if (dx != 0 && dy != 0 && dx != dy)
            {
                throw new InvalidDataException($"{path}: 第 {i} 行位移 ({dx}, {dy}) 不在 8 个方向上。");
            }

            var candidate = new NavSurfaceLinkCandidate
            {
                Steps = (ushort)Math.Max(dx, dy),
                Diagonal = dx != 0 && dy != 0,
                FromX = fromX,
                FromY = fromY,
                ToX = toX,
                ToY = toY,
                DropCm = row[4]!.GetValue<int>(),
            };
            float stored = row[5]!.GetValue<float>();
            if (BitConverter.SingleToInt32Bits(candidate.LengthCells) != BitConverter.SingleToInt32Bits(stored))
            {
                throw new InvalidDataException(
                    $"{path}: 第 {i} 行 lengthCells {stored:R} 与 steps {candidate.Steps}、斜向 {candidate.Diagonal} 推出的 {candidate.LengthCells:R} 不同。");
            }

            candidates[i] = candidate;
        }

        return candidates;
    }
}
