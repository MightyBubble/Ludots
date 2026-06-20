using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression.Components;

namespace Ludots.Core.Gameplay.Progression.Systems
{
    public sealed class ProgressionScopeBindingSystem : BaseSystem<World, float>
    {
        private const int MaxHosts = 512;

        private static readonly QueryDescription HostQuery = new QueryDescription()
            .WithAll<ProgressionScopeHostAuthoring>();

        private static readonly QueryDescription BindingQuery = new QueryDescription()
            .WithAll<ProgressionScopeBindingAuthoring>();

        private readonly ProgressionRequirementEvaluator _evaluator;
        private readonly ProgressionScopeKeyRegistry _scopeKeys;
        private readonly HostEntry[] _hosts = new HostEntry[MaxHosts];
        private int _hostCount;

        public ProgressionScopeBindingSystem(
            World world,
            ProgressionRequirementEvaluator evaluator,
            ProgressionScopeKeyRegistry scopeKeys)
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
            World.InlineEntityQuery<CollectHostJob, ProgressionScopeHostAuthoring>(in HostQuery, ref collectJob);

            var bindJob = new BindMembersJob
            {
                System = this
            };
            World.InlineEntityQuery<BindMembersJob, ProgressionScopeBindingAuthoring>(in BindingQuery, ref bindJob);
        }

        private void AddHost(Entity entity, int scopeNameKeyId, int hostKeyId)
        {
            if (_hostCount >= _hosts.Length)
            {
                throw new InvalidOperationException($"ProgressionScopeBindingSystem supports up to {_hosts.Length} scope hosts per frame.");
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

        private struct CollectHostJob : IForEachWithEntity<ProgressionScopeHostAuthoring>
        {
            public ProgressionScopeBindingSystem System;

            public void Update(Entity entity, ref ProgressionScopeHostAuthoring authoring)
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

        private struct BindMembersJob : IForEachWithEntity<ProgressionScopeBindingAuthoring>
        {
            public ProgressionScopeBindingSystem System;

            public void Update(Entity entity, ref ProgressionScopeBindingAuthoring authoring)
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
                                $"ProgressionScopeBinding references unknown scope '{ResolveConfigKeyName(scopeNameKeyId)}'.");
                        }

                        if (!System.TryResolveHost(scopeNameKeyId, hostKeyId, out Entity host))
                        {
                            throw new InvalidOperationException(
                                $"ProgressionScopeBinding references missing host scope='{ResolveConfigKeyName(scopeNameKeyId)}' hostKey='{ResolveConfigKeyName(hostKeyId)}'.");
                        }

                        if (!System._evaluator.TryBindScope(entity, scopeId, host))
                        {
                            throw new InvalidOperationException(
                                $"ProgressionScopeBinding failed for scope='{ResolveConfigKeyName(scopeNameKeyId)}' hostKey='{ResolveConfigKeyName(hostKeyId)}'. Ensure member and host progression components are preallocated by authoring.");
                        }
                    }
                }
            }
        }
    }
}
