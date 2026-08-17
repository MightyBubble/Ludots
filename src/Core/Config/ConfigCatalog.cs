using System;
using System.Collections.Generic;

namespace Ludots.Core.Config
{
    public sealed class ConfigCatalog
    {
        private readonly Dictionary<string, ConfigCatalogEntry> _entries = new(StringComparer.Ordinal);

        public IEnumerable<ConfigCatalogEntry> Entries => _entries.Values;

        public void Add(in ConfigCatalogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                throw new InvalidOperationException("Config catalog entry path must be non-empty.");
            }

            string normalized = Normalize(entry.RelativePath);
            if (_entries.ContainsKey(normalized))
            {
                throw new InvalidOperationException($"Config catalog contains duplicate path '{normalized}'.");
            }

            _entries.Add(normalized, entry);
        }

        public bool TryGet(string relativePath, out ConfigCatalogEntry entry)
        {
            return _entries.TryGetValue(Normalize(relativePath), out entry);
        }

        private static string Normalize(string relativePath)
        {
            relativePath = relativePath.Replace('\\', '/');
            if (relativePath.StartsWith("/")) relativePath = relativePath.Substring(1);
            return relativePath;
        }
    }
}
