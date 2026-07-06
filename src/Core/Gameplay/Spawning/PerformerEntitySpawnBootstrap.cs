using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Gameplay.Spawning
{
    public sealed class PerformerEntitySpawnBootstrap
    {
        private readonly World _world;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly PerformerEntityRuntime? _performerRuntime;
        private readonly PerformerDefinitionRegistry? _performerDefinitions;
        private readonly CompiledPerformerBootstrapRegistry? _performerBootstrap;
        private readonly PresentationStableIdAllocator _stableIds;

        public PerformerEntitySpawnBootstrap(
            World world,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            PerformerEntityRuntime? performerRuntime = null,
            PerformerDefinitionRegistry? performerDefinitions = null,
            CompiledPerformerBootstrapRegistry? performerBootstrap = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _performerRuntime = performerRuntime;
            _performerDefinitions = performerDefinitions;
            _performerBootstrap = performerBootstrap;
        }

        public void TryBootstrap(Entity owner, string templateId)
        {
            if (_performerRuntime == null || _performerDefinitions == null || _performerBootstrap == null)
            {
                return;
            }

            int templateKeyId = ResolveTemplateKeyId(templateId, owner);
            if (templateKeyId <= 0 ||
                !_performerBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return;
            }

            int stableId = _world.Has<PresentationStableId>(owner)
                ? _world.Get<PresentationStableId>(owner).Value
                : 0;
            for (int i = 0; i < rules.Length; i++)
            {
                ref readonly var rule = ref rules[i];
                if (!PassesBootstrapCondition(rule, owner))
                {
                    continue;
                }

                if (!_performerDefinitions.TryGet(rule.PerformerDefinitionId, out PerformerDefinition definition))
                {
                    throw new InvalidOperationException($"Performer definition id={rule.PerformerDefinitionId} is not registered.");
                }

                int scopeTag = rule.ResolveScopeTag(stableId);
                if (scopeTag <= 0)
                {
                    continue;
                }

                if (_performerRuntime.HasActiveScopedInstance(rule.PerformerDefinitionId, owner, scopeTag, PresentationAnchorKind.Entity, default))
                {
                    continue;
                }

                Entity root = _performerRuntime.CreateHierarchy(
                    _performerDefinitions,
                    rule.PerformerDefinitionId,
                    owner,
                    scopeTag,
                    PresentationAnchorKind.Entity,
                    default,
                    _stableIds.Allocate(),
                    Entity.Null,
                    definition,
                    _stableIds.Allocate);
                MarkHierarchyForBootstrapIfNeeded(root);
                MarkOwnerBootstrapHandled(owner);
            }
        }

        private int ResolveTemplateKeyId(string templateId, Entity owner)
        {
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                int templateKeyId = _templateKeys.GetId(templateId);
                if (templateKeyId > 0)
                {
                    return templateKeyId;
                }
            }

            return _world.Has<EntityTemplateKeyRef>(owner)
                ? _world.Get<EntityTemplateKeyRef>(owner).TemplateKeyId
                : 0;
        }

        private bool PassesBootstrapCondition(CompiledPerformerBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => _world.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => _world.Has<AttributeBuffer>(owner),
                _ => throw new InvalidOperationException($"Unsupported performer bootstrap inline condition '{rule.InlineCondition}'."),
            };
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!_world.IsAlive(root) || !_world.Has<PerformerState>(root))
            {
                return;
            }

            ref readonly PerformerState state = ref _world.Get<PerformerState>(root);
            if (_performerDefinitions != null &&
                _performerDefinitions.TryGet(state.DefId, out PerformerDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkPerformer(root);
            }

            ref PerformerChildren children = ref _world.Get<PerformerChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (_world.IsAlive(child))
                {
                    MarkHierarchyForBootstrapIfNeeded(child);
                }
            }
        }

        private void MarkPerformer(Entity performer)
        {
            if (_world.Has<PerformerBootstrapPending>(performer))
            {
                return;
            }

            _world.Add(performer, new PerformerBootstrapPending());
        }

        private void MarkOwnerBootstrapHandled(Entity owner)
        {
            if (_world.Has<PerformerRootBootstrapHandled>(owner))
            {
                return;
            }

            _world.Add(owner, new PerformerRootBootstrapHandled());
        }
    }
}
