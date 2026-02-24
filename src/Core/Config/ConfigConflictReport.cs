using System;
using System.Collections.Generic;

namespace Ludots.Core.Config
{
    public sealed class ConfigConflictReport
    {
        private readonly Dictionary<string, List<string>> _fragmentsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Path, string Id), string> _winnerById = new();
        private readonly HashSet<(string Path, string Id)> _deleted = new();

        public void RecordFragment(string relativePath, string sourceUri)
        {
            if (!_fragmentsByPath.TryGetValue(relativePath, out var list))
            {
                list = new List<string>();
                _fragmentsByPath[relativePath] = list;
            }
            list.Add(sourceUri);
        }

        public void RecordWinner(string relativePath, string id, string sourceUri)
        {
            _winnerById[(relativePath, id)] = sourceUri;
            _deleted.Remove((relativePath, id));
        }

        public void RecordDeleted(string relativePath, string id, string sourceUri)
        {
            _winnerById.Remove((relativePath, id));
            _deleted.Add((relativePath, id));
            RecordFragment(relativePath, sourceUri);
        }

        public bool TryGetWinner(string relativePath, string id, out string sourceUri)
        {
            return _winnerById.TryGetValue((relativePath, id), out sourceUri);
        }

        public void PrintSummary(int maxLinesPerPath = 10)
        {
            foreach (var kvp in _fragmentsByPath)
            {
                int n = kvp.Value.Count;
                Console.WriteLine($"[ConfigConflictReport] {kvp.Key}: fragments={n}");
                int shown = 0;
                for (int i = 0; i < kvp.Value.Count && shown < maxLinesPerPath; i++, shown++)
                {
                    Console.WriteLine($"  - {kvp.Value[i]}");
                }
            }
        }
    }
}

