using System;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public sealed class NavBakeInput
    {
        public NavBakeInput(
            BoardConfig board,
            LogicTerrainField logicTerrain,
            IVisualHeightmap continuousHeightmap,
            NavObstacleSet staticObstacles,
            NavObstacleSet runtimeStructuralObstacles)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Policy = board.NavBakePolicy
                ?? throw new InvalidOperationException($"Board '{board.Name}' requires NavBakePolicy before a NavBakeInput can be created.");
            NavBakePolicyValidator.Validate(board, Policy);

            LogicTerrain = logicTerrain;
            ContinuousHeightmap = continuousHeightmap;
            StaticObstacles = staticObstacles;
            RuntimeStructuralObstacles = runtimeStructuralObstacles;

            RequireSelectedInput(NavBakeSourceKinds.BoardLogicTerrain, Policy.ClassificationSource, LogicTerrain, "logicTerrain");
            RequireSelectedInput(NavBakeSourceKinds.ContinuousHeightmap, Policy.HeightSource, ContinuousHeightmap, "continuousHeightmap");
            RequireSelectedInput(NavBakeSourceKinds.BoardLogicTerrain, Policy.HeightSource, LogicTerrain, "logicTerrain");
            RequireSelectedInput(NavBakeSourceKinds.MapEntities, Policy.StaticObstacleSource, StaticObstacles, "staticObstacles");
            RequireSelectedInput(NavBakeSourceKinds.RuntimeEntities, Policy.RuntimeObstacleSource, RuntimeStructuralObstacles, "runtimeStructuralObstacles");
        }

        public BoardConfig Board { get; }

        public NavBakePolicy Policy { get; }

        public LogicTerrainField LogicTerrain { get; }

        public IVisualHeightmap ContinuousHeightmap { get; }

        public NavObstacleSet StaticObstacles { get; }

        public NavObstacleSet RuntimeStructuralObstacles { get; }

        private static void RequireSelectedInput(string sourceKind, string selectedSource, object value, string inputName)
        {
            if (!string.Equals(sourceKind, selectedSource, StringComparison.Ordinal))
            {
                return;
            }

            if (value == null)
            {
                throw new InvalidOperationException(
                    $"NavBakePolicy selects '{selectedSource}' but NavBakeInput.{inputName} is missing.");
            }
        }
    }
}
