using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Stores <see cref="PerformerDefinition"/> instances keyed by string.
    /// Uses <see cref="StringIntRegistry"/> for the string-to-int mapping;
    /// int IDs are auto-assigned and opaque.
    /// </summary>
    public sealed class PerformerDefinitionRegistry
    {
        private readonly StringIntRegistry _ids;
        private PerformerDefinition[] _items;
        private bool[] _has;
        private readonly CompiledPerformerBootstrapRegistry _bootstrapRegistry = new();
        private bool _hasPerformerCreatedRules;

        public IReadOnlyList<int> RegisteredIds => _registeredIds;
        private readonly List<int> _registeredIds = new();

        public int Version { get; private set; }

        public CompiledPerformerBootstrapRegistry BootstrapRegistry => _bootstrapRegistry;
        public bool HasPerformerCreatedRules => _hasPerformerCreatedRules;

        public PerformerDefinitionRegistry(int capacity = 1024)
        {
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _items = new PerformerDefinition[capacity];
            _has = new bool[capacity];
        }

        /// <summary>
        /// Register a definition by string key. Returns the auto-assigned int ID.
        /// Overwrites if the key was already registered.
        /// </summary>
        public int Register(string key, PerformerDefinition definition)
        {
            int id = _ids.Register(key);
            EnsureCapacity(id);
            definition.Id = id;
            ValidateRuleCommands(key, definition);
            StampRuleOwners(definition, id);
            definition.BuildBindingIndex();
            definition.BuildRequiredAttributeIds();
            definition.BuildBehaviorMetadata();
            _items[id] = definition;
            if (!_has[id])
            {
                _has[id] = true;
                _registeredIds.Add(id);
            }
            Version++;
            _bootstrapRegistry.Rebuild(this);
            RebuildPerformerCreatedRuleFlag();
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        /// <summary>
        /// Register the key and return its id without storing a definition.
        /// Use when the definition needs to reference its own id (e.g. self-referential rules).
        /// Follow with <see cref="Register"/> to store the full definition.
        /// </summary>
        public int GetOrRegisterId(string key) => _ids.Register(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool Unregister(string key)
        {
            int id = _ids.GetId(key);
            bool removed = false;
            if (id > 0 && id < _items.Length && _has[id])
            {
                _items[id] = null!;
                _has[id] = false;
                _registeredIds.Remove(id);
                removed = true;
            }

            if (_ids.Unregister(key))
            {
                removed = true;
            }

            if (removed)
            {
                Version++;
                _bootstrapRegistry.Rebuild(this);
                RebuildPerformerCreatedRuleFlag();
            }

            return removed;
        }

        public bool TryGet(int id, out PerformerDefinition definition)
        {
            if (id >= 0 && id < _items.Length && _has[id])
            {
                definition = _items[id];
                return true;
            }
            definition = null!;
            return false;
        }

        public PerformerDefinition Get(int id)
        {
            if (!TryGet(id, out var def))
                throw new InvalidOperationException($"PerformerDefinition '{_ids.GetName(id)}' (id={id}) not registered.");
            return def;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _items.Length) return;
            int newLen = Math.Max(_items.Length * 2, id + 1);
            Array.Resize(ref _items, newLen);
            Array.Resize(ref _has, newLen);
        }

        private static void StampRuleOwners(PerformerDefinition definition, int id)
        {
            PerformerRule[] rules = definition.Rules;
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                rules[i].OwnerDefinitionId = id;
            }
        }

        private static void ValidateRuleCommands(string definitionKey, PerformerDefinition definition)
        {
            PerformerRule[] rules = definition.Rules;
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                ValidateCommand(definitionKey, i, in rules[i].Command);
            }
        }

        private static void ValidateCommand(string definitionKey, int ruleIndex, in PerformerCommand command)
        {
            if (!Enum.IsDefined(typeof(PerformerCommandKind), command.CommandKind))
            {
                throw new InvalidOperationException(
                    $"Performer definition '{definitionKey}' rule {ruleIndex} has unsupported command kind '{command.CommandKind}'.");
            }

            if (command.CommandKind == PerformerCommandKind.None)
            {
                throw new InvalidOperationException(
                    $"Performer definition '{definitionKey}' rule {ruleIndex} must declare a performer command kind.");
            }

            if (command.CommandKind == PerformerCommandKind.Extension)
            {
                if (command.CommandKindId < PerformerCommandKindRegistry.FirstModCommandKindId)
                {
                    throw new InvalidOperationException(
                        $"Performer definition '{definitionKey}' rule {ruleIndex} extension command must use a registered mod command kind id.");
                }

                if (command.RouteStrategy == PerformerCommandRouteStrategy.None)
                {
                    throw new InvalidOperationException(
                        $"Performer definition '{definitionKey}' rule {ruleIndex} extension command must declare route strategy.");
                }

                return;
            }

            int builtinKindId = (byte)command.CommandKind;
            if (command.CommandKindId != 0 && command.CommandKindId != builtinKindId)
            {
                throw new InvalidOperationException(
                    $"Performer definition '{definitionKey}' rule {ruleIndex} command kind id {command.CommandKindId} does not match builtin command kind '{command.CommandKind}'.");
            }
        }

        public void RebuildCompiledViews()
        {
            _bootstrapRegistry.Rebuild(this);
            RebuildPerformerCreatedRuleFlag();
        }

        private void RebuildPerformerCreatedRuleFlag()
        {
            _hasPerformerCreatedRules = false;
            for (int i = 0; i < _registeredIds.Count; i++)
            {
                if (!TryGet(_registeredIds[i], out PerformerDefinition definition))
                {
                    continue;
                }

                PerformerRule[] rules = definition.Rules;
                if (rules == null)
                {
                    continue;
                }

                for (int r = 0; r < rules.Length; r++)
                {
                    if (rules[r].Event.Kind == Events.PresentationEventKind.PerformerCreated)
                    {
                        _hasPerformerCreatedRules = true;
                        return;
                    }
                }
            }
        }
    }
}
