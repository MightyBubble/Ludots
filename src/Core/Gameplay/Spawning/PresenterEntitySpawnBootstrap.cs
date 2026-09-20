using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.Gameplay.Spawning
{
    public sealed class PresenterEntitySpawnBootstrap
    {
        private readonly World _world;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly PresenterEntityRuntime? _presenterRuntime;
        private readonly PresenterDefinitionRegistry? _presenterDefinitions;
        private readonly CompiledPresenterBootstrapRegistry? _presenterBootstrap;
        private readonly PresentationStableIdAllocator _stableIds;

        public PresenterEntitySpawnBootstrap(
            World world,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            PresenterEntityRuntime? presenterRuntime = null,
            PresenterDefinitionRegistry? presenterDefinitions = null,
            CompiledPresenterBootstrapRegistry? presenterBootstrap = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _presenterRuntime = presenterRuntime;
            _presenterDefinitions = presenterDefinitions;
            _presenterBootstrap = presenterBootstrap;
        }

        public void TryBootstrap(Entity owner, string templateId)
        {
            if (_presenterRuntime == null || _presenterDefinitions == null || _presenterBootstrap == null)
            {
                return;
            }

            int templateKeyId = ResolveTemplateKeyId(templateId, owner);
            if (templateKeyId <= 0 ||
                !_presenterBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules))
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

                if (!_presenterDefinitions.TryGet(rule.PresenterDefinitionId, out PresenterDefinition definition))
                {
                    throw new InvalidOperationException($"Presenter definition id={rule.PresenterDefinitionId} is not registered.");
                }

                int scopeTag = rule.ResolveScopeTag(stableId);
                if (scopeTag <= 0)
                {
                    continue;
                }

                if (_presenterRuntime.HasActiveScopedInstance(rule.PresenterDefinitionId, owner, scopeTag, PresentationAnchorKind.Entity, default))
                {
                    continue;
                }

                Entity root = _presenterRuntime.CreateHierarchy(
                    _presenterDefinitions,
                    rule.PresenterDefinitionId,
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

        private bool PassesBootstrapCondition(CompiledPresenterBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => _world.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => _world.Has<AttributeBuffer>(owner),
                _ => throw new InvalidOperationException($"Unsupported presenter bootstrap inline condition '{rule.InlineCondition}'."),
            };
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!_world.IsAlive(root) || !_world.Has<PresenterState>(root))
            {
                return;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(root);
            if (_presenterDefinitions != null &&
                _presenterDefinitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkPresenter(root);
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (_world.IsAlive(child))
                {
                    MarkHierarchyForBootstrapIfNeeded(child);
                }
            }
        }

        private void MarkPresenter(Entity presenter)
        {
            if (_world.Has<PresenterBootstrapPending>(presenter))
            {
                return;
            }

            _world.Add(presenter, new PresenterBootstrapPending());
        }

        private void MarkOwnerBootstrapHandled(Entity owner)
        {
            if (_world.Has<PresenterRootBootstrapHandled>(owner))
            {
                return;
            }

            _world.Add(owner, new PresenterRootBootstrapHandled());
        }
    }
}
