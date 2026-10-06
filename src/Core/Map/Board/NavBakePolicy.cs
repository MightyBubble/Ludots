using System;

namespace Ludots.Core.Map.Board
{
    public static class NavBakeHeightSources
    {
        public const string ContinuousHeightmap = "continuous-heightmap";

        public const string LogicTerrain = "logic-terrain";
    }

    /// <summary>
    /// Which height truth a board bakes from. Absent means the board has not
    /// joined this contract and the existing logic-terrain projection remains.
    /// </summary>
    public sealed class NavBakePolicy
    {
        public string HeightSource { get; set; } = string.Empty;

        public bool UsesContinuousHeightmap =>
            string.Equals(HeightSource, NavBakeHeightSources.ContinuousHeightmap, StringComparison.Ordinal);

        public NavBakePolicy Clone() => new() { HeightSource = HeightSource };
    }

    public static class NavBakePolicyRules
    {
        public static void Validate(BoardConfig board, bool mapDeclaresContinuousHeightmap)
        {
            ArgumentNullException.ThrowIfNull(board);
            NavBakePolicy policy = board.NavBakePolicy;
            if (policy == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(policy.HeightSource) ||
                !string.Equals(policy.HeightSource.Trim(), policy.HeightSource, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Board '{board.Name}' NavBakePolicy.heightSource must be a non-empty trimmed string.");
            }

            if (!string.Equals(policy.HeightSource, NavBakeHeightSources.ContinuousHeightmap, StringComparison.Ordinal) &&
                !string.Equals(policy.HeightSource, NavBakeHeightSources.LogicTerrain, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Board '{board.Name}' NavBakePolicy.heightSource '{policy.HeightSource}' is unsupported. Expected '{NavBakeHeightSources.ContinuousHeightmap}' or '{NavBakeHeightSources.LogicTerrain}'.");
            }

            if (!policy.UsesContinuousHeightmap)
            {
                return;
            }

            bool boardDeclaresAsset = !string.IsNullOrWhiteSpace(board.ContinuousHeightmapAsset);
            if (!boardDeclaresAsset && !mapDeclaresContinuousHeightmap)
            {
                throw new InvalidOperationException(
                    $"Board '{board.Name}' selects continuous-heightmap but neither the board nor the map declares a continuous height asset.");
            }
        }
    }
}
