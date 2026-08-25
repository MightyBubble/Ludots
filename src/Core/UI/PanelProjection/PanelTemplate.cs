using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Author-facing panel contract: pins may read the graph output store or an
    /// immutable configuration record. Rendering backends only consume the evaluated
    /// variable set; they do not know which source produced a value.
    /// </summary>
    public sealed class PanelTemplate
    {
        public PanelTemplate(
            string id,
            string? graph,
            IReadOnlyList<PanelPin> pins,
            IReadOnlyList<PanelTemplateEvent>? events = null,
            IReadOnlyList<PanelIntentMapEntry>? intents = null,
            string? skin = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Panel template id is required.", nameof(id));
            }

            if (pins == null || pins.Count == 0)
            {
                throw new ArgumentException($"Panel template '{id}' must declare at least one pin.", nameof(pins));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelPin pin in pins)
            {
                if (pin == null)
                {
                    throw new ArgumentException($"Panel template '{id}' has a null pin entry.", nameof(pins));
                }

                if (!seen.Add(pin.Name))
                {
                    throw new ArgumentException($"Panel template '{id}' declares duplicate pin '{pin.Name}'.", nameof(pins));
                }
            }

            if (string.IsNullOrWhiteSpace(graph))
            {
                foreach (PanelPin pin in pins)
                {
                    if (pin.SourceKind == PanelPinSourceKind.Graph)
                    {
                        throw new ArgumentException($"Panel template '{id}' requires a graph id for graph pin '{pin.Name}'.", nameof(graph));
                    }
                }
            }

            List<PanelTemplateEvent> safeEvents = new List<PanelTemplateEvent>(events ?? Array.Empty<PanelTemplateEvent>());
            foreach (PanelTemplateEvent declaration in safeEvents)
            {
                if (declaration == null)
                {
                    throw new ArgumentException($"Panel template '{id}' has a null event entry.", nameof(events));
                }
            }

            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelTemplateEvent declaration in safeEvents)
            {
                if (!eventIds.Add(declaration.EventId))
                {
                    throw new ArgumentException($"Panel template '{id}' declares duplicate event '{declaration.EventId}'.", nameof(events));
                }
            }

            List<PanelIntentMapEntry> safeIntents = new List<PanelIntentMapEntry>(intents ?? Array.Empty<PanelIntentMapEntry>());
            foreach (PanelIntentMapEntry entry in safeIntents)
            {
                if (entry == null)
                {
                    throw new ArgumentException($"Panel template '{id}' has a null intent entry.", nameof(intents));
                }

                if (!eventIds.Contains(entry.EventId))
                {
                    throw new ArgumentException(
                        $"Panel template '{id}' intent '{entry.Intent}' references undeclared event '{entry.EventId}'.",
                        nameof(intents));
                }
            }

            Id = id.Trim();
            Graph = string.IsNullOrWhiteSpace(graph) ? null : graph.Trim();
            Pins = pins;
            Events = safeEvents;
            Intents = safeIntents;
            Skin = string.IsNullOrWhiteSpace(skin) ? null : skin.Trim();
        }

        public string Id { get; }

        /// <summary>Optional graph used by graph pins; data-only panels leave this null.</summary>
        public string? Graph { get; }
        public IReadOnlyList<PanelPin> Pins { get; }
        public IReadOnlyList<PanelTemplateEvent> Events { get; }
        public IReadOnlyList<PanelIntentMapEntry> Intents { get; }

        /// <summary>Per-template default skin; instance op param wins, then game.json default.</summary>
        public string? Skin { get; }

        /// <summary>Graph program id, resolved once at load; -1 until the loader binds it.</summary>
        public int GraphId { get; internal set; } = -1;

        public PanelPin? FindPin(string pinName)
        {
            foreach (PanelPin pin in Pins)
            {
                if (string.Equals(pin.Name, pinName, StringComparison.Ordinal))
                {
                    return pin;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// One output pin: name on the panel side, source contract, pull mode, and an
    /// explicit numeric default for graph pins whose output is not materialized yet.
    /// </summary>
    public enum PanelPinSourceKind : byte
    {
        Graph = 1,
        Data = 2,
    }

    public sealed class PanelPin
    {
        public PanelPin(string name, string key, bool realtime, float defaultValue)
            : this(name, PanelPinSourceKind.Graph, key, null, null, realtime, defaultValue)
        {
        }

        public PanelPin(
            string name,
            PanelPinSourceKind sourceKind,
            string source,
            string? recordId,
            string? path,
            bool realtime,
            float defaultValue)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Pin name is required.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException($"Pin '{name}' requires a source key or record id.", nameof(source));
            }

            if (sourceKind == PanelPinSourceKind.Data && string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"Data pin '{name}' requires a data path.", nameof(path));
            }

            Name = name.Trim();
            SourceKind = sourceKind;
            Key = source.Trim();
            RecordId = string.IsNullOrWhiteSpace(recordId) ? null : recordId.Trim();
            Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Realtime = realtime;
            Default = defaultValue;
        }

        public string Name { get; }
        public string Key { get; }
        public PanelPinSourceKind SourceKind { get; }
        public string? RecordId { get; }
        public string? Path { get; }

        /// <summary>True = re-evaluated every realtime refresh pass; False = evaluated once at instantiate (snapshot).</summary>
        public bool Realtime { get; }

        /// <summary>Data contract: graph missing/not yet run/failed → this value. No error, no empty.</summary>
        public float Default { get; }
    }
}
