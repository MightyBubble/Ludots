using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Validates and dispatches declared panel events (#1013). Path B/C fan-out:
    /// the registered sink routes validated payloads (graph consumption via the event
    /// bus, signal bridge into orchestration blackboard — order admission downstream).
    /// The dispatcher never mutates gameplay itself.
    /// </summary>
    public sealed class PanelEventDispatcher
    {
        private readonly PanelTemplate _template;
        private readonly Action<string, IReadOnlyDictionary<string, object?>> _sink;

        public PanelEventDispatcher(PanelTemplate template, Action<string, IReadOnlyDictionary<string, object?>> sink)
        {
            _template = template ?? throw new ArgumentNullException(nameof(template));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public void Fire(string eventId, JsonObject args)
        {
            PanelTemplateEvent declaration = RequireEvent(eventId);
            IReadOnlyDictionary<string, object?> payload = ValidatePayload(declaration, args);
            _sink(declaration.EventId, payload);
        }

        private PanelTemplateEvent RequireEvent(string eventId)
        {
            foreach (PanelTemplateEvent declaration in _template.Events)
            {
                if (string.Equals(declaration.EventId, eventId, StringComparison.Ordinal))
                {
                    return declaration;
                }
            }

            throw new InvalidOperationException($"Panel '{_template.Id}' has no declared event '{eventId}'.");
        }

        private static IReadOnlyDictionary<string, object?> ValidatePayload(PanelTemplateEvent declaration, JsonObject args)
        {
            var payload = new Dictionary<string, object?>(declaration.Payload.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, PanelEventPayloadKind> field in declaration.Payload)
            {
                if (!args.TryGetPropertyValue(field.Key, out JsonNode? node) || node is null)
                {
                    throw new InvalidOperationException(
                        $"Panel event '{declaration.EventId}' payload is missing required field '{field.Key}'.");
                }

                payload[field.Key] = field.Value switch
                {
                    PanelEventPayloadKind.String => RequireString(node, declaration.EventId, field.Key),
                    PanelEventPayloadKind.Int => RequireInt(node, declaration.EventId, field.Key),
                    PanelEventPayloadKind.Float => RequireFloat(node, declaration.EventId, field.Key),
                    PanelEventPayloadKind.Bool => RequireBool(node, declaration.EventId, field.Key),
                    _ => throw new InvalidOperationException(
                        $"Panel event '{declaration.EventId}' field '{field.Key}' has unsupported kind '{field.Value}'."),
                };
            }

            foreach (KeyValuePair<string, JsonNode?> property in args)
            {
                if (!declaration.Payload.ContainsKey(property.Key))
                {
                    throw new InvalidOperationException(
                        $"Panel event '{declaration.EventId}' payload has undeclared field '{property.Key}'.");
                }
            }

            return payload;
        }

        private static string RequireString(JsonNode node, string eventId, string field) => node.GetValue<string>();
        private static int RequireInt(JsonNode node, string eventId, string field) => node.GetValue<int>();
        private static float RequireFloat(JsonNode node, string eventId, string field) => node.GetValue<float>();
        private static bool RequireBool(JsonNode node, string eventId, string field) => node.GetValue<bool>();
    }

    /// <summary>
    /// Path-B-to-orchestration signal bridge (#1014/#1013): writes an int payload
    /// field onto the context entity's blackboard so the visibility orchestration
    /// graph can consume it — the only UI-side gameplay write, and only of signals.
    /// </summary>
    public static class PanelSignalBridge
    {
        public static void WriteSignal(World world, Entity contextEntity, string key, int value)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (contextEntity == Entity.Null || !world.IsAlive(contextEntity))
            {
                throw new InvalidOperationException("Panel signal bridge requires a live context entity.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Signal key is required.", nameof(key));
            }

            if (!world.Has<BlackboardIntBuffer>(contextEntity))
            {
                world.Add(contextEntity, new BlackboardIntBuffer());
            }

            int keyId = Gameplay.GAS.Registry.ConfigKeyRegistry.Register(key);
            world.Get<BlackboardIntBuffer>(contextEntity).Set(keyId, value);
        }
    }

    /// <summary>
    /// Resolves intent-map entries against a validated payload plus seat/command-source
    /// attribution context (#1013). Produces PanelIntent records for admission —
    /// this layer never submits orders itself.
    /// </summary>
    public sealed class PanelIntentResolver
    {
        private readonly PanelTemplate _template;

        public PanelIntentResolver(PanelTemplate template)
        {
            _template = template ?? throw new ArgumentNullException(nameof(template));
        }

        public PanelIntent Resolve(string eventId, IReadOnlyDictionary<string, object?> payload, int playerId, Entity actor)
        {
            foreach (PanelIntentMapEntry entry in _template.Intents)
            {
                if (!string.Equals(entry.EventId, eventId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(entry.PlayerSource, "seat", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Panel '{_template.Id}' intent '{entry.Intent}' declares unsupported playerSource '{entry.PlayerSource}' (only 'seat').");
                }

                if (!string.Equals(entry.ActorSource, "commandSource.primary", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Panel '{_template.Id}' intent '{entry.Intent}' declares unsupported actorSource '{entry.ActorSource}' (only 'commandSource.primary').");
                }

                if (playerId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Panel '{_template.Id}' intent '{entry.Intent}' requires a seated player for attribution.");
                }

                if (actor == Entity.Null)
                {
                    throw new InvalidOperationException(
                        $"Panel '{_template.Id}' intent '{entry.Intent}' requires a command source actor for attribution.");
                }

                var args = new Dictionary<string, object?>(entry.Args.Count, StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> mapping in entry.Args)
                {
                    const string prefix = "$payload.";
                    if (!mapping.Value.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Panel '{_template.Id}' intent '{entry.Intent}' arg '{mapping.Key}' must reference $payload.* (got '{mapping.Value}').");
                    }

                    string field = mapping.Value[prefix.Length..];
                    if (!payload.TryGetValue(field, out object? value))
                    {
                        throw new InvalidOperationException(
                            $"Panel '{_template.Id}' intent '{entry.Intent}' arg '{mapping.Key}' references unknown payload field '{field}'.");
                    }

                    args[mapping.Key] = value;
                }

                return new PanelIntent(entry.Intent, args, playerId, actor);
            }

            throw new InvalidOperationException($"Panel '{_template.Id}' has no intent mapped for event '{eventId}'.");
        }
    }
}
