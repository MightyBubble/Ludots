using Ludots.Platform.Abstractions;
using System;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class ResolvedTerrainPresentation
    {
        private ResolvedTerrainPresentation(
            TerrainPresentationSource source,
            string boardName,
            VertexMap? boardTerrain,
            IContinuousHeightmap? continuousHeightmap)
        {
            Source = source;
            BoardName = boardName;
            BoardTerrain = boardTerrain;
            ContinuousHeightmap = continuousHeightmap;
        }

        public TerrainPresentationSource Source { get; }

        public string BoardName { get; }

        public VertexMap? BoardTerrain { get; }

        public IContinuousHeightmap? ContinuousHeightmap { get; }

        public static ResolvedTerrainPresentation? Resolve(MapSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            TerrainPresentationBindingConfig? binding = session.MapConfig?.TerrainPresentation;
            if (binding == null)
            {
                return null;
            }

            switch (binding.Source)
            {
                case TerrainPresentationSource.BoardTerrain:
                {
                    if (string.IsNullOrWhiteSpace(binding.BoardName))
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects BoardTerrain presentation without a BoardName.");
                    }

                    IBoard? board = session.GetBoard(binding.BoardName);
                    if (board == null)
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects terrain board '{binding.BoardName}', but that board is not part of the map session.");
                    }
                    if (board is not ITerrainBoard terrainBoard)
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects board '{binding.BoardName}' for terrain presentation, but the board does not provide terrain.");
                    }
                    if (terrainBoard.VertexMap == null)
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects board '{binding.BoardName}' for terrain presentation, but the board has no loaded VertexMap.");
                    }

                    return new ResolvedTerrainPresentation(
                        TerrainPresentationSource.BoardTerrain,
                        binding.BoardName,
                        terrainBoard.VertexMap,
                        continuousHeightmap: null);
                }

                case TerrainPresentationSource.ContinuousHeightmap:
                    if (!string.IsNullOrWhiteSpace(binding.BoardName))
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects ContinuousHeightmap presentation and must not also declare BoardName '{binding.BoardName}'.");
                    }
                    if (session.ContinuousHeightmap == null)
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects ContinuousHeightmap presentation, but no continuous heightmap was loaded.");
                    }

                    return new ResolvedTerrainPresentation(
                        TerrainPresentationSource.ContinuousHeightmap,
                        boardName: string.Empty,
                        boardTerrain: null,
                        session.ContinuousHeightmap);

                default:
                    throw new InvalidOperationException(
                        $"Map '{session.MapId.Value}' declares unsupported terrain presentation source '{binding.Source}'.");
            }
        }
    }
}
