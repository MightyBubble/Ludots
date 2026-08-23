using System;

namespace Ludots.Core.Map.Board
{
    public static class NavBakeSourceKinds
    {
        public const string None = "none";
        public const string ContinuousHeightmap = "continuous-heightmap";
        public const string BoardLogicTerrain = "board-logic-terrain";
        public const string MapEntities = "map-entities";
        public const string RuntimeEntities = "runtime-entities";
    }

    public sealed class NavBakePolicy
    {
        public string HeightSource { get; set; }

        public string ClassificationSource { get; set; }

        public string StaticObstacleSource { get; set; }

        public string RuntimeObstacleSource { get; set; }

        public static NavBakePolicy ForBoardLogicTerrain()
        {
            return new NavBakePolicy
            {
                HeightSource = NavBakeSourceKinds.BoardLogicTerrain,
                ClassificationSource = NavBakeSourceKinds.BoardLogicTerrain,
                StaticObstacleSource = NavBakeSourceKinds.None,
                RuntimeObstacleSource = NavBakeSourceKinds.None
            };
        }

        public NavBakePolicy Clone()
        {
            return new NavBakePolicy
            {
                HeightSource = HeightSource,
                ClassificationSource = ClassificationSource,
                StaticObstacleSource = StaticObstacleSource,
                RuntimeObstacleSource = RuntimeObstacleSource
            };
        }
    }

    public static class NavBakePolicyValidator
    {
        public static void Validate(BoardConfig board, NavBakePolicy policy)
        {
            ArgumentNullException.ThrowIfNull(board);
            ArgumentNullException.ThrowIfNull(policy);

            string spatialType = board.SpatialType?.Trim() ?? string.Empty;
            if (spatialType.Length == 0)
            {
                throw new InvalidOperationException($"Board '{board.Name}' requires SpatialType before NavBakePolicy validation.");
            }

            ValidateSource(policy.HeightSource, nameof(policy.HeightSource),
                NavBakeSourceKinds.None,
                NavBakeSourceKinds.ContinuousHeightmap,
                NavBakeSourceKinds.BoardLogicTerrain);
            ValidateSource(policy.ClassificationSource, nameof(policy.ClassificationSource),
                NavBakeSourceKinds.None,
                NavBakeSourceKinds.BoardLogicTerrain);
            ValidateSource(policy.StaticObstacleSource, nameof(policy.StaticObstacleSource),
                NavBakeSourceKinds.None,
                NavBakeSourceKinds.MapEntities);
            ValidateSource(policy.RuntimeObstacleSource, nameof(policy.RuntimeObstacleSource),
                NavBakeSourceKinds.None,
                NavBakeSourceKinds.RuntimeEntities);

            if (IsNodeGraph(spatialType))
            {
                RequireNone(policy.HeightSource, nameof(policy.HeightSource), spatialType);
                RequireNone(policy.ClassificationSource, nameof(policy.ClassificationSource), spatialType);
                RequireNone(policy.StaticObstacleSource, nameof(policy.StaticObstacleSource), spatialType);
                RequireNone(policy.RuntimeObstacleSource, nameof(policy.RuntimeObstacleSource), spatialType);
                return;
            }

            if (string.Equals(policy.HeightSource, NavBakeSourceKinds.None, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Board '{board.Name}' NavBakePolicy.heightSource cannot be 'none' for a terrain board.");
            }

            if (!string.Equals(policy.ClassificationSource, NavBakeSourceKinds.BoardLogicTerrain, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Board '{board.Name}' NavBakePolicy.classificationSource must be '{NavBakeSourceKinds.BoardLogicTerrain}' for a terrain board.");
            }

            if (string.Equals(policy.HeightSource, NavBakeSourceKinds.ContinuousHeightmap, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(board.VisualHeightmapAsset))
            {
                throw new InvalidOperationException(
                    $"Board '{board.Name}' selects continuous-heightmap but VisualHeightmapAsset is empty.");
            }

        }

        public static NavBakePolicy Require(BoardConfig board)
        {
            ArgumentNullException.ThrowIfNull(board);
            if (board.NavBakePolicy == null)
            {
                throw new InvalidOperationException(
                    $"Board '{board.Name}' has NavigationEnabled={board.NavigationEnabled} but no NavBakePolicy. " +
                    "Declare the height, classification, static-obstacle, and runtime-obstacle roles explicitly.");
            }

            Validate(board, board.NavBakePolicy);
            return board.NavBakePolicy;
        }

        private static void ValidateSource(string value, string propertyName, params string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value.Trim(), value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"NavBakePolicy.{propertyName} must be a non-empty trimmed string.");
            }

            for (int i = 0; i < allowed.Length; i++)
            {
                if (string.Equals(value, allowed[i], StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"NavBakePolicy.{propertyName} '{value}' is unsupported. Expected: {string.Join(", ", allowed)}.");
        }

        private static void RequireNone(string value, string propertyName, string spatialType)
        {
            if (!string.Equals(value, NavBakeSourceKinds.None, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"NodeGraph board cannot select NavBakePolicy.{propertyName} '{value}'; NodeGraph has no NavMesh bake input (SpatialType='{spatialType}').");
            }
        }

        private static bool IsNodeGraph(string spatialType)
            => string.Equals(spatialType, "NodeGraph", StringComparison.OrdinalIgnoreCase);

    }
}
