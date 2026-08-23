using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Mod-declared custom event vocabulary (<c>Events/custom_events.json</c>, ArrayById by
    /// "id"). Graph entries may name any engine-known event or any declared custom event;
    /// anything else fails closed at mount time with the full vocabulary listed. Mods fire
    /// their declared events through <see cref="TriggerManager.FireMapCustomEvent"/>.
    /// </summary>
    public sealed class CustomEventNameRegistry
    {
        public const string ConfigPath = "Events/custom_events.json";
        public const string GasEventPrefix = "Gas.Event.";

        private readonly HashSet<string> _custom = new(StringComparer.Ordinal);
        private static readonly HashSet<string> EngineKnown = BuildEngineKnownSet();

        public IReadOnlyCollection<string> CustomNames => _custom;

        public void Register(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Custom event id must be a non-empty string.");
            }

            if (!TryValidateNameShape(name, out string shapeError))
            {
                throw new InvalidOperationException($"Custom event '{name}' is invalid: {shapeError}");
            }

            if (!_custom.Add(name.Trim()))
            {
                throw new InvalidOperationException($"Duplicate custom event declaration '{name}'.");
            }
        }

        public bool IsDeclaredCustom(string name)
        {
            return _custom.Contains(name);
        }

        /// <summary>Engine events ∪ declared custom events ∪ GAS tag bridge pattern.</summary>
        public bool IsKnownEntryEvent(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return EngineKnown.Contains(name) ||
                _custom.Contains(name) ||
                name.StartsWith(GasEventPrefix, StringComparison.Ordinal);
        }

        public string DescribeVocabulary()
        {
            var engine = string.Join(", ", EngineKnown.OrderBy(n => n, StringComparer.Ordinal));
            var custom = _custom.Count == 0
                ? "(none declared)"
                : string.Join(", ", _custom.OrderBy(n => n, StringComparer.Ordinal));
            return $"engine: {engine}; custom: {custom}; dynamic: {GasEventPrefix}*";
        }

        private static bool TryValidateNameShape(string name, out string error)
        {
            error = string.Empty;
            string trimmed = name.Trim();
            if (trimmed.Length < 3)
            {
                error = "ids need at least 3 characters.";
                return false;
            }

            foreach (char c in trimmed)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                {
                    error = $"character '{c}' is not allowed (letters, digits, '.', '_', '-').";
                    return false;
                }
            }

            return true;
        }

        private static HashSet<string> BuildEngineKnownSet()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in typeof(GameEvents).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is EventKey key && !string.IsNullOrEmpty(key.Value))
                {
                    names.Add(key.Value);
                }
            }

            return names;
        }
    }

    /// <summary>Config-pipeline loader for <c>Events/custom_events.json</c> (ArrayById, id field).</summary>
    public sealed class CustomEventCatalogLoader
    {
        private readonly ConfigPipeline _configs;

        public CustomEventCatalogLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public CustomEventNameRegistry Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var registry = new CustomEventNameRegistry();
            if (catalog == null || !catalog.TryGet(CustomEventNameRegistry.ConfigPath, out var entry))
            {
                // No mod declares custom events: the vocabulary is simply empty and
                // entry-name validation still covers engine events.
                return registry;
            }
            IReadOnlyList<MergedConfigEntry> merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject node ||
                    node["id"]?.GetValue<string>() is not { } name)
                {
                    throw new InvalidOperationException(
                        $"{CustomEventNameRegistry.ConfigPath} entry #{i} must be an object with a non-empty 'id'.");
                }

                registry.Register(name);
            }

            return registry;
        }
    }
}
