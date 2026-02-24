using System;
using System.Collections.Generic;

namespace Ludots.Core.Config
{
    public sealed class ConfigCatalog
    {
        private readonly Dictionary<string, ConfigCatalogEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<ConfigCatalogEntry> Entries => _entries.Values;

        public void Add(in ConfigCatalogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.RelativePath)) return;
            _entries[Normalize(entry.RelativePath)] = entry;
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

