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
            IVisualHeightmap? visualHeightmap)
        {
            Source = source;
            BoardName = boardName;
            BoardTerrain = boardTerrain;
            VisualHeightmap = visualHeightmap;
        }

        public TerrainPresentationSource Source { get; }

        public string BoardName { get; }

        public VertexMap? BoardTerrain { get; }

        public IVisualHeightmap? VisualHeightmap { get; }

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
                        visualHeightmap: null);
                }

                case TerrainPresentationSource.VisualHeightmap:
                    if (!string.IsNullOrWhiteSpace(binding.BoardName))
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects VisualHeightmap presentation and must not also declare BoardName '{binding.BoardName}'.");
                    }
                    if (session.VisualHeightmap == null)
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' selects VisualHeightmap presentation, but no visual heightmap was loaded.");
                    }

                    return new ResolvedTerrainPresentation(
                        TerrainPresentationSource.VisualHeightmap,
                        boardName: string.Empty,
                        boardTerrain: null,
                        session.VisualHeightmap);

                default:
                    throw new InvalidOperationException(
                        $"Map '{session.MapId.Value}' declares unsupported terrain presentation source '{binding.Source}'.");
            }
        }
    }
}
