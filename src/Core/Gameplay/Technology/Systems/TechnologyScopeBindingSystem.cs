using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Technology.Components;

namespace Ludots.Core.Gameplay.Technology.Systems
{
    public sealed class TechnologyScopeBindingSystem : BaseSystem<World, float>
    {
        private const int MaxHosts = 512;

        private static readonly QueryDescription HostQuery = new QueryDescription()
            .WithAll<TechnologyScopeHostAuthoring>();

        private static readonly QueryDescription BindingQuery = new QueryDescription()
            .WithAll<TechnologyScopeBindingAuthoring>();

        private readonly TechnologyRequirementEvaluator _evaluator;
        private readonly TechnologyScopeKeyRegistry _scopeKeys;
        private readonly HostEntry[] _hosts = new HostEntry[MaxHosts];
        private int _hostCount;

        public TechnologyScopeBindingSystem(
            World world,
            TechnologyRequirementEvaluator evaluator,
            TechnologyScopeKeyRegistry scopeKeys)
            : base(world)
        {
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            _scopeKeys = scopeKeys ?? throw new ArgumentNullException(nameof(scopeKeys));
        }

        public override void Update(in float dt)
        {
            _hostCount = 0;
            var collectJob = new CollectHostJob
            {
                System = this
            };
            World.InlineEntityQuery<CollectHostJob, TechnologyScopeHostAuthoring>(in HostQuery, ref collectJob);

            var bindJob = new BindMembersJob
            {
                System = this
            };
            World.InlineEntityQuery<BindMembersJob, TechnologyScopeBindingAuthoring>(in BindingQuery, ref bindJob);
        }

        private void AddHost(Entity entity, int scopeNameKeyId, int hostKeyId)
        {
            if (_hostCount >= _hosts.Length)
            {
                throw new InvalidOperationException($"TechnologyScopeBindingSystem supports up to {_hosts.Length} scope hosts per frame.");
            }

            _hosts[_hostCount++] = new HostEntry(scopeNameKeyId, hostKeyId, entity);
        }

        private bool TryResolveHost(int scopeNameKeyId, int hostKeyId, out Entity host)
        {
            for (int i = 0; i < _hostCount; i++)
            {
                ref readonly var entry = ref _hosts[i];
                if (entry.ScopeNameKeyId == scopeNameKeyId && entry.HostKeyId == hostKeyId)
                {
                    host = entry.Entity;
                    return World.IsAlive(host);
                }
            }

            host = Entity.Null;
            return false;
        }

        private bool TryResolveScopeId(int scopeNameKeyId, out int scopeId)
        {
            string scopeName = ConfigKeyRegistry.GetName(scopeNameKeyId);
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                scopeId = 0;
                return false;
            }

            return _scopeKeys.TryGetId(scopeName, out scopeId) && scopeId > 0;
        }

        private static string ResolveConfigKeyName(int keyId)
        {
            string name = ConfigKeyRegistry.GetName(keyId);
            return string.IsNullOrWhiteSpace(name) ? $"#{keyId}" : name;
        }

        private readonly struct HostEntry
        {
            public readonly int ScopeNameKeyId;
            public readonly int HostKeyId;
            public readonly Entity Entity;

            public HostEntry(int scopeNameKeyId, int hostKeyId, Entity entity)
            {
                ScopeNameKeyId = scopeNameKeyId;
                HostKeyId = hostKeyId;
                Entity = entity;
            }
        }

        private struct CollectHostJob : IForEachWithEntity<TechnologyScopeHostAuthoring>
        {
            public TechnologyScopeBindingSystem System;

            public void Update(Entity entity, ref TechnologyScopeHostAuthoring authoring)
            {
                unsafe
                {
                    for (int i = 0; i < authoring.Count; i++)
                    {
                        System.AddHost(entity, authoring.ScopeNameKeyIds[i], authoring.HostKeyIds[i]);
                    }
                }
            }
        }

        private struct BindMembersJob : IForEachWithEntity<TechnologyScopeBindingAuthoring>
        {
            public TechnologyScopeBindingSystem System;

            public void Update(Entity entity, ref TechnologyScopeBindingAuthoring authoring)
            {
                unsafe
                {
                    for (int i = 0; i < authoring.Count; i++)
                    {
                        int scopeNameKeyId = authoring.ScopeNameKeyIds[i];
                        int hostKeyId = authoring.HostKeyIds[i];
                        if (!System.TryResolveScopeId(scopeNameKeyId, out int scopeId))
                        {
                            throw new InvalidOperationException(
                                $"TechnologyScopeBinding references unknown scope '{ResolveConfigKeyName(scopeNameKeyId)}'.");
                        }

                        if (!System.TryResolveHost(scopeNameKeyId, hostKeyId, out Entity host))
                        {
                            throw new InvalidOperationException(
                                $"TechnologyScopeBinding references missing host scope='{ResolveConfigKeyName(scopeNameKeyId)}' hostKey='{ResolveConfigKeyName(hostKeyId)}'.");
                        }

                        if (!System._evaluator.TryBindScope(entity, scopeId, host))
                        {
                            throw new InvalidOperationException(
                                $"TechnologyScopeBinding failed for scope='{ResolveConfigKeyName(scopeNameKeyId)}' hostKey='{ResolveConfigKeyName(hostKeyId)}'. Ensure member and host technology components are preallocated by authoring.");
                        }
                    }
                }
            }
        }
    }
}
