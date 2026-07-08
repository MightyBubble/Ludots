using System;
using System.Collections.Generic;
using Ludots.Core.Map;

namespace Ludots.Core.Map.Board
{
    public static class BoardResolution
    {
        public static bool TryGetSingleNodeGraphBoard(
            MapSession session,
            out INodeGraphBoard? board,
            out string error)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            INodeGraphBoard? found = null;
            List<string>? duplicateNames = null;
            IReadOnlyList<IBoard> boards = session.AllBoards;
            for (int i = 0; i < boards.Count; i++)
            {
                if (boards[i] is not INodeGraphBoard candidate)
                {
                    continue;
                }

                if (found == null)
                {
                    found = candidate;
                    continue;
                }

                duplicateNames ??= new List<string> { found.Name };
                duplicateNames.Add(candidate.Name);
            }

            if (duplicateNames != null)
            {
                board = null;
                error = $"Map '{session.MapId.Value}' has multiple NodeGraph boards: {string.Join(", ", duplicateNames)}.";
                return false;
            }

            if (found == null)
            {
                board = null;
                error = $"Map '{session.MapId.Value}' has no NodeGraph board.";
                return false;
            }

            board = found;
            error = string.Empty;
            return true;
        }

        public static INodeGraphBoard RequireSingleNodeGraphBoard(MapSession session, string consumer)
        {
            if (string.IsNullOrWhiteSpace(consumer))
            {
                throw new ArgumentException("consumer is required.", nameof(consumer));
            }

            if (TryGetSingleNodeGraphBoard(session, out INodeGraphBoard? board, out string error))
            {
                return board!;
            }

            throw new InvalidOperationException($"{consumer} requires exactly one NodeGraph board. {error}");
        }
    }
}
