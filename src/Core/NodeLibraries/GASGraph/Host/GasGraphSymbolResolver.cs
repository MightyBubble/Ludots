using System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Resolves graph symbol names to runtime ids by delegating to GAS registries.
    /// Keeps <see cref="GraphProgramLoader"/> decoupled from concrete registry types.
    /// </summary>
    public sealed class GasGraphSymbolResolver : IGraphSymbolResolver
    {
        private readonly RelationshipTypeRegistry _types;
        private readonly RelationshipMetricRegistry _metrics;
        private readonly RelationshipFlagRegistry _flags;
        private readonly RelationshipReasonRegistry _reasons;
        private readonly TargetDispatchPresetRegistry _targetDispatchPresets;
        private readonly EntityTemplateKeyRegistry? _entityTemplateKeys;

        public GasGraphSymbolResolver(
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipReasonRegistry reasons,
            TargetDispatchPresetRegistry targetDispatchPresets,
            EntityTemplateKeyRegistry? entityTemplateKeys = null)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _reasons = reasons ?? throw new ArgumentNullException(nameof(reasons));
            _targetDispatchPresets = targetDispatchPresets ?? throw new ArgumentNullException(nameof(targetDispatchPresets));
            _entityTemplateKeys = entityTemplateKeys;
        }

        public int ResolveTag(string name)
        {
            int id = TagRegistry.GetId(name);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph references unknown tag '{name}'. Register tags before loading graph programs.");
            }
            return id;
        }

        public int ResolveAttribute(string name)
        {
            int id = AttributeRegistry.GetId(name);
            if (id < 0)
            {
                throw new InvalidOperationException(
                    $"Graph references unknown attribute '{name}'. Register attributes before loading graph programs.");
            }
            return id;
        }

        public int ResolveEffectTemplate(string name)
        {
            int id = EffectTemplateIdRegistry.GetId(name);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph references unknown effect template '{name}'.");
            }
            return id;
        }

        public int ResolveRelationshipType(string name)
        {
            return _types.GetId(name);
        }

        public int ResolveRelationshipMetric(string name)
        {
            return _metrics.GetId(name);
        }

        public int ResolveRelationshipFlag(string name)
        {
            return _flags.GetId(name);
        }

        public int ResolveRelationshipReason(string name)
        {
            return _reasons.Register(name);
        }

        public int ResolveTargetDispatchPreset(string name)
        {
            return _targetDispatchPresets.GetId(name);
        }

        public int ResolveEntityTemplate(string name)
        {
            if (_entityTemplateKeys == null)
            {
                throw new InvalidOperationException(
                    $"Graph references entity template '{name}', but no EntityTemplateKeyRegistry was provided.");
            }

            if (!_entityTemplateKeys.TryGetId(name, out int id) || id <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph references unknown entity template '{name}'. Register templates before loading graph programs.");
            }

            return id;
        }
    }
}
