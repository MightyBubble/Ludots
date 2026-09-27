using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Calendar
{
    public sealed class CalendarDefinitionRegistry
    {
        private readonly Dictionary<string, CalendarDefinition> _byId = new(StringComparer.Ordinal);

        public int Count => _byId.Count;

        public IReadOnlyCollection<CalendarDefinition> All => _byId.Values;

        public void Clear() => _byId.Clear();

        public void Register(CalendarDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Calendar definition id must be a non-empty string.");
            }

            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Calendar '{definition.Id}' is already registered.");
            }
        }

        public bool TryGet(string id, out CalendarDefinition definition)
        {
            return _byId.TryGetValue(id, out definition!);
        }

        public CalendarDefinition Require(string id)
        {
            if (TryGet(id, out CalendarDefinition definition))
            {
                return definition;
            }

            throw new InvalidOperationException($"Calendar '{id}' is not registered.");
        }
    }
}
