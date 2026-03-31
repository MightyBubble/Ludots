using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Performers;

namespace CommonControlBuffsPresentationMod.Runtime
{
    internal sealed class CommonControlStatusPresentationSystem : ISystem<float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<GameplayTagContainer>();

        private readonly World _world;
        private readonly PresentationCommandBuffer _commands;
        private readonly PerformerDefinitionRegistry _performers;
        private readonly TagOps _tagOps;
        private readonly HashSet<StatusKey> _active = new();
        private readonly HashSet<StatusKey> _nextActive = new();
        private readonly Dictionary<StatusKey, int> _scopes = new();
        private readonly StatusDescriptor[] _descriptors =
        {
            new("Status.Slowed", "control.common.status.slowed"),
            new("Status.Silenced", "control.common.status.silenced"),
            new("Status.Rooted", "control.common.status.rooted"),
            new("Status.Stunned", "control.common.status.stunned"),
        };

        private int _nextScopeId = 6100;
        private bool _resolved;

        public CommonControlStatusPresentationSystem(
            World world,
            PresentationCommandBuffer commands,
            PerformerDefinitionRegistry performers,
            TagOps tagOps)
        {
            _world = world;
            _commands = commands;
            _performers = performers;
            _tagOps = tagOps;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            EnsureResolved();
            _nextActive.Clear();

            _world.Query(in Query, (Entity entity, ref GameplayTagContainer tags) =>
            {
                for (int i = 0; i < _descriptors.Length; i++)
                {
                    ref readonly StatusDescriptor descriptor = ref _descriptors[i];
                    if (!_tagOps.HasTag(ref tags, descriptor.TagId, TagSense.Effective))
                    {
                        continue;
                    }

                    var key = new StatusKey(entity, i);
                    _nextActive.Add(key);
                    if (_active.Contains(key))
                    {
                        continue;
                    }

                    int scopeId = EnsureScopeId(key);
                    _commands.TryAdd(new PresentationCommand
                    {
                        Kind = PresentationCommandKind.CreatePerformer,
                        IdA = descriptor.PerformerDefinitionId,
                        IdB = scopeId,
                        Source = entity,
                    });
                }
            });

            foreach (StatusKey key in _active)
            {
                if (_nextActive.Contains(key))
                {
                    continue;
                }

                if (_scopes.TryGetValue(key, out int scopeId))
                {
                    _commands.TryAdd(new PresentationCommand
                    {
                        Kind = PresentationCommandKind.DestroyPerformerScope,
                        IdA = scopeId,
                    });
                }
            }

            _active.Clear();
            foreach (StatusKey key in _nextActive)
            {
                _active.Add(key);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EnsureResolved()
        {
            if (_resolved)
            {
                return;
            }

            for (int i = 0; i < _descriptors.Length; i++)
            {
                _descriptors[i].TagId = TagRegistry.Register(_descriptors[i].TagName);
                _descriptors[i].PerformerDefinitionId = _performers.GetId(_descriptors[i].PerformerKey);
                if (_descriptors[i].PerformerDefinitionId <= 0)
                {
                    throw new InvalidOperationException(
                        $"CommonControlBuffsPresentationMod requires performer '{_descriptors[i].PerformerKey}'.");
                }
            }

            _resolved = true;
        }

        private int EnsureScopeId(StatusKey key)
        {
            if (_scopes.TryGetValue(key, out int scopeId))
            {
                return scopeId;
            }

            scopeId = _nextScopeId++;
            _scopes[key] = scopeId;
            return scopeId;
        }

        private readonly record struct StatusKey(Entity Owner, int StatusIndex);

        private struct StatusDescriptor
        {
            public StatusDescriptor(string tagName, string performerKey)
            {
                TagName = tagName;
                PerformerKey = performerKey;
                TagId = 0;
                PerformerDefinitionId = 0;
            }

            public string TagName;
            public string PerformerKey;
            public int TagId;
            public int PerformerDefinitionId;
        }
    }
}
