using System;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Factory: creates an IBoard from BoardConfig.SpatialType.
    /// </summary>
    public static class BoardFactory
    {
        public static IBoard Create(BoardConfig config, BoardIdRegistry registry)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            string name = config.Name ?? "default";
            int internedId = registry.GetOrAdd(name);
            var boardId = new BoardId(name);

            string spatialType = (config.SpatialType ?? "Grid").Trim();

            if (spatialType.Equals("HexGrid", StringComparison.OrdinalIgnoreCase) ||
                spatialType.Equals("Hex", StringComparison.OrdinalIgnoreCase) ||
                spatialType.Equals("Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info(in LogChannels.Map, $"Creating HexGridBoard '{name}' ({config.WidthInTiles}x{config.HeightInTiles}, hexEdge={config.HexEdgeLengthCm}cm)");
                return new HexGridBoard(boardId, name, config);
            }

            if (spatialType.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info(in LogChannels.Map, $"Creating NodeGraphBoard '{name}'");
                return new NodeGraphBoard(boardId, name, config);
            }

            // Default: Grid
            Log.Info(in LogChannels.Map, $"Creating GridBoard '{name}' ({config.WidthInTiles}x{config.HeightInTiles}, cell={config.GridCellSizeCm}cm)");
            return new GridBoard(boardId, name, config);
        }
    }
}
