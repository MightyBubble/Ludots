using System;
using System.Collections.Generic;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Stores <see cref="PresenterDefinition"/> instances keyed by string.
    /// Uses <see cref="StringIntRegistry"/> for the string-to-int mapping;
    /// int IDs are auto-assigned and opaque.
    /// </summary>
    public sealed class PresenterDefinitionRegistry
    {
        private readonly StringIntRegistry _ids;
        private PresenterDefinition[] _items;
        private bool[] _has;
        private readonly CompiledPresenterBootstrapRegistry _bootstrapRegistry = new();
        private readonly Dictionary<int, PresenterCreatePlan> _createPlans = new();
        private int _definitionEpoch;
        private int _compiledCreatePlanEpoch = -1;
        private bool _hasPresenterCreatedRules;

        public IReadOnlyList<int> RegisteredIds => _registeredIds;
        private readonly List<int> _registeredIds = new();

        public int Version { get; private set; }

        public CompiledPresenterBootstrapRegistry BootstrapRegistry => _bootstrapRegistry;
        public bool HasPresenterCreatedRules => _hasPresenterCreatedRules;

        public PresenterDefinitionRegistry(int capacity = 1024)
        {
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _items = new PresenterDefinition[capacity];
            _has = new bool[capacity];
        }

        /// <summary>
        /// Register a definition by string key. Returns the auto-assigned int ID.
        /// Overwrites if the key was already registered.
        /// </summary>
        public int Register(string key, PresenterDefinition definition)
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
            _definitionEpoch++;
            _bootstrapRegistry.Rebuild(this);
            RebuildPresenterCreatedRuleFlag();
            return id;
        }

        /// <summary>
        /// Compiled CreatePresenter command plan for a root definition. Plans flatten the declared
        /// child subtree (definition children plus instance-children materialization) and are
        /// cached until any registration change invalidates them.
        /// </summary>
        public PresenterCreatePlan GetOrCreateCreatePlan(int definitionId)
        {
            if (_compiledCreatePlanEpoch != _definitionEpoch)
            {
                _createPlans.Clear();
                _compiledCreatePlanEpoch = _definitionEpoch;
            }

            if (!_createPlans.TryGetValue(definitionId, out PresenterCreatePlan? plan))
            {
                PresenterDefinition definition = Get(definitionId);
                plan = PresenterCreatePlanCompiler.Compile(this, definition);
                _createPlans[definitionId] = plan;
            }

            return plan;
        }

        public void CompileAllCreatePlans()
        {
            for (int i = 0; i < _registeredIds.Count; i++)
            {
                _ = GetOrCreateCreatePlan(_registeredIds[i]);
            }
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
                _definitionEpoch++;
                _bootstrapRegistry.Rebuild(this);
                RebuildPresenterCreatedRuleFlag();
            }

            return removed;
        }

        public bool TryGet(int id, out PresenterDefinition definition)
        {
            if (id >= 0 && id < _items.Length && _has[id])
            {
                definition = _items[id];
                return true;
            }
            definition = null!;
            return false;
        }

        public PresenterDefinition Get(int id)
        {
            if (!TryGet(id, out var def))
                throw new InvalidOperationException($"PresenterDefinition '{_ids.GetName(id)}' (id={id}) not registered.");
            return def;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _items.Length) return;
            int newLen = Math.Max(_items.Length * 2, id + 1);
            Array.Resize(ref _items, newLen);
            Array.Resize(ref _has, newLen);
        }

        private static void StampRuleOwners(PresenterDefinition definition, int id)
        {
            PresenterRule[] rules = definition.Rules;
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                rules[i].OwnerDefinitionId = id;
            }
        }

        private static void ValidateRuleCommands(string definitionKey, PresenterDefinition definition)
        {
            PresenterRule[] rules = definition.Rules;
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                ValidateCommand(definitionKey, i, in rules[i].Command);
            }
        }

        private static void ValidateCommand(string definitionKey, int ruleIndex, in PresenterCommand command)
        {
            if (!Enum.IsDefined(typeof(PresenterCommandKind), command.CommandKind))
            {
                throw new InvalidOperationException(
                    $"Presenter definition '{definitionKey}' rule {ruleIndex} has unsupported command kind '{command.CommandKind}'.");
            }

            if (command.CommandKind == PresenterCommandKind.None)
            {
                throw new InvalidOperationException(
                    $"Presenter definition '{definitionKey}' rule {ruleIndex} must declare a presenter command kind.");
            }

            if (command.CommandKind == PresenterCommandKind.Extension)
            {
                if (command.CommandKindId < PresenterCommandKindRegistry.FirstModCommandKindId)
                {
                    throw new InvalidOperationException(
                        $"Presenter definition '{definitionKey}' rule {ruleIndex} extension command must use a registered mod command kind id.");
                }

                if (command.RouteStrategy == PresenterCommandRouteStrategy.None)
                {
                    throw new InvalidOperationException(
                        $"Presenter definition '{definitionKey}' rule {ruleIndex} extension command must declare route strategy.");
                }

                return;
            }

            int builtinKindId = (byte)command.CommandKind;
            if (command.CommandKindId != 0 && command.CommandKindId != builtinKindId)
            {
                throw new InvalidOperationException(
                    $"Presenter definition '{definitionKey}' rule {ruleIndex} command kind id {command.CommandKindId} does not match builtin command kind '{command.CommandKind}'.");
            }
        }

        public void RebuildCompiledViews()
        {
            _bootstrapRegistry.Rebuild(this);
            RebuildPresenterCreatedRuleFlag();
        }

        private void RebuildPresenterCreatedRuleFlag()
        {
            _hasPresenterCreatedRules = false;
            for (int i = 0; i < _registeredIds.Count; i++)
            {
                if (!TryGet(_registeredIds[i], out PresenterDefinition definition))
                {
                    continue;
                }

                PresenterRule[] rules = definition.Rules;
                if (rules == null)
                {
                    continue;
                }

                for (int r = 0; r < rules.Length; r++)
                {
                    if (rules[r].Event.Kind == PresentationEventKind.PresenterCreated)
                    {
                        _hasPresenterCreatedRules = true;
                        return;
                    }
                }
            }
        }
    }
}
