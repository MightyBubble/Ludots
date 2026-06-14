using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Performers
{
    public sealed class CompiledPerformerBootstrapRegistry
    {
        private readonly Dictionary<int, BootstrapCreateRule[]> _entitySpawnCreates = new();
        private readonly Dictionary<int, BootstrapDestroyRule[]> _entityDestroyedDestroys = new();
        private readonly HashSet<int> _entitySpawnNonBootstrapRules = new();

        public void Rebuild(PerformerDefinitionRegistry definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            _entitySpawnCreates.Clear();
            _entityDestroyedDestroys.Clear();
            _entitySpawnNonBootstrapRules.Clear();

            IReadOnlyList<int> registeredIds = definitions.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                if (!definitions.TryGet(registeredIds[i], out PerformerDefinition definition))
                {
                    continue;
                }

                PerformerRule[] rules = definition.Rules;
                if (rules == null || rules.Length == 0)
                {
                    continue;
                }

                for (int ri = 0; ri < rules.Length; ri++)
                {
                    ref readonly PerformerRule rule = ref rules[ri];
                    if (rule.Event.KeyId <= 0)
                    {
                        continue;
                    }

                    switch (rule.Event.Kind)
                    {
                        case PresentationEventKind.EntitySpawned:
                            if (TryCompileCreate(in rule, out BootstrapCreateRule createRule))
                            {
                                AppendCreate(rule.Event.KeyId, in createRule);
                            }
                            else
                            {
                                _entitySpawnNonBootstrapRules.Add(rule.Event.KeyId);
                            }
                            break;

                        case PresentationEventKind.EntityDestroyed:
                            if (TryCompileDestroy(in rule, out BootstrapDestroyRule destroyRule))
                            {
                                AppendDestroy(rule.Event.KeyId, in destroyRule);
                            }
                            break;
                    }
                }
            }
        }

        public bool TryGetEntitySpawnCreates(int templateKeyId, out BootstrapCreateRule[] rules)
        {
            return _entitySpawnCreates.TryGetValue(templateKeyId, out rules!);
        }

        public bool TryGetEntityDestroyedDestroys(int templateKeyId, out BootstrapDestroyRule[] rules)
        {
            return _entityDestroyedDestroys.TryGetValue(templateKeyId, out rules!);
        }

        public bool HasNonBootstrapEntitySpawnRules(int templateKeyId)
        {
            return _entitySpawnNonBootstrapRules.Contains(templateKeyId);
        }

        public bool IsRootBootstrapRule(in PerformerRule rule)
        {
            if (rule.Event.KeyId <= 0)
            {
                return false;
            }

            return rule.Event.Kind switch
            {
                PresentationEventKind.EntitySpawned => TryCompileCreate(in rule, out _),
                PresentationEventKind.EntityDestroyed => TryCompileDestroy(in rule, out _),
                _ => false,
            };
        }

        public readonly struct BootstrapCreateRule
        {
            public BootstrapCreateRule(int performerDefinitionId, int fixedScopeTag, PerformerCommandScopeSource scopeSource, InlineConditionKind inlineCondition)
            {
                PerformerDefinitionId = performerDefinitionId;
                FixedScopeTag = fixedScopeTag;
                ScopeSource = scopeSource;
                InlineCondition = inlineCondition;
            }

            public int PerformerDefinitionId { get; }

            public int FixedScopeTag { get; }

            public PerformerCommandScopeSource ScopeSource { get; }

            public InlineConditionKind InlineCondition { get; }

            public int ResolveScopeTag(int stableId)
            {
                return ScopeSource switch
                {
                    PerformerCommandScopeSource.EventPayloadA => stableId,
                    PerformerCommandScopeSource.Fixed => FixedScopeTag,
                    _ => FixedScopeTag,
                };
            }
        }

        public readonly struct BootstrapDestroyRule
        {
            public BootstrapDestroyRule(int fixedScopeTag, PerformerCommandScopeSource scopeSource)
            {
                FixedScopeTag = fixedScopeTag;
                ScopeSource = scopeSource;
            }

            public int FixedScopeTag { get; }

            public PerformerCommandScopeSource ScopeSource { get; }

            public int ResolveScopeTag(int stableId)
            {
                return ScopeSource switch
                {
                    PerformerCommandScopeSource.EventPayloadA => stableId,
                    PerformerCommandScopeSource.Fixed => FixedScopeTag,
                    _ => FixedScopeTag,
                };
            }
        }

        private static bool TryCompileCreate(in PerformerRule rule, out BootstrapCreateRule compiled)
        {
            compiled = default;
            if (rule.Command.CommandKind != PerformerCommandKind.CreatePerformer ||
                rule.Command.PerformerDefinitionId <= 0)
            {
                return false;
            }

            if (rule.Condition.GraphProgramId > 0)
            {
                return false;
            }

            InlineConditionKind inlineCondition = rule.Condition.Inline;
            if (inlineCondition != InlineConditionKind.None &&
                inlineCondition != InlineConditionKind.SourceHasVisualTransform &&
                inlineCondition != InlineConditionKind.SourceHasAttributes)
            {
                return false;
            }

            if (rule.Command.ScopeSource != PerformerCommandScopeSource.Fixed &&
                rule.Command.ScopeSource != PerformerCommandScopeSource.EventPayloadA)
            {
                return false;
            }

            compiled = new BootstrapCreateRule(
                rule.Command.PerformerDefinitionId,
                rule.Command.ScopeTag,
                rule.Command.ScopeSource,
                inlineCondition);
            return true;
        }

        private static bool TryCompileDestroy(in PerformerRule rule, out BootstrapDestroyRule compiled)
        {
            compiled = default;
            if (rule.Command.CommandKind != PerformerCommandKind.DestroyPerformerScope)
            {
                return false;
            }

            if (rule.Condition.GraphProgramId > 0 || rule.Condition.Inline != InlineConditionKind.None)
            {
                return false;
            }

            if (rule.Command.ScopeSource != PerformerCommandScopeSource.Fixed &&
                rule.Command.ScopeSource != PerformerCommandScopeSource.EventPayloadA)
            {
                return false;
            }

            compiled = new BootstrapDestroyRule(rule.Command.ScopeTag, rule.Command.ScopeSource);
            return true;
        }

        private void AppendCreate(int templateKeyId, in BootstrapCreateRule rule)
        {
            if (!_entitySpawnCreates.TryGetValue(templateKeyId, out BootstrapCreateRule[] existing))
            {
                _entitySpawnCreates.Add(templateKeyId, new[] { rule });
                return;
            }

            var expanded = new BootstrapCreateRule[existing.Length + 1];
            Array.Copy(existing, expanded, existing.Length);
            expanded[existing.Length] = rule;
            _entitySpawnCreates[templateKeyId] = expanded;
        }

        private void AppendDestroy(int templateKeyId, in BootstrapDestroyRule rule)
        {
            if (!_entityDestroyedDestroys.TryGetValue(templateKeyId, out BootstrapDestroyRule[] existing))
            {
                _entityDestroyedDestroys.Add(templateKeyId, new[] { rule });
                return;
            }

            var expanded = new BootstrapDestroyRule[existing.Length + 1];
            Array.Copy(existing, expanded, existing.Length);
            expanded[existing.Length] = rule;
            _entityDestroyedDestroys[templateKeyId] = expanded;
        }
    }
}
