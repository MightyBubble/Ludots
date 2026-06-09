using System;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation.Config
{
    public sealed class WorldHudStringTable
    {
        private readonly PresentationTextCatalog? _catalog;
        private readonly PresentationTextLocaleSelection? _localeSelection;
        private readonly string[] _table;
        private int _count;

        public int Count => _count;
        public int Capacity => _table.Length;

        public WorldHudStringTable(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _table = new string[capacity];
            _count = 1;
        }

        public WorldHudStringTable(
            PresentationTextCatalog catalog,
            PresentationTextLocaleSelection localeSelection,
            int runtimeStringCapacity = 256)
            : this(GetBridgeCapacity(catalog, runtimeStringCapacity))
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _localeSelection = localeSelection ?? throw new ArgumentNullException(nameof(localeSelection));
            _count = Math.Max(_count, catalog.TokenCount + 1);
        }

        public int Register(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (_count >= _table.Length) return 0;
            int id = _count++;
            _table[id] = text;
            return id;
        }

        public string? TryGet(int id)
        {
            if (_catalog != null && _localeSelection != null)
            {
                if (_catalog.TryGetTemplate(_localeSelection.ActiveLocaleId, id, out var template))
                    return template.Source;
            }

            if ((uint)id >= (uint)_table.Length) return null;
            return _table[id];
        }

        private static int GetBridgeCapacity(PresentationTextCatalog catalog, int runtimeStringCapacity)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (runtimeStringCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeStringCapacity));
            return checked(catalog.TokenCount + runtimeStringCapacity + 1);
        }
    }
}
