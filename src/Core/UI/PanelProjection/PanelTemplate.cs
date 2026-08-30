using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Author-facing panel contract (graph-pinned panels): a panel is the
    /// output-pin set of ONE graph (ShaderGraph analogy). The template declares pins
    /// plus the graph id; all dataflow — attribute loads, table lookups, aggregation,
    /// nested func graphs — lives inside the graph VM. Pins carry the data contract:
    /// structure errors fail closed at load; missing data resolves to the pin's
    /// declared default (no error, no empty).
    /// </summary>
    public sealed class PanelTemplate
    {
        public PanelTemplate(
            string id,
            string graph,
            IReadOnlyList<PanelPin> pins,
            IReadOnlyList<PanelTemplateEvent>? events = null,
            IReadOnlyList<PanelIntentMapEntry>? intents = null,
            string? skin = null,
            IReadOnlyList<PanelCollectionBinding>? collections = null,
            PanelLayout? layout = null,
            PanelSubjectKind subject = PanelSubjectKind.None,
            PanelOwnerKind ownerKind = PanelOwnerKind.Seat,
            PanelAudience? audience = null,
            IReadOnlyList<PanelInputBinding>? inputs = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Panel template id is required.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(graph))
            {
                throw new ArgumentException($"Panel template '{id}' requires a graph id.", nameof(graph));
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

            List<PanelInputBinding> safeInputs =
                new List<PanelInputBinding>(inputs ?? Array.Empty<PanelInputBinding>());
            var inputNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelInputBinding input in safeInputs)
            {
                if (input == null)
                {
                    throw new ArgumentException($"Panel template '{id}' has a null input entry.", nameof(inputs));
                }

                if (!inputNames.Add(input.Name))
                {
                    throw new ArgumentException(
                        $"Panel template '{id}' declares duplicate input '{input.Name}'.", nameof(inputs));
                }

                if (seen.Contains(input.Name))
                {
                    throw new ArgumentException(
                        $"Panel template '{id}' input '{input.Name}' collides with a pin name.", nameof(inputs));
                }
            }

            List<PanelCollectionBinding> safeCollections =
                new List<PanelCollectionBinding>(collections ?? Array.Empty<PanelCollectionBinding>());

            var collectionNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelCollectionBinding collection in safeCollections)
            {
                if (collection == null)
                {
                    throw new ArgumentException($"Panel template '{id}' has a null collection entry.", nameof(collections));
                }

                if (!collectionNames.Add(collection.Name))
                {
                    throw new ArgumentException(
                        $"Panel template '{id}' declares duplicate collection '{collection.Name}'.", nameof(collections));
                }

                if (seen.Contains(collection.Name) || inputNames.Contains(collection.Name))
                {
                    throw new ArgumentException(
                        $"Panel template '{id}' collection '{collection.Name}' collides with a pin or input name.",
                        nameof(collections));
                }

                if (collection.Source == PanelCollectionSourceKind.Input)
                {
                    if (string.IsNullOrWhiteSpace(collection.InputName) ||
                        !inputNames.Contains(collection.InputName))
                    {
                        throw new ArgumentException(
                            $"Panel template '{id}' collection '{collection.Name}' source=input requires a declared inputs[].name.",
                            nameof(collections));
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
            Graph = graph.Trim();
            Pins = pins;
            Events = safeEvents;
            Intents = safeIntents;
            Skin = string.IsNullOrWhiteSpace(skin) ? null : skin.Trim();
            Inputs = safeInputs;
            Collections = safeCollections;
            Layout = layout;
            Subject = subject;
            OwnerKind = ownerKind;
            Audience = audience ?? PanelAudience.AllSeats;
        }

        public string Id { get; }

        /// <summary>The single data source: one graph whose output schema feeds every pin.</summary>
        public string Graph { get; }
        public IReadOnlyList<PanelPin> Pins { get; }
        public IReadOnlyList<PanelTemplateEvent> Events { get; }
        public IReadOnlyList<PanelIntentMapEntry> Intents { get; }

        /// <summary>
        /// Element subject kind. <see cref="PanelSubjectKind.None"/> = host panel;
        /// non-None = embeddable element that resolves that payload type.
        /// Compound elements may also declare nested collections (explicit source).
        /// </summary>
        public PanelSubjectKind Subject { get; }

        /// <summary>Explicit parent pin inputs (query-graph-collection-outputs §2.4).</summary>
        public IReadOnlyList<PanelInputBinding> Inputs { get; }

        /// <summary>Collection slots: graph collection + reusable element template id.</summary>
        public IReadOnlyList<PanelCollectionBinding> Collections { get; }

        /// <summary>Optional builtin control tree; null keeps legacy auto-row layout.</summary>
        public PanelLayout? Layout { get; }

        /// <summary>Per-template default skin; instance op param wins, then game.json default.</summary>
        public string? Skin { get; }

        public PanelOwnerKind OwnerKind { get; }

        public PanelAudience Audience { get; }

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
    /// One output pin: name on the panel side, key into the graph's output schema,
    /// pull mode, and the default shown whenever the graph has not (yet) produced a
    /// value for the owning scope.
    /// </summary>
    public sealed class PanelPin
    {
        public PanelPin(string name, string key, bool realtime, float defaultValue)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Pin name is required.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException($"Pin '{name}' requires an output key.", nameof(key));
            }

            Name = name.Trim();
            Key = key.Trim();
            Realtime = realtime;
            Default = defaultValue;
        }

        public string Name { get; }
        public string Key { get; }

        /// <summary>True = re-evaluated every realtime refresh pass; False = evaluated once at instantiate (snapshot).</summary>
        public bool Realtime { get; }

        /// <summary>Data contract: graph missing/not yet run/failed → this value. No error, no empty.</summary>
        public float Default { get; }
    }
}
