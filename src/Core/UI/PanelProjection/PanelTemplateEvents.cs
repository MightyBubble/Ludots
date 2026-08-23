using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Zero-code UI event declaration (#1013): one event id, one trigger, one typed
    /// payload schema. Payloads validate strictly — unknown or mistyped fields fail.
    /// </summary>
    public sealed class PanelTemplateEvent
    {
        public PanelTemplateEvent(string eventId, string? control, string gesture, IReadOnlyDictionary<string, PanelEventPayloadKind>? payload = null)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                throw new ArgumentException("Event id is required.", nameof(eventId));
            }

            if (string.IsNullOrWhiteSpace(gesture))
            {
                throw new ArgumentException($"Event '{eventId}' requires a gesture.", nameof(gesture));
            }

            EventId = eventId.Trim();
            Control = string.IsNullOrWhiteSpace(control) ? null : control.Trim();
            Gesture = gesture.Trim();
            Payload = payload ?? new Dictionary<string, PanelEventPayloadKind>();
        }

        public string EventId { get; }
        public string? Control { get; }
        public string Gesture { get; }
        public IReadOnlyDictionary<string, PanelEventPayloadKind> Payload { get; }
    }

    public enum PanelEventPayloadKind : byte
    {
        String = 0,
        Int = 1,
        Float = 2,
        Bool = 3,
    }

    /// <summary>
    /// One intent-map entry (#1013): how a declared event becomes a gameplay intent.
    /// Args map $payload.<field> references; attribution resolves at runtime from
    /// playerSource/actorSource — the panel never constructs orders.
    /// </summary>
    public sealed class PanelIntentMapEntry
    {
        public PanelIntentMapEntry(
            string eventId,
            string intent,
            IReadOnlyDictionary<string, string> args,
            string playerSource,
            string actorSource)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                throw new ArgumentException("Intent entry event is required.", nameof(eventId));
            }

            if (string.IsNullOrWhiteSpace(intent))
            {
                throw new ArgumentException($"Intent entry for '{eventId}' requires an intent id.", nameof(intent));
            }

            EventId = eventId.Trim();
            Intent = intent.Trim();
            Args = args ?? new Dictionary<string, string>(StringComparer.Ordinal);
            PlayerSource = playerSource.Trim();
            ActorSource = actorSource.Trim();
        }

        public string EventId { get; }
        public string Intent { get; }
        public IReadOnlyDictionary<string, string> Args { get; }
        public string PlayerSource { get; }
        public string ActorSource { get; }
    }

    /// <summary>Resolved intent ready for admission (#1013 MVP).</summary>
    public sealed record PanelIntent(
        string Intent,
        IReadOnlyDictionary<string, object?> Args,
        int PlayerId,
        Arch.Core.Entity Actor);
}
