using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Gameplay.GAS.Input
{
    public enum SelectionRuleMode : byte
    {
        None = 0,
        SingleNearest = 1,
        Radius = 2,
    }

    public struct SelectionRule
    {
        public SelectionRuleMode Mode;
        public RelationshipFilter RelationshipFilter;
        public int RadiusCm;
        public int MaxCount;

        public readonly bool IsValid => Mode != SelectionRuleMode.None;
    }

    public sealed class SelectionRuleRegistry
    {
        private readonly Dictionary<int, SelectionRule> _rules = new();

        public void Register(int requestTypeId, in SelectionRule rule)
        {
            if (requestTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestTypeId), "Selection request type id must be non-negative.");
            }

            if (!rule.IsValid)
            {
                throw new ArgumentException("Selection rule must define a valid mode.", nameof(rule));
            }

            _rules[requestTypeId] = rule;
        }

        public bool TryGet(int requestTypeId, out SelectionRule rule)
        {
            return _rules.TryGetValue(requestTypeId, out rule);
        }

        public static SelectionRuleRegistry CreateWithDefaults(
            int singlePickRadiusCm = 120,
            int areaRadiusCm = 250,
            int areaMaxCount = 64)
        {
            var registry = new SelectionRuleRegistry();
            RegisterDefaults(registry, singlePickRadiusCm, areaRadiusCm, areaMaxCount);
            return registry;
        }

        public static void RegisterDefaults(
            SelectionRuleRegistry registry,
            int singlePickRadiusCm = 120,
            int areaRadiusCm = 250,
            int areaMaxCount = 64)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(SelectionRequestTags.DefaultAreaAll, new SelectionRule
            {
                Mode = SelectionRuleMode.Radius,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = areaRadiusCm,
                MaxCount = areaMaxCount,
            });
            registry.Register(SelectionRequestTags.Single, new SelectionRule
            {
                Mode = SelectionRuleMode.SingleNearest,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = singlePickRadiusCm,
                MaxCount = 1,
            });
            registry.Register(SelectionRequestTags.CircleEnemy, new SelectionRule
            {
                Mode = SelectionRuleMode.Radius,
                RelationshipFilter = RelationshipFilter.Hostile,
                RadiusCm = areaRadiusCm,
                MaxCount = areaMaxCount,
            });
            registry.Register(SelectionRequestTags.CircleAll, new SelectionRule
            {
                Mode = SelectionRuleMode.Radius,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = areaRadiusCm,
                MaxCount = areaMaxCount,
            });
        }
    }
}
