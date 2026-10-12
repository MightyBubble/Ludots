using System;
using System.Collections.Generic;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Config
{
    public sealed class ConfigConflictReport
    {
        private readonly Dictionary<string, List<string>> _fragmentsByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Path, string Id), string> _winnerById = new();
        private readonly List<(string Path, string Id)> _winnerOrder = new();
        private readonly Dictionary<string, string> _fileWinnerByPath = new(StringComparer.Ordinal);
        private readonly HashSet<(string Path, string Id)> _deleted = new();
        private readonly Dictionary<(string Path, string Id), (string First, string Second)> _duplicates = new();
        private readonly Dictionary<(string TemplateId, string Component), string> _componentOverrideChains = new();

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
            var key = (relativePath, id);
            if (!_winnerById.ContainsKey(key))
            {
                _winnerOrder.Add(key);
            }

            _winnerById[key] = sourceUri;
            _deleted.Remove(key);
        }

        /// <summary>
        /// DeepObject / Replace / ArrayReplace 的胜出是最后一份片段。
        /// 这些策略把数组整段换掉，没有字段级赢家。
        /// </summary>
        public void RecordFileWinner(string relativePath, string sourceUri)
        {
            _fileWinnerByPath[relativePath] = sourceUri;
        }

        public void RecordDeleted(string relativePath, string id, string sourceUri)
        {
            var key = (relativePath, id);
            _winnerById.Remove(key);
            for (int i = _winnerOrder.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_winnerOrder[i].Path, key.relativePath, StringComparison.Ordinal) &&
                    string.Equals(_winnerOrder[i].Id, key.id, StringComparison.Ordinal))
                {
                    _winnerOrder.RemoveAt(i);
                    break;
                }
            }

            _deleted.Add(key);
            RecordFragment(relativePath, sourceUri);
        }

        public void RecordDuplicateId(string relativePath, string id, string firstSource, string secondSource)
        {
            var key = (relativePath, id);
            if (!_duplicates.TryGetValue(key, out var existing))
            {
                _duplicates[key] = (firstSource, secondSource);
            }
        }

        public IReadOnlyList<(string Id, string FirstSource, string SecondSource)> GetDuplicateIds(string relativePath)
        {
            var result = new List<(string, string, string)>();
            foreach (KeyValuePair<(string Path, string Id), (string First, string Second)> pair in _duplicates)
            {
                if (string.Equals(pair.Key.Path, relativePath, StringComparison.Ordinal))
                {
                    result.Add((pair.Key.Id, pair.Value.First, pair.Value.Second));
                }
            }
            return result;
        }

        /// <summary>
        /// uses 折叠的组件覆盖链：同一组件被多个源写入时留痕，写入者按低到高优先级
        /// 排列（自身记作 "self"）。字段拼写错误导致静默回退底值靠此定位。
        /// </summary>
        public void RecordComponentOverrideChain(string templateId, string component, string writerChain)
        {
            var key = (templateId, component);
            if (!_componentOverrideChains.ContainsKey(key))
            {
                _componentOverrideChains[key] = writerChain;
            }
        }

        public IReadOnlyList<(string Component, string WriterChain)> GetComponentOverrideChains(string templateId)
        {
            var result = new List<(string, string)>();
            foreach (KeyValuePair<(string TemplateId, string Component), string> pair in _componentOverrideChains)
            {
                if (string.Equals(pair.Key.TemplateId, templateId, StringComparison.Ordinal))
                {
                    result.Add((pair.Key.Component, pair.Value));
                }
            }

            return result;
        }

        public bool TryGetWinner(string relativePath, string id, out string sourceUri)
        {
            return _winnerById.TryGetValue((relativePath, id), out sourceUri);
        }

        public bool TryGetFileWinner(string relativePath, out string sourceUri)
        {
            return _fileWinnerByPath.TryGetValue(relativePath, out sourceUri);
        }

        public IReadOnlyList<(string Id, string SourceUri)> GetWinners(string relativePath)
        {
            var result = new List<(string, string)>();
            for (int i = 0; i < _winnerOrder.Count; i++)
            {
                var key = _winnerOrder[i];
                if (!string.Equals(key.Path, relativePath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_winnerById.TryGetValue(key, out string sourceUri))
                {
                    result.Add((key.Id, sourceUri));
                }
            }

            return result;
        }

        public IReadOnlyList<string> GetRelativePaths()
        {
            var result = new List<string>(_fragmentsByPath.Count);
            foreach (string path in _fragmentsByPath.Keys)
            {
                result.Add(path);
            }

            return result;
        }

        public IReadOnlyList<(string TemplateId, string Component, string WriterChain)> GetAllComponentOverrideChains()
        {
            var result = new List<(string, string, string)>(_componentOverrideChains.Count);
            foreach (KeyValuePair<(string TemplateId, string Component), string> pair in _componentOverrideChains)
            {
                result.Add((pair.Key.TemplateId, pair.Key.Component, pair.Value));
            }

            return result;
        }

        public IReadOnlyList<string> GetFragments(string relativePath)
        {
            if (_fragmentsByPath.TryGetValue(relativePath, out var list))
                return list;
            return Array.Empty<string>();
        }

        public IReadOnlyList<(string Path, string Id)> GetDeletions(string relativePath)
        {
            var result = new List<(string, string)>();
            foreach (var d in _deleted)
            {
                if (string.Equals(d.Path, relativePath, StringComparison.Ordinal))
                    result.Add(d);
            }
            return result;
        }

        public void PrintSummary(int maxLinesPerPath = 10)
        {
            foreach (var kvp in _fragmentsByPath)
            {
                int n = kvp.Value.Count;
                Log.Info(in LogChannels.Config, $"{kvp.Key}: fragments={n}");
                int shown = 0;
                for (int i = 0; i < kvp.Value.Count && shown < maxLinesPerPath; i++, shown++)
                {
                    Log.Info(in LogChannels.Config, $"  - {kvp.Value[i]}");
                }
            }
        }
    }
}

